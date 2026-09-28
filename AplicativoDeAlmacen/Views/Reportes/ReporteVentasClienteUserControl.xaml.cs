#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Reportes;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;

namespace AplicativoDeAlmacen.Views.Reportes
{
    public partial class ReporteVentasClienteUserControl : UserControl
    {
        private readonly PersonaComercialService _personaService;
        private readonly ReporteVentasClienteService _reporteService;
        private readonly ReporteExcelService _excelService;

        private int? _clienteSeleccionadoId = null;
        private bool _isUpdatingFicha = false;
        private readonly int _miSedeId;
        private readonly string _miSedeNombre;

        private readonly ObservableCollection<ReporteVentaProductoResumenDTO> _productosList = new();
        private readonly ObservableCollection<ReporteVentaCodigoDetalleDTO> _codigosList = new();

        public ReporteVentasClienteUserControl()
        {
            InitializeComponent();
            _personaService = new PersonaComercialService();
            _reporteService = new ReporteVentasClienteService();
            _excelService = new ReporteExcelService();

            DgProductos.ItemsSource = _productosList;
            DgCodigos.ItemsSource = _codigosList;

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            // 🌟 Identificación inmutable de la sede del usuario conectado
            _miSedeId = SesionSistema.AlmacenActual?.Id ?? 1;
            _miSedeNombre = SesionSistema.AlmacenActual?.Nombre ?? "Sede Principal";
            TxtSedeActual.Text = _miSedeNombre;
        }

        private async void TxtClienteBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;

            string texto = TxtClienteBuscador.Text.Trim();
            if (texto.Length < 2)
            {
                PopClientes.IsOpen = false;
                return;
            }

            try
            {
                var lista = await _personaService.BuscarPorRazonSocialAsync(texto);
                var items = lista?.Take(12).ToList();

                if (items != null && items.Any())
                {
                    LstClientes.ItemsSource = items;
                    PopClientes.IsOpen = true;
                }
                else
                {
                    PopClientes.IsOpen = false;
                }
            }
            catch
            {
                PopClientes.IsOpen = false;
            }
        }

        private void LstClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstClientes.SelectedItem is PersonaComercial p)
            {
                _isUpdatingFicha = true;
                _clienteSeleccionadoId = p.Id;

                TxtClienteBuscador.Text = p.RazonSocial ?? $"{p.Nombres} {p.ApellidoPaterno}".Trim();
                TxtClienteCodigo.Text = p.Id.ToString("D6");

                string localidad = p.Localidad?.Nombre ?? "";
                string zona = p.ZonaPromotoria?.Descripcion ?? "";
                TxtLocalidadZona.Text = $"{localidad} / {zona}".Trim();

                PopClientes.IsOpen = false;
                _isUpdatingFicha = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (!_clienteSeleccionadoId.HasValue)
            {
                MessageBox.Show("Seleccione un Cliente o Institución primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtClienteBuscador.Focus();
                return;
            }

            DateTime desde = DpDesde.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

            if (desde > hasta)
            {
                MessageBox.Show("La fecha 'Desde' no puede ser mayor que la fecha 'Hasta'.", "Rango Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.Wait;
                _productosList.Clear();
                _codigosList.Clear();
                TxtTotalCodigosCantidad.Text = "0";
                TxtTotalCodigosImporte.Text = "S/ 0.00";

                // 🌟 Candado: Se envía estrictamente el ID de la sede actual
                var resumen = await _reporteService.ObtenerResumenProductosVendidosAsync(
                    _clienteSeleccionadoId.Value, desde, hasta, _miSedeId);

                foreach (var item in resumen)
                {
                    _productosList.Add(item);
                }

                decimal granTotal = _productosList.Sum(x => x.Importe);
                TxtTotalProductosImporte.Text = $"S/ {granTotal:N2}";

                if (_productosList.Any())
                {
                    DgProductos.SelectedIndex = 0;
                }
                else
                {
                    MessageBox.Show($"No se encontraron ventas para este cliente en {_miSedeNombre} dentro del rango de fechas.", "Sin Registros", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar ventas: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgProductos.SelectedItem is not ReporteVentaProductoResumenDTO prodSeleccionado || !_clienteSeleccionadoId.HasValue)
            {
                _codigosList.Clear();
                TxtTotalCodigosCantidad.Text = "0";
                TxtTotalCodigosImporte.Text = "S/ 0.00";
                return;
            }

            DateTime desde = DpDesde.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

            try
            {
                Cursor = Cursors.Wait;
                _codigosList.Clear();

                // 🌟 Candado: Se filtran códigos exclusivamente de la sede actual
                var detalles = await _reporteService.ObtenerDetalleCodigosProductoAsync(
                    _clienteSeleccionadoId.Value, prodSeleccionado.ProductoId, desde, hasta, _miSedeId);

                foreach (var d in detalles)
                {
                    _codigosList.Add(d);
                }

                TxtTotalCodigosCantidad.Text = _codigosList.Count.ToString();
                TxtTotalCodigosImporte.Text = $"S/ {_codigosList.Sum(x => x.Importe):N2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar detalle de códigos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (!_productosList.Any() || !_clienteSeleccionadoId.HasValue)
            {
                MessageBox.Show("No hay datos cargados para generar el reporte.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime desde = DpDesde.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

            try
            {
                this.Cursor = Cursors.Wait;

                // 🌟 Recuperamos los códigos de todos los productos del reporte de esta sede
                var todosLosCodigos = new List<ReporteVentaCodigoDetalleDTO>();

                foreach (var prod in _productosList)
                {
                    var codsProd = await _reporteService.ObtenerDetalleCodigosProductoAsync(
                        _clienteSeleccionadoId.Value, prod.ProductoId, desde, hasta, _miSedeId);

                    // Asignamos el ProductoId para poder agruparlos en el Excel
                    foreach (var c in codsProd)
                    {
                        c.ProductoId = prod.ProductoId;
                        todosLosCodigos.Add(c);
                    }
                }

                _excelService.ExportarReporteVentasPorCliente(
                    TxtClienteBuscador.Text,
                    TxtClienteCodigo.Text,
                    TxtLocalidadZona.Text,
                    _miSedeNombre,
                    desde.ToString("dd/MM/yyyy"),
                    hasta.ToString("dd/MM/yyyy"),
                    _productosList.ToList(),
                    todosLosCodigos
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el reporte en Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (Parent is Panel p)
            {
                p.Children.Remove(this);
            }
            else if (Parent is ContentControl cc)
            {
                cc.Content = null;
            }
        }
    }
}