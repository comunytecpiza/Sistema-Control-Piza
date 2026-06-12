using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarItemWindow : Window
    {

        public decimal CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        public Producto _productoSeleccionado = null;
        public List<RangoCodigoItem> ListaRangosAgregados { get; private set; }

        public AgregarItemWindow()
        {
            InitializeComponent();

            _productoService = new ProductoService();
            ListaRangosAgregados = new List<RangoCodigoItem>();

            // =======================================================================
            // ENLACE DE EVENTOS PARA EL BUSCADOR PREDICTIVO EN CALIENTE
            // =======================================================================
            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;
        }

        #region MOTOR DE BÚSQUEDA PREDICTIVA

        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string textoBusqueda = txtProducto.Text.Trim();

            // Si el usuario borra el texto o escribe menos de 2 letras, cerramos las sugerencias
            if (textoBusqueda.Length < 2)
            {
                popupProductos.IsOpen = false;
                return;
            }

            try
            {
                // Solo busca si no se ha seleccionado ya ese producto exacto
                if (_productoSeleccionado == null || _productoSeleccionado.Descripcion != txtProducto.Text)
                {
                    List<Producto> listaFiltrada = _productoService.BuscarProductosPorTexto(textoBusqueda);

                    if (listaFiltrada.Count > 0)
                    {
                        lstSugerenciasProductos.ItemsSource = listaFiltrada;
                        popupProductos.IsOpen = true; // Despliega el panel flotante
                    }
                    else
                    {
                        popupProductos.IsOpen = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en la consulta predictiva: {ex.Message}", "Error de Búsqueda", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LstSugerenciasProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasProductos.SelectedItem is Producto producto)
            {
                // Inyectamos el producto seleccionado en los controles correspondientes
                _productoSeleccionado = producto;
                txtProducto.Text = producto.Descripcion;
                txtUMedida.Text = producto.UnidadMedidaId == 1 ? "UNIDAD" : "MILLAR";

                txtCUnitario.Text = producto.PrecioUnitario.HasValue
                    ? producto.PrecioUnitario.Value.ToString("F2")
                    : "0.00";

                popupProductos.IsOpen = false; // Cerramos el buscador predictivo
                lstSugerenciasProductos.SelectedIndex = -1; // Reseteamos la selección de la lista
            }
        }

        #endregion

        #region GESTIÓN DE RANGOS MASIVOS Y GRABADO

        private void BtnAgregarRangoCodigo_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validar que tengamos el producto seleccionado desde el buscador
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto del buscador antes de agregar códigos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Validar la cantidad de paquetes/lotes masivos que se esperan acumular
            if (!int.TryParse(txtCantidad.Text, out int cantidadPaquetesEsperados) || cantidadPaquetesEsperados <= 0)
            {
                MessageBox.Show("Por favor, ingrese una cantidad de paquetes válida en el campo del producto.", "Aviso ⚠️", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Controlar que no se agreguen más filas a la grilla que la cantidad estipulada
            int paquetesYaAgregados = dgDetalleCodigos.Items.Count;

            if (paquetesYaAgregados >= cantidadPaquetesEsperados)
            {
                MessageBox.Show($"Ya ha registrado los {cantidadPaquetesEsperados} lotes/rangos indicados en la cantidad del producto.", "Lotes Completos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string abreviatura = _productoSeleccionado.Abreviatura ?? "";
                int productoId = _productoSeleccionado.Id;

                // Abrimos el administrador de rangos (este calculará el tramo independientemente)
                AsignarCodigoWindow ventanaCodigo = new AsignarCodigoWindow(dgDetalleCodigos.Items, abreviatura, productoId);
                ventanaCodigo.Owner = this;

                if (ventanaCodigo.ShowDialog() == true && ventanaCodigo.FueConfirmado)
                {
                    RangoCodigoItem nuevoRango = ventanaCodigo.RangoProcesado;
                    if (nuevoRango != null)
                    {
                        dgDetalleCodigos.Items.Add(nuevoRango);
                        txtCantidad.IsReadOnly = true; // Bloqueamos la cantidad para no romper la consistencia de filas
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir el administrador de rangos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null || string.IsNullOrWhiteSpace(txtProducto.Text))
            {
                MessageBox.Show("Por favor, seleccione un producto válido antes de grabar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtCantidad.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal cantidadPaquetesDeclarados))
            {
                MessageBox.Show("Cantidad de paquetes inválida.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // REGLA DE ORO: La cantidad de filas en la grilla debe ser idéntica a la cantidad de paquetes declarada
            int totalFilasRangos = dgDetalleCodigos.Items.Count;

            if ((int)cantidadPaquetesDeclarados != totalFilasRangos)
            {
                MessageBox.Show($"Inconsistencia de Lotes ❌\n\nDeclaró que ingresará {cantidadPaquetesDeclarados} unidades (paquetes), pero en la lista inferior tiene registrados {totalFilasRangos} rangos de códigos.\n\nDebe ingresar exactamente un rango masivo por cada unidad/paquete.", "Error de Cuadrante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtCUnitario.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal costoValido))
            {
                MessageBox.Show("Por favor, ingrese un costo unitario válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            CantidadProductoIngresada = cantidadPaquetesDeclarados;
            CostoUnitarioIngresado = costoValido;

            // Mapeamos los datos de la grilla a la lista para el guardado final
            ListaRangosAgregados.Clear();
            foreach (var item in dgDetalleCodigos.Items)
            {
                if (item is RangoCodigoItem rango)
                {
                    ListaRangosAgregados.Add(rango);
                }
            }

            FueGrabado = true;
            this.DialogResult = true;
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            FueGrabado = false;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnModificarRangoCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Desea limpiar los rangos asignados para configurarlos de nuevo?", "Modificar Rangos", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                dgDetalleCodigos.Items.Clear();
                txtCantidad.IsReadOnly = false;
            }
        }

        #endregion
    }
}