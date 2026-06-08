using System;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class KardexUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;

        private int _productoSeleccionadoId;

        public KardexUserControl()
        {
            InitializeComponent();

            _kardexService = new KardexService();
            _productoService = new ProductoService();

            // Fechas por defecto (Primer día del mes y Hoy)
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpHasta.SelectedDate = DateTime.Today;

            // Evento para cuando el control se cargue en pantalla
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

        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string texto = ((TextBox)sender).Text;

                if (string.IsNullOrWhiteSpace(texto))
                {
                    CboProductos.ItemsSource = null;
                    CboProductos.IsDropDownOpen = false;
                    return;
                }

                // Buscamos en la base de datos
                var productos = _productoService.BuscarProductos(texto);

                CboProductos.ItemsSource = productos;

                // CRÍTICO: Recuerda que tu modelo usa Descripcion, no Nombre
                CboProductos.DisplayMemberPath = "Descripcion";

                // Abrimos el desplegable si hay resultados
                CboProductos.IsDropDownOpen = productos != null && productos.Count > 0;
            }
            catch
            {
                // Ignoramos errores de tipeo rápido
            }
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Cuando seleccionas un producto de la lista desplegable, guardamos su ID
            if (CboProductos.SelectedItem is Producto producto)
            {
                _productoSeleccionadoId = producto.Id;
            }
        }

        private async void BtnEjecutarKardex_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Seleccione un producto válido de la lista.", "Kardex", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Ejecutamos el servicio del Kardex con las fechas
                var reporte = await _kardexService.GenerarKardexFisicoAsync(
                        _productoSeleccionadoId,
                        DpDesde.SelectedDate ?? DateTime.Today,
                        DpHasta.SelectedDate ?? DateTime.Today);

                // Llenamos la tabla
                KardexDataGrid.ItemsSource = reporte.Detalles;

                // Actualizamos los cuadros de resumen (Asegurando formato de 2 decimales)
                TxtStockInicial.Text = reporte.StockInicial.ToString("N2");
                TxtTotalIngresos.Text = reporte.TotalIngresos.ToString("N2");
                TxtTotalSalidas.Text = reporte.TotalSalidas.ToString("N2");
                TxtStockFinal.Text = reporte.StockFinal.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al generar Kardex", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}