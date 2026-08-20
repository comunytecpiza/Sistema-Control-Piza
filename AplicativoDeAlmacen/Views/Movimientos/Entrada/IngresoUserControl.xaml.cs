using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;
using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static AplicativoDeAlmacen.Data.DataConnection;
using MessageBox = System.Windows.MessageBox;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using Window = HandyControl.Controls.Window;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using LiveChartsCore.SkiaSharpView.WPF;

namespace AplicativoDeAlmacen.Views
{
    public partial class IngresoUserControl : UserControl
    {
        // ==========================================
        // VARIABLES GLOBALES
        // ==========================================
        private int? _currentMovimientoId = null;
        private readonly PersonaComercialService _service;
        private readonly IngresoMovimientoService _serviceMovimiento;
        private readonly UbicacionService _ubicacionService;
        private readonly ReporteExcelService _reporteService;
        private readonly ProductoService _productoService = new ProductoService();
        private List<VistaProductoGrid> _productosGridList;
        private List<VistaCodigoGrid> _codigosGridList;
        private List<RangoCodigoItem> _rangosProcesadosGlobal;

        private bool _anularMode = false;
        private Button _btnAnularNearSave = null;
        private bool _isUpdatingFromSelection = false;
        private int? _personaComercialIdSeleccionada = null;
        private int? _idUbicacionSeleccionada = null;

        private bool _isBuscandoDocumento = false;
        private bool _isRegistrandoMovimiento = false;
        private bool _printMode = false;
        private Button _btnPrintNearSave = null;
        private readonly DatabaseConnection _dbConnHelper = new DatabaseConnection();

        // ⏱️ TEMPORIZADORES PARA DEBOUNCE (PARTE A)
        private System.Windows.Threading.DispatcherTimer _timerRazonSocial;
        private System.Windows.Threading.DispatcherTimer _timerUbicacion;

        // ==========================================
        // CONSTRUCTOR
        // ==========================================
        public IngresoUserControl()
        {
            _productosGridList = new List<VistaProductoGrid>();
            _codigosGridList = new List<VistaCodigoGrid>();
            _rangosProcesadosGlobal = new List<RangoCodigoItem>();
            _productoService = new ProductoService();
            _service = new PersonaComercialService();
            _serviceMovimiento = new IngresoMovimientoService();
            _ubicacionService = new UbicacionService();
            _reporteService = new ReporteExcelService();

            // ⏱️ CONFIGURACIÓN DEL TIMER DE RAZÓN SOCIAL (300 ms)
            _timerRazonSocial = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerRazonSocial.Tick += async (s, e) =>
            {
                _timerRazonSocial.Stop();
                await EjecutarBusquedaRazonSocialAsync();
            };

            // ⏱️ CONFIGURACIÓN DEL TIMER DE UBICACIÓN (300 ms)
            _timerUbicacion = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerUbicacion.Tick += async (s, e) =>
            {
                _timerUbicacion.Stop();
                await EjecutarBusquedaUbicacionAsync();
            };

            InitializeComponent();

            ConfigurarEventosIniciales();
            EstablecerEstadoInicial();
        }

        // ==========================================
        // CONFIGURACIÓN DE EVENTOS
        // ==========================================
        public void ConfigurarEventosIniciales()
        {
            txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += LstSugerencias_SelectionChanged;
            cboMotivo.SelectionChanged += CboMotivo_SelectionChanged;
            txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
            lstSugerenciasUbicacion.SelectionChanged += LstSugerenciasUbicacion_SelectionChanged;

            this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
            this.PreviewMouseMove += MovimientosUserControl_PreviewMouseMove;
            Loaded += MovimientosUserControl_Loaded;

            btnAgregar.Click += BtnAgregar_Click;
            btnEditar.Click += BtnEditar_Click;
            btnImprimir.Click += BtnImprimir_Click;
            if (btnAnular != null)
            {
                btnAnular.Click -= BtnAnular_Click;
                btnAnular.Click += BtnAnular_Click;
            }
            btnGrabar.Click += RegistrarMovimientoCompleto;

            btnAgregarProducto.Click += BtnAgregarItem_Click;
            btnModificar.Click += BtnModificar_Click;
            btnEliminar.Click += BtnEliminar_Click;
            btnImportar.Click += BtnImportar_Click;
            btnEscanear.Click -= BtnEscanear_Click;
            btnEscanear.Click += BtnEscanear_Click;

            dgProductos.SelectionChanged += DgProductos_SelectionChanged;
            dgProductos.MouseDoubleClick += DgProductos_MouseDoubleClick;

            if (dgProductos != null) dgProductos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
            if (dgCodigos != null) dgCodigos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
        }

        private async void MovimientosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigurarDataGridsParaVirtualizacion();
            await CargarMotivosAsync();
            await CargarComboAlmacenesOrigenAsync();
        }

        private void EstablecerEstadoInicial()
        {
            _isBuscandoDocumento = false;
            _printMode = false;
            _anularMode = false;

            // 🧹 Doble candado: destruir botones dinámicos residuales
            LimpiarBotonesDinamicosCompletamente();

            if (grdFormulario != null) grdFormulario.Opacity = 1.0;
            if (dgProductos != null) { dgProductos.IsHitTestVisible = true; dgProductos.Focusable = true; }
            if (dgCodigos != null) { dgCodigos.IsHitTestVisible = true; dgCodigos.Focusable = true; }

            LimpiarFormulario();
            HabilitarCamposFormulario(false);
            GestionarBotonesPrincipales(enEdicion: false);
        }

        private async Task CargarComboAlmacenesOrigenAsync()
        {
            try
            {
                using var conn = _dbConnHelper.GetConnection();
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                string query = (miAlmacenId == ALMACEN_CENTRAL_ID)
                    ? "SELECT id, nombre FROM almacenes WHERE id != @miAlmacen AND estado_id = 1 ORDER BY nombre ASC"
                    : "SELECT id, nombre FROM almacenes WHERE id = @centralId AND estado_id = 1";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var p1 = cmd.CreateParameter(); p1.ParameterName = "@miAlmacen"; p1.Value = miAlmacenId; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@centralId"; p2.Value = ALMACEN_CENTRAL_ID; cmd.Parameters.Add(p2);

                var listaAlmacenes = new List<dynamic>();
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    listaAlmacenes.Add(new { Id = rdr.GetInt32(0), Nombre = rdr.GetString(1) });
                }

                cboAlmacenDestino.ItemsSource = listaAlmacenes;
                cboAlmacenDestino.DisplayMemberPath = "Nombre";
                cboAlmacenDestino.SelectedValuePath = "Id";

                cboAlmacenDestino.SelectedIndex = -1;
                cboAlmacenDestino.SelectedValue = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar almacenes origen: {ex.Message}");
            }
        }


        public async void CargarDocumentoParaConsulta(int movimientoSalidaId)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                // 🌟 1. ASEGURAMOS CARGAR LOS COMBOS ANTES DE ASIGNARLOS
                await CargarMotivosAsync();
                await CargarComboAlmacenesOrigenAsync();

                var movCompleto = await _serviceMovimiento.GetSalidaParaRecepcionAsync(movimientoSalidaId);
                if (movCompleto == null || movCompleto.Movimiento == null)
                {
                    MessageBox.Show("No se encontró el registro de transferencia especificado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LimpiarFormulario();

                cboMotivo.SelectedValue = 4;
                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                if (movCompleto.Movimiento.AlmacenOrigenId.HasValue)
                {
                    cboAlmacenDestino.SelectedValue = movCompleto.Movimiento.AlmacenOrigenId.Value;
                    cboAlmacenDestino.IsEnabled = false;
                }

                txtSerieGuia.Clear();
                txtNumeroGuia.Clear();
                txtObservacion.Clear();

                _productosGridList.Clear();
                _codigosGridList.Clear();
                _rangosProcesadosGlobal.Clear();

                var prodService = new ProductoService();

                foreach (var det in movCompleto.Detalles)
                {
                    string descripcionProducto = await _serviceMovimiento.ObtenerDescripcionProductoAsync(det.ProductoId);
                    var prodData = await prodService.ObtenerPorIdAsync(det.ProductoId);

                    var vp = new VistaProductoGrid
                    {
                        ProductoId = det.ProductoId,
                        CodigoProducto = det.ProductoId.ToString(),
                        Descripcion = descripcionProducto,
                        UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                        Cantidad = det.CantidadIngreso,
                        Detalle = new MovimientoDetalle
                        {
                            ProductoId = det.ProductoId,
                            CantidadIngreso = det.CantidadIngreso,
                            CostoUnitario = det.CostoUnitario ?? 0
                        }
                    };
                    _productosGridList.Add(vp);
                }

                foreach (var r in movCompleto.Rangos)
                {
                    _rangosProcesadosGlobal.Add(r);
                    var codigosReconstruidos = _serviceMovimiento.ReconstruirCodigosDesdeRangos(new List<RangoCodigoItem> { r });
                    foreach (var c in codigosReconstruidos)
                    {
                        _codigosGridList.Add(c);
                    }
                }

                RefrescarGrillas();

                HabilitarCamposFormulario(true);
                txtNumSerie.Text = "0001";
                txtNumDocumento.Text = "[ AUTOMÁTICO ]";
                txtNumSerie.IsEnabled = false;
                txtNumDocumento.IsEnabled = false;

                txtSerieGuia.Focus();

                MessageBox.Show("Transferencia cargada. Ingrese el N° de Guía de Remisión que trae el transportista y presione 'Guardar Entrada'.", "Recepción Lista", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar la transferencia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }
        public async void CargarDocumentoParaConsulta(string serie, string numero)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                _printMode = true; // Activa modo lectura / impresión

                // Cargamos las listas de los combos para que no queden vacíos
                await CargarMotivosAsync();
                await CargarComboAlmacenesOrigenAsync();

                await LoadMovimientoBySerieNumeroAsync(serie, numero);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el documento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }


        private async Task CargarMotivosAsync()
        {
            try
            {
                this.Cursor = Cursors.Wait;
                var todosLosMotivos = await _serviceMovimiento.ObtenerMotivosProductosAsync();

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                if (miAlmacenId != ALMACEN_CENTRAL_ID)
                {
                    todosLosMotivos = todosLosMotivos.Where(m => m.Id != 1).ToList();
                }

                cboMotivo.ItemsSource = todosLosMotivos;
                cboMotivo.DisplayMemberPath = "Descripcion";
                cboMotivo.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los motivos: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { this.Cursor = Cursors.Arrow; }
        }

        private async void TxtNumDocumento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 🛑 1. CANDADO ATÓMICO ANTI DOBLE ENTER
                e.Handled = true;

                if (_isBuscandoDocumento) return;
                _isBuscandoDocumento = true;

                string serie = txtNumSerie?.Text?.Trim() ?? "0001";
                string numero = txtNumDocumento?.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(numero))
                {
                    MessageBox.Show("Ingrese el número de documento para cargar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _isBuscandoDocumento = false;
                    return;
                }

                if (int.TryParse(numero, out int numVal))
                {
                    numero = numVal.ToString("D7");
                    txtNumDocumento.Text = numero;
                }

                // 🛑 2. BLOQUEO INMEDIATO DE LA CAJA AL PRIMER ENTER
                txtNumDocumento.IsReadOnly = true;
                txtNumDocumento.Background = System.Windows.Media.Brushes.WhiteSmoke;

                // 🛑 3. LIMPIEZA PREVENTIVA DE MEMORIA PARA EVITAR DUPLICACIONES
                _productosGridList.Clear();
                _codigosGridList.Clear();
                _rangosProcesadosGlobal.Clear();

                try
                {
                    this.Cursor = Cursors.Wait;
                    _currentMovimientoId = null;

                    await LoadMovimientoBySerieNumeroAsync(serie, numero);

                    if (!_currentMovimientoId.HasValue)
                    {
                        MessageBox.Show("Movimiento de ingreso no encontrado en este almacén. Verifique el número e intente de nuevo.",
                                        "Búsqueda fallida", MessageBoxButton.OK, MessageBoxImage.Warning);

                        // 🔓 SI NO EXISTE: RESTAURAR LA CAJA PARA QUE PUEDA VOLVER A ESCRIBIR SIN "DESCONECTARSE"
                        txtNumDocumento.IsReadOnly = false;
                        txtNumDocumento.Background = System.Windows.Media.Brushes.White;
                        txtNumDocumento.Focus();
                        txtNumDocumento.SelectAll();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al buscar movimiento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _currentMovimientoId = null;

                    // 🔓 RESTAURACIÓN EN CASO DE ERROR
                    txtNumDocumento.IsReadOnly = false;
                    txtNumDocumento.Background = System.Windows.Media.Brushes.White;
                    txtNumDocumento.Focus();
                }
                finally
                {
                    _isBuscandoDocumento = false; // 🔓 Libera el candado
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        private void ShowAnularButtonNearSave()
        {
            if (_btnAnularNearSave != null) return;

            if (btnGrabar?.Parent is Panel parentPanel)
            {
                _btnAnularNearSave = new Button
                {
                    Content = "💥 CONFIRMAR ANULACIÓN",
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    Foreground = btnGrabar.Foreground,
                    Style = btnGrabar.Style,
                    Margin = btnGrabar.Margin,
                    Padding = btnGrabar.Padding,
                    MinWidth = btnGrabar.MinWidth,
                    Height = btnGrabar.Height,
                    FontSize = btnGrabar.FontSize,
                    FontWeight = FontWeights.Bold
                };

                _btnAnularNearSave.Click += EjecutarAnulacionDefinitiva_Click;
                parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabar) + 1, _btnAnularNearSave);
                btnGrabar.IsEnabled = false;
            }
        }

        private async void EjecutarAnulacionDefinitiva_Click(object sender, RoutedEventArgs e)
        {
            if (!_currentMovimientoId.HasValue) return;

            // 🌟 1. VALIDACIÓN ESTRICTA DE TRANSFERENCIA
            if (cboMotivo.SelectedValue != null && Convert.ToInt32(cboMotivo.SelectedValue) == 4)
            {
                MessageBox.Show("No se pueden anular transferencias entre almacenes desde esta pantalla.",
                                "Operación Restringida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmacion = MessageBox.Show($"⚠️ ¿Está absolutamente seguro de ANULAR por completo el movimiento actual ({txtNumSerie.Text}-{txtNumDocumento.Text})?\n\nEsta acción revertirá los estados de todos los códigos en el kárdex.", "Confirmar Anulación Histórica", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmacion != MessageBoxResult.Yes) return;

            try
            {
                this.Cursor = Cursors.Wait;
                if (_btnAnularNearSave != null) _btnAnularNearSave.IsEnabled = false;

                pbCargaMasiva.Visibility = Visibility.Visible;
                lblPorcentajeCarga.Visibility = Visibility.Visible;
                pbCargaMasiva.Value = 0;
                lblPorcentajeCarga.Text = "0% Anulando lote...";

                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (pbCargaMasiva != null) pbCargaMasiva.Value = percent;
                        if (lblPorcentajeCarga != null) lblPorcentajeCarga.Text = $"{percent}% Revirtiendo Kárdex...";
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });

                bool resultado = await Task.Run(async () =>
                    await _serviceMovimiento.AnularMovimientoCompletoAsync(_currentMovimientoId.Value, progress)
                );

                if (resultado)
                {
                    MessageBox.Show("¡El movimiento y todo su lote de códigos han sido anulados con éxito!", "Operación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (_btnAnularNearSave != null && _btnAnularNearSave.Parent is Panel p) { p.Children.Remove(_btnAnularNearSave); _btnAnularNearSave = null; }
                    _anularMode = false;

                    EstablecerEstadoInicial();
                    EventBus.NotificarMovimientosChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo ejecutar la anulación:\n\n{ex.Message}", "Restricción de Kárdex", MessageBoxButton.OK, MessageBoxImage.Stop);
                if (_btnAnularNearSave != null) _btnAnularNearSave.IsEnabled = true;
            }
            finally
            {
                pbCargaMasiva.Visibility = Visibility.Collapsed;
                lblPorcentajeCarga.Visibility = Visibility.Collapsed;
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BloquearParaAnulacionVisual()
        {
            // 1. Sombra / Bloquea por completo todo el formulario de cabecera
            grdFormulario.IsEnabled = false;

            // 2. Congela los DataGrids completamente
            if (dgProductos != null)
            {
                dgProductos.IsReadOnly = true;
                dgProductos.IsHitTestVisible = false;
                dgProductos.Focusable = false;
            }
            if (dgCodigos != null)
            {
                dgCodigos.IsReadOnly = true;
                dgCodigos.IsHitTestVisible = false;
                dgCodigos.Focusable = false;
            }

            // 3. Apaga botones secundarios
            btnAgregar.IsEnabled = false;
            btnEditar.IsEnabled = false;
            btnImprimir.IsEnabled = false;
            btnAnular.IsEnabled = false;

            btnAgregarProducto.IsEnabled = false;
            btnModificar.IsEnabled = false;
            btnEliminar.IsEnabled = false;
            btnImportar.IsEnabled = false;
            btnEscanear.IsEnabled = false;
            btnGrabar.IsEnabled = false;

            // 🌟 UNICA EXCEPCIÓN: Cancelar
            if (btnCancelar != null) btnCancelar.IsEnabled = true;
        }

        private async Task LoadMovimientoBySerieNumeroAsync(string serie, string numero)
        {
            LimpiarFormulario();

            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

            var movimientoComp = await _serviceMovimiento.GetMovimientoCompletoAsync(serie, numero, miAlmacenId);

            if (movimientoComp == null || movimientoComp.Movimiento == null)
            {
                MessageBox.Show("No se encontró el movimiento especificado en este Almacén.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var movimiento = movimientoComp.Movimiento;
            _currentMovimientoId = movimiento.Id;

            if (movimiento.FechaMovimiento.HasValue)
                dtpFechaRecepcion.SelectedDate = movimiento.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue);

            txtNumSerie.Text = movimiento.SerieDocumento;
            txtNumDocumento.Text = movimiento.NumeroDocumento;

            _personaComercialIdSeleccionada = movimiento.PersonaComercialId;
            if (movimiento.PersonaComercialId.HasValue)
            {
                txtCodigoRazonSocial.Text = movimiento.PersonaComercialId.Value.ToString("D6");
                try
                {
                    var persona = await _service.ObtenerPorIdAsync(movimiento.PersonaComercialId.Value);
                    if (persona != null)
                    {
                        _isUpdatingFromSelection = true;
                        txtRazonSocial.Text = !string.IsNullOrEmpty(persona.RazonSocial) ? persona.RazonSocial : $"{persona.Nombres} {persona.ApellidoPaterno}";
                        txtDireccion.Text = persona.Direccion ?? "Sin dirección registrada";
                        _isUpdatingFromSelection = false;
                    }
                }
                catch { txtRazonSocial.Text = $"Cliente ID: {movimiento.PersonaComercialId.Value}"; }
            }

            txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
            txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
            txtObservacion.Text = movimiento.Observacion ?? string.Empty;

            if (movimiento.UbicacionId.HasValue)
            {
                try
                {
                    var todas = await _ubicacionService.ObtenerTodasAsync();
                    var ubic = todas?.FirstOrDefault(u => u.Id == movimiento.UbicacionId.Value);
                    if (ubic != null)
                    {
                        _isUpdatingFromSelection = true;
                        txtUbicacion.Text = ubic.Descripcion ?? string.Empty;
                        txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(ubic.Direccion) ? "Sin dirección registrada" : ubic.Direccion;
                        _idUbicacionSeleccionada = ubic.Id;
                        txtCodigoUbicacion.Text = ubic.Id.ToString();
                        _isUpdatingFromSelection = false;
                    }
                }
                catch { }
            }

            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            var prodService = new ProductoService();
            foreach (var det in movimientoComp.Detalles)
            {
                string descripcionProducto = await _serviceMovimiento.ObtenerDescripcionProductoAsync(det.ProductoId);
                var prodData = await prodService.ObtenerPorIdAsync(det.ProductoId);
                string unitBD = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD";

                var vp = new VistaProductoGrid
                {
                    ProductoId = det.ProductoId,
                    CodigoProducto = det.ProductoId.ToString(),
                    Descripcion = descripcionProducto,
                    UnidadMedida = unitBD,
                    Cantidad = det.CantidadIngreso,
                    Detalle = new MovimientoDetalle { Id = det.Id, ProductoId = det.ProductoId, CantidadIngreso = det.CantidadIngreso, CostoUnitario = det.CostoUnitario }
                };

                var rangosForDet = movimientoComp.Rangos.Where(r => r.MovimientoDetalleId == det.Id).ToList();
                foreach (var r in rangosForDet)
                {
                    _rangosProcesadosGlobal.Add(r);
                    for (int i = r.DesdeNum; i <= r.HastaNum; i++)
                    {
                        if (r.DesdeNum == -1) continue;
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = det.Id },
                            CodigoUnique = $"{r.AbreviaturaBase}-{i:D7}",
                            ColeccionTipo = r.ColeccionTipo ?? string.Empty,
                            ProductoId = r.productoId
                        });
                    }

                    if (r.DesdeNum == -1)
                    {
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = det.Id },
                            CodigoUnique = r.AbreviaturaBase,
                            ColeccionTipo = r.ColeccionTipo ?? string.Empty,
                            ProductoId = r.productoId
                        });
                    }
                }
                _productosGridList.Add(vp);
            }

            var listaFusionada = _serviceMovimiento.MergeDuplicateProducts(_productosGridList.ToList());
            _productosGridList.Clear();
            foreach (var item in listaFusionada) _productosGridList.Add(item);

            RefrescarGrillas();

            cboMotivo.SelectedValue = movimiento.MotivoProductoId;
            if (movimiento.AlmacenOrigenId.HasValue)
                cboAlmacenDestino.SelectedValue = movimiento.AlmacenOrigenId.Value;
            else if (movimiento.AlmacenDestinoId.HasValue)
                cboAlmacenDestino.SelectedValue = movimiento.AlmacenDestinoId.Value;

            txtNumDocumento.IsReadOnly = true;
            txtNumDocumento.Background = System.Windows.Media.Brushes.WhiteSmoke;

            
            
            // 🌟 2. EVALUACIÓN SEGÚN EL MODO ACTIVADO
            if (_printMode)
            {
                BloquearParaImpresion();
                ShowPrintButtonNearSave();
            }
            else if (_anularMode)
            {
                BloquearParaAnulacionVisual();
                ShowAnularButtonNearSave();
            }
            else
            {
                HabilitarCamposFormulario(true);
                btnGrabar.IsEnabled = true;
                btnCancelar.IsEnabled = true;

                cboMotivo.IsEnabled = true;
                dtpFechaRecepcion.IsEnabled = true;
                txtObservacion.IsEnabled = true;
                txtSerieGuia.IsEnabled = true;
                txtNumeroGuia.IsEnabled = true;

                btnAgregarProducto.IsEnabled = true;
                btnModificar.IsEnabled = true;
                btnEliminar.IsEnabled = true;
                btnImportar.IsEnabled = true;
                btnEscanear.IsEnabled = true;
            }
        }

        private void LimpiarBotonesDinamicosCompletamente()
        {
            // 🛑 1. Eliminar botón de Anulación si existe en el panel
            if (_btnAnularNearSave != null)
            {
                if (_btnAnularNearSave.Parent is Panel panelAnular)
                {
                    panelAnular.Children.Remove(_btnAnularNearSave);
                }
                _btnAnularNearSave.Click -= EjecutarAnulacionDefinitiva_Click;
                _btnAnularNearSave = null;
            }

            // 🛑 2. Eliminar botón de Impresión/Exportar si existe en el panel
            if (_btnPrintNearSave != null)
            {
                if (_btnPrintNearSave.Parent is Panel panelPrint)
                {
                    panelPrint.Children.Remove(_btnPrintNearSave);
                }
                _btnPrintNearSave = null;
            }

            // 🌟 3. Restaurar siempre la visibilidad del botón Grabar original
            if (btnGrabar != null)
            {
                btnGrabar.Visibility = Visibility.Visible;
                btnGrabar.IsEnabled = false;
            }
        }
        private void PrepararCajaBusqueda()
        {
            txtNumDocumento.Text = string.Empty;
            txtNumDocumento.IsReadOnly = false;
            txtNumDocumento.IsEnabled = true;
            txtNumDocumento.Background = System.Windows.Media.Brushes.White;
            txtNumDocumento.Foreground = System.Windows.Media.Brushes.Black;
            txtNumDocumento.FontWeight = FontWeights.Normal;
            txtNumDocumento.FontStyle = FontStyles.Normal;
            txtNumDocumento.Focus();
        }
        private async void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            try
            {
                _currentMovimientoId = null;
                LimpiarFormulario();
                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                HabilitarCamposFormulario(true);

                // 🛡️ BLOQUEO TOTAL DE CABECERA SUPERIOR
                GestionarBotonesPrincipales(enEdicion: true);
                if (btnCancelar != null) btnCancelar.IsEnabled = true;

                txtNumSerie.Text = "0001";

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                string siguienteCorrelativo = "0000001";

                using (var conn = _dbConnHelper.GetConnection())
                {
                    var dbConn = (System.Data.Common.DbConnection)conn;
                    await dbConn.OpenAsync();

                    string queryMax = @"
                SELECT COALESCE(MAX(CAST(m.numero_documento AS INT)), 0) + 1 
                FROM movimientos m WITH (NOLOCK)
                INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                WHERE m.serie_documento = '0001' 
                  AND mp.tipo_movimiento_id = 1
                  AND ISNULL(m.almacen_id, ISNULL(m.almacen_destino_id, 1)) = @almId";

                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryMax);
                    var p = cmd.CreateParameter(); p.ParameterName = "@almId"; p.Value = miAlmacenId; cmd.Parameters.Add(p);

                    object res = await cmd.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value)
                    {
                        siguienteCorrelativo = Convert.ToInt32(res).ToString("D7");
                    }
                }

                txtNumDocumento.Text = "[ AUTOMÁTICO ]";
                txtNumDocumento.IsReadOnly = true;
                txtNumDocumento.Background = System.Windows.Media.Brushes.WhiteSmoke;
                txtNumDocumento.Foreground = System.Windows.Media.Brushes.Gray;
                txtNumDocumento.FontStyle = FontStyles.Italic;
                txtNumDocumento.FontWeight = FontWeights.Normal;

                txtNumSerie.IsEnabled = false;
                txtNumDocumento.IsEnabled = false;

                cboMotivo.SelectedValue = 1;
                CboMotivo_SelectionChanged(cboMotivo, null);

                cboMotivo.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar correlativo: {ex.Message}");
            }
            finally { this.Cursor = Cursors.Arrow; }
        }



        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            _printMode = false;
            _anularMode = false;

            LimpiarBotonesDinamicosCompletamente();
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;

            // 🛡️ BLOQUEO TOTAL DE CABECERA SUPERIOR
            GestionarBotonesPrincipales(enEdicion: true);
            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            MessageBox.Show("Modo Edición activado.\n\nEscriba el N° de Documento de ingreso y presione ENTER para cargar y modificar sus datos.", "Editar Movimiento", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_printMode) return;
            _printMode = true;
            _anularMode = false;

            LimpiarBotonesDinamicosCompletamente();
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;

            // 🛡️ BLOQUEO TOTAL DE CABECERA SUPERIOR
            GestionarBotonesPrincipales(enEdicion: true);
            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            MessageBox.Show("Modo Imprimir / Consulta activado.\n\nIngrese el número de documento de ingreso y presione ENTER para visualizar el registro.",
                           "Modo Consulta", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var resultado = MessageBox.Show("¿Está seguro que desea cancelar la operación actual?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resultado == MessageBoxResult.Yes)
            {
                _printMode = false;
                _anularMode = false;
                _isBuscandoDocumento = false;

                // 🔓 Desenganchar eventos residuales
                if (txtNumDocumento != null)
                {
                    txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
                }

                // 🧹 DESTRUIR Y REMOVER BOTONES DINÁMICOS DE INMEDIATO
                LimpiarBotonesDinamicosCompletamente();

                EstablecerEstadoInicial();
            }
        }

        private async void RegistrarMovimientoCompleto(object sender, RoutedEventArgs e)
        {
            if (_isRegistrandoMovimiento) return;

            if (cboMotivo.SelectedValue == null)
            {
                MessageBox.Show("Debe seleccionar un Motivo.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int idMotivo = Convert.ToInt32(cboMotivo.SelectedValue);

            // 🛒 1. COMPRA (1) O DEVOLUCIÓN RECIBIDA (2): Obligatorio Razón Social (Proveedor o Cliente)
            if (idMotivo == 1 || idMotivo == 2)
            {
                if (string.IsNullOrWhiteSpace(txtRazonSocial.Text) || !_personaComercialIdSeleccionada.HasValue)
                {
                    MessageBox.Show("Para este motivo es obligatorio seleccionar la Razón Social.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            // 🚚 2. TRANSFERENCIA ENTRE ALMACENES (4): Ubicación O Almacén de Procedencia
            else if (idMotivo == 4)
            {
                bool tieneUbicacion = !string.IsNullOrWhiteSpace(txtUbicacion.Text) && _idUbicacionSeleccionada.HasValue;
                bool tieneAlmacenFisico = cboAlmacenDestino.SelectedValue != null;

                if (!tieneUbicacion && !tieneAlmacenFisico)
                {
                    MessageBox.Show("Para una Transferencia debe seleccionar al menos una Ubicación Referencial O un Almacén Físico de Procedencia.", "Validación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            // 🔄 3. OTROS (13) O PROMOTORÍA / MIGRACIÓN (3): Permite Razón Social, Ubicación o AMBOS
            else if (idMotivo == 13 || idMotivo == 3)
            {
                bool tieneRazonSocial = !string.IsNullOrWhiteSpace(txtRazonSocial.Text) && _personaComercialIdSeleccionada.HasValue;
                bool tieneUbicacion = !string.IsNullOrWhiteSpace(txtUbicacion.Text) && _idUbicacionSeleccionada.HasValue;

                if (!tieneRazonSocial && !tieneUbicacion)
                {
                    MessageBox.Show("Para el motivo seleccionado debe ingresar al menos una Razón Social O una Ubicación (o puede completar ambos).", "Validación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 📄 Validación de Guía / Comprobante
            if (string.IsNullOrWhiteSpace(txtSerieGuia.Text) || string.IsNullOrWhiteSpace(txtNumeroGuia.Text))
            {
                MessageBox.Show("Debe ingresar la Serie y el Número de Guía o Comprobante.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 📦 Validación de Grilla de Productos
            if (_productosGridList == null || !_productosGridList.Any())
            {
                MessageBox.Show("Debe agregar al menos un producto antes de registrar la operación.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentMovimientoId.HasValue)
            {
                _productosGridList = _serviceMovimiento.MergeDuplicateProducts(_productosGridList.ToList());
            }

            try
            {
                _isRegistrandoMovimiento = true;
                btnGrabar.IsEnabled = false;
                btnCancelar.IsEnabled = false;
                Cursor = Cursors.Wait;

                _rangosProcesadosGlobal = _serviceMovimiento.GenerarRangosDesdeCodigos(_codigosGridList);

                var solicitud = CrearSolicitudMovimiento();

                pbCargaMasiva.Visibility = Visibility.Visible;
                lblPorcentajeCarga.Visibility = Visibility.Visible;
                pbCargaMasiva.Value = 0;
                lblPorcentajeCarga.Text = "0% Guardando...";

                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (pbCargaMasiva != null) pbCargaMasiva.Value = percent;
                        if (lblPorcentajeCarga != null) lblPorcentajeCarga.Text = $"{percent}% Guardando Lote...";
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });

                bool resultado = await Task.Run(async () =>
                    await _serviceMovimiento.RegistrarMovimientoCompletoAsync(
                        solicitud.Movimiento,
                        solicitud.Productos.ToList(),
                        _rangosProcesadosGlobal.ToList(),
                        solicitud.Movimiento.UbicacionId ?? 0,
                        solicitud.MovimientoId,
                        progress)
                );

                if (resultado)
                {
                    string numFinal = solicitud.Movimiento.NumeroDocumento;
                    string serieFinal = solicitud.Movimiento.SerieDocumento;

                    txtNumDocumento.Text = numFinal;
                    txtNumDocumento.Foreground = System.Windows.Media.Brushes.Black;
                    txtNumDocumento.FontWeight = FontWeights.Bold;
                    txtNumDocumento.FontStyle = FontStyles.Normal;
                    txtNumDocumento.Background = System.Windows.Media.Brushes.White;

                    MessageBox.Show($"¡Movimiento registrado con éxito!\nNúmero: {serieFinal}-{numFinal}",
                                    "Guardado Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);

                    EstablecerEstadoInicial();
                    EventBus.NotificarMovimientosChanged();
                }
            }
            catch (Exception ex)
            {
                string detalleError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                MessageBox.Show($"Error al guardar movimiento:\n{detalleError}", "Error de Validación de Kárdex", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                pbCargaMasiva.Visibility = Visibility.Collapsed;
                lblPorcentajeCarga.Visibility = Visibility.Collapsed;
                _isRegistrandoMovimiento = false;
                btnGrabar.IsEnabled = true;
                btnCancelar.IsEnabled = true;
                Cursor = Cursors.Arrow;
            }
        }

        private SolicitudMovimiento CrearSolicitudMovimiento()
        {
            foreach (var p in _productosGridList)
            {
                p.Detalle ??= new MovimientoDetalle { ProductoId = p.ProductoId };

                int codigosReales = _codigosGridList.Count(c => c.ProductoId == p.ProductoId);

                if (codigosReales > 0)
                {
                    p.Detalle.CantidadIngreso = codigosReales;
                    p.Cantidad = codigosReales;
                }
                else
                {
                    // 🎒 PRODUCTO SIN CÓDIGO: Forzamos a que tome exactamente la cantidad visual de la grilla (ej. 100)
                    p.Detalle.CantidadIngreso = p.Cantidad > 0 ? p.Cantidad : p.Detalle.CantidadIngreso;
                }
            }

            int miAlmacenActual = SesionSistema.AlmacenActual?.Id ?? 1;

            int? almacenDestinoId = cboAlmacenDestino.SelectedValue != null
                ? Convert.ToInt32(cboAlmacenDestino.SelectedValue)
                : (int?)null;

            int idMotivoIngreso = Convert.ToInt32(cboMotivo.SelectedValue);
            int? almacenOrigenReal = null;
            int? almacenDestinoReal = miAlmacenActual;

            if (idMotivoIngreso == 4)
            {
                almacenOrigenReal = cboAlmacenDestino.SelectedValue != null
                    ? Convert.ToInt32(cboAlmacenDestino.SelectedValue)
                    : (int?)null;

                almacenDestinoReal = miAlmacenActual;
            }

            return new SolicitudMovimiento
            {
                Movimiento = new Movimiento
                {
                    FechaMovimiento = dtpFechaRecepcion.SelectedDate.HasValue
                        ? DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate.Value)
                        : DateOnly.FromDateTime(DateTime.Today),
                    SerieDocumento = txtNumSerie.Text.Trim(),
                    NumeroDocumento = txtNumDocumento.Text.Trim(),
                    MotivoProductoId = idMotivoIngreso,

                    // 🌟 Si es motivo 4 (Transferencia entre almacenes) y hay almacén físico, UbicacionId es null.
                    // 🌟 Para cualquier otro motivo (como Devolución/Promotoría), SIEMPRE guarda _idUbicacionSeleccionada.
                    UbicacionId = (idMotivoIngreso == 4 && almacenOrigenReal.HasValue)
                    ? (int?)null
                    : _idUbicacionSeleccionada,

                    AlmacenId = miAlmacenActual,
                    AlmacenOrigenId = almacenOrigenReal,
                    AlmacenDestinoId = almacenDestinoReal,

                    UsuarioId = SesionSistema.UsuarioActual?.Id ?? 1,
                    PersonaComercialId = _personaComercialIdSeleccionada,
                    SerieGuia = txtSerieGuia.Text.Trim(),
                    NumeroGuia = txtNumeroGuia.Text.Trim(),
                    Observacion = txtObservacion.Text.Trim()
                },
                Productos = _productosGridList,
                Codigos = _codigosGridList,
                MovimientoId = _currentMovimientoId
            };
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

        private void GenerateExcelFromCurrentLoadedMovement()
        {
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
            // 1. Bloqueamos y sombreados todo el formulario principal de cabecera
            grdFormulario.IsEnabled = false;
            grdFormulario.Opacity = 0.65;

            // 🌟 2. Sombreado global para la sección inferior (botones de ítems y tablas de productos)
            // Buscamos el contenedor general o aplicamos opacidad de solo lectura a los paneles de acción:
            if (btnAgregarProducto?.Parent is Panel panelAcciones)
            {
                panelAcciones.IsEnabled = false;
                panelAcciones.Opacity = 0.65; // 👈 Atenúa los botones de Agregar, Modificar, Eliminar, Importar, Escanear
            }

            // 3. Permite interacción de selección en DataGrids, pero en modo lectura
            if (dgProductos != null)
            {
                dgProductos.IsEnabled = true;
                dgProductos.IsReadOnly = true;
                dgProductos.IsHitTestVisible = true;
                dgProductos.Focusable = true;
            }
            if (dgCodigos != null)
            {
                dgCodigos.IsEnabled = true;
                dgCodigos.IsReadOnly = true;
                dgCodigos.IsHitTestVisible = true;
                dgCodigos.Focusable = true;
            }

            // 4. Bloqueamos botones de edición de la barra superior
            btnAgregar.IsEnabled = false;
            btnEditar.IsEnabled = false;
            btnImprimir.IsEnabled = false;
            btnAnular.IsEnabled = false;
            btnGrabar.IsEnabled = false;

            if (btnCancelar != null) btnCancelar.IsEnabled = true; // El único activo para salir
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid productoSeleccionado) return;

            string filtro = txtBuscarCodigo.Text.Trim().ToLower();

            var todosLosCodigosDelProducto = _codigosGridList
                .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                .ToList();

            if (string.IsNullOrEmpty(filtro))
            {
                dgCodigos.ItemsSource = todosLosCodigosDelProducto.Take(500).ToList();
                lblResumenCodigos.Text = todosLosCodigosDelProducto.Count > 500
                    ? $"500 (Viendo) / {todosLosCodigosDelProducto.Count}"
                    : $"{todosLosCodigosDelProducto.Count} / {todosLosCodigosDelProducto.Count}";
            }
            else
            {
                var filtrados = todosLosCodigosDelProducto
                    .Where(c => c.CodigoUnique.ToLower().Contains(filtro))
                    .ToList();

                dgCodigos.ItemsSource = filtrados;
                lblResumenCodigos.Text = $"{filtrados.Count} (Encontrados) / {todosLosCodigosDelProducto.Count}";
            }
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🛡️ 1. Si estamos en modo Consulta/Impresión o Anulación, bloqueamos los botones de edición de detalle
            if (_printMode || _anularMode)
            {
                if (btnModificar != null) btnModificar.IsEnabled = false;
                if (btnEliminar != null) btnEliminar.IsEnabled = false;
            }
            else
            {
                // Comportamiento normal en edición o creación
                if (dgProductos.SelectedItem != null)
                {
                    if (btnModificar != null) btnModificar.IsEnabled = true;
                    if (btnEliminar != null) btnEliminar.IsEnabled = true;
                }
                else
                {
                    if (btnModificar != null) btnModificar.IsEnabled = false;
                    if (btnEliminar != null) btnEliminar.IsEnabled = false;
                }
            }

            // 🌟 2. PERMITIR CARGAR Y FILTRAR CÓDIGOS: Siempre llamamos a esta función para que la grilla de la derecha muestre los códigos del producto seleccionado
            MostrarCodigosProductoSeleccionado();
        }

        private void RefrescarGrillas()
        {
            if (dgProductos == null) return;

            try
            {
                dgProductos.CancelEdit(DataGridEditingUnit.Cell);
                dgProductos.CancelEdit(DataGridEditingUnit.Row);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Aviso en descarte de edición del DataGrid: {ex.Message}");
            }

            // 🌟 BLINDAJE DE CANTIDADES PARA PRODUCTOS CON Y SIN CÓDIGO:
            foreach (var prod in _productosGridList)
            {
                int cantidadCodigosEnGrilla = _codigosGridList.Count(x => x.ProductoId == prod.ProductoId);

                if (cantidadCodigosEnGrilla > 0)
                {
                    // Si tiene códigos unitarios (libros), manda el conteo de los códigos
                    prod.Cantidad = cantidadCodigosEnGrilla;
                    if (prod.Detalle != null) prod.Detalle.CantidadIngreso = cantidadCodigosEnGrilla;
                }
                else if (prod.Detalle != null && prod.Detalle.CantidadIngreso > 0)
                {
                    // 🎒 Si es un producto SIN CÓDIGO (mochilas), respeta estrictamente lo que trajo la BD (ej. 60)
                    prod.Cantidad = (int)prod.Detalle.CantidadIngreso;
                }
            }

            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;

            MostrarCodigosProductoSeleccionado();
        }

        private void MostrarCodigosProductoSeleccionado()
        {
            if (dgProductos == null) return;

            // 🌟 Si no hay ningún producto seleccionado, seleccionar automáticamente el primero
            if (dgProductos.SelectedItem == null && _productosGridList.Any())
            {
                dgProductos.SelectedItem = _productosGridList.First();
                return; // Al asignar SelectedItem, el evento SelectionChanged se volverá a disparar de forma transparente
            }

            if (dgProductos.SelectedItem is not VistaProductoGrid producto)
            {
                // Si no hay productos en la lista general, mostramos todos los códigos o dejamos vacío
                dgCodigos.ItemsSource = _codigosGridList.Take(500).ToList();
                lblResumenCodigos.Text = $"500 (Viendo) / {_codigosGridList.Count}";
                return;
            }

            var todosLosCodigos = _codigosGridList.Where(c => c.ProductoId == producto.ProductoId);
            int totalCodigosProducto = todosLosCodigos.Count();

            var codigosVisiblesPintar = todosLosCodigos.Take(500).ToList();
            dgCodigos.ItemsSource = codigosVisiblesPintar;

            if (totalCodigosProducto > 500)
            {
                lblResumenCodigos.Text = $"500 (Viendo) / {totalCodigosProducto}";
                lblResumenCodigos.ToolTip = "La vista previa se limita a 500 ítems por rendimiento. Todos los códigos están cargados correctamente para ser guardados.";
            }
            else
            {
                lblResumenCodigos.Text = $"{totalCodigosProducto} / {totalCodigosProducto}";
                lblResumenCodigos.ToolTip = null;
            }
        }

        private async void BtnModificar_Click(object sender, RoutedEventArgs e) { await EditSelectedProductAsync(); }

        private async void DgProductos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 🛡️ CANDADO: Si está en modo impresión/consulta, ignora el doble clic completamente
            if (_printMode || _anularMode) return;

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

            // 🌟 OPTIMIZACIÓN VELOZ: Primero buscamos en los rangos globales ya procesados en memoria RAM
            if (_rangosProcesadosGlobal != null && _rangosProcesadosGlobal.Any(r => r.productoId == seleccionado.ProductoId))
            {
                rangosExistentes = _rangosProcesadosGlobal
                    .Where(r => r.productoId == seleccionado.ProductoId)
                    .ToList();
            }
            // Si no están en memoria y es un movimiento existente en BD, los consultamos
            else if (_currentMovimientoId.HasValue && seleccionado.Detalle != null && seleccionado.Detalle.Id > 0)
            {
                rangosExistentes = await _serviceMovimiento
                    .GetRangosByMovimientoDetalleIdAsync(seleccionado.Detalle.Id);
            }

            var modal = new AgregarItemWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };

            // 🌟 ASIGNACIÓN DEL ID DE MOVIMIENTO ACTUAL Y ESTADO
            modal.MovimientoIdActual = _currentMovimientoId;
            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && (mid == 1 || mid == 13)) ? 1 : 4;
            modal.InitializeForEdit(seleccionado, rangosExistentes);

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                seleccionado.Cantidad = (int)seleccionado.Detalle.CantidadIngreso;
                seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                // Limpiamos y actualizamos con los nuevos rangos del modal de manera limpia
                _codigosGridList.RemoveAll(c => c.ProductoId == seleccionado.ProductoId);
                _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);

                if (modal.ListaRangosAgregados != null && modal.ListaRangosAgregados.Any())
                {
                    foreach (var rangoModal in modal.ListaRangosAgregados)
                    {
                        _rangosProcesadosGlobal.Add(rangoModal);
                    }
                    var nuevosCodigos = _serviceMovimiento.ReconstruirCodigosDesdeRangos(modal.ListaRangosAgregados.ToList());
                    _codigosGridList.AddRange(nuevosCodigos);
                }

                dgProductos.CommitEdit(DataGridEditingUnit.Row, true);
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

        private void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (_anularMode) return;

            // 1. Limpieza inicial y preparación de modos
            LimpiarBotonesDinamicosCompletamente();
            HabilitarCamposFormulario(false);

            _anularMode = true;
            _printMode = false;
            _isBuscandoDocumento = false;

            // 2. Habilitar contenedor principal
            grdFormulario.IsEnabled = true;
            grdFormulario.Opacity = 1.0;

            // 3. Preparar la caja de texto para buscar
            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";
            txtNumSerie.IsEnabled = true;
            txtNumSerie.IsReadOnly = false;

            txtNumDocumento.IsEnabled = true;
            txtNumDocumento.IsReadOnly = false;
            txtNumDocumento.Background = System.Windows.Media.Brushes.White;
            txtNumDocumento.Focus();

            // 4. Enlazar evento Enter de forma limpia
            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            // 5. 🛡️ Bloquear cabecera superior (Nuevo, Editar, Imprimir, Anular) y dejar activo Cancelar
            GestionarBotonesPrincipales(enEdicion: true);
            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            // ⚠️ NOTA: ShowAnularButtonNearSave() se ejecutará automáticamente dentro de LoadMovimientoBySerieNumeroAsync una vez que el documento exista y se cargue en pantalla.

            MessageBox.Show("Modo Anulación activado.\n\nIngrese el N° de Documento de ingreso que desea anular y presione ENTER para revisar su contenido.", "Preparando Anulación", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this), IsAddAction = true };

            int motivoActual = cboMotivo.SelectedValue is int mid ? mid : 1;
            modal.EstadoPermitido = (motivoActual == 1 || motivoActual == 13) ? 1 : ((motivoActual == 4) ? 5 : 4);
            modal.ListaProductosExistentesEnPadre = _productosGridList;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados ?? new System.Collections.ObjectModel.ObservableCollection<RangoCodigoItem>();

                if (productoSelected == null) return;

                // 🌟 CAMBIO: Permitir productos SIN CÓDIGOS (ej: mochilas, libretas)
                bool esProductoSinCodigo = string.IsNullOrWhiteSpace(productoSelected.Abreviatura);
                if (!esProductoSinCodigo && !rangosDelModal.Any()) return;

                int idProducto = productoSelected.Id;

                this.Cursor = Cursors.Wait;

                try
                {
                    dgCodigos.ItemsSource = null;
                    dgProductos.ItemsSource = null;

                    int miAlmacenActualId = SesionSistema.AlmacenActual?.Id ?? 1;
                    int ignoradosPorDuplicado = 0;

                    // 1. Desglosamos todos los códigos digitados en el modal a lista plana
                    var listaStrings = new List<string>();
                    foreach (var rango in rangosDelModal)
                    {
                        if (rango.DesdeNum == -1)
                        {
                            listaStrings.Add(rango.AbreviaturaBase);
                        }
                        else
                        {
                            for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                            {
                                listaStrings.Add($"{rango.AbreviaturaBase}-{i:D7}");
                            }
                        }
                    }

                    // 2. 🛡️ CONSULTA DE CONTROL DIRECTA A BD (SOLO si hay códigos)
                    var mapaAlmacenesBD = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    if (listaStrings.Any())
                    {
                        using (var conn = _dbConnHelper.GetConnection())
                        {
                            var dbConn = (System.Data.Common.DbConnection)conn;
                            await dbConn.OpenAsync();
                            using var cmd = dbConn.CreateCommand();

                            cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT cc.codigo, cc.almacen_id 
                FROM codigos_creados cc WITH (NOLOCK)
                WHERE cc.codigo IN (" + string.Join(",", listaStrings.Select((_, idx) => $"@p{idx}")) + ")");

                            for (int i = 0; i < listaStrings.Count; i++)
                            {
                                var p = cmd.CreateParameter();
                                p.ParameterName = $"@p{i}";
                                p.Value = listaStrings[i];
                                cmd.Parameters.Add(p);
                            }

                            using var rdr = await cmd.ExecuteReaderAsync();
                            while (await rdr.ReadAsync())
                            {
                                string codBD = rdr.GetString(0);
                                int almIdBD = rdr.GetInt32(1);
                                string norm = _serviceMovimiento.NormalizarCodigo(codBD);
                                mapaAlmacenesBD[norm] = almIdBD;
                            }
                        }

                        // 🔒 VALIDACIÓN ANTICIPADA: Detectar todos los códigos de otro almacén ANTES de procesarlos
                        var codigosOtroAlmacen = new List<string>();
                        foreach (var codStr in listaStrings)
                        {
                            string codNorm = _serviceMovimiento.NormalizarCodigo(codStr);
                            if (mapaAlmacenesBD.TryGetValue(codNorm, out int almacenResidenteBD))
                            {
                                if (almacenResidenteBD != miAlmacenActualId)
                                {
                                    codigosOtroAlmacen.Add(codStr);
                                }
                            }
                        }

                        // 🚫 SI HAY CÓDIGOS DE OTRO ALMACÉN, DETENER TODO AQUÍ
                        if (codigosOtroAlmacen.Any())
                        {
                            string nombreMiAlmacen = SesionSistema.AlmacenActual?.Nombre ?? "tu almacén actual";
                            MessageBox.Show(
                                $"⚠️ RESTRICCIÓN ESTRICTA DE SEDE / ALMACÉN:\n\n" +
                                $"Se detectaron {codigosOtroAlmacen.Count} código(s) que pertenecen a OTRA SEDE/ALMACÉN (ej. ALMACEN LIMA) y tú estás en {nombreMiAlmacen.ToUpper()}.\n\n" +
                                $"{string.Join("\n", codigosOtroAlmacen.Take(5).Select(c => $"• {c}"))}\n" +
                                $"{(codigosOtroAlmacen.Count > 5 ? $"\n... y {codigosOtroAlmacen.Count - 5} códigos más." : "")}\n\n" +
                                $"No puedes ingresar ni manipular stock de otra sede sin una Guía de Transferencia Oficial.",
                                "Acceso Denegado por Kárdex",
                                MessageBoxButton.OK,
                                MessageBoxImage.Stop);

                            this.Cursor = Cursors.Arrow;
                            return; // 🛑 SALIR SIN AGREGAR NADA
                        }
                    }

                    int idMotivoIngreso = cboMotivo.SelectedValue != null ? Convert.ToInt32(cboMotivo.SelectedValue) : 1;

                    // 3. Procesamos los rangos (SOLO si hay códigos)
                    foreach (var rango in rangosDelModal)
                    {
                        rango.productoId = idProducto;

                        string etiquetaColeccion = string.IsNullOrEmpty(rango.ColeccionTipo) ? "LIBRO VENTA" : rango.ColeccionTipo;

                        var listaCodigosDelRango = (rango.DesdeNum == -1)
                            ? new List<string> { rango.AbreviaturaBase }
                            : Enumerable.Range(rango.DesdeNum, rango.HastaNum - rango.DesdeNum + 1)
                                        .Select(i => $"{rango.AbreviaturaBase}-{i:D7}").ToList();

                        foreach (var codGenerado in listaCodigosDelRango)
                        {
                            string codNorm = _serviceMovimiento.NormalizarCodigo(codGenerado);

                            // 🛡️ CANDADO ANTI-DUPLICADOS EN MEMORIA
                            if (!_codigosGridList.Any(c => c.ProductoId == idProducto && _serviceMovimiento.NormalizarCodigo(c.CodigoUnique) == codNorm))
                            {
                                _codigosGridList.Add(new VistaCodigoGrid
                                {
                                    CodigoUnique = codGenerado,
                                    ColeccionTipo = etiquetaColeccion,
                                    ProductoId = idProducto
                                });
                            }
                            else
                            {
                                ignoradosPorDuplicado++;
                            }
                        }

                        _rangosProcesadosGlobal.Add(rango);
                    }

                    if (ignoradosPorDuplicado > 0)
                    {
                        MessageBox.Show($"Se ignoraron {ignoradosPorDuplicado} código(s) duplicado(s) que ya estaban en la tabla.", "Aviso de Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    // Actualizamos el conteo real con los códigos válidos que SÍ quedaron
                    int totalCodigosProducto = _codigosGridList.Count(c => c.ProductoId == idProducto);

                    var existente = _productosGridList.FirstOrDefault(p => p.ProductoId == idProducto);
                    if (existente != null)
                    {
                        existente.Detalle ??= new MovimientoDetalle { ProductoId = idProducto };
                        existente.Detalle.CantidadIngreso = totalCodigosProducto > 0 ? totalCodigosProducto : modal.CantidadProductoIngresada;
                        existente.Cantidad = (int)existente.Detalle.CantidadIngreso;
                        existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;
                    }
                    else
                    {
                        int cantReal = totalCodigosProducto > 0 ? totalCodigosProducto : modal.CantidadProductoIngresada;
                        var nuevoProductoGrid = new VistaProductoGrid
                        {
                            Detalle = new MovimientoDetalle { ProductoId = idProducto, CantidadIngreso = cantReal, CostoUnitario = modal.CostoUnitarioIngresado },
                            CodigoProducto = idProducto.ToString(),
                            Descripcion = productoSelected.Descripcion,
                            UnidadMedida = productoSelected.UnidadMedida?.Descripcion ?? "UNIDAD",
                            ProductoId = idProducto,
                            Cantidad = cantReal
                        };
                        _productosGridList.Add(nuevoProductoGrid);
                        existente = nuevoProductoGrid;
                    }

                    RefrescarGrillas();
                    dgProductos.SelectedItem = existente;
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        

        private void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
            }

            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is LectorGlobalWindow)
                {
                    window.Focus();
                    if (sender is Button b) b.IsEnabled = true;
                    return;
                }
            }

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

            lector.Closed += (s, ev) =>
            {
                if (sender is Button b) b.IsEnabled = true;
            };

            lector.ShowDialog();
            RefrescarGrillas();
        }

        private async Task<bool> ProcesarCodigoEscaneadoAsync(LectoraResultDTO resultado)
        {
            if (_codigosGridList.Any(x => x.CodigoUnique.Equals(resultado.CodigoCompleto, StringComparison.OrdinalIgnoreCase)))
                return false;

            string tipoBD = await _serviceMovimiento.ObtenerColeccionTipoBDAsync(resultado.CodigoCreadoId);

            var producto = await ObtenerOAgregarProductoEnListaAsync(
            resultado.ProductoId,
            resultado.DescripcionProducto,
            resultado.PrecioUnitario);

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

            _serviceMovimiento.AgregarCodigosIndividuales(_codigosGridList, producto.ProductoId, nuevosCodigos);

            var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == producto.ProductoId).ToList();
            var nuevosRangos = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosDelProducto);

            _serviceMovimiento.ReemplazarRangosProducto(_rangosProcesadosGlobal, producto.ProductoId, nuevosRangos);

            return true;
        }

        private async Task<VistaProductoGrid> ObtenerOAgregarProductoEnListaAsync(int productoId, string descripcion, decimal precio)
        {
            var prod = _productosGridList.FirstOrDefault(p => p.ProductoId == productoId);

            if (prod != null)
                return prod;

            var productoBD = await _productoService.ObtenerPorIdAsync(productoId);

            prod = new VistaProductoGrid
            {
                ProductoId = productoId,
                CodigoProducto = productoId.ToString(),
                Descripcion = descripcion,
                UnidadMedida = productoBD?.UnidadMedida?.Descripcion ?? "UNIDAD",
                Detalle = new MovimientoDetalle
                {
                    ProductoId = productoId,
                    CantidadIngreso = 0,
                    CostoUnitario = precio
                }
            };

            _productosGridList.Add(prod);
            return prod;
        }

        private async void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var win = new ImportarCodigos { Owner = Window.GetWindow(this) };

            if (_codigosGridList != null && _codigosGridList.Any())
            {
                win.CodigosYaAgregadosEnMovimiento = _codigosGridList
                    .Select(c => c.CodigoUnique)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();
            }
            try
            {
                win.EstadoPermitido = (cboMotivo.SelectedValue is int mv && (mv == 1 || mv == 13)) ? 1 : 4;
            }
            catch
            {
                win.EstadoPermitido = 1;
            }

            if (win.ShowDialog() != true) return;

            var listaRaw = win.CodigosImportados ?? new List<string>();
            if (!listaRaw.Any()) return;

            this.Cursor = Cursors.Wait;

            var codigosFisicosAgrupados = new Dictionary<int, List<VistaCodigoGrid>>();

            var loadingTransfer = new ProgressWindow("Procesando Códigos Importados", "Sincronizando registros masivos con el inventario...", async (progress) =>
            {
                await Task.Run(async () =>
                {
                    // 🌟 1. BUSQUEDA MASIVA EN BD (1 Solo Viaje en Lote)
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaRaw);

                    // 🌟 2. HASHSET ULTRA RÁPIDO O(1) PARA EVITAR DISPATCHER EN EL BUCLE
                    var setExistentesEnGrilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var c in _codigosGridList)
                        {
                            if (!string.IsNullOrEmpty(c.CodigoUnique))
                            {
                                setExistentesEnGrilla.Add(_serviceMovimiento.NormalizarCodigo(c.CodigoUnique));
                            }
                        }
                    });

                    int total = listaRaw.Count;
                    int ultimoPorcentaje = -1;

                    var listaNuevosCodigosLocal = new List<VistaCodigoGrid>(total);

                    for (int i = 0; i < total; i++)
                    {
                        string raw = listaRaw[i];
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);

                        if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue)
                            continue;

                        int pId = tup.ProductoId.Value;

                        // 🛡️ BÚSQUEDA RÁPIDA O(1) EN MEMORIA RAM (Cero Dispatcher)
                        if (setExistentesEnGrilla.Contains(norm))
                            continue;

                        setExistentesEnGrilla.Add(norm); // Evita duplicados dentro del mismo lote

                        var nuevoCod = new VistaCodigoGrid
                        {
                            ProductoId = pId,
                            CodigoUnique = tup.CodigoObj.Codigo,
                            ColeccionTipo = "LIBRO VENTA", // Asignación rápida predeterminada
                            MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id }
                        };

                        listaNuevosCodigosLocal.Add(nuevoCod);

                        // Reportar progreso periódicamente (máximo 100 actualizaciones de UI)
                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentaje)
                        {
                            ultimoPorcentaje = pct;
                            progress.Report(pct);
                        }
                    }

                    // Agrupar localmente por producto
                    foreach (var item in listaNuevosCodigosLocal)
                    {
                        if (!codigosFisicosAgrupados.ContainsKey(item.ProductoId))
                            codigosFisicosAgrupados[item.ProductoId] = new List<VistaCodigoGrid>();

                        codigosFisicosAgrupados[item.ProductoId].Add(item);
                    }
                });
            });

            loadingTransfer.Owner = Window.GetWindow(this);

            if (loadingTransfer.ShowDialog() == true)
            {
                this.Cursor = Cursors.Wait;
                try
                {
                    var prodService = new ProductoService();

                    foreach (var kvp in codigosFisicosAgrupados)
                    {
                        int productoId = kvp.Key;
                        var prodData = await prodService.ObtenerPorIdAsync(productoId);
                        string unidadDeBD = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD";

                        var producto = AgregarOActualizarProducto(productoId, prodData?.Descripcion ?? "Desconocido", 0, prodData?.PrecioUnitario ?? 0, unidadDeBD);

                        _serviceMovimiento.AgregarCodigosIndividuales(_codigosGridList, productoId, kvp.Value);

                        var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == productoId).ToList();
                        var nuevosRangos = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosDelProducto);

                        _serviceMovimiento.ReemplazarRangosProducto(_rangosProcesadosGlobal, productoId, nuevosRangos);
                    }

                    RefrescarGrillas();
                    if (_productosGridList.Count > 0) dgProductos.SelectedItem = _productosGridList.Last();

                    MessageBox.Show($"Importación finalizada con éxito. Registros procesados.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al enlazar la grilla principal: {ex.Message}", "Error Interno", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
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
            if (cboMotivo.SelectedValue == null) return;

            // 🛡️ CANDADO DE SEGURIDAD: Si estamos consultando/imprimiendo, SALIR INMEDIATAMENTE
            if (_printMode || _anularMode) return;

            int idMotivo = Convert.ToInt32(cboMotivo.SelectedValue);

            txtSerieGuia.IsEnabled = true;
            txtNumeroGuia.IsEnabled = true;

            if (idMotivo == 1 || idMotivo == 2)
            {
                txtRazonSocial.IsEnabled = true;
                txtUbicacion.IsEnabled = false;
                cboAlmacenDestino.IsEnabled = false;
            }
            else if (idMotivo == 4)
            {
                txtRazonSocial.IsEnabled = false;
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }
            else
            {
                txtRazonSocial.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }

            if (!txtRazonSocial.IsEnabled)
            {
                txtRazonSocial.Clear();
                txtCodigoRazonSocial.Clear();
                txtDireccion.Clear();
                _personaComercialIdSeleccionada = null;
                if (popupSugerencias != null) popupSugerencias.IsOpen = false;
            }

            if (!txtUbicacion.IsEnabled)
            {
                txtUbicacion.Clear();
                txtCodigoUbicacion.Clear();
                txtDireccionUbicacion.Clear();
                _idUbicacionSeleccionada = null;
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
            }

            if (!cboAlmacenDestino.IsEnabled)
            {
                cboAlmacenDestino.SelectedIndex = -1;
                cboAlmacenDestino.SelectedValue = null;
            }
        }

        private void CboAlmacenDestino_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboAlmacenDestino.SelectedValue != null)
            {
                txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                txtUbicacion.Clear();
                txtCodigoUbicacion.Clear();
                txtDireccionUbicacion.Clear();
                _idUbicacionSeleccionada = null;
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
                txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
            }
        }

        // ==========================================
        // EVENTOS DE BÚSQUEDA OPTIMIZADOS (DEBOUNCE)
        // ==========================================
        private void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtRazonSocial.IsEnabled || _isUpdatingFromSelection) return;

            _timerRazonSocial.Stop();
            _timerRazonSocial.Start();
        }

        private async Task EjecutarBusquedaRazonSocialAsync()
        {
            string textoBusqueda = txtRazonSocial.Text.Trim();
            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    var sugerencias = await _service.BuscarPorRazonSocialAsync(textoBusqueda);
                    lstSugerencias.ItemsSource = sugerencias;
                    popupSugerencias.IsOpen = sugerencias != null && sugerencias.Count > 0;
                }
                catch
                {
                    popupSugerencias.IsOpen = false;
                }
            }
            else
            {
                popupSugerencias.IsOpen = false;
            }
        }

        // 🌟 EVENTO RESTAURADO: Selección de cliente
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
            if (!txtUbicacion.IsEnabled || _isUpdatingFromSelection)
            {
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
                return;
            }

            _timerUbicacion.Stop();
            _timerUbicacion.Start();
        }

        private async Task EjecutarBusquedaUbicacionAsync()
        {
            string busqueda = txtUbicacion.Text.Trim();
            if (string.IsNullOrWhiteSpace(busqueda) || busqueda.Length < 2)
            {
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
                return;
            }

            try
            {
                var resultados = await _ubicacionService.BuscarUbicacionesPorNombreAsync(busqueda);
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
            catch
            {
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
            }
        }

        // 🌟 EVENTO RESTAURADO: Selección de ubicación
        private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado)
            {
                txtUbicacion.Text = itemSeleccionado.Descripcion;
                txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString();
                txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(itemSeleccionado.Direccion) ? "Sin dirección registrada" : itemSeleccionado.Direccion;
                _idUbicacionSeleccionada = itemSeleccionado.Id;
                popupUbicacion.IsOpen = false;
            }
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
                    // 🌟 Agregamos btnCancelar aquí para que responda al clic sin ser interceptado
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

        private void dgProductos_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}