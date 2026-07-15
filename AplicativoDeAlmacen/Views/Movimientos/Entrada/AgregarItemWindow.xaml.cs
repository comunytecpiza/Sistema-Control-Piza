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
        // Indica que la ventana fue abierta desde la acción "Agregar Ítem" (nuevo producto)
        // Cuando es true, no permitimos hacer "merge" con un producto ya existente — eso debe hacerse desde Modificar.
        public bool IsAddAction { get; set; } = false;

        // Estado permitido para los códigos cuando se asignan rangos desde este formulario.
        // Debe ser establecido por el flujo que abre esta ventana según el motivo seleccionado
        // (1 = COMPRA, otro => 4). Por defecto 1 para compatibilidad.
        public int EstadoPermitido { get; set; } = 1;

        // Cuando se abre la ventana en modo edición, permitimos saltar la validación de duplicados
        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;
        // Si verdadero, indica que el usuario quiere añadir los códigos al producto ya existente
        public bool MergeWithExisting { get; private set; } = false;

        public decimal CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        public Producto _productoSeleccionado = null;
        public ObservableCollection<RangoCodigoItem> ListaRangosAgregados { get; private set; }
        // Esta lista servirá de "espejo" para saber qué ya se registró en el padre
        public List<VistaProductoGrid> ListaProductosExistentesEnPadre { get; set; }

        // Abre tu AgregarItemWindow.xaml.cs y verifica que tu constructor asigne la fuente de esta manera:
        public AgregarItemWindow()
        {
            InitializeComponent();

            _productoService = new ProductoService();
            ListaRangosAgregados = new ObservableCollection<RangoCodigoItem>();

            // Vincular la colección al DataGrid para evitar operaciones sobre la vista
            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;

            // Permitir editar al hacer doble click en una fila
            dgDetalleCodigos.MouseDoubleClick += DgDetalleCodigos_MouseDoubleClick;

            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;

            // LÓGICA DE BÚSQUEDA EN TIEMPO REAL PARA DETALLE DE CÓDIGOS ÚNICOS
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

        /// <summary>
        /// Inicializa la ventana para edición de un producto ya agregado.
        /// </summary>
        public void InitializeForEdit(VistaProductoGrid item, List<RangoCodigoItem> rangos)
        {
            if (item == null) return;

            IsEdit = true;
            OriginalProductoId = item.ProductoId;

            _productoSeleccionado = new Producto
            {
                Id = item.ProductoId,
                Descripcion = item.Descripcion,
                PrecioUnitario = item.Detalle?.CostoUnitario
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
            txtUMedida.Text = "UNIDAD";
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

                    // 🌟 DETECCIÓN DE FLUJO: Si DesdeNum es -1, es un alfanumérico puro sin secuencia
                    if (desdeN == -1)
                    {
                        txtDesde = abrev; // Usamos el código alfanumérico limpio directo
                        txtHasta = abrev;
                    }
                    else
                    {
                        // Si es secuencial con guiones, mantiene el relleno de ceros estándar de 7 dígitos
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

            // Calcular el total real reflejado por los rangos corregidos
            int totalCantidadRangos = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int cant) ? cant : 0);
            txtCantidad.Text = totalCantidadRangos > 0 ? totalCantidadRangos.ToString() : (item.Detalle?.CantidadIngreso ?? 0m).ToString("0");

            // 🌟 REFRESH SEGURO PARA EL CONTROL VISUAL
            dgDetalleCodigos.ItemsSource = null;
            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;

            txtProducto.IsEnabled = false;
            txtCantidad.IsReadOnly = false;
        }


        #region MOTOR DE BÚSQUEDA PREDICTIVA

        // 🌟 1. Se agrega 'async' a la declaración del evento
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
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
                    // 🌟 2. Se cambia el nombre del método y se usa 'await'
                    List<Producto> listaFiltrada = await _productoService.BuscarProductosPorTextoAsync(textoBusqueda);

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
                // Propagamos el estado permitido para que la ventana de asignación valide correctamente
                ventanaCodigo.EstadoPermitido = this.EstadoPermitido;
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
                    // Si la ventana fue invocada desde la acción 'Agregar Ítem' no permitimos hacer merge
                    if (IsAddAction)
                    {
                        MessageBox.Show("Este producto ya existe en la lista. Use 'Modificar' para agregar códigos a un producto ya registrado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Si el usuario ha agregado rangos/códigos en esta ventana, permitimos
                    // que se añadan esos códigos al producto ya existente (modo merge).
                    if (ListaRangosAgregados.Count > 0)
                    {
                        MergeWithExisting = true;
                        // continuar y cerrar normalmente; el padre deberá integrar los rangos al producto existente
                    }
                    else
                    {
                        MessageBox.Show("Este producto ya existe.", "Aviso");
                        return; // No se cierra
                    }
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
                        // Permitimos que el usuario modifique la cantidad para poder agregar nuevos rangos
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
                // Propagamos el estado permitido para edición
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