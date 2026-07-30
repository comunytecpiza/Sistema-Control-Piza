using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta;
using System;
using System.Data.Common;
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
        private bool _necesitaRecargar = false;
        private int _productoSeleccionadoId = 0;
        private KardexValorizadoReporte _reporteActual;

        public KardexValorizadoUserControl()
        {
            InitializeComponent();

            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;

            // 🌟 SUSCRIPCIÓN AL EVENTBUS
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                if (this.IsVisible && _productoSeleccionadoId > 0)
                {
                    BtnEjecutar_Click(null, null);
                }
                else
                {
                    _necesitaRecargar = true;
                }
            });

            this.IsVisibleChanged += (s, e) => {
                if (this.IsVisible && _necesitaRecargar && _productoSeleccionadoId > 0)
                {
                    _necesitaRecargar = false;
                    BtnEjecutar_Click(null, null);
                }
            };
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

                decimal saldoInicialCalculado = _reporteActual.StockFinalFisico - _reporteActual.TotalIngresoFisico + _reporteActual.TotalSalidaFisico;
                decimal costoPromedioInicial = _reporteActual.Detalles.FirstOrDefault()?.CostoPromedio ?? 95.00m;

                TxtSaldoInicial.Text = Math.Max(0, saldoInicialCalculado).ToString("N2");
                TxtCostoInicial.Text = (saldoInicialCalculado * costoPromedioInicial).ToString("N2");

                // 🌟 CAMBIA ESTAS DOS LÍNEAS AQUÍ:
                TxtTotalIngresos.Text = _reporteActual.TotalIngresoFisico.ToString("N2");  // 👈 Usa TotalIngresoFisico (Cantidad de libros)
                TxtTotalSalidas.Text = _reporteActual.TotalSalidaFisico.ToString("N2");    // 👈 Usa TotalSalidaFisico (Cantidad de libros)

                TxtSaldoFinal.Text = _reporteActual.StockFinalFisico.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar Kardex Valorizado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // 🌟 NUEVO BOTÓN PARA RECALCULAR COSTOS MASIVAMENTE DESDE EL KÁRDEX VALORIZADO
        private void BtnRecalcularCostos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Abre tu ventana tal como la programaste
                var ventana = new ValorizacionProductosWindow
                {
                    Owner = Window.GetWindow(this)
                };

                ventana.ShowDialog();

                // Al cerrarse la ventana, refrescamos el kárdex actual para ver los cambios
                if (_productoSeleccionadoId > 0)
                {
                    BtnEjecutar_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al abrir la ventana de valorización: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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