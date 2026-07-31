using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Core;
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
                // 🌟 1. Obtenemos el ID del almacén de la sesión activa
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                // 🌟 Pasamos el ID correctamente sin errores de parámetros
                var listaCritica = await _productoService.ObtenerStockCriticoAsync(miAlmacenId);

                // 1. Llenamos el DataGrid
                DgStockCritico.ItemsSource = listaCritica;

                // 2. Preparamos los datos para el gráfico
                Series.Clear();
                YAxes.Clear();
                XAxes.Clear();

                if (listaCritica == null || listaCritica.Count == 0) return;

                // 🌟 Usamos la propiedad calculada Faltante para ordenar el top 10
                var topCriticos = listaCritica.OrderByDescending(p => p.Faltante).Take(10).ToList();

                var nombresProductos = topCriticos.Select(p => p.Descripcion).ToList();
                var stockActual = topCriticos.Select(p => (double)p.StockActual).ToList();
                var stockMinimo = topCriticos.Select(p => (double)p.StockMinimo).ToList();

                Series.Add(new RowSeries<double>
                {
                    Values = stockActual,
                    Name = "Stock Actual (Disponible)",
                    Fill = new SolidColorPaint(SKColors.Red),
                    MaxBarWidth = 25,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle
                });

                Series.Add(new RowSeries<double>
                {
                    Values = stockMinimo,
                    Name = "Mínimo Requerido",
                    Fill = new SolidColorPaint(new SKColor(251, 146, 60, 150)),
                    MaxBarWidth = 25
                });

                YAxes.Add(new Axis
                {
                    Labels = nombresProductos,
                    LabelsPaint = new SolidColorPaint(SKColors.DarkSlateGray),
                    TextSize = 12
                });

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