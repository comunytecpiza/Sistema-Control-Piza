using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex.KardexValorizado
{
    public partial class KardexValorizadoUserControl : UserControl
    {
        private readonly KardexService _kardexService = new KardexService();
        private readonly ProductoService _productoService = new ProductoService();
        private readonly ReporteExcelService _reporteExcel = new ReporteExcelService();

        private int _productoSeleccionadoId = 0;
        private KardexValorizadoReporte _reporteActual;

        public KardexValorizadoUserControl()
        {
            InitializeComponent();

            // =======================================================
            // FECHAS POR DEFECTO 
            // =======================================================
            // Desde: Primer día del mes actual (ej. 01/07/2026)
            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            // Hasta: Siempre la fecha de hoy
            DpHasta.SelectedDate = DateTime.Now;
        }

        // ==========================================
        // AUTOCOMPLETADO DE PRODUCTO MULTICOLUMNA
        // ==========================================
        
        private async void CboProducto_KeyUp(object sender, KeyEventArgs e) // 🌟 AGREGADO 'async' AQUÍ
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab) return;

            var textBox = (TextBox)e.OriginalSource;
            string busqueda = textBox.Text;
            int cursorPosition = textBox.SelectionStart;

            if (busqueda.Length >= 2)
            {
                CboProducto.SelectionChanged -= CboProducto_SelectionChanged;

                // 🌟 CORRECCIÓN: Llamamos al método Async con await
                var resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);

                CboProducto.ItemsSource = resultados;
                CboProducto.IsDropDownOpen = resultados.Any();

                CboProducto.SelectionChanged += CboProducto_SelectionChanged;

                textBox.Text = busqueda;
                textBox.SelectionStart = cursorPosition;
            }
            else
            {
                CboProducto.IsDropDownOpen = false;
            }
        }

        private void CboProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProducto.SelectedItem is Producto prod)
            {
                TxtCodProducto.Text = prod.Id.ToString("D3");
                _productoSeleccionadoId = prod.Id;
            }
            else
            {
                TxtCodProducto.Text = string.Empty;
                _productoSeleccionadoId = 0;
            }
        }

        // ==========================================
        // ACCIONES
        // ==========================================
        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Seleccione un producto válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!DpDesde.SelectedDate.HasValue || !DpHasta.SelectedDate.HasValue)
            {
                MessageBox.Show("Seleccione un rango de fechas válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DgResumen.ItemsSource = null;

                // 🌟 CORRECCIÓN: Pasar el ID del almacén actual de la sesión
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                _reporteActual = await _kardexService.GenerarKardexValorizadoAsync(
                    _productoSeleccionadoId,
                    DpDesde.SelectedDate.Value,
                    DpHasta.SelectedDate.Value,
                    miAlmacenId); // 👈 ID de Almacén

                DgResumen.ItemsSource = _reporteActual.Detalles;

                // FOOTER DINÁMICO
                decimal saldoInicialCalculado = _reporteActual.StockFinalFisico - _reporteActual.TotalIngresoFisico + _reporteActual.TotalSalidaFisico;
                decimal costoPromedioInicial = _reporteActual.Detalles.FirstOrDefault()?.CostoPromedio ?? 0;

                TxtSaldoInicial.Text = Math.Max(0, saldoInicialCalculado).ToString("N2");
                TxtCostoInicial.Text = (saldoInicialCalculado * costoPromedioInicial).ToString("N2");
                TxtTotalIngresos.Text = _reporteActual.TotalIngresoValorado.ToString("N2");
                TxtTotalSalidas.Text = _reporteActual.TotalSalidaValorado.ToString("N2");
                TxtSaldoFinal.Text = _reporteActual.SaldoFinalValorado.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar Kardex Valorizado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_reporteActual == null || !_reporteActual.Detalles.Any())
            {
                MessageBox.Show("Por favor, ejecute el Kardex primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string nombreProducto = CboProducto.Text;
                DateTime desde = DpDesde.SelectedDate.Value;
                DateTime hasta = DpHasta.SelectedDate.Value;

                _reporteExcel.ExportarKardexValorizadoSunat(_reporteActual, nombreProducto, desde, hasta);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar: {ex.Message}", "Error de Exportación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            var parentWindow = Window.GetWindow(this);
            parentWindow?.Close();
        }
    }
}