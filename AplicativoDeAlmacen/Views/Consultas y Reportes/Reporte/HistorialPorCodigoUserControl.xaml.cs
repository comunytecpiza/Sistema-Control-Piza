using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Reporte
{
    public partial class HistorialPorCodigoUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService; // Ajusta el nombre si tu servicio se llama distinto

        private int _productoIdSeleccionado = 0;
        private int _categoriaProductoIdSeleccionada = 0; // 1 = Libro Guía, 2 = Libro Venta

        public HistorialPorCodigoUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();

            // Ahora el flujo empieza en Producto, no en Código
            TxtCodigoEscaneado.IsEnabled = false;
            TxtProducto.Focus();
        }

        // 1. Buscador que activa el Popup
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Si el usuario borra o edita el texto, invalidamos la selección anterior
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;
            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;

            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;
            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;

            TxtCodigoEscaneado.IsEnabled = false;

            if (TxtProducto.Text.Trim().Length >= 2)
            {
                var resultados = await _productoService.BuscarProductosPorTextoAsync(TxtProducto.Text.Trim());

                LbProducto.ItemsSource = resultados;
                PopupResultados.IsOpen = resultados != null && resultados.Any();
            }
            else
            {
                PopupResultados.IsOpen = false;
            }
        }

        // 2. Selección del producto
        private void LbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LbProducto.SelectedItem is Producto p)
            {
                _productoIdSeleccionado = p.Id;

                // Quitamos el handler momentáneamente para no relanzar la búsqueda al setear el texto
                TxtProducto.TextChanged -= TxtProducto_TextChanged;
                TxtProducto.Text = p.Descripcion;
                TxtProducto.TextChanged += TxtProducto_TextChanged;

                TxtIdProducto.Text = p.Id.ToString();

                TxtAbreviatura.Text =
                    string.IsNullOrWhiteSpace(p.Abreviatura)
                    ? ""
                    : p.Abreviatura;

                TxtColeccion.Text = "";

                PopupResultados.IsOpen = false;

                // 🌟 Ahora se desbloquea el TIPO, no el código directamente 🌟
                // El código depende de si es Guía o Venta, así que primero hay que elegir eso.
                _categoriaProductoIdSeleccionada = 0;
                RbLibroGuia.IsEnabled = true;
                RbLibroVenta.IsEnabled = true;
                RbLibroGuia.IsChecked = false;
                RbLibroVenta.IsChecked = false;

                TxtCodigoEscaneado.IsEnabled = false;
                TxtCodigoEscaneado.Text = string.Empty;

                RbLibroGuia.Focus();
            }
        }

        // Se dispara al elegir "Libro Guía"
        private void RbLibroGuia_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 1;
            HabilitarCampoCodigo();
        }

        // Se dispara al elegir "Libro Venta"
        private void RbLibroVenta_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 2;
            HabilitarCampoCodigo();
        }

        private void HabilitarCampoCodigo()
        {
            TxtCodigoEscaneado.IsEnabled = true;
            TxtCodigoEscaneado.Focus();
            TxtCodigoEscaneado.SelectAll();
        }

        private async Task ProcesarLecturaAsync()
        {
            string codigo = TxtCodigoEscaneado.Text.Trim();

            // 1. Validaciones de seguridad antes de llamar al servicio
            if (_productoIdSeleccionado == 0)
            {
                MessageBox.Show("Primero debe seleccionar un producto.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione si es Libro Guía o Libro Venta.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(codigo)) return;

            try
            {
                // 🌟 CORRECCIÓN: Ahora pasamos los 3 argumentos que pide el servicio 🌟
                var historial = await _kardexService.ObtenerHistorialCompletoPorCodigoAsync(
                    _productoIdSeleccionado,
                    codigo,
                    _categoriaProductoIdSeleccionada
                );

                if (historial != null && historial.Any())
                {
                    DgHistorial.ItemsSource = historial;
                }
                else
                {
                    MessageBox.Show("No se encontró historial para este código y categoría.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    DgHistorial.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarLecturaAsync();
        }

        // 3. Método para cuando escanean
        private async void TxtCodigoEscaneado_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcesarLecturaAsync();
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (DgHistorial.ItemsSource == null) return;
            MessageBox.Show("Generando formato de impresión...", "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            LimpiarPantalla();
            // Lógica adicional si quieres que se cierre la pestaña en tu TabControl
        }

        private void LimpiarPantalla()
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;

            TxtProducto.TextChanged -= TxtProducto_TextChanged;
            TxtProducto.Text = string.Empty;
            TxtProducto.TextChanged += TxtProducto_TextChanged;

            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;
            TxtCodigoEscaneado.Text = string.Empty;
            TxtCodigoEscaneado.IsEnabled = false;

            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;
            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;

            DgHistorial.ItemsSource = null;

            TxtProducto.Focus();
        }
    }
}