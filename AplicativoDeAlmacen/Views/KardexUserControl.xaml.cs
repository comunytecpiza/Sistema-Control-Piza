using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class KardexUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;
        private bool _estaSeleccionando; // Bandera de seguridad UX para evitar bucles de tipeo

        public KardexUserControl()
        {
            InitializeComponent();

            _kardexService = new KardexService();
            _productoService = new ProductoService();

            // Fechas por defecto (Primer día del mes y Hoy)
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += KardexUserControl_Loaded;
        }

        private void KardexUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Extraemos el TextBox interno del ComboBox para detectar cuando escribes
            var txt = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;

            if (txt != null)
            {
                txt.TextChanged += TxtProducto_TextChanged;
            }
        }

        // CONTROL EXCELENTE DE UX ASÍNCRONO: async void controlado para el evento TextChanged
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando)
                return;

            if (CboProductos.SelectedItem is Producto)
                return;

            try
            {
                string texto = ((TextBox)sender).Text;

                if (string.IsNullOrWhiteSpace(texto))
                {
                    CboProductos.ItemsSource = null;
                    CboProductos.IsDropDownOpen = false;
                    _productoSeleccionadoId = 0;
                    return;
                }

                var productos = await _productoService.BuscarProductos(texto);

                CboProductos.ItemsSource = productos;
                CboProductos.DisplayMemberPath = "Descripcion";
                CboProductos.IsDropDownOpen = productos.Count > 0;
            }
            catch
            {
            }
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;

                _productoSeleccionadoId = producto.Id;

                // Mostrar texto seleccionado
                CboProductos.Text = producto.Descripcion;

                // Cerrar lista
                CboProductos.IsDropDownOpen = false;

                _estaSeleccionando = false;
            }
        }

        private async void BtnEjecutarKardex_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Seleccione un producto válido de la lista antes de ejecutar la consulta.", "Kardex Físico", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Ejecutamos el servicio del Kardex multimotor con rango de fechas
                var reporte = await _kardexService.GenerarKardexFisicoAsync(
                        _productoSeleccionadoId,
                        DpDesde.SelectedDate ?? DateTime.Today,
                        DpHasta.SelectedDate ?? DateTime.Today);

                // Llenamos la tabla del DataGrid de forma directa
                KardexDataGrid.ItemsSource = reporte.Detalles;

                // Actualizamos los cuadros de resumen (Asegurando formato de 2 decimales para auditoría)
                TxtTotalIngresos.Text = reporte.TotalIngresos.ToString("N2");
                TxtTotalDevIngresos.Text = reporte.TotalDevIngresos.ToString("N2");
                TxtTotalSalidas.Text = reporte.TotalSalidas.ToString("N2");
                TxtTotalDevSalidas.Text = reporte.TotalDevSalidas.ToString("N2");
                TxtStockFinal.Text = reporte.StockFinal.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al generar Kardex", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}