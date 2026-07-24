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
using MediaColor = System.Windows.Media.Color;
using MediaConverter = System.Windows.Media.ColorConverter;
using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Drawing;
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
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

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
        private const int UBICACION_ID_SELECCIONADA = 1; // ID Fijo de Almacén Central
        private bool _isRegistrandoMovimiento = false;
        private bool _printMode = false;
        private Button _btnPrintNearSave = null;
        private readonly DatabaseConnection _dbConnHelper = new DatabaseConnection();

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
            await CargarComboAlmacenesOrigenAsync(); // 🌟 OBLIGATORIO: Carga la lista de almacenes al abrir
        }

        private void EstablecerEstadoInicial()
        {
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

                string query;

                if (miAlmacenId == ALMACEN_CENTRAL_ID)
                {
                    // 🟢 Almacén Central puede recibir transferencias de TODOS los sub-almacenes
                    query = "SELECT id, nombre FROM almacenes WHERE id != @miAlmacen AND estado_id = 1 ORDER BY nombre ASC";
                }
                else
                {
                    // 🟡 Un Sub-Almacén SOLO puede seleccionar ALMACÉN CENTRAL
                    query = "SELECT id, nombre FROM almacenes WHERE id = @centralId AND estado_id = 1";
                }

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

                // Asignamos al ComboBox (cboAlmacenDestino es la referencia física en pantalla)
                cboAlmacenDestino.ItemsSource = listaAlmacenes;
                cboAlmacenDestino.DisplayMemberPath = "Nombre";
                cboAlmacenDestino.SelectedValuePath = "Id";

                // Si es sub-almacén, selecciona automáticamente "Almacén Central Trujillo"
                if (miAlmacenId != ALMACEN_CENTRAL_ID && listaAlmacenes.Any())
                {
                    cboAlmacenDestino.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar almacenes origen: {ex.Message}");
            }
        }

        // 🌟 SOBRECARGA 1: Recepción de Transferencias desde Campana/Bandeja (por ID de movimiento único)
        public async void CargarDocumentoParaConsulta(int movimientoSalidaId)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var movCompleto = await _serviceMovimiento.GetSalidaParaRecepcionAsync(movimientoSalidaId);
                if (movCompleto == null || movCompleto.Movimiento == null)
                {
                    MessageBox.Show("No se encontró el registro de transferencia especificado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LimpiarFormulario();

                await CargarComboAlmacenesOrigenAsync();

                cboMotivo.SelectedValue = 4; // Transferencia
                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                if (movCompleto.Movimiento.AlmacenOrigenId.HasValue)
                {
                    cboAlmacenDestino.SelectedValue = movCompleto.Movimiento.AlmacenOrigenId.Value;
                    cboAlmacenDestino.IsEnabled = false; // Bloqueado porque viene predeterminado de la guía de salida
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

        // 🌟 SOBRECARGA 2: Búsqueda normal de Ingresos previos por Serie y Número (Soporta 2 argumentos string)
        public async void CargarDocumentoParaConsulta(string serie, string numero)
        {
            try
            {
                this.Cursor = Cursors.Wait;
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

                // 🌟 REGLA DE NEGOCIO: Si NO es Almacén Central, eliminamos la opción "COMPRA" (ID 1)
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
                    _currentMovimientoId = null;

                    await LoadMovimientoBySerieNumeroAsync(serie, numero);

                    if (!_currentMovimientoId.HasValue)
                    {
                        MessageBox.Show("Movimiento no encontrado. Verifique el número e intente de nuevo.",
                                        "Búsqueda fallida", MessageBoxButton.OK, MessageBoxImage.Warning);

                        txtNumDocumento.Focus();
                        txtNumDocumento.SelectAll();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al buscar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _currentMovimientoId = null;
                }
                finally
                {
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

        private async Task LoadMovimientoBySerieNumeroAsync(string serie, string numero)
        {
            LimpiarFormulario();

            // 🌟 FILTRO ABSOLUTO: Captura el almacén de la sesión activa
            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

            // 🛡️ Pasa el almacén actual para que la búsqueda solo jale registros de este almacén
            var movimientoComp = await _serviceMovimiento.GetMovimientoCompletoAsync(serie, numero, miAlmacenId);

            if (movimientoComp == null)
            {
                MessageBox.Show("No se encontró el movimiento especificado en este Almacén.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var movimiento = movimientoComp.Movimiento;
            _currentMovimientoId = movimiento.Id;

            if (movimiento.EstadoId == 2) // 2 = ANULADO
            {
                if (!_printMode && !_anularMode)
                {
                    MessageBox.Show("Este movimiento ya está ANULADO y no permite modificaciones.", "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Stop);
                    EstablecerEstadoInicial();
                    return;
                }
            }

            btnAnular.IsEnabled = (movimiento.EstadoId != 2);

            if (movimiento.FechaMovimiento.HasValue)
                dtpFechaRecepcion.SelectedDate = movimiento.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue);

            txtNumSerie.Text = movimiento.SerieDocumento;
            txtNumDocumento.Text = movimiento.NumeroDocumento;
            cboMotivo.SelectedValue = movimiento.MotivoProductoId;

            _personaComercialIdSeleccionada = movimiento.PersonaComercialId;
            if (movimiento.PersonaComercialId.HasValue)
            {
                txtCodigoRazonSocial.Text = movimiento.PersonaComercialId.Value.ToString("D6");
                try
                {
                    var persona = await _service.ObtenerPorIdAsync(movimiento.PersonaComercialId.Value);
                    if (persona != null)
                    {
                        txtRazonSocial.TextChanged -= TxtRazonSocial_TextChanged;
                        txtRazonSocial.Text = !string.IsNullOrEmpty(persona.RazonSocial)
                            ? persona.RazonSocial
                            : $"{persona.Nombres} {persona.ApellidoPaterno}";
                        txtDireccion.Text = persona.Direccion ?? "Sin dirección registrada";
                        txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
                        try { lstSugerencias.ItemsSource = null; lstSugerencias.SelectedIndex = -1; } catch { }
                    }
                }
                catch
                {
                    txtRazonSocial.Text = $"Cliente ID: {movimiento.PersonaComercialId.Value}";
                }
            }

            txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
            txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
            txtObservacion.Text = movimiento.Observacion ?? string.Empty;

            if (movimiento.UbicacionId.HasValue)
            {
                try
                {
                    var todas = _ubicacionService.ObtenerTodas();
                    var ubic = todas?.FirstOrDefault(u => u.Id == movimiento.UbicacionId.Value);
                    if (ubic != null)
                    {
                        txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                        txtUbicacion.Text = ubic.Descripcion ?? string.Empty;
                        txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(ubic.Direccion) ? "Sin dirección registrada" : ubic.Direccion;
                        txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
                        try { lstSugerenciasUbicacion.ItemsSource = null; lstSugerenciasUbicacion.SelectedIndex = -1; } catch { }
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

            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
            dgCodigos.ItemsSource = null;
            dgCodigos.ItemsSource = _codigosGridList;

            // 🌟 EVALUACIÓN ESTRICTA Y EXCLUSIVA SEGÚN EL MODO ACTIVO
            if (_anularMode)
            {
                BloquearParaAnulacionVisual();
                ShowAnularButtonNearSave();

                if (movimiento.EstadoId == 2)
                {
                    _btnAnularNearSave.Content = "🔒 EL MOVIMIENTO YA HA SIDO ANULADO";
                    _btnAnularNearSave.IsEnabled = false;
                    _btnAnularNearSave.Background = System.Windows.Media.Brushes.DarkGray;
                }
            }
            else if (_printMode)
            {
                // 🔒 GARANTÍA: Bloquea 100% controles y tablas para evitar edición involuntaria
                BloquearParaImpresion();
                ShowPrintButtonNearSave();
            }
            else
            {
                // ✏️ MODO EDICIÓN NORMAL
                HabilitarCamposFormulario(true);
                GestionarBotonesPrincipales(enEdicion: true);
                if (btnCancelar != null) btnCancelar.IsEnabled = true;

                // Reevaluar visibilidad de Razón Social / Ubicación según el motivo cargado
                CboMotivo_SelectionChanged(cboMotivo, null);
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
                _currentMovimientoId = null;
                LimpiarFormulario();
                dtpFechaRecepcion.SelectedDate = DateTime.Today;

                HabilitarCamposFormulario(true);
                GestionarBotonesPrincipales(enEdicion: true);

                txtNumSerie.Text = "0001";

                // 🌟 CORRELATIVO INDEPENDIENTE BASADO EN ALMACÉN CREADOR (almacen_id)
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

                txtNumDocumento.Text = siguienteCorrelativo;
                txtNumDocumento.IsReadOnly = true;
                txtNumDocumento.Background = System.Windows.Media.Brushes.WhiteSmoke;
                txtNumDocumento.Foreground = System.Windows.Media.Brushes.Gray;
                txtNumDocumento.FontStyle = FontStyles.Italic;

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

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;

            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";

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

            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            // Permitir cancelar en modo impresión
            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            System.Windows.MessageBox.Show("Modo Imprimir activado. Ingrese el número de documento de ingreso y presione ENTER.",
                                           "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var resultado = System.Windows.MessageBox.Show("¿Está seguro que desea cancelar la operación actual?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resultado == MessageBoxResult.Yes)
            {
                if (_printMode) { _printMode = false; }
                if (_anularMode) { _anularMode = false; }

                if (_btnPrintNearSave != null && _btnPrintNearSave.Parent is Panel p1) { p1.Children.Remove(_btnPrintNearSave); _btnPrintNearSave = null; }
                if (_btnAnularNearSave != null && _btnAnularNearSave.Parent is Panel p2) { p2.Children.Remove(_btnAnularNearSave); _btnAnularNearSave = null; }

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

            // =========================================================
            // 1. VALIDACIONES SEGÚN EL MOTIVO SELECCIONADO
            // =========================================================

            // 🔴 IDs 5, 6, 7, 8, 11 -> Exigen Razón Social obligatoria
            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11)
            {
                if (string.IsNullOrWhiteSpace(txtRazonSocial.Text) || !_personaComercialIdSeleccionada.HasValue)
                {
                    MessageBox.Show("Para este motivo es obligatorio seleccionar la Razón Social de destino.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            // 🔵 IDs 4, 10 -> TRANSFERENCIA INTER-ALMACÉN (OBLIGA ALMACÉN DESTINO U UBICACIÓN REFERENCIAL)
            // 🔵 ID 4 -> RECEPCIÓN DE TRANSFERENCIA INTER-ALMACÉN
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
            // 🟣 IDs 9, 12 -> Regla Flexible (Requiere al menos Razón Social O Ubicación)
            else if (idMotivo == 9 || idMotivo == 12)
            {
                bool tieneRazonSocial = !string.IsNullOrWhiteSpace(txtRazonSocial.Text) && _personaComercialIdSeleccionada.HasValue;
                bool tieneUbicacion = !string.IsNullOrWhiteSpace(txtUbicacion.Text) && _idUbicacionSeleccionada.HasValue;

                if (!tieneRazonSocial && !tieneUbicacion)
                {
                    MessageBox.Show("Para este motivo debe seleccionar al menos una Razón Social O una Ubicación de destino.", "Validación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 2. Validación de Guía / Comprobante
            if (string.IsNullOrWhiteSpace(txtSerieGuia.Text) || string.IsNullOrWhiteSpace(txtNumeroGuia.Text))
            {
                MessageBox.Show("Debe ingresar la Serie y el Número de Guía o Comprobante.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Validación de Tabla de Productos
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

                // 🌟 Regeneración de rangos basada en los códigos presentes en la grilla
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
                        solicitud.Movimiento.UbicacionId ?? UBICACION_ID_SELECCIONADA,
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
                if (p.Detalle == null) p.Detalle = new MovimientoDetalle { ProductoId = p.ProductoId };

                int codigosReales = _codigosGridList.Count(c => c.ProductoId == p.ProductoId);

                if (codigosReales > 0)
                {
                    p.Detalle.CantidadIngreso = codigosReales;
                    p.Cantidad = codigosReales;
                }
                else
                {
                    p.Detalle.CantidadIngreso = p.Cantidad;
                }
            }

            // 🌟 DECLARACIÓN DE LA VARIABLE QUE FALTABA
            int miAlmacenActual = SesionSistema.AlmacenActual?.Id ?? 1;

            int? almacenDestinoId = cboAlmacenDestino.SelectedValue != null
                ? Convert.ToInt32(cboAlmacenDestino.SelectedValue)
                : (int?)null;

            // 🌟 DETECCIÓN INTELIGENTE DE ORIGEN Y DESTINO SEGÚN EL MOTIVO
            int idMotivoIngreso = Convert.ToInt32(cboMotivo.SelectedValue);
            int? almacenOrigenReal = null;
            int? almacenDestinoReal = miAlmacenActual;

            if (idMotivoIngreso == 4) // Si es Recepción por Transferencia
            {
                almacenOrigenReal = cboAlmacenDestino.SelectedValue != null
                    ? Convert.ToInt32(cboAlmacenDestino.SelectedValue)
                    : (int?)null; // El almacén que me envió (Ej: Trujillo = 1)

                almacenDestinoReal = miAlmacenActual; // Mi sede actual que recibe (Ej: Lima = 3)
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

                    UbicacionId = _idUbicacionSeleccionada ?? UBICACION_ID_SELECCIONADA,

                    // 🌟 ASIGNACIÓN MÁSTER BLINDADA PARA KÁRDEX Y STOCK INDEPENDIENTE
                    AlmacenId = miAlmacenActual,           // Sede que genera el registro de entrada (Lima)
                    AlmacenOrigenId = almacenOrigenReal,   // Procedencia real de los libros (Trujillo)
                    AlmacenDestinoId = almacenDestinoReal, // Destino final de los libros (Lima)

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
            if (dgProductos != null) { dgProductos.IsEnabled = true; dgProductos.IsReadOnly = true; }
            if (dgCodigos != null) { dgCodigos.IsEnabled = true; dgCodigos.IsReadOnly = true; }

            // Deshabilitar todas las acciones de edición de cabecera y detalle
            dtpFechaRecepcion.IsEnabled = false;
            cboMotivo.IsEnabled = false;
            txtRazonSocial.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            txtSerieGuia.IsEnabled = false;
            txtNumeroGuia.IsEnabled = false;
            txtObservacion.IsEnabled = false;

            btnAgregar.IsEnabled = false;
            btnEditar.IsEnabled = false;
            btnModificar.IsEnabled = false;
            btnEliminar.IsEnabled = false;
            btnImportar.IsEnabled = false;
            btnAgregarProducto.IsEnabled = false;
            btnEscanear.IsEnabled = false;
            btnImprimir.IsEnabled = false;
            btnGrabar.IsEnabled = false;

            // El botón Cancelar siempre se mantiene activo para poder salir
            if (btnCancelar != null) btnCancelar.IsEnabled = true;
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

            _serviceMovimiento.SincronizarCantidadesConCodigos(
                _productosGridList,
                _codigosGridList);

            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;

            MostrarCodigosProductoSeleccionado();
        }

        private void MostrarCodigosProductoSeleccionado()
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid producto)
            {
                dgCodigos.ItemsSource = null;
                lblResumenCodigos.Text = "0 / 0";
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

            if (_currentMovimientoId.HasValue && seleccionado.Detalle != null && seleccionado.Detalle.Id > 0)
            {
                rangosExistentes = await _serviceMovimiento
                    .GetRangosByMovimientoDetalleIdAsync(seleccionado.Detalle.Id);
            }

            var codigosProducto = _codigosGridList
                .Where(c => c.ProductoId == seleccionado.ProductoId)
                .ToList();

            if (codigosProducto.Any())
            {
                rangosExistentes = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosProducto);
            }
            else if (rangosExistentes == null || rangosExistentes.Count == 0)
            {
                rangosExistentes = _rangosProcesadosGlobal
                    .Where(r => r.productoId == seleccionado.ProductoId)
                    .ToList();
            }

            var modal = new AgregarItemWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };

            modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
            modal.InitializeForEdit(seleccionado, rangosExistentes);

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                seleccionado.Cantidad = (int)seleccionado.Detalle.CantidadIngreso;

                // 🌟 REEMPLAZO LIMPIO: Borramos los códigos previos del producto y ponemos los nuevos que aprobó el modal
                _codigosGridList.RemoveAll(c => c.ProductoId == seleccionado.ProductoId);

                var nuevosCodigos = _serviceMovimiento.ReconstruirCodigosDesdeRangos(modal.ListaRangosAgregados.ToList());
                _codigosGridList.AddRange(nuevosCodigos);

                // Regeneramos el mapa global de rangos
                _rangosProcesadosGlobal = _serviceMovimiento.GenerarRangosDesdeCodigos(_codigosGridList);

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

            _anularMode = true;
            _printMode = false;

            HabilitarCamposFormulario(false);
            grdFormulario.IsEnabled = true;

            PrepararCajaBusqueda();
            txtNumSerie.Text = "0001";

            txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
            txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

            GestionarBotonesPrincipales(enEdicion: true);
            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            // 🌟 ARREGLO: Forzar la aparición del botón de confirmación inmediatamente al activar el modo
            ShowAnularButtonNearSave();

            MessageBox.Show("Modo Anulación activado.\n\nIngrese el número de documento de ingreso que desea anular y presione Enter para revisar su contenido.", "Preparando Anulación", MessageBoxButton.OK, MessageBoxImage.Information);
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

                var progressModal = new ProgressWindow("Procesando Lote Manual", "Generando y sincronizando códigos en el kárdex local...", async (progress) =>
                {
                    int nuevosCodigosCount = rangosDelModal.Sum(r => r.DesdeNum == -1 ? 1 : (r.HastaNum - r.DesdeNum + 1));
                    _codigosGridList.Capacity = _codigosGridList.Count + nuevosCodigosCount;

                    var listaStrings = new List<string>(nuevosCodigosCount);
                    int totalRangos = rangosDelModal.Count;

                    for (int rIdx = 0; rIdx < totalRangos; rIdx++)
                    {
                        var rango = rangosDelModal[rIdx];
                        rango.productoId = idProducto;

                        int pctRango = (rIdx * 30) / (totalRangos == 0 ? 1 : totalRangos);
                        progress.Report(pctRango);

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

                    progress.Report(40);
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaStrings);

                    int totalStrings = listaStrings.Count;
                    for (int sIdx = 0; sIdx < totalStrings; sIdx++)
                    {
                        string codStr = listaStrings[sIdx];
                        string norm = _serviceMovimiento.NormalizarCodigo(codStr);

                        if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                        {
                            string tipoColeccionReal = await _serviceMovimiento.ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _codigosGridList.Add(new VistaCodigoGrid
                                {
                                    CodigoUnique = tup.CodigoObj.Codigo,
                                    ColeccionTipo = tipoColeccionReal,
                                    ProductoId = idProducto,
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id }
                                });
                            });
                        }

                        int pctFinal = 40 + ((sIdx * 60) / (totalStrings == 0 ? 1 : totalStrings));
                        progress.Report(pctFinal);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var rango in rangosDelModal)
                        {
                            _rangosProcesadosGlobal.Add(rango);
                        }
                    });
                });

                progressModal.Owner = Window.GetWindow(this);

                if (progressModal.ShowDialog() == true)
                {
                    this.Cursor = Cursors.Wait;
                    if (existente != null && modal.MergeWithExisting)
                    {
                        existente.Detalle = existente.Detalle ?? new MovimientoDetalle { ProductoId = existente.ProductoId };
                        existente.Detalle.CantidadIngreso += modal.CantidadProductoIngresada;
                        existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;
                    }
                    else
                    {
                        var nuevoProductoGrid = new VistaProductoGrid
                        {
                            Detalle = new MovimientoDetalle { ProductoId = idProducto, CantidadIngreso = modal.CantidadProductoIngresada, CostoUnitario = modal.CostoUnitarioIngresado },
                            CodigoProducto = idProducto.ToString(),
                            Descripcion = productoSelected.Descripcion,
                            UnidadMedida = productoSelected.UnidadMedida?.Descripcion ?? "UNIDAD",
                            ProductoId = idProducto
                        };
                        _productosGridList.Add(nuevoProductoGrid);
                        existente = nuevoProductoGrid;
                    }

                    RefrescarGrillas();
                    dgProductos.SelectedItem = existente;
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
                win.EstadoPermitido = (cboMotivo.SelectedValue is int mv && mv == 1) ? 1 : 4;
            }
            catch
            {
                win.EstadoPermitido = 1;
            }

            if (win.ShowDialog() != true) return;

            var listaRaw = win.CodigosImportados ?? new List<string>();
            if (!listaRaw.Any()) return;

            var lookup = new Dictionary<string, (CodigoCreado CodigoObj, int? ProductoId)>(StringComparer.OrdinalIgnoreCase);
            var codigosFisicosAgrupados = new Dictionary<int, List<VistaCodigoGrid>>();

            var loadingTransfer = new ProgressWindow("Procesando Códigos Importados", "Sincronizando registros con la grilla de movimientos principales...", async (progress) =>
            {
                lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaRaw);

                int total = listaRaw.Count;
                int ultimoPorcentaje = -1;

                for (int i = 0; i < total; i++)
                {
                    string raw = listaRaw[i];
                    string norm = _serviceMovimiento.NormalizarCodigo(raw);
                    if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue) continue;

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

                    int pct = (i * 100) / total;
                    if (pct > ultimoPorcentaje)
                    {
                        ultimoPorcentaje = pct;
                        progress.Report(pct);
                    }
                }
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

                        _codigosGridList.RemoveAll(c => c.ProductoId == productoId);
                        _rangosProcesadosGlobal.RemoveAll(r => r.productoId == productoId);

                        var producto = AgregarOActualizarProducto(productoId, prodData?.Descripcion ?? "Desconocido", 0, prodData?.PrecioUnitario ?? 0, unidadDeBD);

                        _serviceMovimiento.AgregarCodigosIndividuales(_codigosGridList, productoId, kvp.Value);

                        var codigosDelProducto = _codigosGridList.Where(c => c.ProductoId == productoId).ToList();
                        var nuevosRangos = _serviceMovimiento.GenerarRangosDesdeCodigos(codigosDelProducto);

                        _serviceMovimiento.ReemplazarRangosProducto(_rangosProcesadosGlobal, productoId, nuevosRangos);
                    }

                    RefrescarGrillas();
                    if (_productosGridList.Count > 0) dgProductos.SelectedItem = _productosGridList.Last();

                    MessageBox.Show($"Importación finalizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
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
            else if (loadingTransfer.ErrorResult != null)
            {
                MessageBox.Show($"Error crítico al procesar la lista en kárdex:\n{loadingTransfer.ErrorResult.Message}", "Error de Negocio", MessageBoxButton.OK, MessageBoxImage.Error);
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

            int idMotivo = Convert.ToInt32(cboMotivo.SelectedValue);

            if (_printMode || _anularMode) return;

            txtSerieGuia.IsEnabled = true;
            txtNumeroGuia.IsEnabled = true;

            // 🔴 MOTIVO 1 (COMPRA / INGRESO DE IMPRENTA)
            if (idMotivo == 1 || idMotivo == 2)
            {
                txtRazonSocial.IsEnabled = true;

                // Bloqueados porque el ingreso es directo a tu almacén de sesión
                txtUbicacion.IsEnabled = false;
                cboAlmacenDestino.IsEnabled = false;
            }
            // 🔵 MOTIVO 4 / 10 (TRANSFERENCIA INTER-ALMACÉN)
            else if (idMotivo == 4)
            {
                txtRazonSocial.IsEnabled = false;

                // Se habilitan Ubicación (Referencial) y el Almacén Origen/Destino de procedencia
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }
            // 🟣 OTROS MOTIVOS (Flexible)
            else
            {
                txtRazonSocial.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }

            // Limpieza de campos si son deshabilitados
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
            }
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
            // 🛡️ Si Ubicación no está habilitada para este motivo, oculta el desplegable y sale
            if (!txtUbicacion.IsEnabled || _isUpdatingFromSelection)
            {
                if (popupUbicacion != null) popupUbicacion.IsOpen = false;
                return;
            }

            string busqueda = txtUbicacion.Text;
            if (string.IsNullOrWhiteSpace(busqueda))
            {
                popupUbicacion.IsOpen = false;
                return;
            }

            var resultados = _ubicacionService.BuscarUbicaciones(busqueda);
            if (resultados != null && resultados.Count > 0)
            {
                lstSugerenciasUbicacion.ItemsSource = resultados;
                popupUbicacion.IsOpen = true;
            }
            else popupUbicacion.IsOpen = false;
        }

        private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado)
            {
                txtUbicacion.Text = itemSeleccionado.Descripcion;
                txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString();
                txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(itemSeleccionado.Direccion) ? "Sin dirección registrada" : itemSeleccionado.Direccion;

                // 🌟 CAMBIO CLAVE: Almacenamos el ID seleccionado para persistirlo en SQL
                _idUbicacionSeleccionada = itemSeleccionado.Id;

                popupUbicacion.IsOpen = false;
            }
        }
        // 🌟 Método público para cargar directamente el documento al abrir la pestaña
       
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