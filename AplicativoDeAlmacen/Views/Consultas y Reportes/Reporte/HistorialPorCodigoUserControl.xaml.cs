using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Reporte
{
    public partial class HistorialPorCodigoUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;

        private int _productoIdSeleccionado = 0;
        private int _categoriaProductoIdSeleccionada = 0; // 1 = Libro Guía, 2 = Libro Venta

        public HistorialPorCodigoUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();

            TxtCodigoEscaneado.IsEnabled = false;
            TxtProducto.Focus();

            DgHistorial.LoadingRow += DgHistorial_LoadingRow;
        }

        private void DgHistorial_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is KardexFisicoItem item)
            {
                if (item.IsAnulado || (item.Tipo != null && item.Tipo.ToUpperInvariant().Contains("ANULADO")))
                {
                    e.Row.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                    e.Row.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    e.Row.FontStyle = FontStyles.Italic;
                    e.Row.ToolTip = "Movimiento ANULADO. Sin efecto sobre el stock.";
                }
                else
                {
                    e.Row.Background = Brushes.White;
                    e.Row.Foreground = Brushes.Black;
                    e.Row.FontStyle = FontStyles.Normal;
                    e.Row.ToolTip = null;
                }
            }
        }

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;
            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;

            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;
            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;

            TxtCodigoEscaneado.IsEnabled = false;

            if (TxtProducto.Text.Trim().Length >= 2)
            {
                var resultados = await _productoService.BuscarProductosPorTextoAsync(TxtProducto.Text.Trim());
                LbProducto.ItemsSource = resultados;
                PopupResultados.IsOpen = resultados != null && resultados.Any();
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
                TxtAbreviatura.Text = string.IsNullOrWhiteSpace(p.Abreviatura) ? "" : p.Abreviatura;
                TxtColeccion.Text = "";

                PopupResultados.IsOpen = false;

                // Restricción inteligente por nombre del producto
                string abrevUpper = p.Abreviatura?.Trim().ToUpperInvariant() ?? "";
                if (abrevUpper.EndsWith("-V") || abrevUpper.EndsWith(" V"))
                {
                    _categoriaProductoIdSeleccionada = 2;
                    RbLibroVenta.IsChecked = true;
                    RbLibroGuia.IsEnabled = false;
                    RbLibroVenta.IsEnabled = true;
                }
                else if (abrevUpper.EndsWith("-G") || abrevUpper.EndsWith(" G"))
                {
                    _categoriaProductoIdSeleccionada = 1;
                    RbLibroGuia.IsChecked = true;
                    RbLibroGuia.IsEnabled = true;
                    RbLibroVenta.IsEnabled = false;
                }
                else
                {
                    _categoriaProductoIdSeleccionada = 0;
                    RbLibroGuia.IsEnabled = true;
                    RbLibroVenta.IsEnabled = true;
                    RbLibroGuia.IsChecked = false;
                    RbLibroVenta.IsChecked = false;
                }

                HabilitarCampoCodigo();
            }
        }

        private void RbLibroGuia_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 1;
            HabilitarCampoCodigo();
        }

        private void RbLibroVenta_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 2;
            HabilitarCampoCodigo();
        }

        private void HabilitarCampoCodigo()
        {
            TxtCodigoEscaneado.IsEnabled = true;
            TxtCodigoEscaneado.Focus();
        }

        // 🌟 BOTÓN LECTOR: Abre la lectora modal para buscar 1 código por lectura
        private async void BtnLector_Click(object sender, RoutedEventArgs e)
        {
            if (_productoIdSeleccionado == 0)
            {
                MessageBox.Show("Seleccione un producto primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione si es Libro Guía o Libro Venta.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Creamos una colección temporal para la LectorWindow
            var itemsTemp = new ObservableCollection<ItemGridDTO>();
            var lectorModal = new LectorWindow(itemsTemp)
            {
                Owner = Window.GetWindow(this)
            };

            lectorModal.ShowDialog();

            // Al cerrar la lectora, recuperamos el código si se leyó alguno
            if (itemsTemp.Any() && itemsTemp.First().Codigos.Any())
            {
                string codigoLeido = itemsTemp.First().Codigos.First().CodigoString;
                TxtCodigoEscaneado.Text = codigoLeido;
                await ProcesarLecturaAsync();
            }
        }

        private async Task ProcesarLecturaAsync()
        {
            string codigo = TxtCodigoEscaneado.Text.Trim();

            if (_productoIdSeleccionado == 0)
            {
                MessageBox.Show("Primero debe seleccionar un producto.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione si es Libro Guía o Libro Venta.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(codigo)) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var historial = await _kardexService.ObtenerHistorialCompletoPorCodigoAsync(
                    _productoIdSeleccionado,
                    codigo,
                    _categoriaProductoIdSeleccionada
                );

                if (historial != null && historial.Any())
                {
                    DgHistorial.ItemsSource = historial;
                }
                else
                {
                    MessageBox.Show($"No se encontró ningún historial para el código '{codigo}'.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    DgHistorial.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al consultar trazabilidad: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarLecturaAsync();
        }

        private async void TxtCodigoEscaneado_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcesarLecturaAsync();
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (DgHistorial.ItemsSource == null) return;
            MessageBox.Show("Generando reporte de trazabilidad...", "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            LimpiarPantalla();
        }

        private void LimpiarPantalla()
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;

            TxtProducto.TextChanged -= TxtProducto_TextChanged;
            TxtProducto.Text = string.Empty;
            TxtProducto.TextChanged += TxtProducto_TextChanged;

            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;
            TxtCodigoEscaneado.Text = string.Empty;
            TxtCodigoEscaneado.IsEnabled = false;

            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;
            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;

            DgHistorial.ItemsSource = null;
            TxtProducto.Focus();
        }
    }
}