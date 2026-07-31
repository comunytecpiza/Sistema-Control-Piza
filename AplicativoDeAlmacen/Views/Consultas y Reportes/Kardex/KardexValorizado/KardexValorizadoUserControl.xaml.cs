using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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

        // ⏱️ Temporizador Debounce (300 ms) para escritura fluida en el ComboBox
        private readonly DispatcherTimer _searchTimer;
        private string _pendingSearchText = string.Empty;

        public KardexValorizadoUserControl()
        {
            InitializeComponent();

            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;

            // Configurar el Debounce Timer
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += async (s, e) =>
            {
                _searchTimer.Stop();
                await EjecutarBusquedaProductosAsync(_pendingSearchText);
            };

            // Suscribirnos de forma segura a la caja de texto interna del ComboBox editable
            Loaded += (s, e) =>
            {
                var textBox = CboProducto.Template?.FindName("PART_EditableTextBox", CboProducto) as TextBox;
                if (textBox != null)
                {
                    textBox.TextChanged += TextBox_TextChanged;
                }
            };

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
        // AUTOCOMPLETADO BLINDADO CONTRA CORTES DE ESCRITURA
        // ==========================================

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Si el texto coincide exactamente con el producto seleccionado, no filtramos de nuevo
                if (CboProducto.SelectedItem is Producto prodSelected && textBox.Text == prodSelected.Descripcion)
                {
                    return;
                }

                _pendingSearchText = textBox.Text;
                _searchTimer.Stop();
                _searchTimer.Start(); // Reinicia el contador de espera
            }
        }

        private async Task EjecutarBusquedaProductosAsync(string busqueda)
        {
            if (string.IsNullOrWhiteSpace(busqueda) || busqueda.Length < 2)
            {
                CboProducto.IsDropDownOpen = false;
                return;
            }

            try
            {
                var resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);
                var listaResultados = resultados?.ToList() ?? new List<Producto>();

                // 🌟 Capturamos el estado actual del cursor para evitar que retroceda o se borre la letra
                var textBox = CboProducto.Template?.FindName("PART_EditableTextBox", CboProducto) as TextBox;
                int cursorPos = textBox?.SelectionStart ?? 0;
                string textoActual = textBox?.Text ?? busqueda;

                CboProducto.SelectionChanged -= CboProducto_SelectionChanged;

                CboProducto.ItemsSource = listaResultados;
                CboProducto.IsDropDownOpen = listaResultados.Any();

                CboProducto.SelectionChanged += CboProducto_SelectionChanged;

                // 🌟 Restauramos el texto y la posición del cursor de manera exacta
                if (textBox != null)
                {
                    textBox.Text = textoActual;
                    textBox.SelectionStart = Math.Min(cursorPos, textBox.Text.Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en búsqueda: {ex.Message}");
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

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                _reporteActual = await _kardexService.GenerarKardexValorizadoAsync(
                    _productoSeleccionadoId,
                    DpDesde.SelectedDate.Value,
                    DpHasta.SelectedDate.Value,
                    miAlmacenId);

                DgResumen.ItemsSource = _reporteActual.Detalles;

                decimal saldoInicialCalculado = _reporteActual.StockFinalFisico - _reporteActual.TotalIngresoFisico + _reporteActual.TotalSalidaFisico;
                decimal costoPromedioInicial = _reporteActual.Detalles.FirstOrDefault()?.CostoPromedio ?? 95.00m;

                TxtSaldoInicial.Text = Math.Max(0, saldoInicialCalculado).ToString("N2");
                TxtCostoInicial.Text = (saldoInicialCalculado * costoPromedioInicial).ToString("N2");

                TxtTotalIngresos.Text = _reporteActual.TotalIngresoFisico.ToString("N2");
                TxtTotalSalidas.Text = _reporteActual.TotalSalidaFisico.ToString("N2");

                TxtSaldoFinal.Text = _reporteActual.StockFinalFisico.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar Kardex Valorizado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRecalcularCostos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ventana = new ValorizacionProductosWindow
                {
                    Owner = Window.GetWindow(this)
                };

                ventana.ShowDialog();

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