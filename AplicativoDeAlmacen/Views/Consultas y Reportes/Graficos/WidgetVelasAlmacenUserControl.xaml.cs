using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos
{
    public partial class WidgetVelasAlmacenUserControl : UserControl
    {
        private readonly ProductoService _productoService = new ProductoService();
        private readonly KardexService _kardexService = new KardexService();
        private readonly PersonaComercialService _personaService = new PersonaComercialService();
        private readonly UbicacionService _ubicacionService = new UbicacionService();

        private int _productoSeleccionadoId;
        private double _stockMinimoSeleccionado = 0;
        private bool _isUpdatingFromSelection = false;

        // Lista de respaldo para abrir la vista previa en el doble clic
        private List<MovimientoAgrupado> _movimientosActualesEnGrafico = new List<MovimientoAgrupado>();

        public ObservableCollection<ISeries> Series { get; set; } = new ObservableCollection<ISeries>();
        public ObservableCollection<ICartesianAxis> XAxes { get; set; } = new ObservableCollection<ICartesianAxis>();
        public ObservableCollection<ICartesianAxis> YAxes { get; set; } = new ObservableCollection<ICartesianAxis>();

        public WidgetVelasAlmacenUserControl()
        {
            InitializeComponent();
            DataContext = this;

            DpDesde.SelectedDate = DateTime.Today.AddMonths(-1);
            DpHasta.SelectedDate = DateTime.Today;

            YAxes.Add(new Axis { MinLimit = 0 });

            TxtBuscarProducto.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtRazonSocial.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtUbicacion.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                PopupResultados.IsOpen = false;
                PopupRazonSocial.IsOpen = false;
                PopupUbicacion.IsOpen = false;
                BtnEjecutar_Click(null, null);
            }
        }

        // ====================================================================
        // 1. BUSCADORES
        // ====================================================================
        private async void TxtBuscarProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromSelection) return;
            string busqueda = TxtBuscarProducto.Text.Trim();

            if (busqueda.Length >= 1)
            {
                List<Producto> resultados;
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
                    PopupResultados.IsOpen = false;
                    return;
                }

                if (resultados != null && resultados.Any())
                {
                    LbProducto.ItemsSource = resultados;
                    PopupResultados.IsOpen = true;
                }
                else { PopupResultados.IsOpen = false; }
            }
            else
            {
                PopupResultados.IsOpen = false;
                _productoSeleccionadoId = 0;
            }
        }

        private void LbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LbProducto.SelectedItem is Producto p)
            {
                _isUpdatingFromSelection = true;
                _productoSeleccionadoId = p.Id;
                _stockMinimoSeleccionado = Convert.ToDouble(p.StockMinimo);

                TxtBuscarProducto.Text = p.Descripcion;
                PopupResultados.IsOpen = false;
                _isUpdatingFromSelection = false;
            }
        }

        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromSelection) return;
            string textoBusqueda = TxtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 1)
            {
                try
                {
                    List<PersonaComercial> sugerencias;
                    if (int.TryParse(textoBusqueda, out int idPersona))
                    {
                        var personaPorId = await _personaService.ObtenerPorIdAsync(idPersona);
                        sugerencias = personaPorId != null
                            ? new List<PersonaComercial> { personaPorId }
                            : await _personaService.BuscarPorRazonSocialAsync(textoBusqueda);
                    }
                    else if (textoBusqueda.Length >= 2)
                    {
                        sugerencias = await _personaService.BuscarPorRazonSocialAsync(textoBusqueda);
                    }
                    else
                    {
                        PopupRazonSocial.IsOpen = false;
                        return;
                    }

                    LstRazonSocial.ItemsSource = sugerencias;
                    PopupRazonSocial.IsOpen = sugerencias != null && sugerencias.Any();
                }
                catch { PopupRazonSocial.IsOpen = false; }
            }
            else PopupRazonSocial.IsOpen = false;
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial persona)
            {
                _isUpdatingFromSelection = true;
                TxtRazonSocial.Text = !string.IsNullOrEmpty(persona.RazonSocial)
                    ? persona.RazonSocial
                    : $"{persona.Nombres} {persona.ApellidoPaterno}";
                PopupRazonSocial.IsOpen = false;
                LstRazonSocial.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
            }
        }

        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromSelection) return;
            string busqueda = TxtUbicacion.Text.Trim();

            if (busqueda.Length >= 1)
            {
                try
                {
                    List<Ubicacion> sugerencias;
                    if (int.TryParse(busqueda, out int idUbi))
                    {
                        var todas = await _ubicacionService.ObtenerTodasAsync();
                        var ubiPorId = todas?.FirstOrDefault(u => u.Id == idUbi);
                        sugerencias = ubiPorId != null
                            ? new List<Ubicacion> { ubiPorId }
                            : await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                    }
                    else if (busqueda.Length >= 2)
                    {
                        sugerencias = await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                    }
                    else
                    {
                        PopupUbicacion.IsOpen = false;
                        return;
                    }

                    LstUbicacion.ItemsSource = sugerencias;
                    PopupUbicacion.IsOpen = sugerencias != null && sugerencias.Any();
                }
                catch { PopupUbicacion.IsOpen = false; }
            }
            else PopupUbicacion.IsOpen = false;
        }

        private void LstUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicacion.SelectedItem is Ubicacion ubi)
            {
                _isUpdatingFromSelection = true;
                TxtUbicacion.Text = ubi.Descripcion;
                PopupUbicacion.IsOpen = false;
                LstUbicacion.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
            }
        }

        // ====================================================================
        // 2. GENERACIÓN DEL GRÁFICO CON N° DE DOCUMENTO
        // ====================================================================
        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                if (int.TryParse(TxtBuscarProducto.Text.Trim(), out int idDirecto))
                {
                    var prod = await _productoService.ObtenerPorIdAsync(idDirecto);
                    if (prod != null)
                    {
                        _productoSeleccionadoId = prod.Id;
                        _stockMinimoSeleccionado = Convert.ToDouble(prod.StockMinimo);
                    }
                }

                if (_productoSeleccionadoId == 0)
                {
                    MessageBox.Show("Seleccione un producto para generar el gráfico de velas.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            if (!DpDesde.SelectedDate.HasValue || !DpHasta.SelectedDate.HasValue) return;

            try
            {
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                string? filtroRazon = !string.IsNullOrWhiteSpace(TxtRazonSocial.Text) ? TxtRazonSocial.Text.Trim() : null;
                string? filtroUbicacion = !string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text.Trim() : null;

                var consultaReporte = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    _productoSeleccionadoId,
                    DpDesde.SelectedDate.Value,
                    DpHasta.SelectedDate.Value,
                    filtroRazon,
                    filtroUbicacion,
                    null,
                    miAlmacenId);

                if (consultaReporte == null || consultaReporte.Movimientos == null || !consultaReporte.Movimientos.Any())
                {
                    Series.Clear();
                    XAxes.Clear();
                    _movimientosActualesEnGrafico.Clear();
                    MessageBox.Show("No se registraron movimientos en el periodo con los filtros indicados.", "Sin Movimientos", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var movimientosProcesados = new List<MovimientoAgrupado>();

                if (CboPeriodo.SelectedIndex == 0) // DÍA / MOVIMIENTO
                {
                    foreach (var m in consultaReporte.Movimientos.Where(x => !x.IsAnulado))
                    {
                        double ing = Convert.ToDouble(m.Ingreso);
                        double sal = Convert.ToDouble(m.Salida);
                        if (ing == 0 && sal == 0) continue;

                        movimientosProcesados.Add(new MovimientoAgrupado
                        {
                            Ingreso = ing,
                            Salida = sal,
                            NumeroRegistro = m.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "",
                            Entidad = m.RazonSocialUbicacion ?? "ALMACÉN",
                            Guia = m.NumeroGuia ?? "",
                            Label = m.Fecha.ToString("dd/MM/yyyy")
                        });
                    }
                }
                else if (CboPeriodo.SelectedIndex == 1) // POR SEMANA
                {
                    var gruposSemana = consultaReporte.Movimientos
                        .Where(m => !m.IsAnulado && (m.Ingreso > 0 || m.Salida > 0))
                        .GroupBy(m => {
                            DateTime date = m.Fecha;
                            int numeroSemana = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                            return new { date.Year, Semana = numeroSemana };
                        })
                        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Semana);

                    foreach (var g in gruposSemana)
                    {
                        double totIngreso = g.Sum(m => Convert.ToDouble(m.Ingreso));
                        double totSalida = g.Sum(m => Convert.ToDouble(m.Salida));
                        double neto = totIngreso - totSalida;

                        movimientosProcesados.Add(new MovimientoAgrupado
                        {
                            Ingreso = neto > 0 ? neto : 0,
                            Salida = neto < 0 ? Math.Abs(neto) : 0,
                            NumeroRegistro = $"{g.Count()} Operaciones",
                            Entidad = "Semana Agrupada",
                            Label = $"Sem {g.Key.Semana} - {g.Key.Year}"
                        });
                    }
                }
                else if (CboPeriodo.SelectedIndex == 2) // POR MES
                {
                    var gruposMes = consultaReporte.Movimientos
                        .Where(m => !m.IsAnulado && (m.Ingreso > 0 || m.Salida > 0))
                        .GroupBy(m => new { m.Fecha.Year, m.Fecha.Month })
                        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

                    foreach (var g in gruposMes)
                    {
                        double totIngreso = g.Sum(m => Convert.ToDouble(m.Ingreso));
                        double totSalida = g.Sum(m => Convert.ToDouble(m.Salida));
                        double neto = totIngreso - totSalida;

                        string nombreMes = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key.Month);
                        nombreMes = char.ToUpper(nombreMes[0]) + nombreMes.Substring(1);

                        movimientosProcesados.Add(new MovimientoAgrupado
                        {
                            Ingreso = neto > 0 ? neto : 0,
                            Salida = neto < 0 ? Math.Abs(neto) : 0,
                            NumeroRegistro = $"{g.Count()} Operaciones",
                            Entidad = "Mes Agrupado",
                            Label = $"{nombreMes} {g.Key.Year}"
                        });
                    }
                }

                if (movimientosProcesados.Count == 0)
                {
                    Series.Clear();
                    XAxes.Clear();
                    _movimientosActualesEnGrafico.Clear();
                    return;
                }

                _movimientosActualesEnGrafico = movimientosProcesados;

                var espaciadores = new List<double>();
                var entradas = new List<double>();
                var salidas = new List<double>();
                var totales = new List<double>();
                var labelsFechas = new List<string>();

                double stockActual = 0;

                foreach (var item in movimientosProcesados)
                {
                    double stockAnterior = stockActual;
                    stockActual += (item.Ingreso - item.Salida);

                    if (item.Ingreso > 0)
                    {
                        espaciadores.Add(stockAnterior);
                        entradas.Add(item.Ingreso);
                        salidas.Add(0);
                    }
                    else
                    {
                        espaciadores.Add(stockActual);
                        entradas.Add(0);
                        salidas.Add(item.Salida);
                    }

                    totales.Add(stockActual);
                    labelsFechas.Add(item.Label);
                }

                Series.Clear();

                // 1. Base Invisible
                Series.Add(new StackedColumnSeries<double>
                {
                    Values = espaciadores,
                    Name = "",
                    Fill = new SolidColorPaint(SKColors.Transparent),
                    Stroke = null,
                    YToolTipLabelFormatter = point => ""
                });

                // 2. Entradas con Doc/Comprobante en Tooltip
                Series.Add(new StackedColumnSeries<double>
                {
                    Values = entradas,
                    Name = "Entrada (+)",
                    Fill = new SolidColorPaint(SKColors.Green),
                    MaxBarWidth = 38,
                    YToolTipLabelFormatter = point =>
                    {
                        if (point.Model > 0)
                        {
                            int index = (int)point.Coordinate.SecondaryValue;
                            var mov = _movimientosActualesEnGrafico[index];
                            string docInfo = !string.IsNullOrEmpty(mov.NumeroRegistro) ? $"\nDoc: {mov.NumeroRegistro}" : "";
                            string entInfo = !string.IsNullOrEmpty(mov.Entidad) ? $"\nEntidad: {mov.Entidad}" : "";

                            return $"📥 Entrada: +{point.Model}{docInfo}{entInfo}\nStock Resultante: {totales[index]}\n(Doble clic para abrir)";
                        }
                        return "";
                    }
                });

                // 3. Salidas con Doc/Comprobante en Tooltip
                Series.Add(new StackedColumnSeries<double>
                {
                    Values = salidas,
                    Name = "Salida (-)",
                    Fill = new SolidColorPaint(SKColors.Red),
                    MaxBarWidth = 38,
                    YToolTipLabelFormatter = point =>
                    {
                        if (point.Model > 0)
                        {
                            int index = (int)point.Coordinate.SecondaryValue;
                            var mov = _movimientosActualesEnGrafico[index];
                            string docInfo = !string.IsNullOrEmpty(mov.NumeroRegistro) ? $"\nDoc: {mov.NumeroRegistro}" : "";
                            string entInfo = !string.IsNullOrEmpty(mov.Entidad) ? $"\nEntidad: {mov.Entidad}" : "";

                            return $"📤 Salida: -{point.Model}{docInfo}{entInfo}\nStock Resultante: {totales[index]}\n(Doble clic para abrir)";
                        }
                        return "";
                    }
                });

                // Línea de Stock Mínimo
                if (_stockMinimoSeleccionado > 0)
                {
                    var puntosLinea = new List<LiveChartsCore.Defaults.ObservablePoint>
                    {
                        new LiveChartsCore.Defaults.ObservablePoint(-0.5, _stockMinimoSeleccionado),
                        new LiveChartsCore.Defaults.ObservablePoint(movimientosProcesados.Count - 0.5, _stockMinimoSeleccionado)
                    };

                    Series.Add(new LineSeries<LiveChartsCore.Defaults.ObservablePoint>
                    {
                        Values = puntosLinea,
                        Name = "Stock Mínimo",
                        Stroke = new SolidColorPaint(SKColors.Orange, 2),
                        Fill = null,
                        GeometrySize = 0,
                        YToolTipLabelFormatter = point => $"Mínimo: {_stockMinimoSeleccionado}"
                    });
                }

                XAxes.Clear();
                XAxes.Add(new Axis
                {
                    Labels = labelsFechas,
                    LabelsRotation = 15,
                    MinStep = 1
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al construir gráfico: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // ====================================================================
        // 3. DOBLE CLIC EN EL GRÁFICO (Abre Vista Previa de Ingreso o Salida)
        // ====================================================================
        private void MiGraficoVelas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CboPeriodo.SelectedIndex != 0)
            {
                MessageBox.Show("Para abrir la vista previa de un comprobante exacto, seleccione el periodo 'Día / Movimiento'.", "Vista Agrupada", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_movimientosActualesEnGrafico == null || !_movimientosActualesEnGrafico.Any()) return;

            // 1. Obtener la posición del cursor
            Point mousePosition = e.GetPosition(MiGraficoVelas);
            var lvcPoint = new LiveChartsCore.Drawing.LvcPointD(mousePosition.X, mousePosition.Y);

            // 2. Obtener los puntos en la coordenada
            var puntosHover = MiGraficoVelas.GetPointsAt(lvcPoint);

            // 3. 🌟 FILTRAR USANDO Coordinate.PrimaryValue (compatible con LiveCharts2)
            var puntoSeleccionado = puntosHover?.FirstOrDefault(p => p.Coordinate.PrimaryValue > 0);
            if (puntoSeleccionado == null) return;

            // 4. 🌟 OBTENER EL ÍNDICE USANDO Coordinate.SecondaryValue
            int index = (int)puntoSeleccionado.Coordinate.SecondaryValue;
            if (index < 0 || index >= _movimientosActualesEnGrafico.Count) return;

            var movimiento = _movimientosActualesEnGrafico[index];
            if (string.IsNullOrWhiteSpace(movimiento.NumeroRegistro)) return;

            string[] partes = movimiento.NumeroRegistro.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string serie = partes.Length > 1 ? partes[0].Trim() : "0001";
            string numero = partes.Length > 1 ? partes[1].Trim() : partes[0].Trim();

            if (int.TryParse(numero, out int numVal))
            {
                numero = numVal.ToString("D7");
            }

            bool esIngreso = movimiento.Ingreso > 0;

            if (Window.GetWindow(this) is IMainWindow mainShell)
            {
                if (esIngreso)
                {
                    var vistaIngreso = new IngresoUserControl();
                    vistaIngreso.CargarDocumentoParaConsulta(serie, numero);
                    mainShell.AbrirPestaña($"📥 Ingreso : {serie}-{numero} (Vista Previa)", vistaIngreso);
                }
                else
                {
                    var vistaSalida = new SalidasUserControl();
                    vistaSalida.CargarDocumentoParaConsulta(serie, numero);
                    mainShell.AbrirPestaña($"📤 Salida : {serie}-{numero} (Vista Previa)", vistaSalida);
                }
            }
        }
    }

    public class MovimientoAgrupado
    {
        public double Ingreso { get; set; }
        public double Salida { get; set; }
        public string NumeroRegistro { get; set; } = string.Empty;
        public string Entidad { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}