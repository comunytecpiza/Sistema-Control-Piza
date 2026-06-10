using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;
        private bool _estaSeleccionando;
        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;
        }

        private void Control_Loaded(object sender, RoutedEventArgs e)
        {
            var txt = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (txt != null)
            {
                txt.TextChanged += TxtProducto_TextChanged;
            }
        }

        // --- EVENTO: Activa/Desactiva las cajas de texto de los filtros ---
        private void ChkFiltros_Click(object sender, RoutedEventArgs e)
        {
            if (TxtRazonSocial != null)
            {
                TxtRazonSocial.IsEnabled = ChkRazonSocial.IsChecked == true;
                if (ChkRazonSocial.IsChecked == false) TxtRazonSocial.Text = string.Empty;
            }
            if (TxtUbicacion != null)
            {
                TxtUbicacion.IsEnabled = ChkUbicacion.IsChecked == true;
                if (ChkUbicacion.IsChecked == false) TxtUbicacion.Text = string.Empty;
            }
        }

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando)
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

                string textoActual = texto;

                CboProductos.ItemsSource = productos;
                CboProductos.DisplayMemberPath = "Descripcion";

                CboProductos.Text = textoActual;

                CboProductos.IsDropDownOpen =
                    productos != null &&
                    productos.Count > 0;
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

                CboProductos.Text = producto.Descripcion;
                CboProductos.IsDropDownOpen = false;

                _estaSeleccionando = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Seleccione un producto para generar el reporte.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ((Button)sender).IsEnabled = false;

                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                // Obtenemos todos los datos brutos desde la BD
                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(_productoSeleccionadoId, desde, hasta);

                // Preparamos la lista para aplicar los filtros de texto
                var movimientosFiltrados = reporte.Movimientos.AsEnumerable();

                // Aplicar Filtro: Razón Social (Colegio, Librería, etc.)
                if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
                {
                    string filtroRazon = TxtRazonSocial.Text.ToLower().Trim();
                    movimientosFiltrados = movimientosFiltrados.Where(m =>
                        m.RazonSocialUbicacion != null &&
                        m.RazonSocialUbicacion.ToLower().Contains(filtroRazon));
                }

                // Aplicar Filtro: Ubicación (Trujillo, Huanchaco, etc.)
                if (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text))
                {
                    string filtroUbi = TxtUbicacion.Text.ToLower().Trim();
                    movimientosFiltrados = movimientosFiltrados.Where(m =>
                        m.RazonSocialUbicacion != null &&
                        m.RazonSocialUbicacion.ToLower().Contains(filtroUbi));
                }

                var listaFinalMovimientos = movimientosFiltrados.ToList();

                // Llenamos tablas
                MovimientosDataGrid.ItemsSource = listaFinalMovimientos;
                CodigosDataGrid.ItemsSource = reporte.Codigos; // Los códigos se mantienen iguales al producto

                // Recalculamos los pies de página según lo filtrado en memoria
                decimal sumaIngresos = listaFinalMovimientos.Sum(m => m.Ingreso);
                decimal sumaSalidas = listaFinalMovimientos.Sum(m => m.Salida);

                TxtTotalIngreso.Text = sumaIngresos.ToString("N2");
                TxtTotalSalida.Text = sumaSalidas.ToString("N2");
                TxtTotalVendidos.Text = sumaSalidas.ToString("N2"); // Ventas netas filtradas

                TxtTotalCodigos.Text = $"{reporte.TotalCodigos} Códigos Registrados";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al generar consulta: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ((Button)sender).IsEnabled = true;
            }
        }
    }
}