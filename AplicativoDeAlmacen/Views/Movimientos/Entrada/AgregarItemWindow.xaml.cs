using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarItemWindow : Window
    {
        public bool IsAddAction { get; set; } = false;
        public int EstadoPermitido { get; set; } = 1; // 1 = COMPRA, 4 = DEVOLUCIÓN/VENTA

        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;
        public bool MergeWithExisting { get; private set; } = false;

        public int CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        public Producto _productoSeleccionado = null;
        public ObservableCollection<RangoCodigoItem> ListaRangosAgregados { get; private set; }
        public List<VistaProductoGrid> ListaProductosExistentesEnPadre { get; set; }

        public AgregarItemWindow()
        {
            InitializeComponent();

            _productoService = new ProductoService();
            ListaRangosAgregados = new ObservableCollection<RangoCodigoItem>();

            
            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
            dgDetalleCodigos.MouseDoubleClick += DgDetalleCodigos_MouseDoubleClick;

            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;

            // 🌟 La caja de cantidad SIEMPRE permanece editable para el operador
            txtCantidad.IsReadOnly = false;

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

        private void RecalcularCantidadTotalEnVivo()
        {
            // 🛡️ REGLA: Si el usuario ya digitó una cantidad manual mayor a 0 (ej. 10), NO la pisamos.
            if (int.TryParse(txtCantidad.Text, out int actual) && actual > 0)
            {
                return;
            }

            if (ListaRangosAgregados != null && ListaRangosAgregados.Any())
            {
                int sumaTotal = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
                txtCantidad.Text = sumaTotal.ToString();
            }
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

            if (rangos != null && rangos.Any())
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

            // 🌟 Mantiene la cantidad original del item sin ser pisada por los rangos parciales
            decimal cantidadOriginal = item.Detalle != null ? (item.Detalle.CantidadIngreso > 0 ? item.Detalle.CantidadIngreso : item.Detalle.CantidadSalida) : item.Cantidad;
            txtCantidad.Text = cantidadOriginal > 0 ? Convert.ToInt32(cantidadOriginal).ToString() : ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int cant) ? cant : 0).ToString();

            txtCantidad.IsReadOnly = false;
            txtProducto.IsEnabled = false;
        }

        #region BUSCADOR DE PRODUCTOS

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
                MessageBox.Show($"Error al buscar el producto:\n{ex.Message}", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        #region ASIGNACIÓN Y MODIFICACIÓN DE RANGOS

        private void BtnAgregarRangoCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto antes de agregar códigos.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura))
            {
                MessageBox.Show("Este producto es genérico (sin series de códigos).", "Producto Genérico", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese primero la cantidad de unidades arriba.", "Cantidad Vacía", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCantidad.Focus();
                return;
            }

            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);
            int totalCodigosYaAgregados = listaDeRangosActual.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
            int cantidadFaltantePorAsignar = cantidadCodigosEsperados - totalCodigosYaAgregados;

            if (cantidadFaltantePorAsignar <= 0)
            {
                MessageBox.Show($"Ya ha asignado el total de la cantidad digitada ({cantidadCodigosEsperados} unidades).", "Lote Completo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                AsignarCodigoWindow ventanaCodigo = new AsignarCodigoWindow(
                    listaDeRangosActual,
                    _productoSeleccionado.Abreviatura,
                    _productoSeleccionado.Id,
                    cantidadFaltantePorAsignar
                )
                {
                    EstadoPermitido = this.EstadoPermitido,
                    Owner = this
                };

                if (ventanaCodigo.ShowDialog() == true && ventanaCodigo.FueConfirmado)
                {
                    // 🛡️ BLOQUEO DE SEGURIDAD CONTRA CLICS EN FALSO
                    this.Cursor = Cursors.Wait;
                    btnGrabar.IsEnabled = false; // Bloquea guardar mientras procesa el cambio

                    RangoCodigoItem nuevoRango = ventanaCodigo.RangoProcesado;
                    if (nuevoRango != null)
                    {
                        ListaRangosAgregados.Add(nuevoRango);
                    }

                    // ⚡ REFRESCO INSTANTÁNEO FORZADO
                    dgDetalleCodigos.Items.Refresh();
                    RecalcularCantidadTotalEnVivo();

                    // Liberamos el bloqueo de inmediato
                    btnGrabar.IsEnabled = true;
                    this.Cursor = Cursors.Arrow;
                }
            }
            catch (Exception ex)
            {
                this.Cursor = Cursors.Arrow;
                btnGrabar.IsEnabled = true;
                MessageBox.Show($"Error al abrir la ventana de códigos:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null || string.IsNullOrWhiteSpace(txtProducto.Text))
            {
                MessageBox.Show("Por favor, seleccione un producto antes de guardar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtCantidad.Text.Trim(), out int cantidadDeclarada) || cantidadDeclarada <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida mayor a 0.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtCUnitario.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal costoValido))
            {
                MessageBox.Show("Ingrese un costo unitario válido.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool esProductoSinCodigo = string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura);

            if (!esProductoSinCodigo)
            {
                if (ListaRangosAgregados.Count == 0)
                {
                    MessageBox.Show("Este producto utiliza códigos únicos. Debe presionar '➕ Agregar Rango' para asignar los códigos.", "Códigos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 🌟 SUMA TOTAL DE CÓDIGOS AGREGADOS ABAJO EN LA TABLA
                int totalCodigosUnicosRegistrados = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);

                // 🛡️ CANDADO DE DESCUADRE (Compara Cantidad General vs Suma de Rangos):
                if (cantidadDeclarada != totalCodigosUnicosRegistrados)
                {
                    string detalleDiferencia = cantidadDeclarada < totalCodigosUnicosRegistrados
                        ? $"Tiene MÁS CÓDIGOS en la lista ({totalCodigosUnicosRegistrados}) de los indicados en la Cantidad General ({cantidadDeclarada})."
                        : $"Tiene MENOS CÓDIGOS en la lista ({totalCodigosUnicosRegistrados}) de los indicados en la Cantidad General ({cantidadDeclarada}).";

                    MessageBox.Show(
                        $"⚠️ Descuadre en la Cantidad General:\n\n" +
                        $"{detalleDiferencia}\n\n" +
                        $"• Cantidad General arriba: {cantidadDeclarada} unidades.\n" +
                        $"• Suma de códigos abajo: {totalCodigosUnicosRegistrados} códigos.\n\n" +
                        $"Por favor, corrija la Cantidad General arriba o ajuste las series en la lista para que coincidan exactamente.",
                        "Inconsistencia de Unidades",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return; // 🛑 Cancela el guardado
                }
            }

            CantidadProductoIngresada = cantidadDeclarada;
            CostoUnitarioIngresado = costoValido;

            FueGrabado = true;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnEliminarRango_Click(object sender, RoutedEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem rangoSeleccionado)
            {
                if (MessageBox.Show("¿Desea quitar este rango de la lista?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ListaRangosAgregados.Remove(rangoSeleccionado);
                }
            }
            else
            {
                MessageBox.Show("Seleccione la fila que desea eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            FueGrabado = false;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnModificarRango_Click(object sender, RoutedEventArgs e) { EditarRangoSeleccionado(); }

        private void DgDetalleCodigos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem) EditarRangoSeleccionado();
        }

        private void EditarRangoSeleccionado()
        {
            if (dgDetalleCodigos.SelectedItem is not RangoCodigoItem rangoSeleccionado)
            {
                MessageBox.Show("Seleccione el rango que desea modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int.TryParse(txtCantidad.Text, out int cantidadTotalArriba);
            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

            int sumaOtrosRangos = listaDeRangosActual.Where(r => r != rangoSeleccionado).Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
            int disponibleParaEsteRango = cantidadTotalArriba - sumaOtrosRangos;

            try
            {
                AsignarCodigoWindow ventanaEdicion = new AsignarCodigoWindow(listaDeRangosActual, rangoSeleccionado, disponibleParaEsteRango > 0 ? disponibleParaEsteRango : 0)
                {
                    EstadoPermitido = this.EstadoPermitido,
                    Owner = this
                };

                if (ventanaEdicion.ShowDialog() == true && ventanaEdicion.FueConfirmado)
                {
                    // 🛡️ BLOQUEO DE SEGURIDAD CONTRA CLICS EN FALSO
                    this.Cursor = Cursors.Wait;
                    btnGrabar.IsEnabled = false; // Evita que guarden a medias

                    RangoCodigoItem rangoModificado = ventanaEdicion.RangoProcesado;
                    int index = ListaRangosAgregados.IndexOf(rangoSeleccionado);
                    if (index >= 0)
                    {
                        ListaRangosAgregados[index] = rangoModificado;
                    }

                    // ⚡ REFRESCO INSTANTÁNEO FORZADO EN MEMORIA
                    dgDetalleCodigos.Items.Refresh();
                    RecalcularCantidadTotalEnVivo();

                    // Restauramos los controles al instante
                    btnGrabar.IsEnabled = true;
                    this.Cursor = Cursors.Arrow;
                }
            }
            catch (Exception ex)
            {
                this.Cursor = Cursors.Arrow;
                btnGrabar.IsEnabled = true;
                MessageBox.Show($"Error al editar el rango:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}