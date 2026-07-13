using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System.Linq;
using System.Globalization;
using LiveChartsCore.Kernel.Sketches;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos
{
    public partial class WidgetVelasAlmacenUserControl : UserControl
    {
        private readonly ProductoService _productoService = new ProductoService();
        private readonly KardexService _kardexService = new KardexService();
        private int _productoSeleccionadoId;
        private double _stockMinimoSeleccionado = 0;
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
            PopupResultados.IsOpen = false;
        }

        private async void TxtBuscarProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtBuscarProducto.Text;
            if (busqueda.Length >= 2)
            {
                var resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);
                if (resultados != null && resultados.Count > 0)
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
                _productoSeleccionadoId = p.Id;
                _stockMinimoSeleccionado = p.StockMinimo; // ¡ATRAPAMOS EL MÍNIMO AQUÍ!

                TxtBuscarProducto.TextChanged -= TxtBuscarProducto_TextChanged;
                TxtBuscarProducto.Text = p.Descripcion;
                TxtBuscarProducto.TextChanged += TxtBuscarProducto_TextChanged;
                PopupResultados.IsOpen = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0) return;

            var kardex = await _kardexService.GenerarKardexFisicoAsync(_productoSeleccionadoId, DpDesde.SelectedDate.Value, DpHasta.SelectedDate.Value);

            // 1. Estructura intermedia para homogeneizar la agrupación seleccionada
            var movimientosProcesados = new List<MovimientoAgrupado>();

            if (CboPeriodo.SelectedIndex == 0) // VISTA: DÍA / MOVIMIENTO
            {
                foreach (var m in kardex.Detalles)
                {
                    double ingreso = (double)(m.IngresoNormal + m.IngresoDevolucion);
                    double salida = (double)(m.SalidaNormal + m.SalidaDevolucion);
                    if (ingreso == 0 && salida == 0) continue;

                    movimientosProcesados.Add(new MovimientoAgrupado
                    {
                        Ingreso = ingreso,
                        Salida = salida,
                        Label = m.Fecha?.ToString("dd/MM/yyyy") ?? "Mov"
                    });
                }
            }
            else if (CboPeriodo.SelectedIndex == 1) // VISTA: POR SEMANA
            {
                var gruposSemana = kardex.Detalles
                    .Where(m => (m.IngresoNormal + m.IngresoDevolucion > 0) || (m.SalidaNormal + m.SalidaDevolucion > 0))
                    .GroupBy(m => {
                        DateTime date = m.Fecha ?? DateTime.Today;
                        int numeroSemana = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                        return new { date.Year, Semana = numeroSemana };
                    })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Semana);

                foreach (var g in gruposSemana)
                {
                    double totIngreso = g.Sum(m => (double)(m.IngresoNormal + m.IngresoDevolucion));
                    double totSalida = g.Sum(m => (double)(m.SalidaNormal + m.SalidaDevolucion));
                    double neto = totIngreso - totSalida;

                    movimientosProcesados.Add(new MovimientoAgrupado
                    {
                        Ingreso = neto > 0 ? neto : 0,
                        Salida = neto < 0 ? Math.Abs(neto) : 0,
                        Label = $"Sem {g.Key.Semana} - {g.Key.Year}"
                    });
                }
            }
            else if (CboPeriodo.SelectedIndex == 2) // VISTA: POR MES
            {
                var gruposMes = kardex.Detalles
                    .Where(m => (m.IngresoNormal + m.IngresoDevolucion > 0) || (m.SalidaNormal + m.SalidaDevolucion > 0))
                    .GroupBy(m => {
                        DateTime date = m.Fecha ?? DateTime.Today;
                        return new { date.Year, date.Month };
                    })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

                foreach (var g in gruposMes)
                {
                    double totIngreso = g.Sum(m => (double)(m.IngresoNormal + m.IngresoDevolucion));
                    double totSalida = g.Sum(m => (double)(m.SalidaNormal + m.SalidaDevolucion));
                    double neto = totIngreso - totSalida;

                    string nombreMes = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key.Month);
                    nombreMes = char.ToUpper(nombreMes[0]) + nombreMes.Substring(1);

                    movimientosProcesados.Add(new MovimientoAgrupado
                    {
                        Ingreso = neto > 0 ? neto : 0,
                        Salida = neto < 0 ? Math.Abs(neto) : 0,
                        Label = $"{nombreMes} {g.Key.Year}"
                    });
                }
            }

            if (movimientosProcesados.Count == 0)
            {
                Series.Clear();
                XAxes.Clear();
                return;
            }

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

            // Bloques Base Invisibles
            Series.Add(new StackedColumnSeries<double>
            {
                Values = espaciadores,
                Name = "",
                Fill = new SolidColorPaint(SKColors.Transparent),
                Stroke = null,
                YToolTipLabelFormatter = point => ""
            });

            // Bloques de Entradas (Verdes)
            Series.Add(new StackedColumnSeries<double>
            {
                Values = entradas,
                Name = "Entrada (+)",
                Fill = new SolidColorPaint(SKColors.Green),
                MaxBarWidth = 40,
                YToolTipLabelFormatter = point =>
                {
                    if (point.Model > 0)
                    {
                        int index = (int)point.Coordinate.SecondaryValue;
                        return $"Cantidad: {point.Model}\nTotal Acumulado: {totales[index]}";
                    }
                    return "";
                }
            });

            // Bloques de Salidas (Rojas)
            Series.Add(new StackedColumnSeries<double>
            {
                Values = salidas,
                Name = "Salida (-)",
                Fill = new SolidColorPaint(SKColors.Red),
                MaxBarWidth = 40,
                YToolTipLabelFormatter = point =>
                {
                    if (point.Model > 0)
                    {
                        int index = (int)point.Coordinate.SecondaryValue;
                        return $"Cantidad: {point.Model}\nTotal Acumulado: {totales[index]}";
                    }
                    return "";
                }
            });

            if (_stockMinimoSeleccionado > 0)
            {
                // Le agregamos "LiveChartsCore.Defaults." para que Visual Studio no se pierda
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
                    YToolTipLabelFormatter = point => $"Mínimo Permitido: {_stockMinimoSeleccionado}"
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
    }

    // Clase auxiliar para mapear las agrupaciones de tiempo de forma homogénea
    public class MovimientoAgrupado
    {
        public double Ingreso { get; set; }
        public double Salida { get; set; }
        public string Label { get; set; }
    }
}