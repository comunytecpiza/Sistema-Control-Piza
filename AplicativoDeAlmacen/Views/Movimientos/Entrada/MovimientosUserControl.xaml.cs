using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views
{
    public partial class MovimientosUserControl : UserControl
    {
        private int? _currentMovimientoId = null;
        private readonly PersonaComercialService _service;
        private readonly IngresoMovimientoService _serviceMovimiento;
        private readonly UbicacionService _ubicacionService;
        private readonly ReporteExcelService _reporteService; // 🌟 NUEVO SERVICIO AÑADIDO

        private List<VistaProductoGrid> _productosGridList;
        private List<VistaCodigoGrid> _codigosGridList;
        private List<RangoCodigoItem> _rangosProcesadosGlobal;

        private bool _isUpdatingFromSelection = false;
        private int? _personaComercialIdSeleccionada = null;
        private const int UBICACION_ID_SELECCIONADA = 1;
        private bool _printMode = false;
        private Button _btnPrintNearSave = null;

        public MovimientosUserControl()
        {
            _productosGridList = new List<VistaProductoGrid>();
            _codigosGridList = new List<VistaCodigoGrid>();
            _rangosProcesadosGlobal = new List<RangoCodigoItem>();

            _service = new PersonaComercialService();
            _serviceMovimiento = new IngresoMovimientoService();
            _ubicacionService = new UbicacionService();
            _reporteService = new ReporteExcelService(); // 🌟 INICIALIZAMOS

            InitializeComponent();

            ConfigurarEventosIniciales();
            EstablecerEstadoInicial();
        }

        public void ConfigurarEventosIniciales()
        {
            txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += LstSugerencias_SelectionChanged;
            cboMotivo.SelectionChanged += CboMotivo_SelectionChanged;
            lstSugerenciasUbicacion.SelectionChanged += LstSugerenciasUbicacion_SelectionChanged;
            this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
            this.PreviewMouseMove += MovimientosUserControl_PreviewMouseMove;
            Loaded += MovimientosUserControl_Loaded;

            btnAgregar.Click += BtnAgregar_Click;
            btnEditar.Click += BtnEditar_Click;
            btnAgregarProducto.Click += BtnAgregarItem_Click;
            btnModificar.Click += BtnModificar_Click;
            btnEliminar.Click += BtnEliminar_Click;
            btnCancelar.Click += BtnCancelar_Click;
            btnGrabar.Click += RegistrarMovimientoCompleto;
            btnImprimir.Click += BtnImprimir_Click;
            btnImportar.Click += BtnImportar_Click;

            dgProductos.SelectionChanged += DgProductos_SelectionChanged;
            dgProductos.MouseDoubleClick += DgProductos_MouseDoubleClick;

            if (dgProductos != null) dgProductos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
            if (dgCodigos != null) dgCodigos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
        }

        private void EstablecerEstadoInicial()
        {
            LimpiarFormulario();
            HabilitarCamposFormulario(false);
            GestionarBotonesPrincipales(enEdicion: false);
        }

        private async void MovimientosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigurarDataGridsParaVirtualizacion();
            await CargarMotivosAsync();
        }

        private async Task CargarMotivosAsync()
        {
            try
            {
                this.Cursor = Cursors.Wait;
                cboMotivo.ItemsSource = await _serviceMovimiento.ObtenerMotivosProductosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los motivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { this.Cursor = Cursors.Arrow; }
        }

        // ==========================================
        // MANEJO DE VISTA Y LIMPIEZA
        // ==========================================

        private void LimpiarFormulario()
        {
            _isUpdatingFromSelection = true;
            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            txtNumSerie.Clear();
            txtNumDocumento.Clear();
            dtpFechaRecepcion.SelectedDate = null;
            cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Clear();
            txtCodigoRazonSocial.Clear();
            txtDireccion.Clear();
            txtUbicacion.Clear();
            txtCodigoUbicacion.Clear();
            txtDireccionUbicacion.Clear();
            txtObservacion.Clear();
            txtSerieGuia.Clear();
            txtNumeroGuia.Clear();

            dgProductos.ItemsSource = null;
            dgCodigos.ItemsSource = null;

            _personaComercialIdSeleccionada = null;
            _isUpdatingFromSelection = false;
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            txtNumSerie.IsEnabled = false;
            txtNumDocumento.IsEnabled = false;
            txtCodigoRazonSocial.IsEnabled = false;
            txtDireccion.IsEnabled = false;

            dtpFechaRecepcion.IsEnabled = habilitar;
            cboMotivo.IsEnabled = habilitar;
            txtRazonSocial.IsEnabled = habilitar;
            txtObservacion.IsEnabled = habilitar;
            txtSerieGuia.IsEnabled = habilitar;
            txtNumeroGuia.IsEnabled = habilitar;

            if (btnModificar != null) btnModificar.IsEnabled = habilitar;
            if (btnEliminar != null) btnEliminar.IsEnabled = habilitar;
            if (btnImportar != null) btnImportar.IsEnabled = habilitar;
            if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = habilitar;
            if (btnGrabar != null) btnGrabar.IsEnabled = habilitar;
            if (btnCancelar != null) btnCancelar.IsEnabled = habilitar;

            if (dgProductos != null) { dgProductos.IsEnabled = habilitar; dgProductos.IsReadOnly = !habilitar; }
            if (dgCodigos != null) { dgCodigos.IsEnabled = habilitar; dgCodigos.IsReadOnly = true; }
        }

        private void GestionarBotonesPrincipales(bool enEdicion)
        {
            btnAgregar.IsEnabled = !enEdicion;
            btnEditar.IsEnabled = !enEdicion;
            btnImprimir.IsEnabled = !enEdicion;
            btnAnular.IsEnabled = !enEdicion;
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Lógica para filtrar el diccionario en el siguiente paso
            string filtro = txtBuscarCodigo.Text.ToLower();
            // Aquí filtraremos la colección y actualizaremos dgCodigos.ItemsSource
        }

        // ==========================================
        // LÓGICA DE BOTONES PRINCIPALES
        // ==========================================

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                LimpiarFormulario();

                txtNumSerie.Text = string.Empty;
                txtNumDocumento.Text = string.Empty;
                txtNumSerie.Visibility = Visibility.Visible;
                txtNumDocumento.Visibility = Visibility.Visible;

                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                HabilitarCamposFormulario(false);

                if (dtpFechaRecepcion != null) dtpFechaRecepcion.IsEnabled = true;
                if (cboMotivo != null) cboMotivo.IsEnabled = true;
                if (txtObservacion != null) txtObservacion.IsEnabled = true;
                if (txtSerieGuia != null) txtSerieGuia.IsEnabled = true;
                if (txtNumeroGuia != null) txtNumeroGuia.IsEnabled = true;

                if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = true;
                if (btnImportar != null) btnImportar.IsEnabled = true;
                if (btnGrabar != null) btnGrabar.IsEnabled = true;
                if (btnCancelar != null) btnCancelar.IsEnabled = true;

                if (dgProductos != null) { dgProductos.IsEnabled = true; dgProductos.IsReadOnly = false; }
                if (dgCodigos != null) { dgCodigos.IsEnabled = true; dgCodigos.IsReadOnly = true; }

                GestionarBotonesPrincipales(enEdicion: true);
                try { cboMotivo.SelectedValue = 1; } catch { }
                cboMotivo.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar el nuevo registro: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { this.Cursor = Cursors.Arrow; }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var resultado = MessageBox.Show("¿Está seguro que desea cancelar la operación actual?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resultado == MessageBoxResult.Yes)
            {
                if (_printMode) { try { CleanupPrintMode(); } catch { _printMode = false; } }
                LimpiarFormulario();
                HabilitarCamposFormulario(false);
                GestionarBotonesPrincipales(enEdicion: false);
                if (btnCancelar != null) btnCancelar.IsEnabled = false;
            }
        }

        private async void RegistrarMovimientoCompleto(object sender, RoutedEventArgs e)
        {
            // 1. Validaciones básicas
            if (_productosGridList == null || _productosGridList.Count == 0)
            {
                MessageBox.Show("Debe agregar al menos un producto.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btnGrabar.IsEnabled = false;
                this.Cursor = Cursors.Wait;

                // 2. Construcción del objeto cabecera
                Movimiento nuevaCabecera = new Movimiento
                {
                    FechaMovimiento = dtpFechaRecepcion.SelectedDate != null ? DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Today),
                    SerieDocumento = txtNumSerie.Text.Trim(),
                    NumeroDocumento = txtNumDocumento.Text.Trim(),
                    MotivoProductoId = Convert.ToInt32(cboMotivo.SelectedValue),
                    UbicacionId = UBICACION_ID_SELECCIONADA,
                    UsuarioId = 1,
                    PersonaComercialId = _personaComercialIdSeleccionada,
                    SerieGuia = txtSerieGuia.Text.Trim(),
                    NumeroGuia = txtNumeroGuia.Text.Trim(),
                    Observacion = txtObservacion.Text.Trim()
                };

                // 3. Sincronización final de datos (Asegurar que los objetos tengan los IDs correctos)
                foreach (var p in _productosGridList)
                {
                    if (p.Detalle == null) p.Detalle = new MovimientoDetalle { ProductoId = p.ProductoId };
                    p.Detalle.CantidadIngreso = p.Cantidad;
                    if (_currentMovimientoId.HasValue) p.Detalle.MovimientoId = _currentMovimientoId.Value;
                }

                // 4. Generación de rangos (Algoritmo que extrajimos al servicio)
                _rangosProcesadosGlobal = await _serviceMovimiento.GenerarRangosDesdeCodigosAsync(_codigosGridList);

                // 5. LLAMADA AL SERVICIO (AQUÍ ESTÁ LA CORRECCIÓN)
                // Pasamos _productosGridList tal cual, ya que el servicio debe ser capaz de iterar su Detalle
                bool exito = await _serviceMovimiento.RegistrarMovimientoCompletoAsync(
                    nuevaCabecera,
                    _productosGridList,
                    _rangosProcesadosGlobal,
                    UBICACION_ID_SELECCIONADA,
                    _currentMovimientoId
                );

                if (exito)
                {
                    MessageBox.Show("Registro procesado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    LimpiarFormulario();
                    HabilitarCamposFormulario(false);
                    GestionarBotonesPrincipales(enEdicion: false);
                }
            }
            catch (Exception ex)
            {
                // 🌟 AQUÍ ESTÁ EL CULPABLE DEL ERROR QUE VES:
                // Si la validación de estados falla en el servicio, atrapamos el error aquí.
                MessageBox.Show($"Error al guardar: {ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                btnGrabar.IsEnabled = true;
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        // ==========================================
        // LÓGICA DE LECTORA Y PRODUCTOS
        // ==========================================

        private void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            var lector = new LectorGlobalWindow(async resultado =>
            {
                bool seAgregoConExito = await ProcesarCodigoEscaneadoAsync(resultado);
                if (seAgregoConExito)
                {
                    Application.Current.Dispatcher.Invoke(() => { RefrescarGrillas(); });
                }
                return seAgregoConExito;
            });

            lector.Owner = Window.GetWindow(this);
            lector.ShowDialog();
            RefrescarGrillas();
        }

        private void RefrescarGrillas()
        {
            SincronizarCantidadesConCodigos();
            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
            dgCodigos.ItemsSource = null;

            if (dgProductos.SelectedItem is VistaProductoGrid seleccionado)
            {
                dgCodigos.ItemsSource = _codigosGridList.Where(c => c.ProductoId == seleccionado.ProductoId).ToList();
            }
            else
            {
                dgCodigos.ItemsSource = _codigosGridList.ToList();
            }
        }

        private async Task<bool> ProcesarCodigoEscaneadoAsync(AplicativoDeAlmacen.Models.Facturación.LectoraResultDTO resultado)
        {
            if (resultado.EstadoId == 3) return false;
            if (_codigosGridList.Any(x => x.CodigoUnique == resultado.CodigoCompleto)) return false;

            // 🌟 LÓGICA DE BD DELEGADA AL SERVICIO
            string tipoBD = await _serviceMovimiento.ObtenerColeccionTipoBDAsync(resultado.CodigoCreadoId);

            _codigosGridList.Add(new VistaCodigoGrid
            {
                ProductoId = resultado.ProductoId,
                CodigoUnique = resultado.CodigoCompleto,
                ColeccionTipo = tipoBD,
                MovCodigo = new MovimientoCodigo { CodigoCreadoId = resultado.CodigoCreadoId }
            });

            var producto = _productosGridList.FirstOrDefault(p => p.ProductoId == resultado.ProductoId);
            if (producto != null)
            {
                producto.Cantidad++;
                if (producto.Detalle != null) producto.Detalle.CantidadIngreso++;
            }
            else
            {
                _productosGridList.Add(new VistaProductoGrid
                {
                    ProductoId = resultado.ProductoId,
                    CodigoProducto = resultado.ProductoId.ToString(),
                    Descripcion = resultado.DescripcionProducto,
                    UnidadMedida = "UNIDAD",
                    Cantidad = 1,
                    Detalle = new MovimientoDetalle { ProductoId = resultado.ProductoId, CantidadIngreso = 1, CostoUnitario = resultado.PrecioUnitario }
                });
            }
            return true;
        }

        private void SincronizarCantidadesConCodigos()
        {
            int i = 1;
            foreach (var codigo in _codigosGridList) codigo.NumeroFila = i++;

            foreach (var producto in _productosGridList)
            {
                int count = _codigosGridList.Count(c => c.ProductoId == producto.ProductoId);
                producto.Cantidad = count;
                if (producto.Detalle != null) producto.Detalle.CantidadIngreso = count;
            }
        }

        // ==========================================
        // EVENTOS DE LA UI RESTANTES (Sin modificar lógica interna)
        // ==========================================

        private void CboMotivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMotivo.SelectedItem == null)
                return;

            // Obtén la descripción del motivo
            dynamic motivo = cboMotivo.SelectedItem;
            string descripcion = motivo.Descripcion?.Trim().ToUpper() ?? "";

            // Primero deshabilitamos todo
            txtRazonSocial.IsEnabled = false;
            txtUbicacion.IsEnabled = false;

            // Limpiamos valores opcionalmente
            txtRazonSocial.Clear();
            txtCodigoRazonSocial.Clear();
            txtDireccion.Clear();

            txtUbicacion.Clear();
            txtCodigoUbicacion.Clear();
            txtDireccionUbicacion.Clear();

            switch (descripcion)
            {
                case "COMPRA":
                    txtRazonSocial.IsEnabled = true;
                    break;

                case "DEVOLUCION RECIBIDA":
                    txtRazonSocial.IsEnabled = true;
                    break;

                case "OTROS":
                    txtRazonSocial.IsEnabled = true;
                    txtUbicacion.IsEnabled = true;
                    break;

                case "PROMOCION/PROMOTORIA":
                    // En PROMOCION/PROMOTORIA solo se habilita la ubicación (no razón social)
                    txtRazonSocial.IsEnabled = false;
                    txtUbicacion.IsEnabled = true;
                    break;

                case "TRANSFERENCIA ENTRE ALMACENES":
                    txtUbicacion.IsEnabled = true;
                    break;
            }
        }

        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtRazonSocial.IsEnabled)
                return;

            if (_isUpdatingFromSelection)
                return;


            if (_isUpdatingFromSelection) return;
            string textoBusqueda = txtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    List<PersonaComercial> sugerencias = await _service.BuscarPorRazonSocialAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        lstSugerencias.ItemsSource = sugerencias;
                        popupSugerencias.IsOpen = true;
                    }
                    else
                    {
                        popupSugerencias.IsOpen = false;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al consultar sugerencias: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                popupSugerencias.IsOpen = false;
            }
        }

        private void LstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerencias.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true;

                _personaComercialIdSeleccionada = personaSeleccionada.Id;
                txtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                  ? personaSeleccionada.RazonSocial
                  : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";

                txtCodigoRazonSocial.Text = personaSeleccionada.Id.ToString("D6");
                txtDireccion.Text = personaSeleccionada.Direccion ?? "Sin dirección registrada";

                popupSugerencias.IsOpen = false;
                lstSugerencias.SelectedIndex = -1;

                _isUpdatingFromSelection = false;
            }
        }

        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtUbicacion.IsEnabled)
                return;


            string busqueda = txtUbicacion.Text;

            if (string.IsNullOrWhiteSpace(busqueda))
            {
                popupUbicacion.IsOpen = false;
                return;
            }

            // Aquí llamas a tu servicio de base de datos
            // Supongamos que tienes un método 'BuscarUbicaciones(string criterio)'
            var resultados = _ubicacionService.BuscarUbicaciones(busqueda);

            if (resultados != null && resultados.Count > 0)
            {
                lstSugerenciasUbicacion.ItemsSource = resultados;
                popupUbicacion.IsOpen = true;
            }
            else
            {
                popupUbicacion.IsOpen = false;
            }
        }

        private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado)
            {
                txtUbicacion.Text = itemSeleccionado.Descripcion;
                txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString(); // Autocompleta el código
                popupUbicacion.IsOpen = false;
            }
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 1. Verificamos si realmente se seleccionó algo en la grilla izquierda
            if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                // 2. LIMPIEZA: Rompemos el origen de datos para limpiar la grilla de la derecha de forma segura
                dgCodigos.ItemsSource = null;

                // 3. FILTRADO: Buscamos en la lista global '_codigosGridList' los códigos que tengan el mismo ProductoId
                var codigosDelProducto = _codigosGridList
                  .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                  .ToList();

                // 4. CARGA: Asignamos la lista filtrada directamente al ItemsSource
                dgCodigos.ItemsSource = codigosDelProducto;
                // Habilitar acciones contextuales cuando hay un producto seleccionado
                if (btnModificar != null) btnModificar.IsEnabled = true;
                if (btnEliminar != null) btnEliminar.IsEnabled = true;
                if (btnImportar != null) btnImportar.IsEnabled = true; // permitir importar cuando hay selección
            }
            else
            {
                // Si no hay ningún producto seleccionado, la tabla de la derecha se queda vacía
                dgCodigos.ItemsSource = null;
                // Deshabilitar acciones contextuales
                if (btnModificar != null) btnModificar.IsEnabled = false;
                if (btnEliminar != null) btnEliminar.IsEnabled = false;
                // Importar queda habilitado si hay productos en la lista global (permitir import masivo)
                if (btnImportar != null) btnImportar.IsEnabled = (_productosGridList != null && _productosGridList.Count > 0);
            }
        }

        private async void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            await EditSelectedProductAsync();
        }


        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                var confirmacion = MessageBox.Show($"¿Está seguro de eliminar el producto \"{productoSeleccionado.Descripcion}\" y todos sus códigos asociados?",
                                 "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmacion == MessageBoxResult.Yes)
                {
                    // 1. Borramos el producto de la grilla principal
                    _productosGridList.Remove(productoSeleccionado);

                    // 2. Borramos todos los códigos físicos asociados a este producto
                    var codigosAEliminar = _codigosGridList.Where(c => c.ProductoId == productoSeleccionado.ProductoId).ToList();
                    foreach (var c in codigosAEliminar)
                    {
                        _codigosGridList.Remove(c);
                    }

                    // 3. Borramos todos los rangos asociados a este producto
                    var rangosAEliminar = _rangosProcesadosGlobal.Where(r => r.productoId == productoSeleccionado.ProductoId).ToList();
                    foreach (var r in rangosAEliminar)
                    {
                        _rangosProcesadosGlobal.Remove(r);
                    }

                    // Refrescamos las grillas
                    dgProductos.ItemsSource = null;
                    dgProductos.ItemsSource = _productosGridList;
                    dgCodigos.ItemsSource = null;
                    dgCodigos.ItemsSource = _codigosGridList;
                }
            }
            else
            {
                MessageBox.Show("Debe seleccionar un producto de la lista para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            AgregarItemWindow modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            // Indicar al modal que fue abierto desde la acción "Agregar Ítem" (nuevo producto)
            modal.IsAddAction = true;
            // Propagar estado permitido según el motivo seleccionado (1 = COMPRA, otro = 4)
            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
            modal.ListaProductosExistentesEnPadre = _productosGridList;
            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados ?? new System.Collections.ObjectModel.ObservableCollection<RangoCodigoItem>();
                if (productoSelected == null)
                {
                    MessageBox.Show("No se seleccionó un producto válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int idProducto = productoSelected.Id;

                // VALIDACIÓN DE SOLAPAMIENTO DE RANGOS: se aplica siempre sobre los rangos propuestos
                foreach (var nuevoRango in rangosDelModal)
                {
                    var solapamiento = _rangosProcesadosGlobal.FirstOrDefault(r =>
                      r.productoId == idProducto &&
                      (nuevoRango.DesdeNum <= r.HastaNum && nuevoRango.HastaNum >= r.DesdeNum)
                    );

                    if (solapamiento != null)
                    {
                        MessageBox.Show($"Conflicto de rangos para el producto: {productoSelected.Descripcion}.\n" +
                                $"El rango propuesto ({nuevoRango.DesdeNum}-{nuevoRango.HastaNum}) " +
                                $"se solapa con el rango ya registrado ({solapamiento.DesdeNum}-{solapamiento.HastaNum}).",
                                "Error de Rangos", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Si el modal indica que se debe MERGEAR con un producto ya existente, hacemos la fusión
                if (modal.MergeWithExisting)
                {
                    var existente = _productosGridList.FirstOrDefault(p => p.ProductoId == idProducto);
                    if (existente != null)
                    {
                        // Actualizar cantidad y costo (sumar cantidades de los rangos añadidos)
                        decimal sumaNuevos = modal.CantidadProductoIngresada;
                        existente.Detalle = existente.Detalle ?? new MovimientoDetalle { ProductoId = existente.ProductoId };
                        existente.Detalle.CantidadIngreso += sumaNuevos;
                        existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                        // Agregar rangos y códigos
                        int contadorFila = _codigosGridList.Count + 1;
                        foreach (var rango in rangosDelModal)
                        {
                            rango.productoId = idProducto;
                            _rangosProcesadosGlobal.Add(rango);
                            for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                            {
                                _codigosGridList.Add(new VistaCodigoGrid
                                {
                                    MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                    CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                    ColeccionTipo = rango.ColeccionTipo,
                                    ProductoId = idProducto
                                });
                            }
                        }

                        // Refrescar UI
                        dgProductos.ItemsSource = null;
                        dgProductos.ItemsSource = _productosGridList;
                        dgProductos.SelectedItem = existente;
                        dgCodigos.ItemsSource = null;
                        dgCodigos.ItemsSource = _codigosGridList;
                        return;
                    }
                    // si no existe ya, proceder como nuevo (caída elegida)
                }

                // Si no se indica merge y el producto ya existe -> rechazar la creación aquí
                bool yaExiste = _productosGridList.Any(p => p.ProductoId == idProducto);
                if (yaExiste)
                {
                    MessageBox.Show("Este producto ya existe.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // --- Agregar como nuevo producto ---
                var nuevoProductoGrid = new VistaProductoGrid
                {
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = idProducto,
                        CantidadIngreso = modal.CantidadProductoIngresada,
                        CostoUnitario = modal.CostoUnitarioIngresado
                    },
                    CodigoProducto = idProducto.ToString(),
                    Descripcion = productoSelected.Descripcion,
                    UnidadMedida = "UNIDAD",
                    ProductoId = idProducto
                };
                _productosGridList.Add(nuevoProductoGrid);

                if (rangosDelModal != null)
                {
                    int contadorFila = _codigosGridList.Count + 1;
                    foreach (var rango in rangosDelModal)
                    {
                        rango.productoId = idProducto;
                        _rangosProcesadosGlobal.Add(rango);

                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        {
                            _codigosGridList.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                ColeccionTipo = rango.ColeccionTipo,
                                ProductoId = idProducto
                            });
                        }
                    }
                }

                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
                dgProductos.SelectedItem = nuevoProductoGrid;
            }
        }

        private async void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ImportarCodigos { Owner = Window.GetWindow(this) };
                // Validamos estado 1 para ingresos nuevos
                try { win.EstadoPermitido = (cboMotivo.SelectedValue is int mv && mv == 1) ? 1 : 4; } catch { win.EstadoPermitido = 1; }

                if (win.ShowDialog() != true) return;

                var listaRaw = win.CodigosImportados ?? new List<string>();
                if (!listaRaw.Any()) return;

                var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaRaw);

                // Agrupar códigos validados por ProductoId
                var codigosFisicosAgrupados = new Dictionary<int, List<string>>();

                foreach (var raw in listaRaw)
                {
                    string norm = _serviceMovimiento.NormalizarCodigo(raw);

                    if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue)
                        continue;

                    int productoId = tup.ProductoId.Value;

                    int categoriaReal = await _serviceMovimiento.ObtenerCategoriaDesdeBDAsync(tup.CodigoObj.Id);

                    if (tup.CodigoObj.EstadoId != win.EstadoPermitido)
                        continue;

                    if (_codigosGridList.Any(c => string.Equals(c.CodigoUnique, tup.CodigoObj.Codigo, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Agregamos a la lista de códigos físicos
                    _codigosGridList.Add(new VistaCodigoGrid
                    {
                        MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                        CodigoUnique = tup.CodigoObj.Codigo,
                        ProductoId = productoId,
                        ColeccionTipo = "Importado"
                    });

                    if (!codigosFisicosAgrupados.ContainsKey(productoId))
                        codigosFisicosAgrupados[productoId] = new List<string>();

                    codigosFisicosAgrupados[productoId].Add(tup.CodigoObj.Codigo);
                }

                var prodService = new ProductoService();

                // 🌟 CREACIÓN AUTOMÁTICA DE PRODUCTOS Y RANGOS
                // 🌟 CREACIÓN AUTOMÁTICA DE PRODUCTOS Y RANGOS
                foreach (var kvp in codigosFisicosAgrupados)
                {
                    int productoId = kvp.Key;
                    var listaDeCodigos = kvp.Value;
                    int cantidadTotal = listaDeCodigos.Count;

                    // 1. Crear o actualizar producto en grilla
                    var prodGridExistente = _productosGridList.FirstOrDefault(p => p.ProductoId == productoId);
                    if (prodGridExistente == null)
                    {
                        var prodData = await prodService.ObtenerPorIdAsync(productoId);
                        prodGridExistente = new VistaProductoGrid
                        {
                            ProductoId = productoId,
                            CodigoProducto = prodData?.Abreviatura ?? productoId.ToString(),
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            UnidadMedida = "UNIDAD",
                            Cantidad = cantidadTotal,
                            Detalle = new MovimientoDetalle { ProductoId = productoId, CantidadIngreso = cantidadTotal, CostoUnitario = prodData?.PrecioUnitario ?? 0 }
                        };
                        _productosGridList.Add(prodGridExistente);
                    }
                    else
                    {
                        prodGridExistente.Cantidad += cantidadTotal;
                        prodGridExistente.Detalle.CantidadIngreso += cantidadTotal;
                    }

                    // 2. 🌟 DECLARACIÓN CORRECTA DE CATEGORÍA
                    // Obtenemos la categoría del primer código que encontramos para este grupo
                    int categoriaReal = await _serviceMovimiento.ObtenerCategoriaDesdeBDAsync(lookup.Values.First(t => t.ProductoId == productoId).CodigoObj.Id);

                    // 3. ALGORITMO PARA CREAR RANGOS (Dentro del mismo bucle para tener acceso a categoriaReal)
                    var secuencias = new List<int>();
                    string baseAbrev = prodGridExistente.CodigoProducto;

                    foreach (var codigo in listaDeCodigos)
                    {
                        string norm = _serviceMovimiento.NormalizarCodigo(codigo);
                        if (norm.Length >= 7 && int.TryParse(norm.Substring(norm.Length - 7), out int seq))
                        {
                            secuencias.Add(seq);
                            baseAbrev = norm.Substring(0, norm.Length - 7);
                        }
                    }

                    secuencias.Sort();

                    if (secuencias.Any())
                    {
                        int start = secuencias[0];
                        int end = start;

                        for (int i = 1; i < secuencias.Count; i++)
                        {
                            if (secuencias[i] == end + 1) end = secuencias[i];
                            else
                            {
                                // Guardar bloque actual usando categoriaReal
                                _rangosProcesadosGlobal.Add(new RangoCodigoItem
                                {
                                    productoId = productoId,
                                    CategoriaProductoId = categoriaReal,
                                    AbreviaturaBase = baseAbrev,
                                    DesdeNum = start,
                                    HastaNum = end,
                                    Cantidad = (end - start + 1).ToString(),
                                    Desde = $"{baseAbrev}{start:D7}",
                                    Hasta = $"{baseAbrev}{end:D7}",
                                    ColeccionTipo = categoriaReal == 1 ? "LIBRO GUÍA" : "LIBRO VENTA"
                                });
                                start = secuencias[i]; end = start;
                            }
                        }
                        // Guardar último bloque
                        _rangosProcesadosGlobal.Add(new RangoCodigoItem
                        {
                            productoId = productoId,
                            CategoriaProductoId = categoriaReal,
                            AbreviaturaBase = baseAbrev,
                            DesdeNum = start,
                            HastaNum = end,
                            Cantidad = (end - start + 1).ToString(),
                            Desde = $"{baseAbrev}{start:D7}",
                            Hasta = $"{baseAbrev}{end:D7}",
                            ColeccionTipo = categoriaReal == 1 ? "LIBRO GUÍA" : "LIBRO VENTA"
                        });
                    }
                }

                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
                dgCodigos.ItemsSource = null;
                dgCodigos.ItemsSource = _codigosGridList;

                MessageBox.Show($"Importación finalizada. Se procesaron {_codigosGridList.Count} códigos correctamente agrupados en rangos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            // Habilitar formulario para edición y permitir que el usuario escriba el número de documento
            // Mantener campos del formulario deshabilitados hasta que se cargue el movimiento
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;
            // Solo permitimos editar el número de documento (txtNumDocumento). NO habilitar la serie.
            if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = false; txtNumDocumento.IsEnabled = true; }
            if (txtNumSerie != null) { txtNumSerie.IsReadOnly = true; txtNumSerie.IsEnabled = false; }
            txtNumDocumento.Focus();

            // Suscribir evento Enter para cargar registro
            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            // Aseguramos que los botones y grillas estén listos para edición
            // Bloquear los botones principales (Nuevo/Editar/Imprimir/Anular) hasta que se confirme con Enter
            GestionarBotonesPrincipales(enEdicion: true);

            // Mantener las acciones de producto y grabado deshabilitadas hasta cargar el registro
            if (dgProductos != null) dgProductos.IsEnabled = false;
            if (dgCodigos != null) dgCodigos.IsEnabled = false;
            if (btnGrabar != null) btnGrabar.IsEnabled = false;
            if (btnCancelar != null) btnCancelar.IsEnabled = true; // permitir cancelar la operación
        }

        private async void DgProductos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ignorar si doble click se produce fuera de una fila
            if (dgProductos.SelectedItem is VistaProductoGrid)
            {
                await EditSelectedProductAsync();
            }
        }

        // ==========================================
        // IMPRESIÓN EXTERNALIZADA AL SERVICIO
        // ==========================================

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_printMode) return;
            _printMode = true;
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;
            if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = false; txtNumDocumento.IsEnabled = true; txtNumDocumento.Focus(); }
            MessageBox.Show("Modo Imprimir activado.", "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void GenerateExcelFromCurrentLoadedMovement()
        {
            // 🌟 LÓGICA DE EXCEL DELEGADA AL NUEVO SERVICIO
            _reporteService.GenerarReporteIngreso(
                $"{txtNumSerie.Text}-{txtNumDocumento.Text}",
                dtpFechaRecepcion.SelectedDate?.ToString("dd/MM/yyyy") ?? "",
                (cboMotivo.SelectedItem as dynamic)?.Descripcion ?? "",
                txtRazonSocial.Text,
                txtDireccion.Text,
                txtUbicacion.Text,
                $"{txtSerieGuia.Text}-{txtNumeroGuia.Text}",
                txtObservacion.Text,
                _productosGridList,
                _codigosGridList,
                _rangosProcesadosGlobal
            );
        }

        private void ConfigurarDataGridsParaVirtualizacion()
        {
            if (dgCodigos != null)
            {
                VirtualizingPanel.SetIsVirtualizing(dgCodigos, true);
                VirtualizingPanel.SetVirtualizationMode(dgCodigos, VirtualizationMode.Recycling);
            }
            if (dgProductos != null)
            {
                VirtualizingPanel.SetIsVirtualizing(dgProductos, true);
                VirtualizingPanel.SetVirtualizationMode(dgProductos, VirtualizationMode.Recycling);
            }
        }
        private void CleanupPrintMode() { _printMode = false; EstablecerEstadoInicial(); }

        // MÉTODOS DE SOPORTE UI OMITIDOS POR BREVEDAD (PreviewMouseWheel, etc.)
        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Cuando el control está dentro de un ScrollViewer padre, el evento rueda lo captura el padre.
            // Aquí forzamos que la grilla interna se desplace verticalmente.
            if (sender is DependencyObject dep)
            {
                var sv = FindVisualChild<ScrollViewer>(dep);
                if (sv != null)
                {
                    // Delta positivo -> hacia arriba
                    double newOffset = sv.VerticalOffset - e.Delta / 3.0; // ajuste de sensibilidad
                    if (newOffset < 0) newOffset = 0;
                    if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;
                    sv.ScrollToVerticalOffset(newOffset);
                    e.Handled = true;
                }
            }
        }
        private void MovimientosUserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Si estamos en modo imprimir, interceptamos clicks en botones y bloqueamos todo excepto Imprimir y Cancelar
            if (_printMode)
            {
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null)
                {
                    if (dep is Button btn)
                    {
                        // permitir acciones en botones: superior Imprimir, Cancelar y el botón persistente junto a Guardar
                        if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave)
                        {
                            // permitir estas acciones
                            break;
                        }

                        // Bloquear cualquier otro botón silenciosamente
                        e.Handled = true;
                        return;
                    }
                    dep = VisualTreeHelper.GetParent(dep);
                }
            }

            // Comportamiento original: cerrar popup de sugerencias si se clickea fuera
            if (!txtRazonSocial.IsMouseOver && !popupSugerencias.IsMouseOver)
            {
                popupSugerencias.IsOpen = false;
            }
        }

        private void MovimientosUserControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Durante modo imprimir, cuando el cursor está sobre botones no permitidos
            // queremos mostrar cursor por defecto (no mano) para indicar que no se puede clicar.
            if (!_printMode)
            {
                if (Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;
                return;
            }

            var dep = e.OriginalSource as DependencyObject;
            bool overBlockedButton = false;

            while (dep != null)
            {
                if (dep is Button btn)
                {
                    // Allow pointer for top Imprimir, Cancelar and the side persistent print button
                    if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave)
                    {
                        overBlockedButton = false;
                        break;
                    }
                    overBlockedButton = true;
                    break;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (overBlockedButton)
            {
                Mouse.OverrideCursor = Cursors.Arrow; // cursor normal
            }
            else
            {
                Mouse.OverrideCursor = null; // restaurar
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private async void TxtNumDocumento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string serie = txtNumSerie?.Text?.Trim();
                string numero = txtNumDocumento?.Text?.Trim();
                if (string.IsNullOrEmpty(numero))
                {
                    MessageBox.Show("Ingrese el número de documento para cargar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    await LoadMovimientoBySerieNumeroAsync(serie, numero);
                    if (_printMode)
                    {
                        ShowPrintButtonNearSave();
                        if (dgProductos != null) dgProductos.IsReadOnly = true;
                        if (dgCodigos != null) dgCodigos.IsReadOnly = true;

                        if (btnAgregar != null) btnAgregar.IsEnabled = false;
                        if (btnModificar != null) btnModificar.IsEnabled = false;
                        if (btnEliminar != null) btnEliminar.IsEnabled = false;
                        if (btnImportar != null) btnImportar.IsEnabled = false;
                        if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = false;
                        if (btnImprimir != null) btnImprimir.IsEnabled = false;
                        if (btnCancelar != null) btnCancelar.IsEnabled = true;
                        if (btnGrabar != null) btnGrabar.IsEnabled = false;

                        if (dtpFechaRecepcion != null) dtpFechaRecepcion.IsEnabled = false;
                        if (cboMotivo != null) cboMotivo.IsEnabled = false;
                        if (txtRazonSocial != null) txtRazonSocial.IsEnabled = false;
                        if (txtUbicacion != null) txtUbicacion.IsEnabled = false;
                        if (txtSerieGuia != null) txtSerieGuia.IsEnabled = false;
                        if (txtNumeroGuia != null) txtNumeroGuia.IsEnabled = false;
                        if (txtObservacion != null) txtObservacion.IsEnabled = false;

                        try { popupSugerencias.IsOpen = false; } catch { }
                        try { popupUbicacion.IsOpen = false; } catch { }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cargar movimiento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private async Task EditSelectedProductAsync()
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid seleccionado) return;

            List<RangoCodigoItem> rangosExistentes = null;

            try
            {
                if (_currentMovimientoId.HasValue && seleccionado.Detalle != null && seleccionado.Detalle.Id > 0)
                {
                    rangosExistentes = await _serviceMovimiento.GetRangosByMovimientoDetalleIdAsync(seleccionado.Detalle.Id);
                }
            }
            catch
            {
                rangosExistentes = null;
            }

            if (rangosExistentes == null || rangosExistentes.Count == 0)
            {
                rangosExistentes = _rangosProcesadosGlobal.Where(r => r.productoId == seleccionado.ProductoId).ToList();
            }

            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
            modal.ListaProductosExistentesEnPadre = _productosGridList;
            modal.InitializeForEdit(seleccionado, rangosExistentes);

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);
                foreach (var r in modal.ListaRangosAgregados)
                {
                    r.productoId = seleccionado.ProductoId;
                    _rangosProcesadosGlobal.Add(r);
                }

                // 🌟 Llama a reconstruir códigos y refresca grillas
                // (Asegúrate de que RebuildCodigosGridList exista en tu código)
                RebuildCodigosGridList();
                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
                dgProductos.SelectedItem = seleccionado;
                DgProductos_SelectionChanged(dgProductos, new SelectionChangedEventArgs(DataGrid.SelectionChangedEvent, new List<object>(), new List<object>()));
            }
        }


        private async Task LoadMovimientoBySerieNumeroAsync(string serie, string numero)
        {
            // 1. Limpeza previa
            LimpiarFormulario();

            // 2. Chamada ao servizo para obter o movemento
            var movimientoComp = await _serviceMovimiento.GetMovimientoCompletoAsync(serie, numero);

            if (movimientoComp == null)
            {
                MessageBox.Show("No se encontró el movimiento especificado.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 3. Relleno da información
            var movimiento = movimientoComp.Movimiento;
            _currentMovimientoId = movimiento.Id;

            if (movimiento.FechaMovimiento.HasValue)
                dtpFechaRecepcion.SelectedDate = movimiento.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue);

            txtNumSerie.Text = movimiento.SerieDocumento;
            txtNumDocumento.Text = movimiento.NumeroDocumento;
            cboMotivo.SelectedValue = movimiento.MotivoProductoId;

            // Rellenar os campos de texto e grillas
            txtRazonSocial.Text = movimiento.PersonaComercialId?.ToString() ?? string.Empty;
            txtCodigoRazonSocial.Text = movimiento.PersonaComercialId?.ToString() ?? string.Empty;
            txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
            txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
            txtObservacion.Text = movimiento.Observacion ?? string.Empty;

            // Rellenar as listas de produtos e códigos
            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            foreach (var det in movimientoComp.Detalles)
            {
                var vp = new VistaProductoGrid
                {
                    ProductoId = det.ProductoId,
                    CodigoProducto = det.ProductoId.ToString(),
                    Descripcion = "Producto cargado", // O teu método de obter descripción iría aquí ou no servizo
                    UnidadMedida = "UNIDAD",
                    Detalle = new MovimientoDetalle { Id = det.Id, ProductoId = det.ProductoId, CantidadIngreso = det.CantidadIngreso, CostoUnitario = det.CostoUnitario }
                };
                _productosGridList.Add(vp);
            }

            // Actualizar UI
            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
        }

        private void RebuildCodigosGridList()
        {
            _codigosGridList.Clear();
            int contadorFila = 1;

            foreach (var producto in _productosGridList)
            {
                var rangosProducto = _rangosProcesadosGlobal.Where(r => r.productoId == producto.ProductoId).ToList();
                foreach (var rango in rangosProducto)
                {
                    for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                    {
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                            CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                            ColeccionTipo = rango.ColeccionTipo,
                            ProductoId = producto.ProductoId
                        });
                    }
                }
            }

            try
            {
                var counts = _codigosGridList.GroupBy(c => c.ProductoId).ToDictionary(g => g.Key, g => g.Count());
                foreach (var p in _productosGridList)
                {
                    if (counts.TryGetValue(p.ProductoId, out int cnt))
                    {
                        p.Detalle = p.Detalle ?? new MovimientoDetalle { ProductoId = p.ProductoId };
                        p.Detalle.CantidadIngreso = Convert.ToDecimal(cnt);
                    }
                }
            }
            catch { }
        }

        private void ShowPrintButtonNearSave()
        {
            if (_btnPrintNearSave != null) return;

            if (btnGrabar?.Parent is Panel parentPanel)
            {
                _btnPrintNearSave = new Button
                {
                    Content = "🖨️ Imprimir Registro",
                    Background = btnGrabar.Background,
                    Foreground = btnGrabar.Foreground,
                    Style = btnGrabar.Style,
                    Margin = btnGrabar.Margin,
                    Padding = btnGrabar.Padding,
                    MinWidth = btnGrabar.MinWidth,
                    Height = btnGrabar.Height,
                    FontSize = btnGrabar.FontSize
                };

                _btnPrintNearSave.Click += (s, e) => { GenerateExcelFromCurrentLoadedMovement(); };

                parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabar) + 1, _btnPrintNearSave);
                btnGrabar.IsEnabled = false;
            }
        }
    }
}