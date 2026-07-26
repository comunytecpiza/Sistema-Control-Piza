using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex
{
    public partial class KardexUbicacionUserControl : UserControl
    {
        private readonly KardexService _kardexService = new KardexService();
        private readonly ReporteExcelService _reporteExcel = new ReporteExcelService();
        private int _productoSeleccionadoId = 0;
        private int _ubicacionSeleccionadaId = 0;
        private ProductoService _productoService = new ProductoService();
        private UbicacionService _ubicacionService = new UbicacionService();

        private ConsultaMovimientoReporte _reporteActual;

        public KardexUbicacionUserControl()
        {
            InitializeComponent();
            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;

            DgResumen.MouseDoubleClick += (s, e) => { DgResumen_SelectionChanged(null, null); };
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
                MessageBox.Show("Por favor, seleccione un producto válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _reporteActual = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    _productoSeleccionadoId,
                    DpDesde.SelectedDate.Value,
                    DpHasta.SelectedDate.Value);

                DgResumen.ItemsSource = _reporteActual.Movimientos;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el Kardex: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgResumen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgResumen.SelectedItem is ConsultaMovimientoItem movimiento)
            {
                DgDetalles.ItemsSource = _reporteActual.Codigos
                    .Where(c => c.NumeroRegistro == movimiento.NumeroRegistro)
                    .ToList();
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is IMainWindow mainWindow)
            {
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_reporteActual == null || _reporteActual.Movimientos == null)
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
                MessageBox.Show("Error al exportar: " + ex.Message);
            }
        }

        // 1. Lógica para PRODUCTO
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
            }
        }

        private void LstProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProducto.SelectedItem is Producto prod)
            {
                TxtProducto.Text = prod.Descripcion;
                TxtCodProducto.Text = prod.Id.ToString();
                _productoSeleccionadoId = prod.Id;
                PopupProducto.IsOpen = false;
            }
        }

        // 2. Lógica para UBICACIÓN (CORREGIDA ASÍNCRONA CON AWAIT)
        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtUbicacion.Text.Trim();
            if (busqueda.Length >= 2)
            {
                var resultados = await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
                LstUbicacion.ItemsSource = resultados;
                PopupUbicacion.IsOpen = resultados != null && resultados.Any();
            }
            else { PopupUbicacion.IsOpen = false; }
        }

        private void LstUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicacion.SelectedItem is Ubicacion ubi)
            {
                TxtUbicacion.Text = ubi.Descripcion;
                TxtCodUbicacion.Text = ubi.Id.ToString("D3");
                _ubicacionSeleccionadaId = ubi.Id;
                PopupUbicacion.IsOpen = false;
            }
        }
    }
}