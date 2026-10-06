#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.facturaciòn;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Reporte.contabilidad
{
    public partial class HistorialPorContabilidadUserControl : UserControl
    {
        private readonly FacturacionService _facturacionService;
        private readonly ProductoService _productoService;
        private int _productoIdSeleccionado = 0;
        private int _categoriaProductoIdSeleccionada = 0;

        public HistorialPorContabilidadUserControl()
        {
            InitializeComponent();
            _facturacionService = new FacturacionService();
            _productoService = new ProductoService();
            TxtCodigoEscaneado.IsEnabled = false;
        }

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;
            TxtIdProducto.Clear();
            TxtAbreviatura.Clear();
            TxtCodigoEscaneado.IsEnabled = false;

            string query = TxtProducto.Text.Trim();
            if (query.Length >= 1)
            {
                List<Producto> lista;
                if (int.TryParse(query, out int idProd))
                {
                    var prod = await _productoService.ObtenerPorIdAsync(idProd);
                    lista = prod != null ? new List<Producto> { prod } : await _productoService.BuscarProductosPorTextoAsync(query);
                }
                else
                {
                    lista = await _productoService.BuscarProductosPorTextoAsync(query);
                }

                LbProducto.ItemsSource = lista;
                PopupResultados.IsOpen = lista.Any();
            }
            else
            {
                PopupResultados.IsOpen = false;
            }
        }

        private void LbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LbProducto.SelectedItem is Producto p)
            {
                _productoIdSeleccionado = p.Id;
                TxtProducto.TextChanged -= TxtProducto_TextChanged;
                TxtProducto.Text = p.Descripcion;
                TxtProducto.TextChanged += TxtProducto_TextChanged;

                TxtIdProducto.Text = p.Id.ToString();
                TxtAbreviatura.Text = p.Abreviatura ?? "";
                PopupResultados.IsOpen = false;

                string abrev = (p.Abreviatura ?? "").Trim().ToUpperInvariant();
                if (abrev.EndsWith("-V") || abrev.EndsWith(" V"))
                {
                    _categoriaProductoIdSeleccionada = 2;
                    RbLibroVenta.IsChecked = true;
                    RbLibroGuia.IsEnabled = false;
                }
                else if (abrev.EndsWith("-G") || abrev.EndsWith(" G"))
                {
                    _categoriaProductoIdSeleccionada = 1;
                    RbLibroGuia.IsChecked = true;
                    RbLibroVenta.IsEnabled = false;
                }
                else
                {
                    _categoriaProductoIdSeleccionada = 0;
                    RbLibroGuia.IsEnabled = true;
                    RbLibroVenta.IsEnabled = true;
                }

                TxtCodigoEscaneado.IsEnabled = true;
                TxtCodigoEscaneado.Focus();
            }
        }

        private void RbLibroGuia_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 1;
            TxtCodigoEscaneado.IsEnabled = true;
        }

        private void RbLibroVenta_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 2;
            TxtCodigoEscaneado.IsEnabled = true;
        }

        private async void BtnConsultar_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarConsultaAsync();
        }

        private async void TxtCodigoEscaneado_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcesarConsultaAsync();
            }
        }

        private void BtnLector_Click(object sender, RoutedEventArgs e)
        {
            if (_productoIdSeleccionado == 0 || _categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione el producto y el tipo primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LectorGlobalWindow lectorModal = null!;
            lectorModal = new LectorGlobalWindow(async res =>
            {
                TxtCodigoEscaneado.Text = res.CodigoCompleto;
                await ProcesarConsultaAsync();
                Application.Current.Dispatcher.Invoke(() => lectorModal.Close());
                return (true, "Código cargado");
            })
            {
                Owner = Window.GetWindow(this)
            };
            lectorModal.ShowDialog();
        }

        private async Task ProcesarConsultaAsync()
        {
            string codigo = TxtCodigoEscaneado.Text.Trim();
            if (_productoIdSeleccionado == 0 || _categoriaProductoIdSeleccionada == 0 || string.IsNullOrEmpty(codigo))
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                int almacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                var data = await _facturacionService.ObtenerHistorialContablePorCodigoAsync(
                    _productoIdSeleccionado, codigo, _categoriaProductoIdSeleccionada, almacenId);

                if (data != null)
                {
                    // Panel lateral
                    TxtClienteVenta.Text = data.ClienteNombre;
                    TxtColegioVenta.Text = $"{data.InstitucionColegio} ({data.PuntoVentaNombre})";
                    TxtComprobanteVenta.Text = $"{data.TipoDocumento} {data.SerieNumero}";
                    TxtMedioPagoVenta.Text = data.CanalesPago;

                    // Pestaña 1
                    DgKardexFisico.ItemsSource = data.MovimientosKardex;

                    // Pestaña 2
                    DgPagosFiscales.ItemsSource = data.DetallePagosFiscales;
                }
                else
                {
                    MessageBox.Show("No se encontró información para el código consultado.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                    LimpiarFormularioCompleto();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void LimpiarFormularioCompleto()
        {
            TxtClienteVenta.Clear();
            TxtColegioVenta.Clear();
            TxtComprobanteVenta.Clear();
            TxtMedioPagoVenta.Clear();
            DgKardexFisico.ItemsSource = null;
            DgPagosFiscales.ItemsSource = null;
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;
            TxtProducto.Clear();
            TxtIdProducto.Clear();
            TxtAbreviatura.Clear();
            TxtCodigoEscaneado.Clear();
            TxtCodigoEscaneado.IsEnabled = false;
            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;
            LimpiarFormularioCompleto();
        }
    }
}