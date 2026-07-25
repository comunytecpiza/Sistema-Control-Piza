using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos
{
    public partial class WidgetDistribucionAlmacenUserControl : UserControl
    {
        private readonly ProductoService _productoService;

        // Variables para guardar los dos productos seleccionados
        private Producto _productoA;
        private Producto _productoB;

        public ObservableCollection<ISeries> Series { get; set; } = new ObservableCollection<ISeries>();

        public WidgetDistribucionAlmacenUserControl()
        {
            InitializeComponent();
            _productoService = new ProductoService();
            DataContext = this;

            // Inicializar gráfico vacío
            Series.Clear();
        }

        // ==========================================
        // LÓGICA PRODUCTO A
        // ==========================================
        private async void TxtBuscarA_TextChanged(object sender, TextChangedEventArgs e)
        {
            string texto = TxtBuscarA.Text.Trim();

            if (texto.Length < 2)
            {
                LstProductosA.Visibility = Visibility.Collapsed;
                return;
            }

            // 🌟 CORRECCIÓN: Llamamos a la función asincrónica directamente, sin Task.Run
            var resultados = await _productoService.BuscarProductosPorTextoAsync(texto);

            LstProductosA.ItemsSource = resultados.Take(15).ToList();
            LstProductosA.Visibility = resultados.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LstProductosA_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProductosA.SelectedItem is not Producto p) return;

            _productoA = p;

            TxtBuscarA.TextChanged -= TxtBuscarA_TextChanged;
            TxtBuscarA.Text = p.Abreviatura ?? p.Descripcion;
            TxtBuscarA.TextChanged += TxtBuscarA_TextChanged;

            TxtProductoA.Text = p.Descripcion;
            TxtStockA.Text = $"Stock: {p.CantidadCodigos:N0}"; // 🌟 Usamos CantidadCodigos

            LstProductosA.Visibility = Visibility.Collapsed;

            ActualizarGrafico();
        }

        // ==========================================
        // LÓGICA PRODUCTO B
        // ==========================================
        private async void TxtBuscarB_TextChanged(object sender, TextChangedEventArgs e)
        {
            string texto = TxtBuscarB.Text.Trim();

            if (texto.Length < 2)
            {
                LstProductosB.Visibility = Visibility.Collapsed;
                return;
            }

            // 🌟 CORRECCIÓN: Llamamos a la función asincrónica directamente
            var resultados = await _productoService.BuscarProductosPorTextoAsync(texto);

            LstProductosB.ItemsSource = resultados.Take(15).ToList();
            LstProductosB.Visibility = resultados.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LstProductosB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProductosB.SelectedItem is not Producto p) return;

            _productoB = p;

            TxtBuscarB.TextChanged -= TxtBuscarB_TextChanged;
            TxtBuscarB.Text = p.Abreviatura ?? p.Descripcion;
            TxtBuscarB.TextChanged += TxtBuscarB_TextChanged;

            TxtProductoB.Text = p.Descripcion;
            TxtStockB.Text = $"Stock: {p.CantidadCodigos:N0}"; // 🌟 Usamos CantidadCodigos

            LstProductosB.Visibility = Visibility.Collapsed;

            ActualizarGrafico();
        }

        // ==========================================
        // LÓGICA DEL GRÁFICO
        // ==========================================
        private void ActualizarGrafico()
        {
            Series.Clear();

            // Si hay producto A, lo agregamos
            if (_productoA != null && _productoA.CantidadCodigos > 0)
            {
                Series.Add(new PieSeries<double>
                {
                    Name = _productoA.Abreviatura ?? "Prod 1",
                    Values = new double[] { _productoA.CantidadCodigos },
                    InnerRadius = 70,
                    MaxRadialColumnWidth = 60,
                    DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:N0} uds",
                    Fill = new SolidColorPaint(SKColors.SteelBlue)
                });
            }

            // Si hay producto B, lo agregamos
            if (_productoB != null && _productoB.CantidadCodigos > 0)
            {
                Series.Add(new PieSeries<double>
                {
                    Name = _productoB.Abreviatura ?? "Prod 2",
                    Values = new double[] { _productoB.CantidadCodigos },
                    InnerRadius = 70,
                    MaxRadialColumnWidth = 60,
                    DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:N0} uds",
                    Fill = new SolidColorPaint(SKColors.OrangeRed)
                });
            }
        }
    }
}