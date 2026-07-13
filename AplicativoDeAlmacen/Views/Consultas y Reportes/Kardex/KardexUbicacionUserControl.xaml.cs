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
    /// DESCARTADO PPORQUE SE TIENE QUE VINCULAR CON VENTAS, NO CON SALIDAS Y ENTRADAS DE ALMACEN
    public partial class KardexUbicacionUserControl : UserControl
    {
        private readonly KardexService _kardexService = new KardexService();
        private readonly ReporteExcelService _reporteExcel = new ReporteExcelService();
        private int _productoSeleccionadoId = 0;
        private int _ubicacionSeleccionadaId = 0;
        private ProductoService _productoService = new ProductoService();
        private UbicacionService _ubicacionService = new UbicacionService(); // Necesario para autocompletado

        
        
        private ConsultaMovimientoReporte _reporteActual;
        public KardexUbicacionUserControl()
        {
            InitializeComponent();
            DpDesde.SelectedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DpHasta.SelectedDate = DateTime.Now;
        }



        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validar que los DatePicker tengan una fecha seleccionada
            if (!DpDesde.SelectedDate.HasValue || !DpHasta.SelectedDate.HasValue)
            {
                MessageBox.Show("Por favor, seleccione un rango de fechas válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Validar que el producto haya sido seleccionado
            if (_productoSeleccionadoId <= 0)
            {
                MessageBox.Show("Por favor, seleccione un producto válido.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 3. Ahora sí, usamos .Value de forma segura porque ya validamos que no son nulos
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
                // Filtra los códigos basándote en el documento/registro del movimiento
                DgDetalles.ItemsSource = _reporteActual.Codigos
                    .Where(c => c.NumeroRegistro == movimiento.NumeroRegistro)
                    .ToList();
            }
        }

        // Añade esto para solucionar el error del botón Salir
        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            // Si estás dentro de una pestaña de MainShell, simplemente cierra la pestaña
            if (Window.GetWindow(this) is IMainWindow mainWindow)
            {
                // Opcional: Lógica para cerrar la pestaña actual
            }
        }

        // Añade esto para el botón Imprimir
        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_reporteActual == null || _reporteActual.Movimientos == null)
            {
                MessageBox.Show("Por favor, ejecute el Kardex primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Extraemos los valores de los controles de la vista
                string producto = TxtProducto.Text;
                string ubicacion = TxtUbicacion.Text;
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Now;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Now;

                // Pasamos los 5 argumentos requeridos por la firma del método
                _reporteExcel.ExportarKardexUbicacion(_reporteActual, producto, ubicacion, desde, hasta);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al exportar: " + ex.Message);
            }
        }

        // 1. Lógica para PRODUCTO
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e) // 🌟 1. AGREGADO 'async' AQUÍ
        {
            string busqueda = TxtProducto.Text.Trim();
            if (busqueda.Length >= 2)
            {
                // 🌟 2. CAMBIO DE MÉTODO Y AGREGADO DE 'await'
                var resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);

                LstProducto.ItemsSource = resultados;
                PopupProducto.IsOpen = resultados.Any();
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
                TxtCodProducto.Text = prod.Id.ToString(); // O el campo de código que tengas
                _productoSeleccionadoId = prod.Id;
                PopupProducto.IsOpen = false;
            }
        }

        // 2. Lógica para UBICACIÓN
        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = TxtUbicacion.Text.Trim();
            if (busqueda.Length >= 2)
            {
                var resultados = _ubicacionService.BuscarUbicacionesPorNombre(busqueda);
                LstUbicacion.ItemsSource = resultados;
                PopupUbicacion.IsOpen = resultados.Any();
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
