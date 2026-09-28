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
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Reportes;
using AplicativoDeAlmacen.Services.Reportes;

namespace AplicativoDeAlmacen.Views.Reportes
{
    public partial class ReporteVentasUbicacionUserControl : UserControl
    {
        private readonly ReporteVentasUbicacionService _ubicacionReporteService;
        private readonly ReporteExcelService _excelService;

        private int? _ubicacionSeleccionadaId = null;
        private bool _isUpdatingFicha = false;
        private readonly int _miSedeId;
        private readonly string _miSedeNombre;

        private readonly ObservableCollection<ReporteVentaProductoResumenDTO> _productosList = new();
        private readonly ObservableCollection<ReporteVentaCodigoDetalleDTO> _codigosList = new();

        public ReporteVentasUbicacionUserControl()
        {
            InitializeComponent();
            _ubicacionReporteService = new ReporteVentasUbicacionService();
            _excelService = new ReporteExcelService();

            DgProductos.ItemsSource = _productosList;
            DgCodigos.ItemsSource = _codigosList;

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            _miSedeId = SesionSistema.AlmacenActual?.Id ?? 1;
            _miSedeNombre = SesionSistema.AlmacenActual?.Nombre ?? "Sede Principal";
            TxtSedeActual.Text = _miSedeNombre;
        }

        private async void TxtUbicacionBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;

            string texto = TxtUbicacionBuscador.Text.Trim();
            if (texto.Length < 2)
            {
                PopUbicaciones.IsOpen = false;
                return;
            }

            try
            {
                var lista = await _ubicacionReporteService.BuscarUbicacionesAsync(texto);

                if (lista.Any())
                {
                    LstUbicaciones.ItemsSource = lista;
                    PopUbicaciones.IsOpen = true;
                }
                else
                {
                    PopUbicaciones.IsOpen = false;
                }
            }
            catch
            {
                PopUbicaciones.IsOpen = false;
            }
        }

        private void LstUbicaciones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicaciones.SelectedItem is Ubicacion u)
            {
                _isUpdatingFicha = true;
                _ubicacionSeleccionadaId = u.Id;

                TxtUbicacionBuscador.Text = u.Descripcion;
                TxtUbicacionCodigo.Text = u.Id.ToString("D3");
                TxtLocalidadZona.Text = u.Localidad?.Nombre ?? "/";

                PopUbicaciones.IsOpen = false;
                _isUpdatingFicha = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (!_ubicacionSeleccionadaId.HasValue)
            {
                MessageBox.Show("Seleccione una Ubicación primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtUbicacionBuscador.Focus();
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

                var resumen = await _ubicacionReporteService.ObtenerResumenPorUbicacionAsync(
                    _ubicacionSeleccionadaId.Value, desde, hasta, _miSedeId);

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
                    MessageBox.Show($"No se encontraron ventas para esta ubicación en {_miSedeNombre} dentro del rango de fechas.", "Sin Registros", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar ventas por ubicación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgProductos.SelectedItem is not ReporteVentaProductoResumenDTO prodSeleccionado || !_ubicacionSeleccionadaId.HasValue)
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

                var detalles = await _ubicacionReporteService.ObtenerDetalleCodigosUbicacionAsync(
                    _ubicacionSeleccionadaId.Value, prodSeleccionado.ProductoId, desde, hasta, _miSedeId);

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
            if (!_productosList.Any() || !_ubicacionSeleccionadaId.HasValue)
            {
                MessageBox.Show("No hay datos cargados para generar el reporte.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime desde = DpDesde.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

            try
            {
                Cursor = Cursors.Wait;

                var todosLosCodigos = new List<ReporteVentaCodigoDetalleDTO>();

                foreach (var prod in _productosList)
                {
                    var codsProd = await _ubicacionReporteService.ObtenerDetalleCodigosUbicacionAsync(
                        _ubicacionSeleccionadaId.Value, prod.ProductoId, desde, hasta, _miSedeId);

                    foreach (var c in codsProd)
                    {
                        c.ProductoId = prod.ProductoId;
                        todosLosCodigos.Add(c);
                    }
                }

                _excelService.ExportarReporteVentasPorUbicacion(
                    TxtUbicacionBuscador.Text,
                    TxtUbicacionCodigo.Text,
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
                MessageBox.Show($"Error al abrir Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
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