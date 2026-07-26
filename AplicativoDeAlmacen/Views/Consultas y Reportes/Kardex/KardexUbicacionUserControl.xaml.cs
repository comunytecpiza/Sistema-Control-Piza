using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex
{
    public partial class KardexUbicacionUserControl : UserControl
    {
        private readonly KardexService _kardexService = new KardexService();
        private readonly ReporteExcelService _reporteExcel = new ReporteExcelService();
        private readonly ProductoService _productoService = new ProductoService();
        private readonly UbicacionService _ubicacionService = new UbicacionService();

        private int _productoSeleccionadoId = 0;
        private int _ubicacionSeleccionadaId = 0;
        private ConsultaMovimientoReporte _reporteActual;

        public KardexUbicacionUserControl()
        {
            InitializeComponent();
            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (!DpDesde.SelectedDate.HasValue || !DpHasta.SelectedDate.HasValue)
            {
                MessageBox.Show("Por favor, seleccione un rango de fechas válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_productoSeleccionadoId <= 0)
            {
                MessageBox.Show("Por favor, busque y seleccione un producto de la lista.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // 🌟 TOMA AUTOMÁTICAMENTE EL ALMACÉN DE LA SESIÓN ACTIVA
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                string? filtroUbicacion = !string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text.Trim() : null;

                // Consulta al KardexService enviando Producto, Fechas, Ubicación y Almacén
                _reporteActual = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    productoId: _productoSeleccionadoId,
                    fechaDesde: DpDesde.SelectedDate.Value,
                    fechaHasta: DpHasta.SelectedDate.Value,
                    razonSocial: null,
                    ubicacion: filtroUbicacion,
                    categoriaProductoId: null,
                    almacenId: miAlmacenId);

                DgResumen.ItemsSource = _reporteActual.Movimientos;
                DgDetalles.ItemsSource = null; // Limpiar lista derecha hasta seleccionar fila
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el Kardex por Ubicación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void DgResumen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgResumen.SelectedItem is ConsultaMovimientoItem movimiento && _reporteActual != null)
            {
                string regLimpio = movimiento.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "";

                DgDetalles.ItemsSource = _reporteActual.Codigos
                    .Where(c => c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) ||
                                c.NumeroRegistro.Equals(movimiento.NumeroRegistro, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        // 1. Buscador Autocompletado para PRODUCTO
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtProducto.Text.Trim();
            if (busqueda.Length >= 2)
            {
                var resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);
                LstProducto.ItemsSource = resultados;
                PopupProducto.IsOpen = resultados != null && resultados.Any();
            }
            else
            {
                PopupProducto.IsOpen = false;
                _productoSeleccionadoId = 0;
                TxtCodProducto.Text = string.Empty;
            }
        }

        private void LstProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProducto.SelectedItem is Producto prod)
            {
                TxtProducto.TextChanged -= TxtProducto_TextChanged;
                TxtProducto.Text = prod.Descripcion;
                TxtProducto.TextChanged += TxtProducto_TextChanged;

                TxtCodProducto.Text = prod.Id.ToString();
                _productoSeleccionadoId = prod.Id;
                PopupProducto.IsOpen = false;
            }
        }

        // 2. Buscador Autocompletado para UBICACIÓN
        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtUbicacion.Text.Trim();
            if (busqueda.Length >= 2)
            {
                var resultados = await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                LstUbicacion.ItemsSource = resultados;
                PopupUbicacion.IsOpen = resultados != null && resultados.Any();
            }
            else
            {
                PopupUbicacion.IsOpen = false;
                _ubicacionSeleccionadaId = 0;
                TxtCodUbicacion.Text = string.Empty;
            }
        }

        private void LstUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicacion.SelectedItem is Ubicacion ubi)
            {
                TxtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                TxtUbicacion.Text = ubi.Descripcion;
                TxtUbicacion.TextChanged += TxtUbicacion_TextChanged;

                TxtCodUbicacion.Text = ubi.Id.ToString("D3");
                _ubicacionSeleccionadaId = ubi.Id;
                PopupUbicacion.IsOpen = false;
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_reporteActual == null || _reporteActual.Movimientos == null || !_reporteActual.Movimientos.Any())
            {
                MessageBox.Show("Por favor, ejecute el Kardex primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string producto = TxtProducto.Text;
                string ubicacion = TxtUbicacion.Text;
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Now;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Now;

                _reporteExcel.ExportarKardexUbicacion(_reporteActual, producto, ubicacion, desde, hasta);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al exportar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            _productoSeleccionadoId = 0;
            _ubicacionSeleccionadaId = 0;
            TxtProducto.Text = string.Empty;
            TxtUbicacion.Text = string.Empty;
            TxtCodProducto.Text = string.Empty;
            TxtCodUbicacion.Text = string.Empty;
            DgResumen.ItemsSource = null;
            DgDetalles.ItemsSource = null;
        }
    }
}