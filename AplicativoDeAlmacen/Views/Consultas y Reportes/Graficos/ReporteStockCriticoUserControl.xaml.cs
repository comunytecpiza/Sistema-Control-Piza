using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos
{
    public partial class ReporteStockCriticoUserControl : UserControl
    {
        private readonly ProductoService _productoService;

        // Colecciones para el Binding del Gráfico
        public ObservableCollection<ISeries> Series { get; set; } = new ObservableCollection<ISeries>();
        public ObservableCollection<ICartesianAxis> YAxes { get; set; } = new ObservableCollection<ICartesianAxis>();
        public ObservableCollection<ICartesianAxis> XAxes { get; set; } = new ObservableCollection<ICartesianAxis>();

        public ReporteStockCriticoUserControl()
        {
            InitializeComponent();
            _productoService = new ProductoService();

            // Contexto de datos para que el XAML lea el gráfico
            DataContext = this;

            // Cargamos la data al iniciar la vista
            _ = CargarReporteAsync();
        }

        private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            await CargarReporteAsync();
        }

        private async System.Threading.Tasks.Task CargarReporteAsync()
        {
            try
            {
                // Obtenemos los productos cuyo stock actual es <= al stock mínimo
                var listaCritica = await _productoService.ObtenerStockCriticoAsync();

                // 1. Llenamos el DataGrid
                DgStockCritico.ItemsSource = listaCritica;

                // 2. Preparamos los datos para el gráfico
                Series.Clear();
                YAxes.Clear();
                XAxes.Clear();

                if (listaCritica.Count == 0) return;

                // Tomamos solo los 10 peores (con más faltante) para que el gráfico no se sature
                var topCriticos = listaCritica.OrderByDescending(p => p.Faltante).Take(10).ToList();

                var nombresProductos = topCriticos.Select(p => p.Descripcion).ToList();
                var stockActual = topCriticos.Select(p => (double)p.StockActual).ToList();
                var stockMinimo = topCriticos.Select(p => (double)p.StockMinimo).ToList();

                // Serie del Stock Actual (Rojo porque es crítico)
                Series.Add(new RowSeries<double>
                {
                    Values = stockActual,
                    Name = "Stock Actual",
                    Fill = new SolidColorPaint(SKColors.Red),
                    MaxBarWidth = 25,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle
                });

                // Serie del Stock Mínimo (Gris/Naranja como referencia)
                Series.Add(new RowSeries<double>
                {
                    Values = stockMinimo,
                    Name = "Mínimo Requerido",
                    Fill = new SolidColorPaint(new SKColor(251, 146, 60, 150)), // Naranja con opacidad
                    MaxBarWidth = 25
                });

                // Eje Y: Muestra los nombres de los productos a la izquierda
                YAxes.Add(new Axis
                {
                    Labels = nombresProductos,
                    LabelsPaint = new SolidColorPaint(SKColors.DarkSlateGray),
                    TextSize = 12
                });

                // Eje X: Empieza en 0
                XAxes.Add(new Axis
                {
                    MinLimit = 0
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al generar reporte: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}