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
            // 1. Validar producto seleccionado
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto antes de agregar códigos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Validar cantidad esperada
            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida de unidades.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. EXTRAER los datos del DataGrid a una lista compatible
            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>();
            foreach (var item in dgDetalleCodigos.Items)
            {
                if (item is RangoCodigoItem rango)
                {
                    listaDeRangosActual.Add(rango);
                }
            }

            // 4. Calcular el total acumulado usando la lista extraída
            int totalCodigosYaAgregados = 0;
            foreach (var rango in listaDeRangosActual)
            {
                if (int.TryParse(rango.Cantidad, out int cantRango))
                {
                    totalCodigosYaAgregados += cantRango;
                }
            }

            if (totalCodigosYaAgregados >= cantidadCodigosEsperados)
            {
                MessageBox.Show("Ya ha registrado el total de códigos indicado.", "Lotes Completos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int cantidadFaltantePorAsignar = cantidadCodigosEsperados - totalCodigosYaAgregados;

            // 5. LLAMAR A LA VENTANA usando la lista en lugar del control DataGrid
            try
            {
                AsignarCodigoWindow ventanaCodigo = new AsignarCodigoWindow(
                    listaDeRangosActual, // <--- Aquí pasas la lista de tipo List<RangoCodigoItem>
                    _productoSeleccionado.Abreviatura,
                    _productoSeleccionado.Id,
                    cantidadFaltantePorAsignar
                );
                ventanaCodigo.Owner = this;

                if (ventanaCodigo.ShowDialog() == true && ventanaCodigo.FueConfirmado)
                {
                    RangoCodigoItem nuevoRango = ventanaCodigo.RangoProcesado;
                    if (nuevoRango != null)
                    {
                        dgDetalleCodigos.Items.Add(nuevoRango);
                        txtCantidad.IsReadOnly = true;
                        if (txtProducto != null) txtProducto.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            int totalCodigosUnicosRegistrados = 0;

            foreach (var item in dgDetalleCodigos.Items)
            {
                if (item is RangoCodigoItem rango)
                {
                    // Sumamos la propiedad Cantidad que se calculó en la ventana AsignarCodigoWindow
                    if (int.TryParse(rango.Cantidad, out int cantidadDelRango))
                    {
                        totalCodigosUnicosRegistrados += cantidadDelRango;
                    }
                }
            }

            // Ahora comparamos unidades físicas vs total de códigos únicos generados
            if ((int)cantidadPaquetesDeclarados != totalCodigosUnicosRegistrados)
            {
                MessageBox.Show($"Inconsistencia de Códigos Únicos ❌\n\n" +
                                $"En la cantidad del producto indicó: {cantidadPaquetesDeclarados} unidades.\n" +
                                $"Sin embargo, la suma de los códigos en los rangos agregados es de: {totalCodigosUnicosRegistrados} códigos.\n\n" +
                                $"Por favor, configure los rangos para que la cantidad total de códigos coincida exactamente con la cantidad de productos a ingresar.",
                                "Error de Cuadrante", MessageBoxButton.OK, MessageBoxImage.Warning);
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