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

            string? filtroUbicacion = !string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text.Trim() : null;

            // 🌟 NUEVA REGLA: Si no hay producto, debe haber al menos una ubicación escrita
            if (_productoSeleccionadoId <= 0 && string.IsNullOrWhiteSpace(filtroUbicacion))
            {
                MessageBox.Show("Seleccione un Producto o escriba una Ubicación para consultar.", "Faltan criterios de búsqueda", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                if (_productoSeleccionadoId > 0)
                {
                    // 🔍 1. Búsqueda específica tradicional por Producto + Ubicación opcional
                    _reporteActual = await _kardexService.ConsultarKardexPorUbicacionAsync(
                        productoId: _productoSeleccionadoId,
                        fechaDesde: DpDesde.SelectedDate.Value,
                        fechaHasta: DpHasta.SelectedDate.Value,
                        filtroUbicacionTexto: filtroUbicacion,
                        almacenId: miAlmacenId);
                }
                else
                {
                    // 🌟 2. BÚSQUEDA GLOBAL POR UBICACIÓN (Muestra todos los productos recibidos/enviados)
                    _reporteActual = await _kardexService.ConsultarKardexPorUbicacionSinProductoAsync(
                        fechaDesde: DpDesde.SelectedDate.Value,
                        fechaHasta: DpHasta.SelectedDate.Value,
                        filtroUbicacionTexto: filtroUbicacion,
                        almacenId: miAlmacenId);
                }

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

                // 🌟 FILTRO EXACTO POR DETALLE O (DOCUMENTO + PRODUCTO)
                DgDetalles.ItemsSource = _reporteActual.Codigos
                    .Where(c => (movimiento.MovimientoDetalleId > 0 && c.MovimientoDetalleId == movimiento.MovimientoDetalleId)
                             || (c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) && c.ProductoId == movimiento.ProductoId))
                    .ToList();
            }
        }

        // 1. Buscador Autocompletado para PRODUCTO
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtProducto.Text.Trim();
            if (busqueda.Length >= 1)
            {
                List<Producto> resultados;

                // 🔍 Búsqueda por ID numérico directo o texto
                if (int.TryParse(busqueda, out int idProd))
                {
                    var prodPorId = await _productoService.ObtenerPorIdAsync(idProd);
                    resultados = prodPorId != null
                        ? new List<Producto> { prodPorId }
                        : await _productoService.BuscarProductosPorTextoAsync(busqueda);
                }
                else if (busqueda.Length >= 2)
                {
                    resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);
                }
                else
                {
                    PopupProducto.IsOpen = false;
                    return;
                }

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
            if (busqueda.Length >= 1)
            {
                List<Ubicacion> resultados;

                // 🔍 Búsqueda por ID numérico directo o texto
                if (int.TryParse(busqueda, out int idUbi))
                {
                    var todas = await _ubicacionService.ObtenerTodasAsync();
                    var ubiPorId = todas?.FirstOrDefault(u => u.Id == idUbi);

                    resultados = ubiPorId != null
                        ? new List<Ubicacion> { ubiPorId }
                        : await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                }
                else if (busqueda.Length >= 2)
                {
                    resultados = await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                }
                else
                {
                    PopupUbicacion.IsOpen = false;
                    return;
                }

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

        private async void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_reporteActual == null || _reporteActual.Movimientos == null || !_reporteActual.Movimientos.Any())
            {
                MessageBox.Show("Por favor, ejecute el Kardex primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ventanaModal = new FiltroImpresionKardexUbicacionWindow();
            var windowPadre = Window.GetWindow(this);
            if (windowPadre != null) ventanaModal.Owner = windowPadre;

            ventanaModal.ShowDialog();
            if (!ventanaModal.SeConfirmoImpresion) return;

            try
            {
                string ubicacion = TxtUbicacion.Text.Trim();
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Now;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Now;
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                if (ventanaModal.EsModoAvanzado)
                {
                    int? catId = ventanaModal.CategoriaIdSeleccionada;
                    string campana = ventanaModal.CampanaSeleccionada;
                    var alcance = ventanaModal.AlcanceMatriz;

                    var listaPaquetes = new List<(string NombreUbicacion, List<MatrizKardexItemDTO> Movimientos)>();

                    if (alcance == FiltroImpresionKardexUbicacionWindow.ModoAlcanceMatriz.SoloActual)
                    {
                        // 🎯 1. Solo la ubicación actual
                        var (movs, catProds) = await _kardexService.ObtenerDatosMatrizAvanzadaAsync(
                            _ubicacionSeleccionadaId > 0 ? _ubicacionSeleccionadaId : null,
                            ubicacion, desde, hasta, catId, miAlmacenId);

                        listaPaquetes.Add((string.IsNullOrWhiteSpace(ubicacion) ? "UBICACIÓN ACTUAL" : ubicacion, movs));

                        _reporteExcel.GenerarLibroMatrizLiquidacionCompleto(campana, catProds, listaPaquetes, false);
                    }
                    else
                    {
                        // 🌐 2 y 3. Todas las ubicaciones (Cada una en su pestaña + Consolidado opcional)
                        var todasLasUbicaciones = await _ubicacionService.ObtenerTodasAsync();
                        List<ProductoColumnaDTO> catalogo = new List<ProductoColumnaDTO>();

                        foreach (var ub in todasLasUbicaciones)
                        {
                            var (movs, catProds) = await _kardexService.ObtenerDatosMatrizAvanzadaAsync(
                                ub.Id, ub.Descripcion, desde, hasta, catId, miAlmacenId);

                            if (movs.Any())
                            {
                                listaPaquetes.Add((ub.Descripcion, movs));
                                if (!catalogo.Any()) catalogo = catProds;
                            }
                        }

                        bool incluirConsolidado = (alcance == FiltroImpresionKardexUbicacionWindow.ModoAlcanceMatriz.TotalConsolidado || alcance == FiltroImpresionKardexUbicacionWindow.ModoAlcanceMatriz.TodosLosPromotores);

                        _reporteExcel.GenerarLibroMatrizLiquidacionCompleto(campana, catalogo, listaPaquetes, incluirConsolidado);
                    }
                }
                else
                {
                    // MODO NORMAL DETALLADO TRADICIONAL
                    bool porFila = ventanaModal.IncluirCodigosPorFila;
                    bool tablaLateral = ventanaModal.IncluirTablaLateral;

                    if (_productoSeleccionadoId > 0)
                        _reporteExcel.ExportarKardexUbicacion(_reporteActual, TxtProducto.Text, ubicacion, desde, hasta, porFila, tablaLateral);
                    else
                        _reporteExcel.ExportarKardexUbicacionGeneral(_reporteActual, ubicacion, desde, hasta, porFila, tablaLateral);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al exportar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }
}