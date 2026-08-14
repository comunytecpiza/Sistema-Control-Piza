using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;
using System;
using System.Collections.Generic;
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

        private bool _necesitaRecargar = false;
        private int _productoSeleccionadoId = 0;
        private int _ubicacionSeleccionadaId = 0;
        private ConsultaMovimientoReporte _reporteActual;

        public KardexUbicacionUserControl()
        {
            InitializeComponent();

            // 🌟 1. SUSCRIPCIÓN AL DOBLE CLIC EN LA GRILLA
            DgResumen.MouseDoubleClick += DgResumen_MouseDoubleClick;

            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;

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

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                string? filtroUbicacion = !string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text.Trim() : null;

                // 🌟 LLAMADA AL NUEVO MÉTODO EXCLUSIVO PARA UBICACIONES
                _reporteActual = await _kardexService.ConsultarKardexPorUbicacionAsync(
                    productoId: _productoSeleccionadoId,
                    fechaDesde: DpDesde.SelectedDate.Value,
                    fechaHasta: DpHasta.SelectedDate.Value,
                    filtroUbicacionTexto: filtroUbicacion,
                    almacenId: miAlmacenId);

                DgResumen.ItemsSource = _reporteActual.Movimientos;
                DgDetalles.ItemsSource = null; // Limpiar lista derecha
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

        // 🌟 DOBLE CLIC EN LA GRILLA: Abre la vista previa del documento
        private void DgResumen_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgResumen.SelectedItem is ConsultaMovimientoItem fila)
            {
                string regLimpio = fila.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "";
                if (string.IsNullOrWhiteSpace(regLimpio)) return;

                string[] partes = regLimpio.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string serie = partes.Length > 1 ? partes[0].Trim() : "0001";
                string numero = partes.Length > 1 ? partes[1].Trim() : partes[0].Trim();

                if (int.TryParse(numero, out int numVal))
                {
                    numero = numVal.ToString("D7");
                }

                bool esIngreso = fila.Ingreso > 0;

                if (Window.GetWindow(this) is IMainWindow mainShell)
                {
                    if (esIngreso)
                    {
                        var vistaIngreso = new IngresoUserControl();
                        vistaIngreso.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📥 Ingreso : {serie}-{numero} (Vista Previa)", vistaIngreso);
                    }
                    else if (fila.Salida > 0)
                    {
                        var vistaSalida = new SalidasUserControl();
                        vistaSalida.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📤 Salida : {serie}-{numero} (Vista Previa)", vistaSalida);
                    }
                }
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

        
    }
}