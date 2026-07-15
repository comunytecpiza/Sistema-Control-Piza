using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes; // 🌟 SERVICIO DE REPORTE IMPORTADO
using AplicativoDeAlmacen.Services.Ubicaciones;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;
using HandyControl.Controls;
using System;

using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static AplicativoDeAlmacen.Data.DataConnection;
using MessageBox = System.Windows.MessageBox;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using Window = HandyControl.Controls.Window;

namespace AplicativoDeAlmacen.Views
{
    public partial class MovimientosUserControl : UserControl
    {
        // ==========================================
        // VARIABLES GLOBALES
        // ==========================================
        private int? _currentMovimientoId = null;
        private readonly PersonaComercialService _service;
        private readonly IngresoMovimientoService _serviceMovimiento;
        private readonly UbicacionService _ubicacionService;
        private readonly ReporteExcelService _reporteService; // 🌟 INICIALIZAMOS SERVICIO DE EXCEL

        private List<VistaProductoGrid> _productosGridList;
        private List<VistaCodigoGrid> _codigosGridList;
        private List<RangoCodigoItem> _rangosProcesadosGlobal;

        private bool _isUpdatingFromSelection = false;
        private int? _personaComercialIdSeleccionada = null;
        private const int UBICACION_ID_SELECCIONADA = 1; // ID Fijo de Almacén Central
        private bool _printMode = false;
        private Button _btnPrintNearSave = null;
        private readonly DatabaseConnection _dbConnHelper = new DatabaseConnection();

        // ==========================================
        // CONSTRUCTOR
        // ==========================================
        public MovimientosUserControl()
        {
            _productosGridList = new List<VistaProductoGrid>();
            _codigosGridList = new List<VistaCodigoGrid>();
            _rangosProcesadosGlobal = new List<RangoCodigoItem>();

            _service = new PersonaComercialService();
            _serviceMovimiento = new IngresoMovimientoService();
            _ubicacionService = new UbicacionService();
            _reporteService = new ReporteExcelService();

            InitializeComponent();

            ConfigurarEventosIniciales();
            EstablecerEstadoInicial();
        }

        // ==========================================
        // CONFIGURACIÓN DE EVENTOS
        // ==========================================
        public void ConfigurarEventosIniciales()
        {
            // Eventos de TextBoxes y ComboBoxes
            txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += LstSugerencias_SelectionChanged;
            cboMotivo.SelectionChanged += CboMotivo_SelectionChanged;
            txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
            lstSugerenciasUbicacion.SelectionChanged += LstSugerenciasUbicacion_SelectionChanged;

            // Eventos de la ventana
            this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
            this.PreviewMouseMove += MovimientosUserControl_PreviewMouseMove;
            Loaded += MovimientosUserControl_Loaded;

            // Botones Principales (Cabecera)
            btnAgregar.Click += BtnAgregar_Click;
            btnEditar.Click += BtnEditar_Click;
            btnImprimir.Click += BtnImprimir_Click;
            
            btnGrabar.Click += RegistrarMovimientoCompleto;

            // Botones de Grilla (Detalle)
            btnAgregarProducto.Click += BtnAgregarItem_Click;
            btnModificar.Click += BtnModificar_Click;
            btnEliminar.Click += BtnEliminar_Click;
            btnImportar.Click += BtnImportar_Click;
            btnEscanear.Click -= BtnEscanear_Click;
            btnEscanear.Click += BtnEscanear_Click;

            // Grillas
            dgProductos.SelectionChanged += DgProductos_SelectionChanged;
            dgProductos.MouseDoubleClick += DgProductos_MouseDoubleClick; // 🌟 RESTAURADO EL DOBLE CLIC

            if (dgProductos != null) dgProductos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
            if (dgCodigos != null) dgCodigos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
        }

        private async void MovimientosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigurarDataGridsParaVirtualizacion();
            await CargarMotivosAsync();
        }

        private void EstablecerEstadoInicial()
        {
            LimpiarFormulario();
            HabilitarCamposFormulario(false);
            GestionarBotonesPrincipales(enEdicion: false);
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
                MessageBox.Show($"Error al cargar los motivos: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { this.Cursor = Cursors.Arrow; }
        }

        // ==========================================
        // LÓGICA DE CARGA (EDITAR E IMPRIMIR)
        // ==========================================
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
                    this.Cursor = Cursors.Wait;
                    await LoadMovimientoBySerieNumeroAsync(serie, numero);

                    if (_printMode)
                    {
                        ShowPrintButtonNearSave();
                        BloquearParaImpresion();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cargar movimiento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { this.Cursor = Cursors.Arrow; }
            }
        }

        private async Task LoadMovimientoBySerieNumeroAsync(string serie, string numero)
        {
            LimpiarFormulario();
            var movimientoComp = await _serviceMovimiento.GetMovimientoCompletoAsync(serie, numero);

            if (movimientoComp == null)
            {
                MessageBox.Show("No se encontró el movimiento especificado.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var movimiento = movimientoComp.Movimiento;
            _currentMovimientoId = movimiento.Id;

            if (movimiento.FechaMovimiento.HasValue)
                dtpFechaRecepcion.SelectedDate = movimiento.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue);

            txtNumSerie.Text = movimiento.SerieDocumento;
            txtNumDocumento.Text = movimiento.NumeroDocumento;
            cboMotivo.SelectedValue = movimiento.MotivoProductoId;

            txtRazonSocial.Text = movimiento.PersonaComercialId?.ToString() ?? string.Empty;
            txtCodigoRazonSocial.Text = movimiento.PersonaComercialId?.ToString() ?? string.Empty;
            txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
            txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
            txtObservacion.Text = movimiento.Observacion ?? string.Empty;

            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            // 🌟 AHORA USAMOS EL SERVICIO PARA LA DESCRIPCIÓN (Limpio y profesional)
            foreach (var det in movimientoComp.Detalles)
            {
                // Llamada al método limpio en IngresoMovimientoService
                string descripcionProducto = await _serviceMovimiento.ObtenerDescripcionProductoAsync(det.ProductoId);

                var vp = new VistaProductoGrid
                {
                    ProductoId = det.ProductoId,
                    CodigoProducto = det.ProductoId.ToString(),
                    Descripcion = descripcionProducto,
                    UnidadMedida = "UNIDAD",
                    Detalle = new MovimientoDetalle { Id = det.Id, ProductoId = det.ProductoId, CantidadIngreso = det.CantidadIngreso, CostoUnitario = det.CostoUnitario }
                };

                var rangosForDet = movimientoComp.Rangos.Where(r => r.MovimientoDetalleId == det.Id).ToList();
                foreach (var r in rangosForDet)
                {
                    _rangosProcesadosGlobal.Add(r);
                    for (int i = r.DesdeNum; i <= r.HastaNum; i++)
                    {
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = det.Id },
                            CodigoUnique = $"{r.AbreviaturaBase}-{i:D7}",
                            ColeccionTipo = r.ColeccionTipo ?? string.Empty,
                            ProductoId = r.productoId
                        });
                    }
                }
                _productosGridList.Add(vp);
            }

            
            var listaFusionada = _serviceMovimiento.MergeDuplicateProducts(_productosGridList.ToList());

            _productosGridList.Clear();
            foreach (var item in listaFusionada)
            {
                _productosGridList.Add(item);
            }

            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
            dgCodigos.ItemsSource = null;
            dgCodigos.ItemsSource = _codigosGridList;

                     

            if (!_printMode)
            {
                HabilitarCamposFormulario(true);
                GestionarBotonesPrincipales(enEdicion: true);
            }
        }

        // ==========================================
        // BOTONES DE CABECERA
        // ==========================================
        private async void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            try
            {
                LimpiarFormulario();
                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                HabilitarCamposFormulario(true);
                GestionarBotonesPrincipales(enEdicion: true);

                // 🌟 CORRECCIÓN DE INTEGRIDAD: Autogenerar serie y número correlativo
                string seriePorDefecto = "0001"; // O la serie que use tu empresa
                var siguienteCorrelativo = await _serviceMovimiento.GenerarSiguienteCorrelativoAsync(seriePorDefecto);

                txtNumSerie.Text = siguienteCorrelativo.SerieDocumento;
                txtNumDocumento.Text = siguienteCorrelativo.NumeroDocumento;

                // Bloqueamos para que el usuario no altere el correlativo automático de forma maliciosa
                txtNumSerie.IsEnabled = false;
                txtNumDocumento.IsEnabled = false;

                cboMotivo.SelectedValue = 1;
                cboMotivo.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar correlativo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;
            if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = false; txtNumDocumento.IsEnabled = true; txtNumDocumento.Focus(); }

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            GestionarBotonesPrincipales(enEdicion: true);
            if (btnCancelar != null) btnCancelar.IsEnabled = true;
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_printMode) return;
            _printMode = true;
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;

            if (txtNumDocumento != null)
            {
                txtNumDocumento.IsReadOnly = false;
                txtNumDocumento.IsEnabled = true;
                txtNumDocumento.Focus();
            }

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            System.Windows.MessageBox.Show("Modo Imprimir activado. Ingrese el número y presione Enter.", "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var resultado = System.Windows.MessageBox.Show("¿Está seguro que desea cancelar la operación actual?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resultado == MessageBoxResult.Yes)
            {
                if (_printMode) { _printMode = false; }
                if (_btnPrintNearSave != null && _btnPrintNearSave.Parent is Panel p) { p.Children.Remove(_btnPrintNearSave); _btnPrintNearSave = null; }

                EstablecerEstadoInicial();
            }
        }

        // ==========================================
        // BOTÓN GUARDAR (REGISTRO)
        // ==========================================
        private async void RegistrarMovimientoCompleto(object sender, RoutedEventArgs e)
        {

            if (txtRazonSocial.IsEnabled && string.IsNullOrWhiteSpace(txtRazonSocial.Text))
            {
                MessageBox.Show("La Razón Social es obligatoria para este motivo.");
                return;
            }

            if (txtUbicacion.IsEnabled && string.IsNullOrWhiteSpace(txtUbicacion.Text))
            {
                MessageBox.Show("La Ubicación es obligatoria para este motivo.");
                return;
            }

            if (txtSerieGuia.IsEnabled && (string.IsNullOrWhiteSpace(txtSerieGuia.Text) || string.IsNullOrWhiteSpace(txtNumeroGuia.Text)))
            {
                MessageBox.Show("La Guía de Remisión es obligatoria para este motivo.");
                return;
            }

            if (_productosGridList == null || !_productosGridList.Any())
            {
                MessageBox.Show(
                    "Debe agregar al menos un producto antes de guardar.", "Validación", MessageBoxButton.OK,MessageBoxImage.Warning);

                return;
            }

            try
            {
                btnGrabar.IsEnabled = false;
                Cursor = Cursors.Wait;
                // Crear solicitud para el servicio
                var solicitud = CrearSolicitudMovimiento();

                // Ejecutar toda la lógica en el Service
                // Desarmamos el objeto 'solicitud' para pasar los 5 parámetros requeridos
                bool resultado = await _serviceMovimiento.RegistrarMovimientoCompletoAsync(solicitud.Movimiento, solicitud.Productos, _rangosProcesadosGlobal.ToList(), solicitud.Movimiento.UbicacionId ?? 1, solicitud.MovimientoId);

                if (resultado)
                {
                    // 🌟 AVISO EN GRANDE CON EL NÚMERO
                    string mensaje = $"¡Movimiento registrado con éxito!\n\n" +
                                     $"Número de Registro: {solicitud.Movimiento.SerieDocumento}-{solicitud.Movimiento.NumeroDocumento}";

                    MessageBox.Show(mensaje, "Guardado Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);

                    EstablecerEstadoInicial();
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error al guardar movimiento:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnGrabar.IsEnabled = true;
                Cursor = Cursors.Arrow;
            }
        }

        private SolicitudMovimiento CrearSolicitudMovimiento()
        {

            // 🌟 RE-SINCRONIZAR CANTIDADES ANTES DE ENVIAR
            foreach (var p in _productosGridList)
            {
                if (p.Detalle == null) p.Detalle = new MovimientoDetalle { ProductoId = p.ProductoId };

                // Contamos los códigos reales en la lista _codigosGridList para este producto
                p.Detalle.CantidadIngreso = _codigosGridList.Count(c => c.ProductoId == p.ProductoId);
                p.Cantidad = (int)p.Detalle.CantidadIngreso;
            }
            return new SolicitudMovimiento
            {
                Movimiento = new Movimiento
                {
                    FechaMovimiento =
                        dtpFechaRecepcion.SelectedDate.HasValue
                        ? DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate.Value)
                        : DateOnly.FromDateTime(DateTime.Today),


                    SerieDocumento =
                        txtNumSerie.Text.Trim(),


                    NumeroDocumento =
                        txtNumDocumento.Text.Trim(),


                    MotivoProductoId =
                        Convert.ToInt32(cboMotivo.SelectedValue),


                    UbicacionId =
                        UBICACION_ID_SELECCIONADA,


                    UsuarioId = 1,


                    PersonaComercialId =
                        _personaComercialIdSeleccionada,


                    SerieGuia =
                        txtSerieGuia.Text.Trim(),


                    NumeroGuia =
                        txtNumeroGuia.Text.Trim(),


                    Observacion =
                        txtObservacion.Text.Trim()
                },


                Productos = _productosGridList,


                Codigos = _codigosGridList,


                MovimientoId = _currentMovimientoId
            };
        }
        
        // ==========================================
        // EXPORTAR REPORTE (A EXCEL)
        // ==========================================
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

        private void GenerateExcelFromCurrentLoadedMovement()
        {
            // 🌟 LLAMADA OFICIAL A TU SERVICIO DE REPORTES
            _reporteService.GenerarReporteIngreso(
                numeroRegistro: $"{txtNumSerie.Text}-{txtNumDocumento.Text}",
                fecha: dtpFechaRecepcion.SelectedDate?.ToString("dd/MM/yyyy") ?? "",
                motivo: (cboMotivo.SelectedItem as dynamic)?.Descripcion ?? "",
                razonSocial: txtRazonSocial.Text,
                direccion: txtDireccion.Text,
                ubicacion: txtUbicacion.Text,
                guia: $"{txtSerieGuia.Text}-{txtNumeroGuia.Text}",
                observacion: txtObservacion.Text,
                productosGridList: _productosGridList,
                codigosGridList: _codigosGridList,
                rangosProcesadosGlobal: _rangosProcesadosGlobal
            );
        }

        private void BloquearParaImpresion()
        {
            if (dgProductos != null) dgProductos.IsReadOnly = true;
            if (dgCodigos != null) dgCodigos.IsReadOnly = true;

            btnAgregar.IsEnabled = false;
            btnModificar.IsEnabled = false;
            btnEliminar.IsEnabled = false;
            btnImportar.IsEnabled = false;
            btnAgregarProducto.IsEnabled = false;
            btnImprimir.IsEnabled = false;
            btnCancelar.IsEnabled = true;
            btnGrabar.IsEnabled = false;
        }

        // ==========================================
        // GESTIÓN DE GRILLAS Y BÚSQUEDA
        // ==========================================
        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 🌟 BUSCADOR EN TIEMPO REAL 
            if (dgProductos.SelectedItem is not VistaProductoGrid productoSeleccionado) return;

            string filtro = txtBuscarCodigo.Text.Trim().ToLower();
            var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == productoSeleccionado.ProductoId).ToList();

            if (string.IsNullOrEmpty(filtro))
            {
                dgCodigos.ItemsSource = codigosDelProducto;
            }
            else
            {
                var filtrados = codigosDelProducto.Where(c => c.CodigoUnique.ToLower().Contains(filtro)).ToList();
                dgCodigos.ItemsSource = filtrados;
            }
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MostrarCodigosProductoSeleccionado();
        }

        private void RefrescarGrillas()
        {
            // AHORA LLAMAS AL SERVICIO, pasando las listas que tienes en la UI
            _serviceMovimiento.SincronizarCantidadesConCodigos(_productosGridList.ToList(), _codigosGridList.ToList());

            // Fuerza la actualización de la vista
            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
            dgProductos.Items.Refresh();

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

        private void MostrarCodigosProductoSeleccionado()
        {
            // Verificación de nulidad segura
            var producto = dgProductos.SelectedItem as VistaProductoGrid;

            if (producto == null)
            {
                dgCodigos.ItemsSource = null;
                lblResumenCodigos.Text = "0 / 0";
                return;
            }

            // Filtramos códigos
            var codigosFiltrados = _codigosGridList.Where(c => c.ProductoId == producto.ProductoId).ToList();
            dgCodigos.ItemsSource = codigosFiltrados;

            // Actualizar el resumen
            // Usamos el conteo del detalle (o la suma total de códigos asociados al ID)
            int totalDelProducto = _codigosGridList.Count(c => c.ProductoId == producto.ProductoId);
            lblResumenCodigos.Text = $"{codigosFiltrados.Count} / {totalDelProducto}";
        }



        // ==========================================
        // BOTONES DE PRODUCTO (MODIFICAR, AGREGAR, LECTORA)
        // ==========================================
        private async void BtnModificar_Click(object sender, RoutedEventArgs e) { await EditSelectedProductAsync(); }

        // 🌟 DOBLE CLIC RESTAURADO
        private async void DgProductos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // El doble clic ahora fuerza la edición del producto seleccionado
            if (dgProductos.SelectedItem is VistaProductoGrid)
            {
                await EditSelectedProductAsync();
            }
        }

        private async Task EditSelectedProductAsync()
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid seleccionado)
            {
                MessageBox.Show("No hay producto seleccionado.");
                return;
            }

            List<RangoCodigoItem> rangosExistentes = null;

            // 1. Obtener rangos desde la BD si el movimiento ya existe
            if (_currentMovimientoId.HasValue && seleccionado.Detalle != null && seleccionado.Detalle.Id > 0)
            {
                rangosExistentes = await _serviceMovimiento
                    .GetRangosByMovimientoDetalleIdAsync(seleccionado.Detalle.Id);
            }

            // 2. Si no existen en BD o es un registro temporal en memoria (Escáner/Excel), 
            // lo ideal y más seguro es RECONSTRUIRLOS directamente desde los códigos reales en pantalla
            var codigosProducto = _codigosGridList
                .Where(c => c.ProductoId == seleccionado.ProductoId)
                .ToList();

            if (codigosProducto.Any())
            {
                // Forzamos la regeneración matemática limpia basada en los códigos reales que sí están en la lista
                rangosExistentes = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosProducto);
            }
            else if (rangosExistentes == null || rangosExistentes.Count == 0)
            {
                rangosExistentes = _rangosProcesadosGlobal
                    .Where(r => r.productoId == seleccionado.ProductoId)
                    .ToList();
            }

            // 🌟 ENRIQUECIMIENTO SEGURO: Garantizar formatos de texto exactos para el DataGrid del Modal
            foreach (var r in rangosExistentes)
            {
                // Si detectamos que es un alfanumérico puro (por el corte o indicador de guion)
                if (r.DesdeNum == -1 || !r.AbreviaturaBase.Contains("-") && r.DesdeNum == 0)
                {
                    r.DesdeNum = -1;
                    r.HastaNum = -1;
                    r.Cantidad = "1";
                    r.Desde = r.AbreviaturaBase;
                    r.Hasta = r.AbreviaturaBase;
                }
                else
                {
                    // Conservar formato numérico secuencial normal de 7 dígitos para los que sí tienen guiones
                    int cantCalculada = (r.HastaNum - r.DesdeNum + 1);
                    r.Cantidad = (string.IsNullOrEmpty(r.Cantidad) || r.Cantidad == "0") ? cantCalculada.ToString() : r.Cantidad;
                    r.Desde = $"{r.AbreviaturaBase}-{r.DesdeNum:D7}";
                    r.Hasta = $"{r.AbreviaturaBase}-{r.HastaNum:D7}";
                }

                if (string.IsNullOrEmpty(r.ColeccionTipo))
                {
                    string tipoDeducido = (r.CategoriaProductoId == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
                    r.ColeccionTipo = $"C2026 / {tipoDeducido}";
                }
            }

            // 4. Abrir la ventana de edición
            var modal = new AgregarItemWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };

            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;

            // Pasamos el producto seleccionado junto con sus rangos enriquecidos con texto real
            modal.InitializeForEdit(seleccionado, rangosExistentes);

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                // 1. Actualizar cantidades
                seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                seleccionado.Cantidad = (int)seleccionado.Detalle.CantidadIngreso;

                // 2. Reemplazar rangos en la lista global en memoria
                _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);
                _rangosProcesadosGlobal.AddRange(modal.ListaRangosAgregados);

                // 3. Reconstruir los códigos del grid a partir de los rangos modificados
                _codigosGridList.RemoveAll(c => c.ProductoId == seleccionado.ProductoId);

                var nuevosCodigos = _serviceMovimiento.ReconstruirCodigosDesdeRangos(modal.ListaRangosAgregados.ToList());
                _codigosGridList.AddRange(nuevosCodigos);

                // 4. Actualizar visualización de las grillas principales
                RefrescarGrillas();
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                var confirmacion = MessageBox.Show($"¿Está seguro de eliminar el producto \"{productoSeleccionado.Descripcion}\"?", "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmacion == MessageBoxResult.Yes)
                {
                    _productosGridList.Remove(productoSeleccionado);
                    _codigosGridList.RemoveAll(c => c.ProductoId == productoSeleccionado.ProductoId);
                    _rangosProcesadosGlobal.RemoveAll(r => r.productoId == productoSeleccionado.ProductoId);
                    RefrescarGrillas();
                }
            }
        }

        private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this), IsAddAction = true };
            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
            modal.ListaProductosExistentesEnPadre = _productosGridList;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados ?? new System.Collections.ObjectModel.ObservableCollection<RangoCodigoItem>();

                if (productoSelected == null) return;
                int idProducto = productoSelected.Id;

                var existente = _productosGridList.FirstOrDefault(p => p.ProductoId == idProducto);
                if (existente != null && modal.MergeWithExisting)
                {
                    existente.Detalle = existente.Detalle ?? new MovimientoDetalle { ProductoId = existente.ProductoId };
                    existente.Detalle.CantidadIngreso += modal.CantidadProductoIngresada;
                    existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                    foreach (var rango in rangosDelModal)
                    {
                        rango.productoId = idProducto;
                        _rangosProcesadosGlobal.Add(rango);
                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                            _codigosGridList.Add(new VistaCodigoGrid { CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}", ColeccionTipo = rango.ColeccionTipo, ProductoId = idProducto });
                    }
                    RefrescarGrillas();
                    dgProductos.SelectedItem = existente;
                    return;
                }

                if (_productosGridList.Any(p => p.ProductoId == idProducto))
                {
                    MessageBox.Show("Este producto ya existe.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var nuevoProductoGrid = new VistaProductoGrid
                {
                    Detalle = new MovimientoDetalle { ProductoId = idProducto, CantidadIngreso = modal.CantidadProductoIngresada, CostoUnitario = modal.CostoUnitarioIngresado },
                    CodigoProducto = idProducto.ToString(),
                    Descripcion = productoSelected.Descripcion,
                    UnidadMedida = "UNIDAD",
                    ProductoId = idProducto
                };
                _productosGridList.Add(nuevoProductoGrid);

                foreach (var rango in rangosDelModal)
                {
                    rango.productoId = idProducto;
                    _rangosProcesadosGlobal.Add(rango);
                    for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        _codigosGridList.Add(new VistaCodigoGrid { CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}", ColeccionTipo = rango.ColeccionTipo, ProductoId = idProducto });
                }
                RefrescarGrillas();
                dgProductos.SelectedItem = nuevoProductoGrid;
            }
        }


        private void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            // 🌟 EVITAR DOBLE DISPARO: Deshabilitamos el botón inmediatamente al hacer clic
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
            }

            // Comprobamos si ya hay una ventana de este tipo abierta en la aplicación
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is LectorGlobalWindow)
                {
                    window.Focus(); // Si ya existe, le da el foco a la abierta y no crea otra
                    if (sender is Button b) b.IsEnabled = true;
                    return;
                }
            }

            // Crear la instancia única de la lectora
            var lector = new LectorGlobalWindow(async resultado =>
            {
                bool seAgregoConExito = await ProcesarCodigoEscaneadoAsync(resultado);
                if (seAgregoConExito)
                {
                    Application.Current.Dispatcher.Invoke(() => { RefrescarGrillas(); });
                }
                return seAgregoConExito;
            });

            lector.Owner = System.Windows.Window.GetWindow(this);

            // Al cerrar la ventana modal, volvemos a habilitar el botón de la UI principal
            lector.Closed += (s, ev) =>
            {
                if (sender is Button b) b.IsEnabled = true;
            };

            lector.ShowDialog();
            RefrescarGrillas();
        }

        private async Task<bool> ProcesarCodigoEscaneadoAsync(LectoraResultDTO resultado)
        {
            // 1. Validar duplicados en memoria
            if (_codigosGridList.Any(x => x.CodigoUnique.Equals(resultado.CodigoCompleto, StringComparison.OrdinalIgnoreCase)))
                return false;

            // 2. Obtener tipo de colección desde el servicio
            string tipoBD = await _serviceMovimiento.ObtenerColeccionTipoBDAsync(resultado.CodigoCreadoId);

            // 3. Obtener o registrar el producto en la grilla izquierda
            var producto = ObtenerOAgregarProductoEnLista(resultado.ProductoId, resultado.DescripcionProducto, resultado.PrecioUnitario);

            // 4. Crear el código único
            var nuevosCodigos = new List<VistaCodigoGrid>
    {
        new VistaCodigoGrid
        {
            ProductoId = resultado.ProductoId,
            CodigoUnique = resultado.CodigoCompleto,
            ColeccionTipo = tipoBD,
            MovCodigo = new MovimientoCodigo { CodigoCreadoId = resultado.CodigoCreadoId }
        }
    };

            // 🌟 CORRECCIÓN: Le pasamos directamente '_codigosGridList' para que los inserte en la lista REAL, no en una copia .ToList()
            _serviceMovimiento.AgregarCodigosIndividuales(_codigosGridList, producto.ProductoId, nuevosCodigos);

            // 5. Generar y sincronizar rangos matemáticos para el producto escaneado
            var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == producto.ProductoId).ToList();
            // Verificación obligatoria:
            var nuevosRangos = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosDelProducto);

            _serviceMovimiento.ReemplazarRangosProducto(_rangosProcesadosGlobal, producto.ProductoId, nuevosRangos);

            return true;
        }

        private VistaProductoGrid ObtenerOAgregarProductoEnLista(int productoId, string descripcion, decimal precio)
        {
            var prod = _productosGridList.FirstOrDefault(p => p.ProductoId == productoId);
            if (prod == null)
            {
                prod = new VistaProductoGrid
                {
                    ProductoId = productoId,
                    CodigoProducto = productoId.ToString(),
                    Descripcion = descripcion,
                    UnidadMedida = "UNIDAD",
                    Detalle = new MovimientoDetalle { ProductoId = productoId, CantidadIngreso = 0, CostoUnitario = precio }
                };
                _productosGridList.Add(prod);
            }
            return prod;
        }

        private async void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var win = new ImportarCodigos { Owner = Window.GetWindow(this) };
            try { win.EstadoPermitido = (cboMotivo.SelectedValue is int mv && mv == 1) ? 1 : 4; } catch { win.EstadoPermitido = 1; }
            if (win.ShowDialog() != true) return;

            var listaRaw = win.CodigosImportados ?? new List<string>();
            if (!listaRaw.Any()) return;

            this.Cursor = Cursors.Wait;
            try
            {
                var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaRaw);
                var codigosFisicosAgrupados = new Dictionary<int, List<VistaCodigoGrid>>();

                foreach (var raw in listaRaw)
                {
                    string norm = _serviceMovimiento.NormalizarCodigo(raw);
                    if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue) continue;

                    if (_codigosGridList.Any(c => string.Equals(c.CodigoUnique, tup.CodigoObj.Codigo, StringComparison.OrdinalIgnoreCase))) continue;

                    string tipoBD = await _serviceMovimiento.ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                    if (!codigosFisicosAgrupados.ContainsKey(tup.ProductoId.Value))
                        codigosFisicosAgrupados[tup.ProductoId.Value] = new List<VistaCodigoGrid>();

                    codigosFisicosAgrupados[tup.ProductoId.Value].Add(new VistaCodigoGrid
                    {
                        ProductoId = tup.ProductoId.Value,
                        CodigoUnique = tup.CodigoObj.Codigo,
                        ColeccionTipo = tipoBD,
                        MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id }
                    });
                }

                var prodService = new ProductoService();
                foreach (var kvp in codigosFisicosAgrupados)
                {
                    int productoId = kvp.Key;
                    var prodData = await prodService.ObtenerPorIdAsync(productoId);

                    string unidadDeBD = prodData?.Descripcion ?? "UNIDAD";

                    var producto = AgregarOActualizarProducto(
                    productoId,
                    prodData?.Descripcion ?? "Desconocido",
                    0,
                    prodData?.PrecioUnitario ?? 0,
                    unidadDeBD);

                    // 🌟 CORRECCIÓN: Pasamos '_codigosGridList' directamente sin el .ToList()
                    _serviceMovimiento.AgregarCodigosIndividuales(_codigosGridList, productoId, kvp.Value);

                    // 🌟 CORRECCIÓN: Generar y enlazar rangos en memoria para que aparezcan en el Modal de Edición
                    var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == productoId).ToList();
                    var nuevosRangos = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosDelProducto);

                    _serviceMovimiento.ReemplazarRangosProducto(_rangosProcesadosGlobal, productoId, nuevosRangos);
                }

                RefrescarGrillas();

                if (_productosGridList.Count > 0)
                    dgProductos.SelectedItem = _productosGridList.Last();

                MessageBox.Show($"Importación finalizada. Se procesaron {codigosFisicosAgrupados.Values.Sum(v => v.Count)} códigos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Error al importar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { this.Cursor = Cursors.Arrow; }
        }

        private VistaProductoGrid AgregarOActualizarProducto(int productoId, string descripcion, int cantidad, decimal precio, string unidadMedida)
        {
            var prodExistente = _productosGridList.FirstOrDefault(p => p.ProductoId == productoId);

            if (prodExistente != null)
            {
                prodExistente.Detalle ??= new MovimientoDetalle { ProductoId = productoId };
                prodExistente.Detalle.CantidadIngreso += cantidad;
                prodExistente.Detalle.CostoUnitario = precio;
                prodExistente.Cantidad = (int)prodExistente.Detalle.CantidadIngreso;
                return prodExistente;
            }
            else
            {
                var nuevoProd = new VistaProductoGrid
                {
                    ProductoId = productoId,
                    CodigoProducto = productoId.ToString(),
                    Descripcion = descripcion,
                    // 🌟 AHORA USAMOS LA UNIDAD RECIBIDA POR PARÁMETRO
                    UnidadMedida = unidadMedida,
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = productoId,
                        CantidadIngreso = cantidad,
                        CostoUnitario = precio
                    }
                };
                nuevoProd.Cantidad = cantidad;
                _productosGridList.Add(nuevoProd);
                return nuevoProd;
            }
        }
        // ==========================================
        // MÉTODOS VISUALES
        // ==========================================
        private void LimpiarFormulario()
        {
            _isUpdatingFromSelection = true;
            _productosGridList.Clear(); _codigosGridList.Clear(); _rangosProcesadosGlobal.Clear();
            txtNumSerie.Clear(); txtNumDocumento.Clear(); dtpFechaRecepcion.SelectedDate = null; cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Clear(); txtCodigoRazonSocial.Clear(); txtDireccion.Clear(); txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear(); txtObservacion.Clear(); txtSerieGuia.Clear(); txtNumeroGuia.Clear();
            dgProductos.ItemsSource = null; dgCodigos.ItemsSource = null;
            _personaComercialIdSeleccionada = null; _isUpdatingFromSelection = false;
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            txtNumSerie.IsEnabled = false; txtNumDocumento.IsEnabled = false; txtCodigoRazonSocial.IsEnabled = false; txtDireccion.IsEnabled = false;
            dtpFechaRecepcion.IsEnabled = habilitar; cboMotivo.IsEnabled = habilitar; txtRazonSocial.IsEnabled = habilitar; txtObservacion.IsEnabled = habilitar; txtSerieGuia.IsEnabled = habilitar; txtNumeroGuia.IsEnabled = habilitar;
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
            if (btnAgregar != null) btnAgregar.IsEnabled = !enEdicion;
            if (btnEditar != null) btnEditar.IsEnabled = !enEdicion;
            if (btnImprimir != null) btnImprimir.IsEnabled = !enEdicion;
            if (btnAnular != null) btnAnular.IsEnabled = !enEdicion;
        }

        private void ConfigurarDataGridsParaVirtualizacion()
        {
            if (dgCodigos != null) { VirtualizingPanel.SetIsVirtualizing(dgCodigos, true); VirtualizingPanel.SetVirtualizationMode(dgCodigos, VirtualizationMode.Recycling); dgCodigos.EnableRowVirtualization = true; dgCodigos.EnableColumnVirtualization = false; ScrollViewer.SetCanContentScroll(dgCodigos, true); }
            if (dgProductos != null) { VirtualizingPanel.SetIsVirtualizing(dgProductos, true); VirtualizingPanel.SetVirtualizationMode(dgProductos, VirtualizationMode.Recycling); dgProductos.EnableRowVirtualization = true; dgProductos.EnableColumnVirtualization = false; ScrollViewer.SetCanContentScroll(dgProductos, true); }
        }

        private void CboMotivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMotivo.SelectedItem == null) return;
            dynamic motivo = cboMotivo.SelectedItem; string descripcion = motivo.Descripcion?.Trim().ToUpper() ?? "";
            txtRazonSocial.IsEnabled = false; txtUbicacion.IsEnabled = false;
            txtRazonSocial.Clear(); txtCodigoRazonSocial.Clear(); txtDireccion.Clear(); txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear();
            if (descripcion == "COMPRA" || descripcion == "DEVOLUCION RECIBIDA") txtRazonSocial.IsEnabled = true;
            else if (descripcion == "OTROS") { txtRazonSocial.IsEnabled = true; txtUbicacion.IsEnabled = true; }
            else if (descripcion == "PROMOCION/PROMOTORIA" || descripcion == "TRANSFERENCIA ENTRE ALMACENES") txtUbicacion.IsEnabled = true;
        }

        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtRazonSocial.IsEnabled || _isUpdatingFromSelection) return;
            string textoBusqueda = txtRazonSocial.Text.Trim();
            if (textoBusqueda.Length >= 2)
            {
                try { var sugerencias = await _service.BuscarPorRazonSocialAsync(textoBusqueda); lstSugerencias.ItemsSource = sugerencias; popupSugerencias.IsOpen = sugerencias != null && sugerencias.Count > 0; } catch { }
            }
            else popupSugerencias.IsOpen = false;
        }

        private void LstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerencias.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true; _personaComercialIdSeleccionada = personaSeleccionada.Id;
                txtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial) ? personaSeleccionada.RazonSocial : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";
                txtCodigoRazonSocial.Text = personaSeleccionada.Id.ToString("D6"); txtDireccion.Text = personaSeleccionada.Direccion ?? "Sin dirección registrada";
                popupSugerencias.IsOpen = false; lstSugerencias.SelectedIndex = -1; _isUpdatingFromSelection = false;
            }
        }

        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtUbicacion.IsEnabled) return;
            string busqueda = txtUbicacion.Text;
            if (string.IsNullOrWhiteSpace(busqueda)) { popupUbicacion.IsOpen = false; return; }
            var resultados = _ubicacionService.BuscarUbicaciones(busqueda);
            if (resultados != null && resultados.Count > 0) { lstSugerenciasUbicacion.ItemsSource = resultados; popupUbicacion.IsOpen = true; } else popupUbicacion.IsOpen = false;
        }

        private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado) { txtUbicacion.Text = itemSeleccionado.Descripcion; txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString(); popupUbicacion.IsOpen = false; }
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is DependencyObject dep)
            {
                var sv = FindVisualChild<ScrollViewer>(dep);
                if (sv != null)
                {
                    double newOffset = sv.VerticalOffset - e.Delta / 3.0;
                    if (newOffset < 0) newOffset = 0;
                    if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;
                    sv.ScrollToVerticalOffset(newOffset);
                    e.Handled = true;
                }
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

        private void MovimientosUserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_printMode)
            {
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null)
                {
                    if (dep is Button btn) { if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave) break; e.Handled = true; return; }
                    dep = VisualTreeHelper.GetParent(dep);
                }
            }
            if (!txtRazonSocial.IsMouseOver && !popupSugerencias.IsMouseOver) popupSugerencias.IsOpen = false;
        }

        private void MovimientosUserControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_printMode) { if (Mouse.OverrideCursor != null) Mouse.OverrideCursor = null; return; }
            var dep = e.OriginalSource as DependencyObject; bool overBlockedButton = false;
            while (dep != null) { if (dep is Button btn) { if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave) { overBlockedButton = false; break; } overBlockedButton = true; break; } dep = VisualTreeHelper.GetParent(dep); }
            Mouse.OverrideCursor = overBlockedButton ? Cursors.Arrow : null;
        }
    }
}