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

                _idMovimientoActual = movCompleto.Movimiento.Id;
                dtpFechaDespacho.SelectedDate = movCompleto.Movimiento.FechaMovimiento?.ToDateTime(TimeOnly.MinValue);
                cboMotivoSalida.SelectedValue = movCompleto.Movimiento.MotivoProductoId;
                txtSerieGuia.Text = movCompleto.Movimiento.SerieGuia;
                txtNumeroGuia.Text = movCompleto.Movimiento.NumeroGuia;
                txtObservacionSalida.Text = movCompleto.Movimiento.Observacion;

                if (movCompleto.Movimiento.PersonaComercialId.HasValue)
                {
                    _idClienteSeleccionado = movCompleto.Movimiento.PersonaComercialId;
                    try
                    {
                        var persona = await _personaComercialService.ObtenerPorIdAsync(_idClienteSeleccionado.Value);
                        if (persona != null)
                        {
                            txtCliente.TextChanged -= TxtCliente_TextChanged;
                            txtCliente.Text = persona.RazonSocial;
                            txtDireccionCliente.Text = persona.Direccion ?? "";
                            txtCodigoCliente.Text = persona.Id.ToString("D6");
                            txtCliente.TextChanged += TxtCliente_TextChanged;
                        }
                    }
                    catch { }
                }

                if (movCompleto.Movimiento.UbicacionId.HasValue)
                {
                    _idUbicacionSeleccionada = movCompleto.Movimiento.UbicacionId;
                    try
                    {
                        var todas = await _ubicacionService.ObtenerTodasAsync();
                        var ub = todas?.FirstOrDefault(u => u.Id == _idUbicacionSeleccionada.Value);
                        if (ub != null)
                        {
                            txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                            txtUbicacion.Text = ub.Descripcion;
                            txtDireccionUbicacion.Text = ub.Direccion ?? "";
                            txtCodigoUbicacion.Text = ub.Id.ToString();
                            txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
                        }
                    }
                    catch { }
                }

                _productosLista.Clear();
                _codigosLista.Clear();

                var todosLosCodigosPlanos = new List<(int DetId, int CodId, string CodString)>();
                using (var conn = _database.GetConnection())
                {
                    await ((DbConnection)conn).OpenAsync();
                    using (var cmd = ((DbConnection)conn).CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    SELECT mc.movimiento_detalle_id, cc.id, cc.codigo 
                    FROM movimiento_codigos mc 
                    INNER JOIN codigos_creados cc ON mc.codigo_creado_id = cc.id 
                    WHERE mc.movimiento_id = @movId");

                        var p = cmd.CreateParameter(); p.ParameterName = "@movId"; p.Value = _idMovimientoActual.Value; cmd.Parameters.Add(p);

                        using var rdr = await cmd.ExecuteReaderAsync();
                        while (await rdr.ReadAsync())
                        {
                            todosLosCodigosPlanos.Add((rdr.GetInt32(0), rdr.GetInt32(1), rdr.GetString(2)));
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
                            string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(c.CodId);

                            _codigosLista.Add(new VistaCodigoGrid
                            {
                                ProductoId = det.ProductoId,
                                CodigoUnique = c.CodString,
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = c.CodId },
                                ColeccionTipo = tipoColeccionReal
                            });
                        }
                    }
                }

                RefrescarGrillas();

                HabilitarCamposFormulario(false);
                ShowExportButtonNearSave();

                txtNumeroSalida.IsReadOnly = true;
                txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar la salida automáticamente: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            if (dgProductosSalida.ItemsSource == null)
                dgProductosSalida.ItemsSource = _productosLista;

            dgProductosSalida.Items.Refresh();

            if (dgProductosSalida.SelectedItem is VistaProductoGrid seleccionado)
            {
                var codigosDelProducto = _codigosLista.Where(x => x.ProductoId == seleccionado.ProductoId);
                int totalCodigosProducto = codigosDelProducto.Count();

                dgCodigosSalida.ItemsSource = codigosDelProducto.Take(500).ToList();

                if (totalCodigosProducto > 500)
                {
                    lblResumenCodigos.Text = $"500 (Viendo) / {totalCodigosProducto}";
                    lblResumenCodigos.ToolTip = "La vista previa lateral se limita a 500 ítems por rendimiento físico. El lote completo está cargado en memoria para guardado.";
                }
                else
                {
                    lblResumenCodigos.Text = $"{totalCodigosProducto} / {totalCodigosProducto}";
                    lblResumenCodigos.ToolTip = null;
                }
            }
            else
            {
                dgCodigosSalida.ItemsSource = _codigosLista.Take(500).ToList();
                lblResumenCodigos.Text = $"500 (Viendo) / {_codigosLista.Count}";
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
            grdFormularioSalida.IsEnabled = false;

            btnAgregarItem.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnModificarDetalle.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;
            btnCancelar.IsEnabled = false;
            btnEscanear.IsEnabled = false;

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

            btnNuevo.IsEnabled = false;
            btnModificarCabecera.IsEnabled = false;

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

            txtNumeroSalida.Text = siguienteCorrelativo;
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

            grdFormularioSalida.IsEnabled = true;
            HabilitarCamposFormulario(false);

            PrepararCajaBusqueda();
            txtSerieSalida.IsEnabled = true;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            btnCancelar.IsEnabled = true;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para ver e imprimir.", "Modo Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            dtpFechaDespacho.IsEnabled = habilitar;
            cboMotivoSalida.IsEnabled = habilitar;
            txtCliente.IsEnabled = habilitar;
            txtUbicacion.IsEnabled = habilitar;
            txtSerieGuia.IsEnabled = habilitar;
            txtNumeroGuia.IsEnabled = habilitar;
            txtObservacionSalida.IsEnabled = habilitar;

            btnAgregarItem.IsEnabled = habilitar;
            btnEliminarItem.IsEnabled = habilitar;
            btnModificarDetalle.IsEnabled = habilitar;
            btnImportarExcel.IsEnabled = habilitar;
            btnEscanear.IsEnabled = habilitar;
            btnGrabarSalida.IsEnabled = habilitar;
        }

        private void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (_anularMode) return;

            EstadoInicialFormulario();
            _anularMode = true;
            _modoActual = ModoFormulario.BuscandoParaEditar;

            grdFormularioSalida.IsEnabled = true;
            HabilitarCamposFormulario(false);

            PrepararCajaBusqueda();
            txtSerieSalida.IsEnabled = true;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            btnNuevo.IsEnabled = false;
            btnModificarCabecera.IsEnabled = false;
            btnImprimirTicket.IsEnabled = false;
            btnAnularSalida.IsEnabled = false;
            btnCancelar.IsEnabled = true;

            ShowAnularButtonNearSave();

            MessageBox.Show("Modo Anulación activado.\n\nIngrese el número de documento que desea anular y presione ENTER para revisar su contenido.", "Preparando Anulación", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    Foreground = btnGrabarSalida.Foreground,
                    Style = btnGrabarSalida.Style,
                    Margin = btnGrabarSalida.Margin,
                    Padding = btnGrabarSalida.Padding,
                    MinWidth = btnGrabarSalida.MinWidth,
                    Height = btnGrabarSalida.Height,
                    FontSize = btnGrabarSalida.FontSize,
                    FontWeight = FontWeights.Bold
                };

                _btnAnularDefinitivoSalida.Click += EjecutarAnulacionDefinitivaSalida_Click;
                parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabarSalida) + 1, _btnAnularDefinitivoSalida);
                btnGrabarSalida.IsEnabled = false;
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
            dgProductosSalida.IsReadOnly = true;
            dgCodigosSalida.IsReadOnly = true;

            btnAgregarItem.IsEnabled = false;
            btnModificarDetalle.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnEscanear.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;
        }

        private void LimpiarBotonAnularDinamico()
        {
            if (_btnExportarNearSaveSalida != null && _btnExportarNearSaveSalida.Parent is Panel panelExcel)
            {
                panelExcel.Children.Remove(_btnExportarNearSaveSalida);
                _btnExportarNearSaveSalida = null;
            }
        }

        private async void txtNumeroSalida_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (_modoActual == ModoFormulario.BuscandoParaEditar || _modoActual == ModoFormulario.BuscandoParaImprimir))
            {
                string serie = txtSerieSalida.Text.Trim();
                string numStr = txtNumeroSalida.Text.Trim();

                if (string.IsNullOrEmpty(numStr)) return;
                if (int.TryParse(numStr, out int numVal)) numStr = numVal.ToString("D7");
                txtNumeroSalida.Text = numStr;

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

                    if (movCompleto.Movimiento.EstadoId == 2)
                    {
                        if (_modoActual != ModoFormulario.BuscandoParaImprimir && !_anularMode)
                        {
                            MessageBox.Show("Este movimiento de salida ya está ANULADO y no permite modificaciones.", "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Stop);
                            EstadoInicialFormulario();
                            return;
                        }
                    }

                    _idMovimientoActual = movCompleto.Movimiento.Id;
                    dtpFechaDespacho.SelectedDate = movCompleto.Movimiento.FechaMovimiento?.ToDateTime(TimeOnly.MinValue);
                    cboMotivoSalida.SelectedValue = movCompleto.Movimiento.MotivoProductoId;
                    txtSerieGuia.Text = movCompleto.Movimiento.SerieGuia;
                    txtNumeroGuia.Text = movCompleto.Movimiento.NumeroGuia;
                    txtObservacionSalida.Text = movCompleto.Movimiento.Observacion;

                    if (movCompleto.Movimiento.PersonaComercialId.HasValue)
                    {
                        _idClienteSeleccionado = movCompleto.Movimiento.PersonaComercialId;
                        try
                        {
                            var persona = await _personaComercialService.ObtenerPorIdAsync(_idClienteSeleccionado.Value);
                            txtCliente.TextChanged -= TxtCliente_TextChanged;
                            txtCliente.Text = persona?.RazonSocial ?? "";
                            txtCliente.TextChanged += TxtCliente_TextChanged;
                            popupClientes.IsOpen = false;
                        }
                        catch { txtCliente.Text = $"ID CLIENTE: {_idClienteSeleccionado}"; }
                    }

                    if (movCompleto.Movimiento.UbicacionId.HasValue)
                    {
                        _idUbicacionSeleccionada = movCompleto.Movimiento.UbicacionId;
                        try
                        {
                            var todas = await _ubicacionService.ObtenerTodasAsync();
                            var ub = todas?.FirstOrDefault(u => u.Id == _idUbicacionSeleccionada.Value);
                            txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                            txtUbicacion.Text = ub?.Descripcion ?? "";
                            txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
                            popupUbicaciones.IsOpen = false;
                        }
                        catch { txtUbicacion.Text = $"ID UBIC: {_idUbicacionSeleccionada}"; }
                    }

                    _productosLista.Clear();
                    _codigosLista.Clear();

                    if (movCompleto.Detalles == null || !movCompleto.Detalles.Any())
                    {
                        RefrescarGrillas();
                        return;
                    }

                    var todosLosCodigosPlanos = new List<(int DetId, int CodId, string CodString)>();

                    string indexHint = QueryAdapter.EsMySQL ? "" : "WITH (INDEX(IX_codigos_creados_codigo_perf))";

                    using (var conn = new DatabaseConnection().GetConnection())
                    {
                        await ((DbConnection)conn).OpenAsync();
                        using (var cmd = ((DbConnection)conn).CreateCommand())
                        {
                            cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                    SELECT mc.movimiento_detalle_id, cc.id, cc.codigo, c.ano, rc.categoria_producto_id
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
                                    todosLosCodigosPlanos.Add((rdr.GetInt32(0), rdr.GetInt32(1), rdr.GetString(2)));
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
                                string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(c.CodId);

                                _codigosLista.Add(new VistaCodigoGrid
                                {
                                    ProductoId = det.ProductoId,
                                    CodigoUnique = c.CodString,
                                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = c.CodId },
                                    ColeccionTipo = tipoColeccionReal
                                });
                            }
                        }
                    }

                    RefrescarGrillas();

                    txtNumeroSalida.IsReadOnly = true;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;

                    if (_modoActual == ModoFormulario.BuscandoParaEditar)
                    {
                        if (_anularMode)
                        {
                            BloquearParaAnulacionVisual();
                            ShowAnularButtonNearSave();

                            if (movCompleto.Movimiento.EstadoId == 2)
                            {
                                _btnAnularDefinitivoSalida.Content = "🔒 LA SALIDA YA HA SIDO ANULADA";
                                _btnAnularDefinitivoSalida.IsEnabled = false;
                                _btnAnularDefinitivoSalida.Background = System.Windows.Media.Brushes.DarkGray;
                            }
                        }
                        else
                        {
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
                        HabilitarCamposFormulario(false);
                        ShowExportButtonNearSave();
                    }
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
        private void btnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.IsAddAction = true;
            modal.EstadoPermitido = 3;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados;
                if (productoSelected == null) return;

                int idProducto = productoSelected.Id;

                this.Cursor = Cursors.Wait;
                try
                {
                    dgCodigosSalida.ItemsSource = null;
                    dgProductosSalida.ItemsSource = null;

                    var ingService = new IngresoMovimientoService();
                    int ignoradosPorDuplicado = 0;

                    foreach (var rango in rangosDelModal)
                    {
                        string tipoColeccion = string.IsNullOrEmpty(rango.ColeccionTipo) ? "LIBRO VENTA" : rango.ColeccionTipo;

                        if (rango.DesdeNum == -1)
                        {
                            string codNorm = ingService.NormalizarCodigo(rango.AbreviaturaBase);
                            if (!_codigosLista.Any(c => c.ProductoId == idProducto && ingService.NormalizarCodigo(c.CodigoUnique) == codNorm))
                            {
                                _codigosLista.Add(new VistaCodigoGrid
                                {
                                    CodigoUnique = rango.AbreviaturaBase,
                                    ProductoId = idProducto,
                                    ColeccionTipo = tipoColeccion
                                });
                            }
                            else
                            {
                                ignoradosPorDuplicado++;
                            }
                        }
                        else
                        {
                            for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                            {
                                string codGenerado = $"{rango.AbreviaturaBase}-{i:D7}";
                                string codNorm = ingService.NormalizarCodigo(codGenerado);

                                // 🛡️ CANDADO ANTI-DUPLICADOS
                                if (!_codigosLista.Any(c => c.ProductoId == idProducto && ingService.NormalizarCodigo(c.CodigoUnique) == codNorm))
                                {
                                    _codigosLista.Add(new VistaCodigoGrid
                                    {
                                        CodigoUnique = codGenerado,
                                        ProductoId = idProducto,
                                        ColeccionTipo = tipoColeccion
                                    });
                                }
                                else
                                {
                                    ignoradosPorDuplicado++;
                                }
                            }
                        }
                    }

                    if (ignoradosPorDuplicado > 0)
                    {
                        MessageBox.Show($"Se ignoraron {ignoradosPorDuplicado} código(s) duplicado(s) que ya estaban en la lista.", "Aviso de Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    var prodService = new ProductoService();
                    var prodData = Task.Run(() => prodService.ObtenerPorIdAsync(idProducto)).Result;

                    // Recalcular cantidad total real basándonos en los códigos de la grilla
                    int conteoCodigosReal = _codigosLista.Count(c => c.ProductoId == idProducto);
                    int cantidadFinal = conteoCodigosReal > 0 ? conteoCodigosReal : modal.CantidadProductoIngresada;

                    var existente = _productosLista.FirstOrDefault(p => p.ProductoId == idProducto);
                    if (existente != null)
                    {
                        existente.Cantidad = cantidadFinal;
                        if (existente.Detalle != null) existente.Detalle.CantidadSalida = cantidadFinal;
                    }
                    else
                    {
                        _productosLista.Add(new VistaProductoGrid
                        {
                            ProductoId = idProducto,
                            CodigoProducto = prodData?.Abreviatura ?? idProducto.ToString(),
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                            Cantidad = cantidadFinal,
                            Detalle = new MovimientoDetalle
                            {
                                ProductoId = idProducto,
                                CantidadSalida = cantidadFinal,
                                CostoUnitario = prodData?.PrecioUnitario ?? 0
                            }
                        });
                    }

                    RefrescarGrillas();
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        private async void DgProductosSalida_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
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

            var todosLosCodigosDelProducto = _codigosLista
         .Where(c => c.ProductoId == seleccionado.ProductoId)
         .ToList();

            var rangos = ReconstruirRangosDeCodigos(seleccionado.ProductoId);

            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.InitializeForEdit(seleccionado, rangos);

            if (seleccionado.Detalle.CostoUnitario == 0)
            {
                var prodService = new ProductoService();
                var prodActualizado = await prodService.ObtenerPorIdAsync(seleccionado.ProductoId);
                seleccionado.Detalle.CostoUnitario = prodActualizado?.PrecioUnitario ?? 0;
            }

            seleccionado.Detalle.CantidadIngreso = seleccionado.Detalle.CantidadSalida;
            var rangosReconstruidos = ReconstruirRangosDeCodigos(seleccionado.ProductoId);

            modal.InitializeForEdit(seleccionado, rangosReconstruidos);
            modal.EstadoPermitido = 3;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                seleccionado.Detalle.CantidadSalida = modal.CantidadProductoIngresada;
                seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;
                seleccionado.Cantidad = modal.CantidadProductoIngresada;

                if (modal.ListaRangosAgregados != null)
                {
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

                    var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings);

                    foreach (var codStr in listaStrings)
                    {
                        string norm = ingService.NormalizarCodigo(codStr);
                        if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                        {
                            string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                            if (_codigosLista.Any(x => x.MovCodigo.CodigoCreadoId == tup.CodigoObj.Id))
                            {
                                continue;
                            }
                            _codigosLista.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                CodigoUnique = tup.CodigoObj.Codigo,
                                ProductoId = seleccionado.ProductoId,
                                ColeccionTipo = tipoColeccionReal
                            });
                        }
                    }
                }
                RefrescarGrillas();
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
            if (dgProductosSalida.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                var codigosFiltrados = _codigosLista.Where(c => c.ProductoId == productoSeleccionado.ProductoId).ToList();
                dgCodigosSalida.ItemsSource = codigosFiltrados;

                btnModificarDetalle.IsEnabled = true;
                btnEliminarItem.IsEnabled = true;
            }
            else
            {
                dgCodigosSalida.ItemsSource = _codigosLista.ToList();
                btnModificarDetalle.IsEnabled = false;
                btnEliminarItem.IsEnabled = false;
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

            if (idMotivo == 4 || idMotivo == 10)
            {
                bool tieneUbicacion = !string.IsNullOrWhiteSpace(txtUbicacion.Text) && _idUbicacionSeleccionada.HasValue;
                bool tieneAlmacenDestino = cboAlmacenDestino.SelectedValue != null;

                if (!tieneUbicacion && !tieneAlmacenDestino)
                {
                    MessageBox.Show("Para una Transferencia debe seleccionar al menos una Ubicación Referencial O un Almacén Destino.", "Validación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                var movimiento = new Movimiento
                {
                    SerieDocumento = txtSerieSalida.Text,
                    NumeroDocumento = txtNumeroSalida.Text,
                    FechaMovimiento = dtpFechaDespacho.SelectedDate.HasValue ? DateOnly.FromDateTime(dtpFechaDespacho.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Now),

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
                    NumeroGuia = txtNumeroGuia.Text,
                    CreatedAt = DateTime.Now
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
                var detallesPlanos = snapshot.Select(p => new VistaProductoGrid
                {
                    ProductoId = p.ProductoId,
                    CodigoProducto = p.CodigoProducto,
                    Descripcion = p.Descripcion,
                    UnidadMedida = p.UnidadMedida,
                    Cantidad = p.Detalle != null ? Convert.ToDecimal(p.Detalle.CantidadSalida) : p.Cantidad,
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = p.ProductoId,
                        CantidadSalida = p.Detalle?.CantidadSalida ?? p.Cantidad,
                        CostoUnitario = p.Detalle?.CostoUnitario ?? 0,
                        Id = p.Detalle?.Id ?? 0
                    }
                }).ToList();

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
                win.EstadoPermitido = 3;

                if (win.ShowDialog() != true) return;

                var listaRaw = win.CodigosImportados ?? new List<string>();
                if (!listaRaw.Any()) return;

                var ingService = new IngresoMovimientoService();
                var prodService = new ProductoService();

                var codigosProcesadosLote = new List<VistaCodigoGrid>();

                this.Cursor = Cursors.Wait;

                var progressModal = new ProgressWindow("Procesando Archivo de Despacho", "Sincronizando registros con la grilla de salidas principales...", async (progress) =>
                {
                    var lookup = await ingService.ObtenerCodigosPorListaAsync(listaRaw);
                    int total = listaRaw.Count;
                    int ultimoPorcentajeReportado = -1;

                    for (int i = 0; i < total; i++)
                    {
                        string norm = ingService.NormalizarCodigo(listaRaw[i]);
                        if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null && tup.ProductoId.HasValue)
                        {
                            if (tup.CodigoObj.EstadoId != 3) continue;

                            int pId = tup.ProductoId.Value;

                            // 🛡️ CANDADO ANTI-DUPLICADOS EN MEMORIA
                            bool yaExisteEnGrilla = false;
                            Application.Current.Dispatcher.Invoke(() => {
                                yaExisteEnGrilla = _codigosLista.Any(c => c.ProductoId == pId && ingService.NormalizarCodigo(c.CodigoUnique) == norm);
                            });
                            if (yaExisteEnGrilla) continue;

                            string tipoBD = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                            var nuevoCodigoGrid = new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                CodigoUnique = tup.CodigoObj.Codigo,
                                ProductoId = pId,
                                ColeccionTipo = tipoBD
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

                progressModal.Owner = Window.GetWindow(this);

                if (progressModal.ShowDialog() == true)
                {
                    foreach (var nuevoCod in codigosProcesadosLote)
                    {
                        _codigosLista.Add(nuevoCod);
                    }

                    // Sincronizar cantidades exactas de productos según los códigos ingresados
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
                    MessageBox.Show("Lote de Excel importado correctamente. Los códigos duplicados fueron omitidos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
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

            var lector = new LectorGlobalWindow(async resultado =>
            {
                bool ok = await ProcesarCodigoEscaneadoAsync(resultado);
                if (ok)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RefrescarGrillas();
                    });
                }
                return ok;
            });

            lector.Owner = Window.GetWindow(this);

            lector.Closed += (s, ev) =>
            {
                if (sender is Button b) b.IsEnabled = true;
            };

            lector.ShowDialog();
            RefrescarGrillas();
        }

        private async Task<bool> ProcesarCodigoEscaneadoAsync(LectoraResultDTO resultado)
        {
            int miAlmacenIdSesion = SesionSistema.AlmacenActual?.Id ?? 1;

            if (resultado.AlmacenId != miAlmacenIdSesion)
            {
                MessageBox.Show(
                    $"El código '{resultado.CodigoCompleto}' no se encuentra en su stock.",
                    "Aviso de Stock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            if (resultado.EstadoId != 3)
            {
                MessageBox.Show(
                    $"El código '{resultado.CodigoCompleto}' no está disponible para despacho en almacén.",
                    "Aviso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            if (_codigosLista.Any(x => x.MovCodigo.CodigoCreadoId == resultado.CodigoCreadoId))
            {
                return false;
            }

            string tipoBD = await ObtenerColeccionTipoBDAsync(resultado.CodigoCreadoId);

            _codigosLista.Add(new VistaCodigoGrid
            {
                ProductoId = resultado.ProductoId,
                CodigoUnique = resultado.CodigoCompleto,
                ColeccionTipo = tipoBD,
                MovCodigo = new MovimientoCodigo
                {
                    CodigoCreadoId = resultado.CodigoCreadoId
                }
            });

            var producto = _productosLista
                .FirstOrDefault(x => x.ProductoId == resultado.ProductoId);

            if (producto != null)
            {
                producto.Cantidad++;

                if (producto.Detalle != null)
                    producto.Detalle.CantidadSalida++;
            }
            else
            {
                var prodService = new ProductoService();
                var prodData = await prodService.ObtenerPorIdAsync(resultado.ProductoId);

                _productosLista.Add(new VistaProductoGrid
                {
                    ProductoId = resultado.ProductoId,
                    CodigoProducto = prodData?.Abreviatura ?? resultado.ProductoId.ToString(),
                    Descripcion = prodData?.Descripcion ?? resultado.DescripcionProducto,
                    UnidadMedida = "UNIDAD",
                    Cantidad = 1,
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = resultado.ProductoId,
                        CantidadSalida = 1,
                        CostoUnitario = prodData?.PrecioUnitario ?? 0
                    }
                });
            }

            RefrescarGrillas();

            return true;
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
            int idMotivo = Convert.ToInt32(cboMotivoSalida.SelectedValue);

            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;
            cboAlmacenDestino.IsEnabled = false;

            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11)
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

            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11)
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
            if (filtro.Length >= 2)
            {
                _listaClientes = await _personaComercialService.BuscarPorRazonSocialAsync(filtro);
                lstClientes.ItemsSource = _listaClientes;
                popupClientes.IsOpen = _listaClientes != null && _listaClientes.Count > 0;
            }
            else
            {
                popupClientes.IsOpen = false;
            }
        }

        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtUbicacion.IsEnabled) return;
            _timerUbicacionSalida.Stop();
            _timerUbicacionSalida.Start();
        }

        private async Task EjecutarBusquedaUbicacionSalidaAsync()
        {
            string filtro = txtUbicacion.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaUbicaciones = await _ubicacionService.BuscarUbicacionesPorNombreAsync(filtro);
                lstUbicaciones.ItemsSource = _listaUbicaciones;
                popupUbicaciones.IsOpen = _listaUbicaciones != null && _listaUbicaciones.Count > 0;
            }
            else
            {
                popupUbicaciones.IsOpen = false;
            }
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
                e.Handled = true;
            }
        }

        private void SincronizarCantidadesConCodigos()
        {
            foreach (var producto in _productosLista)
            {
                int count = _codigosLista.Count(c => c.ProductoId == producto.ProductoId);

                if (producto.Detalle != null)
                {
                    producto.Detalle.CantidadSalida = count;
                }
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