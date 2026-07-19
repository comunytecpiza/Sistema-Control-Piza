using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarItemWindow : Window
    {
        // Indica que la ventana fue abierta desde la acción "Agregar Ítem" (nuevo producto)
        public bool IsAddAction { get; set; } = false;

        // Estado permitido para los códigos (1 = COMPRA, otro => 4)
        public int EstadoPermitido { get; set; } = 1;

        // Cuando se abre la ventana en modo edición, permitimos saltar la validación de duplicados
        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;

        // Si verdadero, indica que el usuario quiere añadir los códigos al producto ya existente
        public bool MergeWithExisting { get; private set; } = false;

        public int CantidadProductoIngresada { get; set; } // 🌟 Cambiado a int estricto para sincronía con BD
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

            _productoService = new ProductoService(); // 🌟 Inicialización explícita y segura
            ListaRangosAgregados = new ObservableCollection<RangoCodigoItem>();

            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
            dgDetalleCodigos.MouseDoubleClick += DgDetalleCodigos_MouseDoubleClick;

            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;

            txtBuscarRangoInterno.TextChanged += (s, e) =>
            {
                string filtro = txtBuscarRangoInterno.Text.Trim().ToLower();
                if (string.IsNullOrEmpty(filtro))
                {
                    dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
                }
                else
                {
                    dgDetalleCodigos.ItemsSource = ListaRangosAgregados
                        .Where(r => (r.Desde != null && r.Desde.ToLower().Contains(filtro)) ||
                                    (r.Hasta != null && r.Hasta.ToLower().Contains(filtro)) ||
                                    (r.ColeccionTipo != null && r.ColeccionTipo.ToLower().Contains(filtro)))
                        .ToList();
                }
            };
        }

        public void InitializeForEdit(VistaProductoGrid item, List<RangoCodigoItem> rangos)
        {
            if (item == null) return;

            IsEdit = true;
            OriginalProductoId = item.ProductoId;

            _productoSeleccionado = new Producto
            {
                Id = item.ProductoId,
                Descripcion = item.Descripcion,
                PrecioUnitario = item.Detalle?.CostoUnitario,
                UnidadMedida = new UnidadMedida { Descripcion = item.UnidadMedida }
            };

            if (rangos != null)
            {
                var primera = rangos.FirstOrDefault();
                if (primera != null && !string.IsNullOrWhiteSpace(primera.AbreviaturaBase))
                {
                    _productoSeleccionado.Abreviatura = primera.AbreviaturaBase;
                }
            }

            txtProducto.Text = _productoSeleccionado.Descripcion;
            txtUMedida.Text = !string.IsNullOrWhiteSpace(item.UnidadMedida) ? item.UnidadMedida.ToUpperInvariant() : "UNIDAD";
            txtCUnitario.Text = (_productoSeleccionado.PrecioUnitario ?? 0m).ToString("F2");

            ListaRangosAgregados.Clear();
            if (rangos != null)
            {
                foreach (var r in rangos)
                {
                    string txtDesde, txtHasta;
                    int desdeN = r.DesdeNum;
                    int hastaN = r.HastaNum;
                    string abrev = string.IsNullOrEmpty(r.AbreviaturaBase) ? (_productoSeleccionado?.Abreviatura ?? "COD") : r.AbreviaturaBase;
                    int cantCalcular = (desdeN == -1) ? 1 : (hastaN - desdeN + 1);

                    if (desdeN == -1)
                    {
                        txtDesde = abrev;
                        txtHasta = abrev;
                    }
                    else
                    {
                        txtDesde = string.IsNullOrEmpty(r.Desde) ? $"{abrev}-{desdeN:D7}" : r.Desde;
                        txtHasta = string.IsNullOrEmpty(r.Hasta) ? $"{abrev}-{hastaN:D7}" : r.Hasta;
                    }

                    int categoriaId = r.CategoriaProductoId == 0 ? (EstadoPermitido == 1 ? 1 : 2) : r.CategoriaProductoId;
                    string tipoTexto = (categoriaId == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";

                    string textoColeccionFinal = $"C26 / {tipoTexto}";
                    if (!string.IsNullOrEmpty(r.ColeccionTipo) && r.ColeccionTipo.Contains("/"))
                    {
                        var partesColeccion = r.ColeccionTipo.Split('/');
                        textoColeccionFinal = $"{partesColeccion[0].Trim()} / {tipoTexto}";
                    }

                    string txtCantidadRango = string.IsNullOrEmpty(r.Cantidad) || r.Cantidad == "0" ? cantCalcular.ToString() : r.Cantidad;

                    // Evaluar visibilidad/bloqueo de controles si el producto cargado no tiene abreviatura
                    if (string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura))
                    {
                        dgDetalleCodigos.IsEnabled = false;
                        txtBuscarRangoInterno.IsEnabled = false;
                    }

                    ListaRangosAgregados.Add(new RangoCodigoItem
                    {
                        Cantidad = txtCantidadRango,
                        Desde = txtDesde,
                        Hasta = txtHasta,
                        ColeccionTipo = textoColeccionFinal,
                        DesdeNum = desdeN,
                        HastaNum = hastaN,
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = abrev,
                        productoId = r.productoId == 0 ? item.ProductoId : r.productoId
                    });
                }
            }

            int totalCantidadRangos = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int cant) ? cant : 0);
            txtCantidad.Text = totalCantidadRangos > 0 ? totalCantidadRangos.ToString() : (item.Detalle?.CantidadIngreso ?? 0).ToString("0");

            dgDetalleCodigos.ItemsSource = null;
            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;

            txtProducto.IsEnabled = false;
            txtCantidad.IsReadOnly = false;
        }

        #region MOTOR DE BÚSQUEDA PREDICTIVA

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string textoBusqueda = txtProducto.Text.Trim();

            if (textoBusqueda.Length < 2)
            {
                popupProductos.IsOpen = false;
                return;
            }

            try
            {
                if (_productoSeleccionado == null || _productoSeleccionado.Descripcion != txtProducto.Text)
                {
                    List<Producto> listaFiltrada = await _productoService.BuscarProductosPorTextoAsync(textoBusqueda);

                    if (listaFiltrada.Count > 0)
                    {
                        lstSugerenciasProductos.ItemsSource = listaFiltrada;
                        popupProductos.IsOpen = true;
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
                _productoSeleccionado = producto;
                txtProducto.Text = producto.Descripcion;

                txtUMedida.Text = (producto.UnidadMedida != null && !string.IsNullOrWhiteSpace(producto.UnidadMedida.Descripcion))
                    ? producto.UnidadMedida.Descripcion.ToUpperInvariant()
                    : "UNIDAD";

                txtCUnitario.Text = producto.PrecioUnitario.HasValue
                    ? producto.PrecioUnitario.Value.ToString("F2")
                    : "0.00";

                // 🌟 LÓGICA PREVENTIVA: Si el producto no usa códigos, deshabilitamos la sección inferior del modal
                if (string.IsNullOrWhiteSpace(producto.Abreviatura))
                {
                    ListaRangosAgregados.Clear();
                    dgDetalleCodigos.IsEnabled = false;
                    txtBuscarRangoInterno.IsEnabled = false;
                }
                else
                {
                    dgDetalleCodigos.IsEnabled = true;
                    txtBuscarRangoInterno.IsEnabled = true;
                }

                popupProductos.IsOpen = false;
                lstSugerenciasProductos.SelectedIndex = -1;
            }
        }

        #endregion

        #region GESTIÓN DE RANGOS MASIVOS Y GRABADO

        private void BtnAgregarRangoCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto antes de agregar códigos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🌟 CONTROL DE ESCALABILIDAD: Bloqueo para evitar colapsos visuales en ítems genéricos
            if (string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura))
            {
                MessageBox.Show("Este producto es de tipo genérico/otros (Mochilas/Cuadernos) y no requiere la asignación de rangos de códigos.", "Producto Genérico sin Códigos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida de unidades.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

            int totalCodigosYaAgregados = 0;
            foreach (var rango in listaDeRangosActual)
            {
                if (int.TryParse(rango.Cantidad, out int cantRango)) totalCodigosYaAgregados += cantRango;
            }

            if (totalCodigosYaAgregados >= cantidadCodigosEsperados)
            {
                MessageBox.Show("Ya ha registrado el total de códigos indicado.", "Lotes Completos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int cantidadFaltantePorAsignar = cantidadCodigosEsperados - totalCodigosYaAgregados;

            try
            {
                AsignarCodigoWindow ventanaCodigo = new AsignarCodigoWindow(
                    listaDeRangosActual,
                    _productoSeleccionado.Abreviatura,
                    _productoSeleccionado.Id,
                    cantidadFaltantePorAsignar
                );

                ventanaCodigo.EstadoPermitido = this.EstadoPermitido;
                ventanaCodigo.Owner = this;

                if (ventanaCodigo.ShowDialog() == true && ventanaCodigo.FueConfirmado)
                {
                    RangoCodigoItem nuevoRango = ventanaCodigo.RangoProcesado;

                    if (nuevoRango != null)
                    {
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
                            return;
                        }

                        ListaRangosAgregados.Add(nuevoRango);

                        txtCantidad.IsReadOnly = true;
                        if (txtProducto != null) txtProducto.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al invocar asignación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null || string.IsNullOrWhiteSpace(txtProducto.Text))
            {
                MessageBox.Show("Por favor, seleccione un producto válido antes de grabar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ListaProductosExistentesEnPadre != null)
            {
                bool existe = ListaProductosExistentesEnPadre.Exists(p => p.ProductoId == _productoSeleccionado.Id);
                if (existe && !(IsEdit && OriginalProductoId == _productoSeleccionado.Id))
                {
                    if (IsAddAction)
                    {
                        MessageBox.Show("Este producto ya existe en la lista. Use 'Modificar' para agregar códigos a un producto ya registrado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (ListaRangosAgregados.Count > 0)
                    {
                        MergeWithExisting = true;
                    }
                    else
                    {
                        MessageBox.Show("Este producto ya se encuentra mapeado en el documento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            if (!int.TryParse(txtCantidad.Text.Trim(), out int cantidadDeclarada) || cantidadDeclarada <= 0)
            {
                MessageBox.Show("Cantidad inválida. Debe ser un número entero mayor a 0.", "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtCUnitario.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal costoValido))
            {
                MessageBox.Show("Por favor, ingrese un costo unitario válido.", "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🌟 RESOLUCIÓN DEL DETECTOR DE PREFIJOS DINÁMICOS Y COMPORTAMIENTO HÍBRIDO
            bool esProductoSinCodigo = string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura);

            if (esProductoSinCodigo)
            {
                // Camino A: Flujo libre para Mochilas y Cuadernos (No valida cuadrante numérico)
                CantidadProductoIngresada = cantidadDeclarada;
                CostoUnitarioIngresado = costoValido;
            }
            else
            {
                // Camino B: Flujo estricto para Libros con códigos de barra/QR
                if (ListaRangosAgregados.Count == 0)
                {
                    MessageBox.Show("Los productos con código (libros) requieren que registre al menos un rango correlativo en la tabla.", "Validación de Lote", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int totalCodigosUnicosRegistrados = 0;
                foreach (var rango in ListaRangosAgregados)
                {
                    if (int.TryParse(rango.Cantidad, out int cantidadDelRango)) totalCodigosUnicosRegistrados += cantidadDelRango;
                }

                if (cantidadDeclarada != totalCodigosUnicosRegistrados)
                {
                    MessageBox.Show($"Inconsistencia de Códigos Únicos ❌\n\n" +
                                    $"En la cantidad del producto indicó: {cantidadDeclarada} unidades.\n" +
                                    $"Sin embargo, la suma de los códigos en los rangos agregados es de: {totalCodigosUnicosRegistrados} códigos.\n\n" +
                                    $"Por favor, configure los rangos para que la cantidad total coincida perfectamente con la cantidad física declarada.",
                                    "Error de Cuadrante Numérico", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                CantidadProductoIngresada = cantidadDeclarada;
                CostoUnitarioIngresado = costoValido;
            }

            FueGrabado = true;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnEliminarRango_Click(object sender, RoutedEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem rangoSeleccionado)
            {
                var confirmacion = MessageBox.Show("¿Está seguro de que desea eliminar este rango de códigos?",
                                                   "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmacion == MessageBoxResult.Yes)
                {
                    StopEdit(); // Forzar el fin de hilos activos de edición en la celda
                    ListaRangosAgregados.Remove(rangoSeleccionado);

                    if (ListaRangosAgregados.Count == 0)
                    {
                        txtCantidad.IsReadOnly = false;
                        txtProducto.IsEnabled = true;
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una fila de la grilla para eliminar.", "Selección necesaria", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void StopEdit()
        {
            try
            {
                dgDetalleCodigos.CancelEdit(DataGridEditingUnit.Cell);
                dgDetalleCodigos.CancelEdit(DataGridEditingUnit.Row);
            }
            catch { }
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
            if (dgDetalleCodigos.SelectedItem is not RangoCodigoItem rangoSeleccionado)
            {
                MessageBox.Show("Por favor, seleccione un rango de la tabla para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida de unidades antes de modificar un rango.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

            int totalCodigosYaAgregados = 0;
            int cantidadDelItemSeleccionado = 0;
            foreach (var rango in listaDeRangosActual)
            {
                if (int.TryParse(rango.Cantidad, out int cantRango)) totalCodigosYaAgregados += cantRango;

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
                ventanaEdicion.EstadoPermitido = this.EstadoPermitido;
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