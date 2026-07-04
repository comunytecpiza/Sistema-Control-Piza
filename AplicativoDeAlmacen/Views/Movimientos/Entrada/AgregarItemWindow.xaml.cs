using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarItemWindow : Window
    {

        // Cuando se abre la ventana en modo edición, permitimos saltar la validación de duplicados
        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;

        public decimal CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        public Producto _productoSeleccionado = null;
        public ObservableCollection<RangoCodigoItem> ListaRangosAgregados { get; private set; }
        // Esta lista servirá de "espejo" para saber qué ya se registró en el padre
        public List<VistaProductoGrid> ListaProductosExistentesEnPadre { get; set; }

        public AgregarItemWindow()
        {
            InitializeComponent();

            _productoService = new ProductoService();
            ListaRangosAgregados = new ObservableCollection<RangoCodigoItem>();

            // Vincular la colección al DataGrid para evitar operaciones sobre la vista
            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;

            // Permitir editar al hacer doble click en una fila
            dgDetalleCodigos.MouseDoubleClick += DgDetalleCodigos_MouseDoubleClick;

            // =======================================================================
            // ENLACE DE EVENTOS PARA EL BUSCADOR PREDICTIVO EN CALIENTE
            // =======================================================================
            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;
        }

        /// <summary>
        /// Inicializa la ventana para edición de un producto ya agregado.
        /// </summary>
        public void InitializeForEdit(VistaProductoGrid item, List<RangoCodigoItem> rangos)
        {
            if (item == null) return;

            IsEdit = true;
            OriginalProductoId = item.ProductoId;

            // Crear un objeto Producto mínimo para uso interno
            _productoSeleccionado = new Producto
            {
                Id = item.ProductoId,
                Descripcion = item.Descripcion,
                PrecioUnitario = item.Detalle?.CostoUnitario
            };

            txtProducto.Text = _productoSeleccionado.Descripcion;
            txtUMedida.Text = "UNIDAD"; // mantener como antes o derivar si dispone
            txtCUnitario.Text = (_productoSeleccionado.PrecioUnitario ?? 0m).ToString("F2");
            txtCantidad.Text = (item.Detalle?.CantidadIngreso ?? 0m).ToString("0");

            // Cargar rangos existentes
            ListaRangosAgregados.Clear();
            if (rangos != null)
            {
                foreach (var r in rangos)
                {
                    ListaRangosAgregados.Add(r);
                }
            }

            // Bloquear edición del selector de producto
            txtProducto.IsEnabled = false;
            txtCantidad.IsReadOnly = true;
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

            // 3. EXTRAER los datos de la colección enlazada a la grilla
            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

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
                        // 🔥 AQUÍ ESTÁ LA SOLUCIÓN: Validar duplicidad antes de agregar
                        bool yaExiste = false;
                        foreach (var existente in ListaRangosAgregados)
                        {
                            if (existente.DesdeNum == nuevoRango.DesdeNum &&
                                existente.HastaNum == nuevoRango.HastaNum &&
                                existente.CategoriaProductoId == nuevoRango.CategoriaProductoId)
                            {
                                yaExiste = true;
                                break;
                            }
                        }

                        if (yaExiste)
                        {
                            MessageBox.Show("Este rango ya ha sido agregado a la lista.", "Rango Duplicado", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return; // No agregamos nada y salimos
                        }

                        // Si no existe, procedemos a agregar a la colección enlazada
                        ListaRangosAgregados.Add(nuevoRango);

                        // Bloqueo de controles
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
            // En AgregarItemWindow.xaml.cs: validar duplicados excepto si estamos editando el mismo producto
            if (ListaProductosExistentesEnPadre != null)
            {
                bool existe = ListaProductosExistentesEnPadre.Exists(p => p.ProductoId == _productoSeleccionado.Id);
                if (existe && !(IsEdit && OriginalProductoId == _productoSeleccionado.Id))
                {
                    MessageBox.Show("Este producto ya está en la lista.", "Aviso");
                    return; // No se cierra
                }
            }
            if (!decimal.TryParse(txtCantidad.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal cantidadPaquetesDeclarados))
            {
                MessageBox.Show("Cantidad de paquetes inválida.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // REGLA DE ORO: La cantidad de filas en la grilla debe ser idéntica a la cantidad de paquetes declarada
            int totalCodigosUnicosRegistrados = 0;
            foreach (var rango in ListaRangosAgregados)
            {
                if (int.TryParse(rango.Cantidad, out int cantidadDelRango))
                {
                    totalCodigosUnicosRegistrados += cantidadDelRango;
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

            // Los datos ya están en ListaRangosAgregados (colección enlazada)

            FueGrabado = true;
            this.DialogResult = true;
        }

        private void BtnEliminarRango_Click(object sender, RoutedEventArgs e)
        {
            // 1. Verificamos si hay una fila seleccionada en el DataGrid
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem rangoSeleccionado)
            {
                // 2. Pedimos confirmación al usuario
                var confirmacion = MessageBox.Show("¿Está seguro de que desea eliminar este rango de códigos?",
                                                   "Confirmar Eliminación",
                                                   MessageBoxButton.YesNo,
                                                   MessageBoxImage.Question);

                if (confirmacion == MessageBoxResult.Yes)
                {
                    // 3. Eliminamos de la colección enlazada
                    ListaRangosAgregados.Remove(rangoSeleccionado);

                    // 4. Lógica extra: Si la grilla queda vacía, permitimos editar la cantidad de nuevo
                    if (ListaRangosAgregados.Count == 0)
                    {
                        txtCantidad.IsReadOnly = false;
                        txtProducto.IsEnabled = true;
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una fila de la grilla para eliminar.",
                                "Selección necesaria",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
        }
        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            FueGrabado = false;
            this.DialogResult = false;
            this.Close();
        }

    
        private void BtnModificarRango_Click(object sender, RoutedEventArgs e)
        {
            EditarRangoSeleccionado();
        }

        private void DgDetalleCodigos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem)
            {
                EditarRangoSeleccionado();
            }
        }

        private void EditarRangoSeleccionado()
        {
            // 1. Validar que haya una fila seleccionada
            if (dgDetalleCodigos.SelectedItem is not RangoCodigoItem rangoSeleccionado)
            {
                MessageBox.Show("Por favor, seleccione un rango de la tabla para modificar.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. Extraer cantidad total declarada
            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida de unidades antes de modificar un rango.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Obtener la lista actual de rangos desde la colección enlazada
            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

            // 4. Calcular el total ya registrado y la cantidad del ítem seleccionado
            int totalCodigosYaAgregados = 0;
            int cantidadDelItemSeleccionado = 0;
            foreach (var rango in listaDeRangosActual)
            {
                if (int.TryParse(rango.Cantidad, out int cantRango))
                {
                    totalCodigosYaAgregados += cantRango;
                }

                if (ReferenceEquals(rango, rangoSeleccionado) && int.TryParse(rango.Cantidad, out int cantSel))
                {
                    cantidadDelItemSeleccionado = cantSel;
                }
            }

            int cantidadFaltantePorAsignar = cantidadCodigosEsperados - (totalCodigosYaAgregados - cantidadDelItemSeleccionado);
            if (cantidadFaltantePorAsignar < 0) cantidadFaltantePorAsignar = 0;

            try
            {
                AsignarCodigoWindow ventanaEdicion = new AsignarCodigoWindow(listaDeRangosActual, rangoSeleccionado, cantidadFaltantePorAsignar);
                ventanaEdicion.Owner = this;

                if (ventanaEdicion.ShowDialog() == true && ventanaEdicion.FueConfirmado)
                {
                    RangoCodigoItem rangoModificado = ventanaEdicion.RangoProcesado;

                    int index = ListaRangosAgregados.IndexOf(rangoSeleccionado);
                    if (index >= 0)
                    {
                        ListaRangosAgregados[index] = rangoModificado;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al intentar editar el rango: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}