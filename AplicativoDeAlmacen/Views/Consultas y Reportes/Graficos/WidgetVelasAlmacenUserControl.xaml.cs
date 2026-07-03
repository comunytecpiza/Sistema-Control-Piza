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
using LiveChartsCore.Kernel.Sketches;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos
{
    public partial class WidgetVelasAlmacenUserControl : UserControl
    {
        private readonly ProductoService _productoService = new ProductoService();
        private readonly KardexService _kardexService = new KardexService();

        private int _productoSeleccionadoId;

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
                var resultados = await _productoService.BuscarProductos(busqueda);
                if (resultados != null && resultados.Count > 0)
                {
                    LbProducto.ItemsSource = resultados;
                    PopupResultados.IsOpen = true;
                }
                else
                {
                    PopupResultados.IsOpen = false;
                }
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

            var espaciadores = new List<double>();
            var entradas = new List<double>();
            var salidas = new List<double>();
            var totales = new List<double>();
            var labelsFechas = new List<string>();

            double stockActual = 0;

            foreach (var m in kardex.Detalles)
            {
                double ingreso = (double)(m.IngresoNormal + m.IngresoDevolucion);
                double salida = (double)(m.SalidaNormal + m.SalidaDevolucion);

                // Si no hay movimientos reales, los ignoramos
                if (ingreso == 0 && salida == 0) continue;

                double stockAnterior = stockActual;
                stockActual += (ingreso - salida);

                if (ingreso > 0)
                {
                    espaciadores.Add(stockAnterior);
                    entradas.Add(ingreso);
                    salidas.Add(0);
                }
                else if (salida > 0)
                {
                    espaciadores.Add(stockActual);
                    entradas.Add(0);
                    salidas.Add(salida);
                }

                totales.Add(stockActual);
                labelsFechas.Add(m.Fecha?.ToString("dd/MM/yyyy") ?? "Mov");
            }

            // 🌟 LIMPIEZA AUTOMÁTICA: Si el rango de fechas no tiene nada, vaciamos el gráfico 🌟
            if (labelsFechas.Count == 0)
            {
                Series.Clear();
                XAxes.Clear();
                return;
            }

            Series.Clear();

            // 1. Serie Invisible 
            Series.Add(new StackedColumnSeries<double>
            {
                Values = espaciadores,
                Name = "",
                Fill = new SolidColorPaint(SKColors.Transparent),
                Stroke = null,
                YToolTipLabelFormatter = point => ""
            });

            // 2. Serie Entradas (Verdes)
            Series.Add(new StackedColumnSeries<double>
            {
                Values = entradas,
                Name = "Entrada (+)",
                Fill = new SolidColorPaint(SKColors.Green),
                MaxBarWidth = 40,
                YToolTipLabelFormatter = point =>
                {
                    // SOLUCIÓN: En tu versión, el valor está en "Model" 
                    // y el índice está en la coordenada X ("Coordinate.SecondaryValue")
                    if (point.Model > 0)
                    {
                        int index = (int)point.Coordinate.SecondaryValue;
                        return $"Cantidad: {point.Model}\nTotal Acumulado: {totales[index]}";
                    }
                    return "";
                }
            });

            // 3. Serie Salidas (Rojas)
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

            XAxes.Clear();
            XAxes.Add(new Axis
            {
                Labels = labelsFechas,
                LabelsRotation = 15,
                MinStep = 1
            });
        }
    }
}