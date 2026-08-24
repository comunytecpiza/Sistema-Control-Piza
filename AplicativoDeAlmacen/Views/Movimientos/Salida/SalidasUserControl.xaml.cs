using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.facturaciòn;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class SalidasUserControl : UserControl
    {
        private readonly SalidaMovimientoService _salidaService;
        private readonly PersonaComercialService _personaComercialService;
        private readonly UbicacionService _ubicacionService;

        private List<PersonaComercial> _listaClientes;
        private List<Ubicacion> _listaUbicaciones;

        int idUsuarioLogueado = 1;
        int estadoSalida = 1;

        private readonly ReporteExcelService _reporteService;
        private bool _anularMode = false;
        private Button? _btnAnularDefinitivoSalida = null;
        private bool _isUpdatingFromSelection = false;

        private bool _isBuscandoDocumento = false;
        private Button? _btnExportarNearSaveSalida = null;
        private ObservableCollection<VistaProductoGrid> _productosLista;
        private ObservableCollection<VistaCodigoGrid> _codigosLista;

        private int? _idClienteSeleccionado;
        private int? _idUbicacionSeleccionada;
        private int? _idMovimientoActual;

        private enum ModoFormulario { Ninguno, Nuevo, BuscandoParaEditar, BuscandoParaImprimir }
        private ModoFormulario _modoActual = ModoFormulario.Ninguno;

        private readonly DatabaseConnection _database;

        // ⏱️ TEMPORIZADORES DEBOUNCE (300 ms)
        private System.Windows.Threading.DispatcherTimer _timerClienteSalida;
        private System.Windows.Threading.DispatcherTimer _timerUbicacionSalida;

        public SalidasUserControl()
        {
            _database = new DatabaseConnection();
            InitializeComponent();
            _salidaService = new SalidaMovimientoService();
            _personaComercialService = new PersonaComercialService();
            _ubicacionService = new UbicacionService();
            _reporteService = new ReporteExcelService();
            _productosLista = new ObservableCollection<VistaProductoGrid>();
            _codigosLista = new ObservableCollection<VistaCodigoGrid>();

            // ⏱️ CONFIGURACIÓN DEL TIMER DE CLIENTE (300 ms)
            _timerClienteSalida = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerClienteSalida.Tick += async (s, e) =>
            {
                _timerClienteSalida.Stop();
                await EjecutarBusquedaClienteSalidaAsync();
            };

            // ⏱️ CONFIGURACIÓN DEL TIMER DE UBICACIÓN (300 ms)
            _timerUbicacionSalida = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerUbicacionSalida.Tick += async (s, e) =>
            {
                _timerUbicacionSalida.Stop();
                await EjecutarBusquedaUbicacionSalidaAsync();
            };

            dgProductosSalida.ItemsSource = _productosLista;
            dgCodigosSalida.ItemsSource = _codigosLista;

            ConfigurarFormatoCamposGuia();
            EstadoInicialFormulario();
            CargarComboMotivosSalida();
            _ = CargarComboAlmacenesDestinoAsync();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? System.DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async void CargarDocumentoParaConsulta(string serie, string numero)
        {
            try
            {
                _modoActual = ModoFormulario.BuscandoParaImprimir;
                this.Cursor = Cursors.Wait;

                CargarComboMotivosSalida();
                await CargarComboAlmacenesDestinoAsync();

                txtSerieSalida.Text = serie;
                txtNumeroSalida.Text = numero;

                if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                var movCompleto = await _salidaService.GetMovimientoCompletoAsync(serie, numero, miAlmacenId);
                if (movCompleto == null || movCompleto.Movimiento == null)
                {
                    MessageBox.Show("No se encontró el movimiento de salida especificado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var movimiento = movCompleto.Movimiento;
                _idMovimientoActual = movimiento.Id;

                dtpFechaDespacho.SelectedDate = movimiento.FechaMovimiento;
                cboMotivoSalida.SelectedValue = movimiento.MotivoProductoId;
                txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
                txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
                txtObservacionSalida.Text = movimiento.Observacion ?? string.Empty;

                if (movimiento.AlmacenDestinoId.HasValue)
                {
                    cboAlmacenDestino.SelectedValue = movimiento.AlmacenDestinoId.Value;
                }

                if (movimiento.PersonaComercialId.HasValue)
                {
                    _idClienteSeleccionado = movimiento.PersonaComercialId;
                    try
                    {
                        var persona = await _personaComercialService.ObtenerPorIdAsync(_idClienteSeleccionado.Value);
                        if (persona != null)
                        {
                            _isUpdatingFromSelection = true; // 🌟 BLOQUEO DE POPUP
                            txtCliente.Text = persona.RazonSocial;
                            txtDireccionCliente.Text = persona.Direccion ?? "";
                            txtCodigoCliente.Text = persona.Id.ToString("D6");
                            _isUpdatingFromSelection = false;
                        }
                    }
                    catch { txtCliente.Text = $"ID CLIENTE: {_idClienteSeleccionado}"; }
                }

                if (movimiento.UbicacionId.HasValue)
                {
                    _idUbicacionSeleccionada = movimiento.UbicacionId;
                    try
                    {
                        var todas = await _ubicacionService.ObtenerTodasAsync();
                        var ub = todas?.FirstOrDefault(u => u.Id == _idUbicacionSeleccionada.Value);
                        if (ub != null)
                        {
                            _isUpdatingFromSelection = true; // 🌟 BLOQUEO DE POPUP
                            txtUbicacion.Text = ub.Descripcion;
                            txtDireccionUbicacion.Text = ub.Direccion ?? "";
                            txtCodigoUbicacion.Text = ub.Id.ToString();
                            _isUpdatingFromSelection = false;
                        }
                    }
                    catch { txtUbicacion.Text = $"ID UBIC: {_idUbicacionSeleccionada}"; }
                }

                _productosLista.Clear();
                _codigosLista.Clear();

                if (movCompleto.Detalles != null && movCompleto.Detalles.Any())
                {
                    // 1. Añadimos el ID del código a la tupla
                    var todosLosCodigosPlanos = new List<(int DetId, int CodId, string CodString, string TipoColeccion)>();
                    string indexHint = QueryAdapter.EsMySQL ? "" : "WITH (INDEX(IX_codigos_creados_codigo_perf))";

                    using (var conn = new DatabaseConnection().GetConnection())
                    {
                        await ((DbConnection)conn).OpenAsync();
                        using (var cmd = ((DbConnection)conn).CreateCommand())
                        {
                            cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                SELECT mc.movimiento_detalle_id, cc.id, cc.codigo, 
                       CASE 
                           WHEN rc.categoria_producto_id = 1 THEN CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), 'LIBRO GUÍA')
                           ELSE CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), 'LIBRO VENTA')
                       END AS tipo_coleccion
                FROM movimiento_codigos mc 
                INNER JOIN codigos_creados cc {indexHint} ON mc.codigo_creado_id = cc.id 
                LEFT JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                LEFT JOIN colecciones c ON rc.coleccion_id = c.id
                WHERE mc.movimiento_id = @movId");

                            var p = cmd.CreateParameter(); p.ParameterName = "@movId"; p.Value = _idMovimientoActual.Value; cmd.Parameters.Add(p);

                            using (var rdr = await cmd.ExecuteReaderAsync())
                            {
                                while (await rdr.ReadAsync())
                                {
                                    int detId = rdr.GetInt32(0);
                                    int codigoId = rdr.GetInt32(1); // 👈 ID real obtenido de la BD
                                    string codigoStr = rdr.GetString(2);
                                    string tipoCol = rdr.IsDBNull(3) ? "LIBRO VENTA" : rdr.GetString(3);

                                    todosLosCodigosPlanos.Add((detId, codigoId, codigoStr, tipoCol));
                                }
                            }
                        }
                    }

                    var lookupCodigos = todosLosCodigosPlanos.ToLookup(x => x.DetId);
                    var prodService = new ProductoService();

                    foreach (var det in movCompleto.Detalles)
                    {
                        var prodData = await prodService.ObtenerPorIdAsync(det.ProductoId);

                        // 🌟 Identificamos si es un producto sin código (Abreviatura vacía)
                        bool esProductoSinCod = string.IsNullOrWhiteSpace(prodData?.Abreviatura);

                        var vistaProd = new VistaProductoGrid
                        {
                            Detalle = det,
                            ProductoId = det.ProductoId,
                            CodigoProducto = prodData?.Abreviatura ?? det.ProductoId.ToString(),
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            Cantidad = Convert.ToInt32(det.CantidadSalida), // 👈 Muestra la cantidad guardada en BD
                            UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                            EsProductoSinCodigo = esProductoSinCod // 👈 Marca la bandera para evitar que RefrescarGrillas lo ponga en 0
                        };
                        _productosLista.Add(vistaProd);

                        if (lookupCodigos.Contains(det.Id))
                        {
                            foreach (var c in lookupCodigos[det.Id])
                            {
                                _codigosLista.Add(new VistaCodigoGrid
                                {
                                    ProductoId = det.ProductoId,
                                    CodigoUnique = c.CodString,
                                    ColeccionTipo = c.TipoColeccion,
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = c.CodId }
                                });
                            }
                        }
                    }
                }

                dgProductosSalida.ItemsSource = null;
                dgProductosSalida.ItemsSource = _productosLista;
                RefrescarGrillas();

                if (movimiento.EstadoId == 2)
                {
                    if (_modoActual != ModoFormulario.BuscandoParaImprimir && !_anularMode)
                    {
                        MessageBox.Show("Este movimiento de salida está ANULADO (Modo solo lectura).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                        HabilitarCamposFormulario(false);
                        btnGrabarSalida.IsEnabled = false;
                        return;
                    }
                }

                
                if (_modoActual == ModoFormulario.BuscandoParaEditar)
                {
                    if (_anularMode)
                    {
                        BloquearParaAnulacionVisual();
                        ShowAnularButtonNearSave();

                        if (movimiento.EstadoId == 2)
                        {
                            _btnAnularDefinitivoSalida.Content = "🔒 LA SALIDA YA HA SIDO ANULADA";
                            _btnAnularDefinitivoSalida.IsEnabled = false;
                            _btnAnularDefinitivoSalida.Background = System.Windows.Media.Brushes.DarkGray;
                        }
                    }
                    else
                    {
                        // 🌟 MODO EDICIÓN NORMAL: Brillo al 100% y formulario habilitado
                        grdFormularioSalida.IsEnabled = true;
                        grdFormularioSalida.Opacity = 1.0;

                        if (btnAgregarItem?.Parent is Panel panelAccionesDetalle)
                        {
                            panelAccionesDetalle.IsEnabled = true;
                            panelAccionesDetalle.Opacity = 1.0;
                        }

                        cboMotivoSalida.IsEnabled = true;
                        dtpFechaDespacho.IsEnabled = true;
                        txtObservacionSalida.IsEnabled = true;
                        txtNumeroGuia.IsEnabled = true;
                        txtSerieGuia.IsEnabled = true;

                        btnAgregarItem.IsEnabled = true;
                        btnImportarExcel.IsEnabled = true;
                        btnEscanear.IsEnabled = true;
                        btnGrabarSalida.IsEnabled = true;
                        btnCancelar.IsEnabled = true;

                        // 🛡️ SEGURIDAD EN EDICIÓN: Bloquear cabecera superior para evitar anulaciones o saltos de modo
                        btnNuevo.IsEnabled = false;
                        btnModificarCabecera.IsEnabled = false;
                        btnImprimirTicket.IsEnabled = false;
                        btnAnularSalida.IsEnabled = false;

                        ActualizarVisibilidadCampos();
                    }
                }
                else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
                {
                    // 🌟 Sombreamos y bloqueamos formulario y botones inferiores
                    grdFormularioSalida.IsEnabled = false;
                    grdFormularioSalida.Opacity = 0.65;

                    if (btnAgregarItem?.Parent is Panel panelAccionesDetalle)
                    {
                        panelAccionesDetalle.IsEnabled = false;
                        panelAccionesDetalle.Opacity = 0.65;
                    }

                    // 🌟 Bloqueamos los botones superiores de la barra principal
                    btnNuevo.IsEnabled = false;
                    btnModificarCabecera.IsEnabled = false;
                    btnImprimirTicket.IsEnabled = false;
                    btnAnularSalida.IsEnabled = false;

                    HabilitarCamposFormulario(false);

                    if (dgProductosSalida != null)
                    {
                        dgProductosSalida.IsEnabled = true;
                        dgProductosSalida.IsReadOnly = true;
                        dgProductosSalida.IsHitTestVisible = true;
                        dgProductosSalida.Focusable = true;
                    }
                    if (dgCodigosSalida != null)
                    {
                        dgCodigosSalida.IsEnabled = true;
                        dgCodigosSalida.IsReadOnly = true;
                        dgCodigosSalida.IsHitTestVisible = true;
                        dgCodigosSalida.Focusable = true;
                    }

                    if (btnCancelar != null) btnCancelar.IsEnabled = true;

                    ShowExportButtonNearSave();
                }

                txtNumeroSalida.IsReadOnly = true;
                txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al cargar registros de salida: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private async Task<string> ObtenerColeccionTipoBDAsync(int codigoCreadoId)
        {
            try
            {
                using var conn = new DatabaseConnection().GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    SELECT c.ano, rc.categoria_producto_id 
                    FROM codigos_creados cc
                    JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    LEFT JOIN colecciones c ON rc.coleccion_id = c.id
                    WHERE cc.id = @id");

                var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = codigoCreadoId; cmd.Parameters.Add(p);
                using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    string ano = rdr.IsDBNull(0) ? "" : rdr.GetValue(0).ToString();
                    int cat = rdr.IsDBNull(1) ? 1 : rdr.GetInt32(1);
                    string tipo = cat == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    if (!string.IsNullOrEmpty(ano)) return $"C{ano} / {tipo}";
                    return tipo;
                }
            }
            catch { }
            return "LIBRO VENTA";
        }

        private void RecalcularNumerosFila()
        {
            int i = 1;
            foreach (var codigo in _codigosLista)
            {
                codigo.NumeroFila = i++;
            }

            SincronizarCantidadesConCodigos();
        }

        private void RefrescarGrillas()
        {
            RecalcularNumerosFila();

            // 🌟 BLINDAJE DE CANTIDADES PARA PRODUCTOS CON Y SIN CÓDIGO
            foreach (var prod in _productosLista)
            {
                int cantidadCodigosEnGrilla = _codigosLista.Count(x => x.ProductoId == prod.ProductoId);

                if (cantidadCodigosEnGrilla > 0)
                {
                    // Producto con códigos
                    prod.Cantidad = cantidadCodigosEnGrilla;

                    if (prod.Detalle != null)
                        prod.Detalle.CantidadSalida = cantidadCodigosEnGrilla;
                }
                else if (prod.Detalle != null && prod.Detalle.CantidadSalida > 0)
                {
                    // 🎒 Producto sin código: respetar la cantidad guardada
                    prod.Cantidad = (int)prod.Detalle.CantidadSalida;
                }
            }

            if (dgProductosSalida.ItemsSource == null)
                dgProductosSalida.ItemsSource = _productosLista;

            dgProductosSalida.Items.Refresh();

            if (dgProductosSalida.SelectedItem == null && _productosLista.Any())
                dgProductosSalida.SelectedItem = _productosLista.First();

            if (dgProductosSalida.SelectedItem is VistaProductoGrid seleccionado)
            {
                var codigosDelProducto = _codigosLista
                    .Where(x => x.ProductoId == seleccionado.ProductoId)
                    .ToList();

                int totalCodigosProducto = codigosDelProducto.Count;

                dgCodigosSalida.ItemsSource = codigosDelProducto.Take(500);

                if (totalCodigosProducto > 500)
                {
                    lblResumenCodigos.Text = $"500 (Viendo) / {totalCodigosProducto}";
                    lblResumenCodigos.ToolTip =
                        "La vista previa muestra los primeros 500 códigos por rendimiento de pantalla. El lote completo está resguardado en memoria RAM para el despacho.";
                }
                else
                {
                    lblResumenCodigos.Text = $"{totalCodigosProducto} / {totalCodigosProducto}";
                    lblResumenCodigos.ToolTip = null;
                }
            }
            else
            {
                dgCodigosSalida.ItemsSource = _codigosLista.Take(500);
                lblResumenCodigos.Text = $"500 / {_codigosLista.Count}";
            }

            dgCodigosSalida.Items.Refresh();
        }

        private List<RangoCodigoItem> ReconstruirRangosDeCodigos(int productoId)
        {
            var resultado = new List<RangoCodigoItem>();
            var codigosDelProducto = _codigosLista.Where(c => c.ProductoId == productoId).ToList();
            if (!codigosDelProducto.Any()) return resultado;

            var secuenciales = new List<VistaCodigoGrid>();
            var alfanumericosPuros = new List<VistaCodigoGrid>();

            foreach (var c in codigosDelProducto.Where(x => !string.IsNullOrWhiteSpace(x.CodigoUnique)))
            {
                int posGuion = c.CodigoUnique.LastIndexOf('-');
                if (posGuion >= 0 && int.TryParse(c.CodigoUnique.Substring(posGuion + 1), out _))
                    secuenciales.Add(c);
                else
                    alfanumericosPuros.Add(c);
            }

            if (secuenciales.Any())
            {
                var gruposBase = secuenciales.Select(c =>
                {
                    int pos = c.CodigoUnique.LastIndexOf('-');
                    return new
                    {
                        Codigo = c,
                        Abreviatura = c.CodigoUnique.Substring(0, pos),
                        Numero = int.Parse(c.CodigoUnique.Substring(pos + 1))
                    };
                }).GroupBy(x => x.Abreviatura);

                foreach (var grupo in gruposBase)
                {
                    var listaOrdered = grupo.OrderBy(x => x.Numero).ToList();
                    int inicio = listaOrdered[0].Numero;
                    int anterior = listaOrdered[0].Numero;

                    for (int i = 1; i <= listaOrdered.Count; i++)
                    {
                        bool cerrarRango = i == listaOrdered.Count || listaOrdered[i].Numero != anterior + 1;
                        if (cerrarRango)
                        {
                            int cant = (anterior - inicio + 1);
                            resultado.Add(new RangoCodigoItem
                            {
                                productoId = productoId,
                                AbreviaturaBase = grupo.Key,
                                DesdeNum = inicio,
                                HastaNum = anterior,
                                Cantidad = cant.ToString(),
                                Desde = $"{grupo.Key}-{inicio:D7}",
                                Hasta = $"{grupo.Key}-{anterior:D7}",
                                ColeccionTipo = listaOrdered[i - 1].Codigo.ColeccionTipo,
                                CategoriaProductoId = (listaOrdered[i - 1].Codigo.ColeccionTipo != null && listaOrdered[i - 1].Codigo.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")) ? 1 : 2
                            });

                            if (i < listaOrdered.Count)
                            {
                                inicio = listaOrdered[i].Numero;
                                anterior = listaOrdered[i].Numero;
                            }
                        }
                        else { anterior = listaOrdered[i].Numero; }
                    }
                }
            }

            foreach (var alfa in alfanumericosPuros)
            {
                resultado.Add(new RangoCodigoItem
                {
                    productoId = productoId,
                    AbreviaturaBase = alfa.CodigoUnique,
                    DesdeNum = -1,
                    HastaNum = -1,
                    Cantidad = "1",
                    Desde = alfa.CodigoUnique,
                    Hasta = alfa.CodigoUnique,
                    ColeccionTipo = alfa.ColeccionTipo,
                    CategoriaProductoId = (alfa.ColeccionTipo != null && alfa.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")) ? 1 : 2
                });
            }

            return resultado;
        }

        private void EstadoInicialFormulario()
        {
            _isBuscandoDocumento = false;

            if (grdFormularioSalida != null)
            {
                grdFormularioSalida.IsEnabled = true;
                grdFormularioSalida.Opacity = 1.0;
            }

            if (btnAgregarItem?.Parent is Panel panelAccionesInit)
            {
                panelAccionesInit.IsEnabled = true;
                panelAccionesInit.Opacity = 1.0;
            }

            if (dgProductosSalida != null) { dgProductosSalida.IsHitTestVisible = true; dgProductosSalida.Focusable = true; }
            if (dgCodigosSalida != null) { dgCodigosSalida.IsHitTestVisible = true; dgCodigosSalida.Focusable = true; }

            grdFormularioSalida.IsEnabled = false;

            btnAgregarItem.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnModificarDetalle.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;
            btnCancelar.IsEnabled = false;
            btnEscanear.IsEnabled = false;

            // 🌟 RESTAURAR BARRA SUPERIOR
            btnNuevo.IsEnabled = true;
            btnModificarCabecera.IsEnabled = true;
            btnImprimirTicket.IsEnabled = true;
            btnAnularSalida.IsEnabled = true;

            txtSerieSalida.Clear();
            txtNumeroSalida.Clear();
            txtNumeroSalida.IsReadOnly = true;
            txtNumeroGuia.Clear();
            txtSerieGuia.Clear();
            dtpFechaDespacho.SelectedDate = null;
            cboMotivoSalida.SelectedIndex = -1;

            txtCliente.Clear(); txtCodigoCliente.Clear(); txtDireccionCliente.Clear();
            txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear();
            txtObservacionSalida.Clear();

            _idClienteSeleccionado = null; _idUbicacionSeleccionada = null; _idMovimientoActual = null;
            _productosLista.Clear(); _codigosLista.Clear();
            if (cboAlmacenDestino != null)
            {
                cboAlmacenDestino.SelectedIndex = -1;
                cboAlmacenDestino.IsEnabled = false;
            }
            dgProductosSalida.ItemsSource = null; dgCodigosSalida.ItemsSource = null;
            _modoActual = ModoFormulario.Ninguno;
        }

        private void ConfigurarFormatoCamposGuia()
        {
            if (txtSerieGuia != null && txtNumeroGuia != null)
            {
                txtSerieGuia.MaxLength = 4;
                txtNumeroGuia.MaxLength = 7;

                txtSerieGuia.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };

                txtSerieGuia.LostFocus += (s, e) => {
                    if (int.TryParse(txtSerieGuia.Text, out int val))
                        txtSerieGuia.Text = val.ToString("D4");
                    else if (!string.IsNullOrWhiteSpace(txtSerieGuia.Text))
                        txtSerieGuia.Text = txtSerieGuia.Text.PadLeft(4, '0');
                };

                txtNumeroGuia.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };

                txtNumeroGuia.LostFocus += (s, e) => {
                    if (int.TryParse(txtNumeroGuia.Text, out int val))
                        txtNumeroGuia.Text = val.ToString("D7");
                    else if (!string.IsNullOrWhiteSpace(txtNumeroGuia.Text))
                        txtNumeroGuia.Text = txtNumeroGuia.Text.PadLeft(7, '0');
                };
            }
        }

        private void PrepararCajaBusqueda()
        {
            txtNumeroSalida.Text = string.Empty;
            txtNumeroSalida.IsReadOnly = false;
            txtNumeroSalida.IsEnabled = true;
            txtNumeroSalida.Background = System.Windows.Media.Brushes.White;
            txtNumeroSalida.Foreground = System.Windows.Media.Brushes.Black;
            txtNumeroSalida.FontWeight = FontWeights.Normal;
            txtNumeroSalida.FontStyle = FontStyles.Normal;
            txtNumeroSalida.Focus();
        }

        private async void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.Nuevo;
            grdFormularioSalida.IsEnabled = true;

            cboMotivoSalida.IsEnabled = true;
            dtpFechaDespacho.IsEnabled = true;
            txtObservacionSalida.IsEnabled = true;
            txtNumeroGuia.IsEnabled = true;
            txtSerieGuia.IsEnabled = true;

            btnAgregarItem.IsEnabled = true;
            btnImportarExcel.IsEnabled = true;
            btnEscanear.IsEnabled = true;
            btnGrabarSalida.IsEnabled = true;
            btnCancelar.IsEnabled = true;

            // 🛡️ BLOQUEO DE SEGURIDAD: Todos los botones superiores desactivados
            btnNuevo.IsEnabled = false;
            btnModificarCabecera.IsEnabled = false;
            btnImprimirTicket.IsEnabled = false;
            btnAnularSalida.IsEnabled = false;

            dtpFechaDespacho.SelectedDate = DateTime.Today;
            ActualizarVisibilidadCampos();

            txtSerieSalida.Text = "0001";


            

            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
            string siguienteCorrelativo = "0000001";

            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (System.Data.Common.DbConnection)conn;
                    await dbConn.OpenAsync();

                    string queryMax = @"
                SELECT COALESCE(MAX(CAST(m.numero_documento AS INT)), 0) + 1 
                FROM movimientos m WITH (NOLOCK)
                INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                WHERE m.serie_documento = '0001' 
                  AND mp.tipo_movimiento_id = 2
                  AND ISNULL(m.almacen_id, ISNULL(m.almacen_origen_id, 1)) = @almId";

                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryMax);
                    AgregarParametro(cmd, "@almId", miAlmacenId);

                    object res = await cmd.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value)
                    {
                        siguienteCorrelativo = Convert.ToInt32(res).ToString("D7");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al calcular correlativo de salida: {ex.Message}");
            }

            
            txtNumeroSalida.Text = "[ AUTOMÁTICO ]";
            txtNumeroSalida.IsReadOnly = true;
            txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;
            txtNumeroSalida.Foreground = System.Windows.Media.Brushes.Gray;
            txtNumeroSalida.FontStyle = FontStyles.Italic;
            txtNumeroSalida.FontWeight = FontWeights.Normal;

            txtSerieSalida.IsEnabled = false;
            txtNumeroSalida.IsEnabled = false;
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.BuscandoParaEditar;

            grdFormularioSalida.IsEnabled = true;
            cboMotivoSalida.IsEnabled = false;
            dtpFechaDespacho.IsEnabled = false;

            // 🛡️ SEGURIDAD: Bloquear toda la cabecera superior mientras se busca/edita
            btnNuevo.IsEnabled = false;
            btnModificarCabecera.IsEnabled = false;
            btnImprimirTicket.IsEnabled = false;
            btnAnularSalida.IsEnabled = false;

            PrepararCajaBusqueda();
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            btnCancelar.IsEnabled = true;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para cargar y modificar.", "Modo Edición", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.BuscandoParaImprimir;

            // 🌟 1. Mantenemos el contenedor principal HABILITADO para permitir tipear
            grdFormularioSalida.IsEnabled = true;

            // 🌟 2. Bloqueamos solo los campos secundarios de datos
            dtpFechaDespacho.IsEnabled = false;
            cboMotivoSalida.IsEnabled = false;
            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            txtSerieGuia.IsEnabled = false;
            txtNumeroGuia.IsEnabled = false;
            txtObservacionSalida.IsEnabled = false;

            // 🌟 3. Preparamos y enfocamos la caja de búsqueda del número
            PrepararCajaBusqueda();
            txtSerieSalida.IsEnabled = true;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            if (btnCancelar != null) btnCancelar.IsEnabled = true;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para ver e imprimir.", "Modo Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            // 1. Bloquea o habilita el contenedor principal del formulario
            grdFormularioSalida.IsEnabled = habilitar;

            // 2. Controla los controles de fecha y selección
            dtpFechaDespacho.IsEnabled = habilitar;
            cboMotivoSalida.IsEnabled = habilitar;
            txtCliente.IsEnabled = habilitar;
            txtUbicacion.IsEnabled = habilitar;
            txtSerieGuia.IsEnabled = habilitar;
            txtNumeroGuia.IsEnabled = habilitar;
            txtObservacionSalida.IsEnabled = habilitar;

            // 3. Botones de acción sobre el detalle
            btnAgregarItem.IsEnabled = habilitar;
            btnEliminarItem.IsEnabled = habilitar;
            btnModificarDetalle.IsEnabled = habilitar;
            btnImportarExcel.IsEnabled = habilitar;
            btnEscanear.IsEnabled = habilitar;
            btnGrabarSalida.IsEnabled = habilitar;

            // 4. Congela o libera la interacción con las grillas (impidiendo clics/selecciones en modo lectura)
            if (dgProductosSalida != null)
            {
                dgProductosSalida.IsReadOnly = true;
                dgProductosSalida.IsHitTestVisible = habilitar; // 👈 Evita interacción en vista previa
                dgProductosSalida.Focusable = habilitar;
            }
            if (dgCodigosSalida != null)
            {
                dgCodigosSalida.IsReadOnly = true;
                dgCodigosSalida.IsHitTestVisible = habilitar; // 👈 Evita interacción en vista previa
                dgCodigosSalida.Focusable = habilitar;
            }
        }

        private void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (_anularMode) return;

            EstadoInicialFormulario();
            _anularMode = true;
            _modoActual = ModoFormulario.BuscandoParaEditar;

            // 1. 🌟 HABILITAR EL CONTENEDOR PARA PODER ESCRIBIR
            grdFormularioSalida.IsEnabled = true;
            grdFormularioSalida.Opacity = 1.0;

            // 2. 🌟 BLOQUEAR SOLO LOS CAMPOS DE DATOS (NO LA CAJA DE BÚSQUEDA)
            dtpFechaDespacho.IsEnabled = false;
            cboMotivoSalida.IsEnabled = false;
            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            txtSerieGuia.IsEnabled = false;
            txtNumeroGuia.IsEnabled = false;
            txtObservacionSalida.IsEnabled = false;

            btnAgregarItem.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnModificarDetalle.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnEscanear.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;

            // 3. 🌟 PREPARAR Y DESBLOQUEAR LA CAJA DE TEXTO DEL NÚMERO
            PrepararCajaBusqueda();
            txtSerieSalida.IsEnabled = true;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.IsEnabled = true;
            txtNumeroSalida.IsReadOnly = false;
            txtNumeroSalida.Background = System.Windows.Media.Brushes.White;
            txtNumeroSalida.Focus();

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            // 4. 🛡️ BLOQUEAR BOTONES DE CABECERA SUPERIOR
            btnNuevo.IsEnabled = false;
            btnModificarCabecera.IsEnabled = false;
            btnImprimirTicket.IsEnabled = false;
            btnAnularSalida.IsEnabled = false;
            btnCancelar.IsEnabled = true;

            MessageBox.Show("Modo Anulación activado.\n\nIngrese el número de documento y presione ENTER para revisar su contenido.", "Preparando Anulación", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAnularButtonNearSave()
        {
            if (_btnAnularDefinitivoSalida != null) return;

            if (btnGrabarSalida?.Parent is Panel parentPanel)
            {
                _btnAnularDefinitivoSalida = new Button
                {
                    Content = "💥 CONFIRMAR ANULACIÓN",
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC2626")),
                    Foreground = System.Windows.Media.Brushes.White, // 👈 Texto blanco 100% visible
                    Cursor = Cursors.Hand,
                    Margin = btnGrabarSalida.Margin,
                    Padding = btnGrabarSalida.Padding,
                    MinWidth = btnGrabarSalida.MinWidth > 0 ? btnGrabarSalida.MinWidth : 180,
                    Height = btnGrabarSalida.Height > 0 ? btnGrabarSalida.Height : 38,
                    FontSize = btnGrabarSalida.FontSize,
                    FontWeight = FontWeights.Bold
                };

                _btnAnularDefinitivoSalida.Click += EjecutarAnulacionDefinitivaSalida_Click;

                // Ocultar botón grabar y poner el de anular
                btnGrabarSalida.Visibility = Visibility.Collapsed;
                parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabarSalida) + 1, _btnAnularDefinitivoSalida);
            }
        }

        private async void EjecutarAnulacionDefinitivaSalida_Click(object sender, RoutedEventArgs e)
        {
            if (!_idMovimientoActual.HasValue) return;

            var confirmacion = MessageBox.Show($"⚠️ ¿Está absolutamente seguro de ANULAR por completo esta Salida de mercadería ({txtSerieSalida.Text}-{txtNumeroSalida.Text})?\n\nTodos los códigos volverán a estar en Almacén disponibles para despacho.", "Confirmar Reversión de Stock", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmacion != MessageBoxResult.Yes) return;

            try
            {
                this.Cursor = Cursors.Wait;
                if (_btnAnularDefinitivoSalida != null) _btnAnularDefinitivoSalida.IsEnabled = false;

                pbCargaMasiva.Visibility = Visibility.Visible;
                lblPorcentajeCarga.Visibility = Visibility.Visible;
                pbCargaMasiva.Value = 0;
                lblPorcentajeCarga.Text = "0% Iniciando anulación...";

                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (pbCargaMasiva != null) pbCargaMasiva.Value = percent;
                        if (lblPorcentajeCarga != null) lblPorcentajeCarga.Text = $"{percent}% Revirtiendo stock en Almacén...";
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });

                bool resultado = await Task.Run(async () =>
                    await _salidaService.AnularMovimientoSalidaCompletoAsync(_idMovimientoActual.Value, progress)
                );

                if (resultado)
                {
                    MessageBox.Show("¡El movimiento de salida ha sido anulado y el stock ha retornado al almacén con éxito!", "Operación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    LimpiarBotonAnularDinamico();
                    _anularMode = false;
                    EstadoInicialFormulario();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restricción de Kárdex:\n\n{ex.Message}", "Falla en Reversión", MessageBoxButton.OK, MessageBoxImage.Stop);
                if (_btnAnularDefinitivoSalida != null) _btnAnularDefinitivoSalida.IsEnabled = true;

                EventBus.NotificarMovimientosChanged();
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
            // 1. Bloquea visualmente todo el formulario
            grdFormularioSalida.IsEnabled = false;

            // 2. Congela los DataGrids totalmente
            if (dgProductosSalida != null)
            {
                dgProductosSalida.IsReadOnly = true;
                dgProductosSalida.IsHitTestVisible = false;
                dgProductosSalida.Focusable = false;
            }
            if (dgCodigosSalida != null)
            {
                dgCodigosSalida.IsReadOnly = true;
                dgCodigosSalida.IsHitTestVisible = false;
                dgCodigosSalida.Focusable = false;
            }

            // 3. Deshabilita barra de acciones de ítems
            btnAgregarItem.IsEnabled = false;
            btnModificarDetalle.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnEscanear.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;

            // 🌟 Única excepción activa
            if (btnCancelar != null) btnCancelar.IsEnabled = true;
        }

        private void LimpiarBotonAnularDinamico()
        {
            if (_btnAnularDefinitivoSalida != null && _btnAnularDefinitivoSalida.Parent is Panel p)
            {
                p.Children.Remove(_btnAnularDefinitivoSalida);
                _btnAnularDefinitivoSalida = null;
            }

            if (btnGrabarSalida != null)
            {
                btnGrabarSalida.Visibility = Visibility.Visible;
                btnGrabarSalida.IsEnabled = false;
            }
        }


        private async void txtNumeroSalida_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (_modoActual == ModoFormulario.BuscandoParaEditar || _modoActual == ModoFormulario.BuscandoParaImprimir))
            {
                // 🛑 1. CONSUMIR EVENTO Y BLOQUEO ATÓMICO INMEDIATO
                e.Handled = true;

                if (_isBuscandoDocumento) return;
                _isBuscandoDocumento = true;

                // 🛑 2. DESENGANCHAR EVENTO Y BLOQUEAR CAJA AL PRIMER ENTER
                txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
                txtNumeroSalida.IsReadOnly = true;
                txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;

                string serie = txtSerieSalida.Text.Trim();
                string numStr = txtNumeroSalida.Text.Trim();

                if (string.IsNullOrWhiteSpace(numStr))
                {
                    txtNumeroSalida.IsReadOnly = false;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.White;
                    txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;
                    _isBuscandoDocumento = false;
                    return;
                }

                if (int.TryParse(numStr, out int numVal)) numStr = numVal.ToString("D7");
                txtNumeroSalida.Text = numStr;

                // 🛑 3. LIMPIEZA PREVENTIVA DE MEMORIA
                _productosLista.Clear();
                _codigosLista.Clear();

                try
                {
                    this.Cursor = Cursors.Wait;
                    _idMovimientoActual = null;

                    int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                    var movCompleto = await _salidaService.GetMovimientoCompletoAsync(serie, numStr, miAlmacenId);

                    if (movCompleto == null || movCompleto.Movimiento == null)
                    {
                        MessageBox.Show("Movimiento no encontrado o no corresponde a una Salida.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Warning);
                        EstadoInicialFormulario();
                        return;
                    }

                    var movimiento = movCompleto.Movimiento;
                    _idMovimientoActual = movimiento.Id;

                    // CARGA DE CABECERA
                    dtpFechaDespacho.SelectedDate = movimiento.FechaMovimiento;
                    cboMotivoSalida.SelectedValue = movimiento.MotivoProductoId;
                    txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
                    txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
                    txtObservacionSalida.Text = movimiento.Observacion ?? string.Empty;

                    if (movimiento.AlmacenDestinoId.HasValue)
                    {
                        cboAlmacenDestino.SelectedValue = movimiento.AlmacenDestinoId.Value;
                    }

                    if (movimiento.PersonaComercialId.HasValue)
                    {
                        _idClienteSeleccionado = movimiento.PersonaComercialId;
                        try
                        {
                            var persona = await _personaComercialService.ObtenerPorIdAsync(_idClienteSeleccionado.Value);
                            txtCliente.TextChanged -= TxtCliente_TextChanged;
                            txtCliente.Text = persona?.RazonSocial ?? "";
                            txtDireccionCliente.Text = persona?.Direccion ?? "";
                            txtCodigoCliente.Text = persona != null ? persona.Id.ToString("D6") : "";
                            txtCliente.TextChanged += TxtCliente_TextChanged;
                            if (popupClientes != null) popupClientes.IsOpen = false;
                        }
                        catch { txtCliente.Text = $"ID CLIENTE: {_idClienteSeleccionado}"; }
                    }

                    if (movimiento.UbicacionId.HasValue)
                    {
                        _idUbicacionSeleccionada = movimiento.UbicacionId;
                        try
                        {
                            var todas = await _ubicacionService.ObtenerTodasAsync();
                            var ub = todas?.FirstOrDefault(u => u.Id == _idUbicacionSeleccionada.Value);
                            txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                            txtUbicacion.Text = ub?.Descripcion ?? "";
                            txtDireccionUbicacion.Text = ub?.Direccion ?? "";
                            txtCodigoUbicacion.Text = ub != null ? ub.Id.ToString() : "";
                            txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
                            if (popupUbicaciones != null) popupUbicaciones.IsOpen = false;
                        }
                        catch { txtUbicacion.Text = $"ID UBIC: {_idUbicacionSeleccionada}"; }
                    }

                    // CARGA DE GRILLAS
                    if (movCompleto.Detalles != null && movCompleto.Detalles.Any())
                    {
                        var todosLosCodigosPlanos = new List<(int DetId, int CodId, string CodString, string TipoColeccion)>();
                        string indexHint = QueryAdapter.EsMySQL ? "" : "WITH (INDEX(IX_codigos_creados_codigo_perf))";

                        using (var conn = new DatabaseConnection().GetConnection())
                        {
                            await ((DbConnection)conn).OpenAsync();
                            using (var cmd = ((DbConnection)conn).CreateCommand())
                            {
                                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                                    SELECT mc.movimiento_detalle_id, cc.id, cc.codigo, 
                                           CASE 
                                               WHEN rc.categoria_producto_id = 1 THEN CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), 'LIBRO GUÍA')
                                               ELSE CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), 'LIBRO VENTA')
                                           END AS tipo_coleccion
                                    FROM movimiento_codigos mc 
                                    INNER JOIN codigos_creados cc {indexHint} ON mc.codigo_creado_id = cc.id 
                                    LEFT JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                                    LEFT JOIN colecciones c ON rc.coleccion_id = c.id
                                    WHERE mc.movimiento_id = @movId");

                                var p = cmd.CreateParameter(); p.ParameterName = "@movId"; p.Value = _idMovimientoActual.Value; cmd.Parameters.Add(p);

                                using (var rdr = await cmd.ExecuteReaderAsync())
                                {
                                    while (await rdr.ReadAsync())
                                    {
                                        todosLosCodigosPlanos.Add((
                                            rdr.GetInt32(0),
                                            rdr.GetInt32(1),
                                            rdr.GetString(2),
                                            rdr.IsDBNull(3) ? "LIBRO VENTA" : rdr.GetString(3)
                                        ));
                                    }
                                }
                            }
                        }

                        var lookupCodigos = todosLosCodigosPlanos.ToLookup(x => x.DetId);
                        var prodService = new ProductoService();

                        foreach (var det in movCompleto.Detalles)
                        {
                            var prodData = await prodService.ObtenerPorIdAsync(det.ProductoId);

                            var vistaProd = new VistaProductoGrid
                            {
                                Detalle = det,
                                ProductoId = det.ProductoId,
                                CodigoProducto = prodData?.Abreviatura ?? det.ProductoId.ToString(),
                                Descripcion = prodData?.Descripcion ?? "Desconocido",
                                Cantidad = Convert.ToInt32(det.CantidadSalida),
                                UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD"
                            };
                            _productosLista.Add(vistaProd);

                            if (lookupCodigos.Contains(det.Id))
                            {
                                foreach (var c in lookupCodigos[det.Id])
                                {
                                    _codigosLista.Add(new VistaCodigoGrid
                                    {
                                        ProductoId = det.ProductoId,
                                        CodigoUnique = c.CodString,
                                        ColeccionTipo = c.TipoColeccion,
                                        MovCodigo = new MovimientoCodigo { CodigoCreadoId = c.CodId }
                                    });
                                }
                            }
                        }
                    }

                    dgProductosSalida.ItemsSource = null;
                    dgProductosSalida.ItemsSource = _productosLista;
                    RefrescarGrillas();

                    // CONTROL DE MODO Y BOTONES
                    if (movimiento.EstadoId == 2)
                    {
                        if (_modoActual != ModoFormulario.BuscandoParaImprimir && !_anularMode)
                        {
                            MessageBox.Show("Este movimiento de salida está ANULADO (Modo solo lectura).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                            HabilitarCamposFormulario(false);
                            btnGrabarSalida.IsEnabled = false;
                            return;
                        }
                    }

                    if (_modoActual == ModoFormulario.BuscandoParaEditar)
                    {
                        if (_anularMode)
                        {
                            BloquearParaAnulacionVisual();
                            ShowAnularButtonNearSave();

                            if (movimiento.EstadoId == 2)
                            {
                                _btnAnularDefinitivoSalida.Content = "🔒 LA SALIDA YA HA SIDO ANULADA";
                                _btnAnularDefinitivoSalida.IsEnabled = false;
                                _btnAnularDefinitivoSalida.Background = System.Windows.Media.Brushes.DarkGray;
                            }
                        }
                        else
                        {
                            btnNuevo.IsEnabled = false;
                            btnModificarCabecera.IsEnabled = false;
                            btnImprimirTicket.IsEnabled = false;
                            btnAnularSalida.IsEnabled = false;

                            cboMotivoSalida.IsEnabled = true;
                            dtpFechaDespacho.IsEnabled = true;
                            txtObservacionSalida.IsEnabled = true;
                            txtNumeroGuia.IsEnabled = true;
                            txtSerieGuia.IsEnabled = true;

                            btnAgregarItem.IsEnabled = true;
                            btnImportarExcel.IsEnabled = true;
                            btnEscanear.IsEnabled = true;
                            btnGrabarSalida.IsEnabled = true;
                            btnCancelar.IsEnabled = true;

                            ActualizarVisibilidadCampos();
                        }
                    }
                    else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
                    {
                        grdFormularioSalida.IsEnabled = false;
                        grdFormularioSalida.Opacity = 0.65;

                        if (btnAgregarItem?.Parent is Panel panelAccionesDetalle)
                        {
                            panelAccionesDetalle.IsEnabled = false;
                            panelAccionesDetalle.Opacity = 0.65;
                        }

                        btnNuevo.IsEnabled = false;
                        btnModificarCabecera.IsEnabled = false;
                        btnImprimirTicket.IsEnabled = false;
                        btnAnularSalida.IsEnabled = false;

                        HabilitarCamposFormulario(false);

                        if (dgProductosSalida != null)
                        {
                            dgProductosSalida.IsEnabled = true;
                            dgProductosSalida.IsReadOnly = true;
                            dgProductosSalida.IsHitTestVisible = true;
                            dgProductosSalida.Focusable = true;
                        }
                        if (dgCodigosSalida != null)
                        {
                            dgCodigosSalida.IsEnabled = true;
                            dgCodigosSalida.IsReadOnly = true;
                            dgCodigosSalida.IsHitTestVisible = true;
                            dgCodigosSalida.Focusable = true;
                        }

                        if (btnCancelar != null) btnCancelar.IsEnabled = true;

                        ShowExportButtonNearSave();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error crítico al cargar registros de salida: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _isBuscandoDocumento = false;
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        private void LimpiarBotonesDinamicosCompletamente()
        {
            if (_btnAnularDefinitivoSalida != null && _btnAnularDefinitivoSalida.Parent is Panel p)
            {
                p.Children.Remove(_btnAnularDefinitivoSalida);
                _btnAnularDefinitivoSalida = null;
            }

            if (_btnExportarNearSaveSalida != null && _btnExportarNearSaveSalida.Parent is Panel p2)
            {
                p2.Children.Remove(_btnExportarNearSaveSalida);
                _btnExportarNearSaveSalida = null;
            }
        }

        private void ShowExportButtonNearSave()
        {
            if (_btnExportarNearSaveSalida != null) return;

            if (btnGrabarSalida?.Parent is Panel parentPanel)
            {
                _btnExportarNearSaveSalida = new Button
                {
                    Content = "📊 Exportar Excel",
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")),
                    Foreground = System.Windows.Media.Brushes.White,
                    Style = btnGrabarSalida.Style,
                    Margin = btnGrabarSalida.Margin,
                    Padding = btnGrabarSalida.Padding,
                    MinWidth = 150,
                    Height = btnGrabarSalida.Height,
                    FontSize = btnGrabarSalida.FontSize,
                    FontWeight = FontWeights.Bold
                };

                _btnExportarNearSaveSalida.Click += (s, e) => {
                    _reporteService.GenerarReporteSalida(
                        numeroRegistro: $"{txtSerieSalida.Text}-{txtNumeroSalida.Text}",
                        fecha: dtpFechaDespacho.Text,
                        motivo: cboMotivoSalida.Text,
                        cliente: txtCliente.Text,
                        direccion: txtDireccionCliente.Text,
                        ubicacion: txtUbicacion.Text,
                        guia: $"{txtSerieGuia.Text}-{txtNumeroGuia.Text}",
                        observacion: txtObservacionSalida.Text,
                        productosGridList: _productosLista.ToList(),
                        codigosGridList: _codigosLista.ToList()
                    );
                };

                parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabarSalida) + 1, _btnExportarNearSaveSalida);
                btnGrabarSalida.IsEnabled = false;
            }
        }

        // ==========================================
        // DETALLE: AGREGAR (OPTIMIZADO TRASPASO RÁPIDO)
        // ==========================================

        private async void btnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.IsAddAction = true;
            modal.EstadoPermitido = 3; // Estado 3 = Disponible en Almacén

            modal.MovimientoIdActual = _idMovimientoActual;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados;
                if (productoSelected == null) return;

                int idProducto = productoSelected.Id;
                int miAlmacenActualId = SesionSistema.AlmacenActual?.Id ?? 1;

                // 🌟 Identificamos si es un producto genérico SIN CÓDIGO (ej. Mochilas)
                bool esProductoSinCodigo = string.IsNullOrWhiteSpace(productoSelected.Abreviatura);

                if (!esProductoSinCodigo && (rangosDelModal == null || !rangosDelModal.Any())) return;

                this.Cursor = Cursors.Wait;
                try
                {
                    var ingService = new IngresoMovimientoService();
                    int cantidadFinal = 0;

                    if (esProductoSinCodigo)
                    {
                        // 🎒 PRODUCTO SIN CÓDIGO: La cantidad viene directamente de la caja general de la ventana modal
                        cantidadFinal = modal.CantidadProductoIngresada;
                    }
                    else
                    {
                        // 📚 PRODUCTO CON CÓDIGO: Desglosamos y validamos las series en stock
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

                        var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings, miAlmacenActualId);
                        int ignoradosPorDuplicado = 0;
                        int ignoradosPorAlmacenOEstado = 0;

                        string primerRangoTipo = rangosDelModal.FirstOrDefault()?.ColeccionTipo ?? "LIBRO VENTA";
                        var nuevosCodigosBatch = new List<VistaCodigoGrid>();

                        foreach (var codStr in listaStrings)
                        {
                            string norm = ingService.NormalizarCodigo(codStr);

                            if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                            {
                                if (tup.CodigoObj.EstadoId != 3)
                                {
                                    ignoradosPorAlmacenOEstado++;
                                    continue;
                                }

                                int codigoCreadoId = tup.CodigoObj.Id;

                                if (_codigosLista.Any(c => c.MovCodigo != null && c.MovCodigo.CodigoCreadoId == codigoCreadoId) ||
                                    nuevosCodigosBatch.Any(c => c.MovCodigo?.CodigoCreadoId == codigoCreadoId))
                                {
                                    ignoradosPorDuplicado++;
                                    continue;
                                }

                                nuevosCodigosBatch.Add(new VistaCodigoGrid
                                {
                                    ProductoId = idProducto,
                                    CodigoUnique = tup.CodigoObj.Codigo,
                                    ColeccionTipo = primerRangoTipo,
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoCreadoId }
                                });
                            }
                            else
                            {
                                ignoradosPorAlmacenOEstado++;
                            }
                        }

                        if (ignoradosPorAlmacenOEstado > 0)
                        {
                            MessageBox.Show($"⚠️ Advertencia de Stock:\n\nSe omitieron {ignoradosPorAlmacenOEstado} código(s) porque NO pertenecen a su Almacén actual o ya fueron despachados.", "Stock No Disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }

                        if (ignoradosPorDuplicado > 0)
                        {
                            MessageBox.Show($"Se ignoraron {ignoradosPorDuplicado} código(s) duplicado(s) que ya estaban en la lista.", "Aviso de Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        foreach (var nc in nuevosCodigosBatch)
                        {
                            _codigosLista.Add(nc);
                        }

                        cantidadFinal = _codigosLista.Count(c => c.ProductoId == idProducto);
                    }

                    var prodService = new ProductoService();
                    var prodData = await prodService.ObtenerPorIdAsync(idProducto);

                    int cantidadMostrar = esProductoSinCodigo
                    ? modal.CantidadProductoIngresada
                    : cantidadFinal;

                    var existente = _productosLista.FirstOrDefault(p => p.ProductoId == idProducto);

                    if (existente != null)

                    {
                        existente.EsProductoSinCodigo = esProductoSinCodigo;
                        existente.Cantidad = cantidadMostrar;

                        if (existente.Detalle != null)
                        {
                            existente.Detalle.CantidadSalida = cantidadMostrar;
                            existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado > 0
                                ? modal.CostoUnitarioIngresado
                                : existente.Detalle.CostoUnitario;
                        }
                    }
                    else if (cantidadMostrar > 0)
                    {
                        _productosLista.Add(new VistaProductoGrid
                        {
                            ProductoId = idProducto,
                            CodigoProducto = prodData?.Abreviatura ?? idProducto.ToString(),
                            Descripcion = prodData?.Descripcion ?? productoSelected.Descripcion,
                            UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                            Cantidad = cantidadMostrar,
                            EsProductoSinCodigo = esProductoSinCodigo,
                            Detalle = new MovimientoDetalle
                            {
                                ProductoId = idProducto,
                                CantidadSalida = cantidadMostrar,
                                CostoUnitario = modal.CostoUnitarioIngresado > 0
                                    ? modal.CostoUnitarioIngresado
                                    : (prodData?.PrecioUnitario ?? 0)
                            }
                        });
                    }

                    RefrescarGrillas();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al procesar los ítems: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }



        private async void DgProductosSalida_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 🛡️ CANDADO: Si está consultando o en modo lectura, ignora el doble clic
            if (_modoActual == ModoFormulario.BuscandoParaImprimir || _anularMode) return;

            if (dgProductosSalida.SelectedItem is VistaProductoGrid)
            {
                await EditSelectedProductAsync();
            }
        }

        private async void BtnModificarDetalle_Click(object sender, RoutedEventArgs e)
        {
            if (dgProductosSalida.SelectedItem == null)
            {
                MessageBox.Show("Seleccione un producto del detalle para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await EditSelectedProductAsync();
        }

        private async Task EditSelectedProductAsync()
        {
            if (dgProductosSalida.SelectedItem is not VistaProductoGrid seleccionado) return;

            var rangos = ReconstruirRangosDeCodigos(seleccionado.ProductoId);

            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.MovimientoIdActual = _idMovimientoActual;
            modal.EstadoPermitido = _idMovimientoActual.HasValue ? 0 : 3;
            modal.InitializeForEdit(seleccionado, rangos);

            if (seleccionado.Detalle.CostoUnitario == 0)
            {
                var prodService = new ProductoService();
                var prodActualizado = await prodService.ObtenerPorIdAsync(seleccionado.ProductoId);
                seleccionado.Detalle.CostoUnitario = prodActualizado?.PrecioUnitario ?? 0;
            }

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                this.Cursor = Cursors.Wait;
                try
                {
                    seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                    if (!seleccionado.EsProductoSinCodigo && modal.ListaRangosAgregados != null)
                    {
                        // 1. Limpiar los códigos previos de este producto en memoria
                        var codigosViejos = _codigosLista.Where(c => c.ProductoId == seleccionado.ProductoId).ToList();
                        foreach (var cv in codigosViejos) _codigosLista.Remove(cv);

                        var ingService = new IngresoMovimientoService();
                        var listaStrings = new List<string>();
                        foreach (var rango in modal.ListaRangosAgregados)
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

                        // 2. 🌟 Consulta directa con ID real sin filtrar por almacén (para recuperar tanto los que están en Lima como los de Trujillo)
                        var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings, null);
                        string primerRangoTipo = modal.ListaRangosAgregados.FirstOrDefault()?.ColeccionTipo ?? "LIBRO VENTA";

                        foreach (var codStr in listaStrings)
                        {
                            string norm = ingService.NormalizarCodigo(codStr);
                            if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                            {
                                if (_codigosLista.Any(x => x.MovCodigo?.CodigoCreadoId == tup.CodigoObj.Id)) continue;

                                _codigosLista.Add(new VistaCodigoGrid
                                {
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                    CodigoUnique = tup.CodigoObj.Codigo,
                                    ProductoId = seleccionado.ProductoId,
                                    ColeccionTipo = primerRangoTipo
                                });
                            }
                        }

                        // 3. 🌟 Asignar la cantidad real desglosada
                        int cantidadReal = _codigosLista.Count(c => c.ProductoId == seleccionado.ProductoId);

                        // Si por alguna razón el lookup en memoria no cargó todos pero el modal reportó la cantidad válida:
                        if (cantidadReal == 0 && modal.CantidadProductoIngresada > 0)
                        {
                            cantidadReal = modal.CantidadProductoIngresada;
                        }

                        seleccionado.Cantidad = cantidadReal;
                        if (seleccionado.Detalle != null)
                        {
                            seleccionado.Detalle.CantidadSalida = cantidadReal;
                        }
                    }
                    else if (seleccionado.EsProductoSinCodigo)
                    {
                        seleccionado.Cantidad = modal.CantidadProductoIngresada;
                        if (seleccionado.Detalle != null)
                        {
                            seleccionado.Detalle.CantidadSalida = modal.CantidadProductoIngresada;
                        }
                    }

                    RefrescarGrillas();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al actualizar el producto: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgProductosSalida.SelectedItem is VistaProductoGrid seleccionado)
            {
                if (MessageBox.Show("¿Está seguro de eliminar este producto y sus códigos del detalle?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _productosLista.Remove(seleccionado);

                    var codigosAQuitar = _codigosLista.Where(c => c.ProductoId == seleccionado.ProductoId).ToList();
                    foreach (var c in codigosAQuitar) _codigosLista.Remove(c);

                    RefrescarGrillas();
                }
            }
            else
            {
                MessageBox.Show("Seleccione un producto para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DgProductosSalida_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 1. Control de habilitación de botones según si estamos en modo lectura/consulta o edición
            if (_modoActual == ModoFormulario.BuscandoParaImprimir || _anularMode)
            {
                btnModificarDetalle.IsEnabled = false;
                btnEliminarItem.IsEnabled = false;
            }
            else
            {
                if (dgProductosSalida.SelectedItem != null)
                {
                    btnModificarDetalle.IsEnabled = true;
                    btnEliminarItem.IsEnabled = true;
                }
                else
                {
                    btnModificarDetalle.IsEnabled = false;
                    btnEliminarItem.IsEnabled = false;
                }
            }

            // 2. 🌟 FILTRADO DE CÓDIGOS: Esto es lo que hacía falta que no se interrumpa
            if (dgProductosSalida.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                var codigosFiltrados = _codigosLista.Where(c => c.ProductoId == productoSeleccionado.ProductoId).ToList();
                dgCodigosSalida.ItemsSource = codigosFiltrados;
            }
            else
            {
                dgCodigosSalida.ItemsSource = _codigosLista.ToList();
            }
        }

        private async void btnGrabarSalida_Click(object sender, RoutedEventArgs e)
        {
            if (_productosLista.Count == 0)
            {
                MessageBox.Show("No hay productos en la lista para procesar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (cboMotivoSalida.SelectedValue == null)
            {
                MessageBox.Show("Debe seleccionar un Motivo de Salida.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int idMotivo = Convert.ToInt32(cboMotivoSalida.SelectedValue);



            // 🚚 TRANSFERENCIA ENTRE ALMACENES (Motivo 4 o 10): REGLA DE EXCLUSIÓN MUTUA
            if (idMotivo == 4 || idMotivo == 10)
            {
                bool tieneUbicacion = !string.IsNullOrWhiteSpace(txtUbicacion.Text) && _idUbicacionSeleccionada.HasValue;
                bool tieneAlmacenDestino = cboAlmacenDestino.SelectedValue != null;

                // 🚫 RECHAZO SI SE LLENARON AMBOS A LA VEZ
                if (tieneUbicacion && tieneAlmacenDestino)
                {
                    // Limpieza automática de la cabecera referencial
                    txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                    txtUbicacion.Clear();
                    txtCodigoUbicacion.Clear();
                    txtDireccionUbicacion.Clear();
                    _idUbicacionSeleccionada = null;
                    txtUbicacion.TextChanged += TxtUbicacion_TextChanged;

                    cboAlmacenDestino.SelectedIndex = -1;
                    cboAlmacenDestino.SelectedValue = null;

                    MessageBox.Show(
                        "⚠️ Conflicto de Destino:\n\nPara una Transferencia solo puede seleccionar UNA de las dos opciones:\n" +
                        "• O una Ubicación Referencial/Interna.\n" +
                        "• O un Almacén Destino Físico (Inter-Sedes).\n\n" +
                        "No puede enviar a ambos destinos simultáneamente. Se han limpiado ambos campos para que elija uno solo.",
                        "Destino Inválido",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 🚫 RECHAZO SI NO SE LLENÓ NINGUNO
                if (!tieneUbicacion && !tieneAlmacenDestino)
                {
                    MessageBox.Show(
                        "Para una Transferencia debe seleccionar al menos una Ubicación Referencial O un Almacén Destino Físico.",
                        "Validación Requerida",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            else if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11 || idMotivo == 14)
            {
                if (string.IsNullOrWhiteSpace(txtCliente.Text) || !_idClienteSeleccionado.HasValue)
                {
                    MessageBox.Show("Para este tipo de venta es obligatorio seleccionar el Cliente / Razón Social.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }           

            try
            {
                btnGrabarSalida.IsEnabled = false;
                btnCancelar.IsEnabled = false;
                this.Cursor = Cursors.Wait;

                int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
                int miAlmacenOrigen = SesionSistema.AlmacenActual?.Id ?? 1;
                int? almacenDestinoSel = cboAlmacenDestino.SelectedValue != null ? Convert.ToInt32(cboAlmacenDestino.SelectedValue) : (int?)null;

                // 🌟 1. Combinar fecha elegida con la hora actual exacta
                DateTime fechaDespachoBase = dtpFechaDespacho.SelectedDate ?? DateTime.Today;
                DateTime fechaDespachoConHora = fechaDespachoBase.Date.Add(DateTime.Now.TimeOfDay);

                var movimiento = new Movimiento
                {
                    Id = _idMovimientoActual ?? 0,
                    SerieDocumento = txtSerieSalida.Text,
                    NumeroDocumento = txtNumeroSalida.Text,
                    FechaMovimiento = fechaDespachoConHora, // 👈 Se envía como DateTime con hora

                    UbicacionId = (almacenDestinoSel.HasValue || string.IsNullOrWhiteSpace(txtUbicacion.Text))
                        ? (int?)null
                        : _idUbicacionSeleccionada,

                    PersonaComercialId = txtCliente.IsEnabled ? _idClienteSeleccionado : null,
                    MotivoProductoId = idMotivo,

                    AlmacenId = miAlmacenOrigen,
                    AlmacenOrigenId = miAlmacenOrigen,
                    AlmacenDestinoId = almacenDestinoSel,

                    UsuarioId = usuarioActivoId,
                    Observacion = txtObservacionSalida.Text,
                    SerieGuia = txtSerieGuia.Text,
                    NumeroGuia = txtNumeroGuia.Text
                    // 👈 CreatedAt ya no se asigna aquí para proteger la fecha original al editar
                };

                pbCargaMasiva.Visibility = Visibility.Visible;
                lblPorcentajeCarga.Visibility = Visibility.Visible;
                pbCargaMasiva.Value = 0;
                lblPorcentajeCarga.Text = "0% Procesando despacho...";

                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (pbCargaMasiva != null) pbCargaMasiva.Value = percent;
                        if (lblPorcentajeCarga != null) lblPorcentajeCarga.Text = $"{percent}% Despachando Kárdex...";
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });

                var snapshot = _productosLista.ToList();

                // ✅ VALIDACIÓN CRÍTICAs
                if (snapshot == null || !snapshot.Any())
                {
                    MessageBox.Show("❌ Error crítico: La lista de productos está vacía al guardar.", "Error de Datos", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var detallesPlanos = snapshot.Select(p => new VistaProductoGrid
                {
                    ProductoId = p.ProductoId,
                    CodigoProducto = p.CodigoProducto,
                    Descripcion = p.Descripcion,
                    UnidadMedida = p.UnidadMedida,
                    Cantidad = p.Cantidad,
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = p.ProductoId,
                        CantidadIngreso = 0,
                        CantidadSalida = p.Cantidad,  // ✅ Usa directamente p.Cantidad
                        CostoUnitario = p.Detalle?.CostoUnitario ?? 0,
                        
                        Id = p.Detalle?.Id ?? 0
                    }
                }).ToList();

                // ✅ VALIDACIÓN
                if (!detallesPlanos.Any())
                {
                    MessageBox.Show("❌ Error al procesar los productos para guardar.", "Error de Conversión", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ✅ VALIDACIÓN
                bool tieneProductosConCodigo = _productosLista.Any(p => !p.EsProductoSinCodigo);
                if (tieneProductosConCodigo && (_codigosLista == null || !_codigosLista.Any()))
                {
                    MessageBox.Show("❌ Error: No hay códigos en la lista para procesar.", "Error de Códigos", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool resultado = await Task.Run(async () =>
                    await _salidaService.RegistrarSalidaCompletaAsync(
                     movimiento,
                     detallesPlanos,
                     _codigosLista.ToList(),
                     usuarioActivoId,
                     estadoSalida,
                     _idMovimientoActual,
                     progress)
                );

                if (resultado)
                {
                    string numFinal = movimiento.NumeroDocumento;
                    string serieFinal = movimiento.SerieDocumento;

                    txtNumeroSalida.Text = numFinal;
                    txtNumeroSalida.Foreground = System.Windows.Media.Brushes.Black;
                    txtNumeroSalida.FontWeight = FontWeights.Bold;
                    txtNumeroSalida.FontStyle = FontStyles.Normal;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.White;

                    MessageBox.Show($"¡Salida registrada con éxito!\n\nNúmero de Registro: {serieFinal}-{numFinal}",
                                    "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);

                    EstadoInicialFormulario();
                    EventBus.NotificarMovimientosChanged();
                }
            }
            catch (Exception ex)
            {
                string mensaje = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                MessageBox.Show(mensaje, "Restricción de Kárdex", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                pbCargaMasiva.Visibility = Visibility.Collapsed;
                lblPorcentajeCarga.Visibility = Visibility.Collapsed;
                btnGrabarSalida.IsEnabled = true;
                btnCancelar.IsEnabled = true;
                this.Cursor = Cursors.Arrow;
            }
        }

        private async void BtnImportarExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ImportarCodigos { Owner = Window.GetWindow(this) };
                win.EstadoPermitido = 3; // Estado 3 = Disponible en Almacén

                if (win.ShowDialog() != true) return;

                var listaRaw = win.CodigosImportados ?? new List<string>();
                if (!listaRaw.Any()) return;

                var ingService = new IngresoMovimientoService();
                var prodService = new ProductoService();

                var codigosProcesadosLote = new List<VistaCodigoGrid>();

                this.Cursor = Cursors.Wait;

                var progressModal = new ProgressWindow("Procesando Archivo de Despacho", "Sincronizando registros con la grilla de salidas...", async (progress) =>
                {
                    await Task.Run(async () =>
                    {
                        // 🌟 1. BÚSQUEDA MASIVA EN BD
                        var lookup = await ingService.ObtenerCodigosPorListaAsync(listaRaw);

                        // 🌟 2. HASHSET O(1) PARA GRILLA EXISTENTE (Cero Dispatcher en el bucle)
                        var setCodigosGrilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var c in _codigosLista)
                            {
                                if (!string.IsNullOrEmpty(c.CodigoUnique))
                                {
                                    setCodigosGrilla.Add(ingService.NormalizarCodigo(c.CodigoUnique));
                                }
                            }
                        });

                        int total = listaRaw.Count;
                        int ultimoPorcentajeReportado = -1;

                        for (int i = 0; i < total; i++)
                        {
                            string norm = ingService.NormalizarCodigo(listaRaw[i]);

                            if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null && tup.ProductoId.HasValue)
                            {
                                // Regla de Negocio: En Salida solo entran los que estén en Estado 3
                                if (tup.CodigoObj.EstadoId != 3) continue;

                                int pId = tup.ProductoId.Value;

                                // 🛡️ Búsqueda ultra rápida O(1)
                                if (setCodigosGrilla.Contains(norm)) continue;
                                setCodigosGrilla.Add(norm); // Evita duplicados dentro del lote Excel

                                var nuevoCodigoGrid = new VistaCodigoGrid
                                {
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                    CodigoUnique = tup.CodigoObj.Codigo,
                                    ProductoId = pId,
                                    ColeccionTipo = "LIBRO VENTA"
                                };

                                lock (codigosProcesadosLote)
                                {
                                    codigosProcesadosLote.Add(nuevoCodigoGrid);
                                }
                            }

                            int pct = (i * 100) / total;
                            if (pct > ultimoPorcentajeReportado)
                            {
                                ultimoPorcentajeReportado = pct;
                                progress.Report(pct);
                            }
                        }
                    });
                });

                progressModal.Owner = Window.GetWindow(this);

                if (progressModal.ShowDialog() == true)
                {
                    foreach (var nuevoCod in codigosProcesadosLote)
                    {
                        _codigosLista.Add(nuevoCod);
                    }

                    // Sincronizar cantidades exactas
                    var productosAfectados = codigosProcesadosLote.Select(c => c.ProductoId).Distinct();
                    foreach (var pId in productosAfectados)
                    {
                        int conteoTotal = _codigosLista.Count(c => c.ProductoId == pId);
                        var existente = _productosLista.FirstOrDefault(p => p.ProductoId == pId);

                        if (existente != null)
                        {
                            existente.Cantidad = conteoTotal;
                            if (existente.Detalle != null) existente.Detalle.CantidadSalida = conteoTotal;
                        }
                        else
                        {
                            var prodData = await prodService.ObtenerPorIdAsync(pId);
                            _productosLista.Add(new VistaProductoGrid
                            {
                                ProductoId = pId,
                                CodigoProducto = prodData?.Abreviatura ?? pId.ToString(),
                                Descripcion = prodData?.Descripcion ?? "Desconocido",
                                UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                                Cantidad = conteoTotal,
                                Detalle = new MovimientoDetalle
                                {
                                    ProductoId = pId,
                                    CantidadSalida = conteoTotal,
                                    CostoUnitario = prodData?.PrecioUnitario ?? 0
                                }
                            });
                        }
                    }

                    RefrescarGrillas();
                    MessageBox.Show("Lote importado correctamente. Los códigos duplicados o no disponibles fueron omitidos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar códigos masivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;

            // Evitar múltiples instancias de la ventana lectora
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is LectorGlobalWindow)
                {
                    window.Focus();
                    if (sender is Button b) b.IsEnabled = true;
                    return;
                }
            }

            // 🌟 Instanciación compatible con la tupla (bool Exito, string Mensaje)
            var lector = new LectorGlobalWindow(async resultado =>
            {
                return await ProcesarCodigoEscaneadoAsync(resultado);
            });

            lector.Owner = Window.GetWindow(this);

            lector.Closed += (s, ev) =>
            {
                if (sender is Button b) b.IsEnabled = true;
                RefrescarGrillas();
            };

            lector.ShowDialog();
        }

        private async Task<(bool Exito, string Mensaje)> ProcesarCodigoEscaneadoAsync(LectoraResultDTO resultado)
        {
            if (resultado == null || resultado.CodigoCreadoId <= 0)
                return (false, "El código escaneado no existe en el sistema.");

            int miAlmacenIdSesion = SesionSistema.AlmacenActual?.Id ?? 1;

            // 🛑 1. BLOQUEO DE CÓDIGOS EN TRÁNSITO (ESTADO 5)
            if (resultado.EstadoId == 5)
            {
                return (false, $"'{resultado.CodigoCompleto}' se encuentra en tránsito entre sedes.");
            }

            // 🔒 2. REGLA DE SEDE / ALMACÉN
            if (resultado.AlmacenId != miAlmacenIdSesion)
            {
                return (false, $"'{resultado.CodigoCompleto}' pertenece a otra sede y no está disponible en su stock actual.");
            }

            // 🌟 3. REGLA DE ESTADO OPERATIVO (DEBE ESTAR DISPONIBLE EN ALMACÉN - ESTADO 3)
            if (resultado.EstadoId == 1)
            {
                return (false, $"'{resultado.CodigoCompleto}' está creado pero nunca ingresó a almacén (Estado 1).");
            }
            if (resultado.EstadoId == 4 || resultado.TieneSalida)
            {
                return (false, $"'{resultado.CodigoCompleto}' ya fue despachado o vendido previamente.");
            }
            if (resultado.EstadoId != 3)
            {
                return (false, $"'{resultado.CodigoCompleto}' no está disponible para salida (Estado actual: {resultado.EstadoId}).");
            }

            // 🛡️ 4. REGLA DE CONDICIÓN DEL CÓDIGO (DAÑADO / EXTRAVIADO)
            bool permiteSalida = true;
            string nombreCondicion = "OPERATIVO";
            using (var conn = new DatabaseConnection().GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmdCond = dbConn.CreateCommand();

                string qCond = QueryAdapter.EsMySQL
                    ? @"SELECT COALESCE(cond.permitir_salida, 0), COALESCE(cond.nombre, 'SIN CONDICIÓN')
                FROM codigos_creados cc 
                LEFT JOIN condiciones_codigo cond ON cc.condicion_id = cond.id 
                WHERE cc.id = @codId 
                LIMIT 1;"
                    : @"SELECT ISNULL(cond.permitir_salida, 0), ISNULL(cond.nombre, 'SIN CONDICIÓN') 
                FROM codigos_creados cc WITH (NOLOCK) 
                LEFT JOIN condiciones_codigo cond WITH (NOLOCK) ON cc.condicion_id = cond.id 
                WHERE cc.id = @codId;";

                cmdCond.CommandText = qCond;
                var p = cmdCond.CreateParameter();
                p.ParameterName = "@codId";
                p.Value = resultado.CodigoCreadoId;
                cmdCond.Parameters.Add(p);

                using var rdrCond = await cmdCond.ExecuteReaderAsync();
                if (await rdrCond.ReadAsync())
                {
                    permiteSalida = Convert.ToBoolean(rdrCond.GetValue(0));
                    nombreCondicion = rdrCond.GetString(1);
                }
            }

            if (!permiteSalida)
            {
                return (false, $"'{resultado.CodigoCompleto}' tiene la condición de '{nombreCondicion.ToUpper()}' y no tiene permitida la salida.");
            }

            // 🛡️ 5. ESCUDO CRONOLÓGICO FUTURO (SI SE EDITA UN MOVIMIENTO PASADO)
            if (_idMovimientoActual.HasValue)
            {
                DateTime fechaDoc = DateTime.Today;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    fechaDoc = dtpFechaDespacho.SelectedDate ?? DateTime.Today;
                });

                bool tieneMovPost = await _salidaService.TieneMovimientosPosterioresAsync(
                    resultado.CodigoCreadoId,
                    _idMovimientoActual.Value,
                    fechaDoc,
                    null,
                    null
                );

                if (tieneMovPost)
                {
                    return (false, $"'{resultado.CodigoCompleto}' registra movimientos o despachos posteriores en la línea de tiempo.");
                }
            }

            // 🔑 6. CANDADO ANTI-DUPLICADOS EN LA GRILLA
            if (_codigosLista.Any(x => x.MovCodigo?.CodigoCreadoId == resultado.CodigoCreadoId))
            {
                return (false, $"'{resultado.CodigoCompleto}' ya fue escaneado en este despacho.");
            }

            // 🌟 7. INSERCIÓN INMEDIATA EN MEMORIA (Reutiliza el DTO sin consultas pesadas)
            var prodService = new ProductoService();
            var prodData = await prodService.ObtenerPorIdAsync(resultado.ProductoId);

            Application.Current.Dispatcher.Invoke(() =>
            {
                _codigosLista.Add(new VistaCodigoGrid
                {
                    ProductoId = resultado.ProductoId,
                    CodigoUnique = resultado.CodigoCompleto,
                    ColeccionTipo = resultado.CategoriaProducto,
                    MovCodigo = new MovimientoCodigo
                    {
                        CodigoCreadoId = resultado.CodigoCreadoId
                    }
                });

                var producto = _productosLista.FirstOrDefault(x => x.ProductoId == resultado.ProductoId);

                if (producto != null)
                {
                    producto.Cantidad++;
                    if (producto.Detalle != null)
                        producto.Detalle.CantidadSalida++;
                }
                else
                {
                    _productosLista.Add(new VistaProductoGrid
                    {
                        ProductoId = resultado.ProductoId,
                        CodigoProducto = prodData?.Abreviatura ?? resultado.ProductoId.ToString(),
                        Descripcion = prodData?.Descripcion ?? resultado.DescripcionProducto,
                        UnidadMedida = "PACK",
                        Cantidad = 1,
                        Detalle = new MovimientoDetalle
                        {
                            ProductoId = resultado.ProductoId,
                            CantidadSalida = 1,
                            CostoUnitario = prodData?.PrecioUnitario ?? resultado.PrecioUnitario
                        }
                    });
                }

                RefrescarGrillas();
            });

            return (true, "Aceptado");
        }

        private async void CargarComboMotivosSalida()
        {
            try
            {
                var motivosSalida = await _salidaService.ObtenerMotivosSalidaAsync();
                cboMotivoSalida.ItemsSource = motivosSalida;
                cboMotivoSalida.DisplayMemberPath = "Descripcion";
                cboMotivoSalida.SelectedValuePath = "Id";
            }
            catch (Exception ex) { }
        }

        private async Task CargarComboAlmacenesDestinoAsync()
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                string query = (miAlmacenId == ALMACEN_CENTRAL_ID)
                    ? "SELECT id, nombre FROM almacenes WHERE id != @miAlmacen AND estado_id = 1 ORDER BY nombre ASC"
                    : "SELECT id, nombre FROM almacenes WHERE id = @centralId AND estado_id = 1";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);
                AgregarParametro(cmd, "@centralId", ALMACEN_CENTRAL_ID);

                var listaAlmacenes = new List<dynamic>();
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    listaAlmacenes.Add(new { Id = rdr.GetInt32(0), Nombre = rdr.GetString(1) });
                }

                cboAlmacenDestino.ItemsSource = listaAlmacenes;
                cboAlmacenDestino.SelectedIndex = -1;
                cboAlmacenDestino.SelectedValue = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar almacenes destino: {ex.Message}");
            }
        }

        private void CboMotivoSalida_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMotivoSalida.SelectedValue == null) return;

            // 🛡️ CANDADO DE SEGURIDAD: Si estamos consultando/imprimiendo o en modo lectura, SALIR INMEDIATAMENTE
            if (_modoActual == ModoFormulario.BuscandoParaImprimir || _anularMode) return;

            int idMotivo = Convert.ToInt32(cboMotivoSalida.SelectedValue);

            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            cboAlmacenDestino.IsEnabled = false;

            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11 || idMotivo == 14)
            {
                txtCliente.IsEnabled = true;
            }
            else if (idMotivo == 10 || idMotivo == 4)
            {
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }
            else if (idMotivo == 9 || idMotivo == 12)
            {
                txtCliente.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }

            if (!txtCliente.IsEnabled) { txtCliente.Clear(); txtCodigoCliente.Clear(); txtDireccionCliente.Clear(); _idClienteSeleccionado = null; }
            if (!txtUbicacion.IsEnabled) { txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear(); _idUbicacionSeleccionada = null; }
            if (!cboAlmacenDestino.IsEnabled) { cboAlmacenDestino.SelectedIndex = -1; }
        }

        private void ActualizarVisibilidadCampos()
        {
            if (cboMotivoSalida.SelectedValue == null) return;
            int idMotivo = Convert.ToInt32(cboMotivoSalida.SelectedValue);

            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            cboAlmacenDestino.IsEnabled = false;

            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11 || idMotivo == 14)
            {
                txtCliente.IsEnabled = true;
            }
            else if (idMotivo == 4 || idMotivo == 10)
            {
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }
            else if (idMotivo == 9 || idMotivo == 12)
            {
                txtCliente.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
                cboAlmacenDestino.IsEnabled = true;
            }

            if (!txtCliente.IsEnabled)
            {
                txtCliente.Clear(); txtCodigoCliente.Clear(); txtDireccionCliente.Clear();
                _idClienteSeleccionado = null;
            }

            if (!txtUbicacion.IsEnabled)
            {
                txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear();
                _idUbicacionSeleccionada = null;
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
                if (popupUbicaciones != null) popupUbicaciones.IsOpen = false;
                txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Desea cancelar la operación actual?", "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _anularMode = false;
                LimpiarBotonAnularDinamico();

                if (_btnExportarNearSaveSalida != null && _btnExportarNearSaveSalida.Parent is Panel panelExcel)
                {
                    panelExcel.Children.Remove(_btnExportarNearSaveSalida);
                    _btnExportarNearSaveSalida = null;
                }

                EstadoInicialFormulario();
            }
        }

        // ==========================================
        // BUSCADORES OPTIMIZADOS (DEBOUNCE 300ms)
        // ==========================================
        private void TxtCliente_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtCliente.IsEnabled) return;
            _timerClienteSalida.Stop();
            _timerClienteSalida.Start();
        }

        private async Task EjecutarBusquedaClienteSalidaAsync()
        {
            string filtro = txtCliente.Text.Trim();
            if (filtro.Length >= 1)
            {
                // 🔍 Si es numérico, busca por ID directo; si no, busca por texto
                if (int.TryParse(filtro, out int idCliente))
                {
                    var clientePorId = await _personaComercialService.ObtenerPorIdAsync(idCliente);
                    _listaClientes = clientePorId != null
                        ? new List<PersonaComercial> { clientePorId }
                        : await _personaComercialService.BuscarPorRazonSocialAsync(filtro);
                }
                else if (filtro.Length >= 2)
                {
                    _listaClientes = await _personaComercialService.BuscarPorRazonSocialAsync(filtro);
                }
                else
                {
                    popupClientes.IsOpen = false;
                    return;
                }

                lstClientes.ItemsSource = _listaClientes;
                popupClientes.IsOpen = _listaClientes != null && _listaClientes.Count > 0;
            }
            else
            {
                popupClientes.IsOpen = false;
            }
        }

        private async Task EjecutarBusquedaUbicacionSalidaAsync()
        {
            string filtro = txtUbicacion.Text.Trim();
            if (filtro.Length >= 1)
            {
                // 🔍 Si es numérico, busca por ID directo; si no, busca por texto
                if (int.TryParse(filtro, out int idUbicacion))
                {
                    var todas = await _ubicacionService.ObtenerTodasAsync();
                    var ubicacionPorId = todas?.FirstOrDefault(u => u.Id == idUbicacion);

                    _listaUbicaciones = ubicacionPorId != null
                        ? new List<Ubicacion> { ubicacionPorId }
                        : await _ubicacionService.BuscarUbicacionesPorNombreAsync(filtro);
                }
                else if (filtro.Length >= 2)
                {
                    _listaUbicaciones = await _ubicacionService.BuscarUbicacionesPorNombreAsync(filtro);
                }
                else
                {
                    popupUbicaciones.IsOpen = false;
                    return;
                }

                lstUbicaciones.ItemsSource = _listaUbicaciones;
                popupUbicaciones.IsOpen = _listaUbicaciones != null && _listaUbicaciones.Count > 0;
            }
            else
            {
                popupUbicaciones.IsOpen = false;
            }
        }

        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtUbicacion.IsEnabled) return;
            _timerUbicacionSalida.Stop();
            _timerUbicacionSalida.Start();
        }

        

        private void LstClientes_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (lstClientes.SelectedItem is PersonaComercial cliente)
            {
                txtCliente.TextChanged -= TxtCliente_TextChanged;
                txtCliente.Text = cliente.RazonSocial;
                txtCodigoCliente.Text = cliente.Id.ToString("D6");
                txtDireccionCliente.Text = cliente.Direccion;
                _idClienteSeleccionado = cliente.Id;
                popupClientes.IsOpen = false;
                lstClientes.SelectedIndex = -1;
                txtCliente.TextChanged += TxtCliente_TextChanged;
                e.Handled = true;
            }
        }

        private void LstUbicaciones_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (lstUbicaciones.SelectedItem is Ubicacion ub)
            {
                txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                txtUbicacion.Text = ub.Descripcion;
                txtCodigoUbicacion.Text = ub.Id.ToString();
                txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(ub.Direccion) ? "Sin dirección registrada" : ub.Direccion;
                _idUbicacionSeleccionada = ub.Id;
                popupUbicaciones.IsOpen = false;
                lstUbicaciones.SelectedIndex = -1;
                txtUbicacion.TextChanged += TxtUbicacion_TextChanged;

                // 🌟 Exclusión mutua: Limpia el almacén destino si se escogió ubicación
                if (cboAlmacenDestino != null)
                {
                    cboAlmacenDestino.SelectedIndex = -1;
                    cboAlmacenDestino.SelectedValue = null;
                }

                e.Handled = true;
            }
        }

        private void SincronizarCantidadesConCodigos()
        {
            foreach (var producto in _productosLista)
            {
                if (producto.EsProductoSinCodigo)
                    continue;

                int count = _codigosLista.Count(c => c.ProductoId == producto.ProductoId);

                producto.Cantidad = count;

                if (producto.Detalle != null)
                    producto.Detalle.CantidadSalida = count;
            }

            dgProductosSalida.Items.Refresh();
            dgCodigosSalida.Items.Refresh();
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (dgProductosSalida.SelectedItem is not VistaProductoGrid productoSeleccionado) return;

            string filtro = txtBuscarCodigo.Text.Trim().ToLower();

            var todosLosCodigosDelProducto = _codigosLista
                .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                .ToList();

            if (string.IsNullOrEmpty(filtro))
            {
                dgCodigosSalida.ItemsSource = todosLosCodigosDelProducto.Take(500).ToList();
                lblResumenCodigos.Text = todosLosCodigosDelProducto.Count > 500
                    ? $"500 (Viendo) / {todosLosCodigosDelProducto.Count}"
                    : $"{todosLosCodigosDelProducto.Count} / {todosLosCodigosDelProducto.Count}";
            }
            else
            {
                var filtrados = todosLosCodigosDelProducto
                    .Where(c => c.CodigoUnique.ToLower().Contains(filtro))
                    .ToList();

                dgCodigosSalida.ItemsSource = filtrados;
                lblResumenCodigos.Text = $"{filtrados.Count} (Encontrados) / {todosLosCodigosDelProducto.Count}";
            }
        }
    }
}