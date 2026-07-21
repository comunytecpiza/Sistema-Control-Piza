using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Views.Movimientos.Lectora;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.facturaciòn;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Facturación;
using System.Windows.Media;
using AplicativoDeAlmacen.Services.Reportes;

namespace AplicativoDeAlmacen.Views
{
    public partial class SalidasUserControl : UserControl
    {
        private readonly SalidaMovimientoService _salidaService;
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


        private readonly DatabaseConnection _database; // ¡Debe estar declarado aquí!

      
        
        public SalidasUserControl()
        {

            _database = new DatabaseConnection(); // ¡Debe estar inicializado aquí!
            InitializeComponent();
            _salidaService = new SalidaMovimientoService();
            _reporteService = new ReporteExcelService();
            _productosLista = new ObservableCollection<VistaProductoGrid>();
            _codigosLista = new ObservableCollection<VistaCodigoGrid>();

            dgProductosSalida.ItemsSource = _productosLista;
            dgCodigosSalida.ItemsSource = _codigosLista;

            // 🌟 INYECCIÓN LOGÍSTICA DE LIMITACIONES Y FORMATOS EN CALIENTE
            ConfigurarFormatoCamposGuia();

            EstadoInicialFormulario();
            CargarComboMotivosSalida();
        }

        // Helper local para agregar parámetros a comandos DbCommand
        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? System.DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // ==========================================
        // UTILIDADES Y REFRESCO VISUAL
        // ==========================================

        // 🌟 Método clave para obtener la etiqueta REAL (Venta o Guía) desde la BD
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
            return "LIBRO VENTA"; // Default fallback
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
                // 🚀 OPTIMIZACIÓN LOGÍSTICA: Filtrado directo en memoria sin duplicar objetos pesados (.ToList() masivo)
                var codigosDelProducto = _codigosLista.Where(x => x.ProductoId == seleccionado.ProductoId);
                int totalCodigosProducto = codigosDelProducto.Count();

                // Truncamos la vista a 500 para liberar al motor gráfico de WPF
                dgCodigosSalida.ItemsSource = codigosDelProducto.Take(500).ToList();

                // Sincronizamos las etiquetas de conteo informativas de la UI
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
                // Si no hay producto seleccionado, limitamos la grilla global
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

        // ==========================================
        // ESTADOS DE LA UI
        // ==========================================
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

            // La cabecera siempre activa para iniciar acciones
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

            dgProductosSalida.ItemsSource = null; dgCodigosSalida.ItemsSource = null;
            _modoActual = ModoFormulario.Ninguno;
        }

        private void ConfigurarFormatoCamposGuia()
        {
            if (txtSerieGuia != null && txtNumeroGuia != null)
            {
                // Configurar límites máximos físicos
                txtSerieGuia.MaxLength = 4;
                txtNumeroGuia.MaxLength = 7;

                // Forzar ingreso exclusivo de números en la Serie
                txtSerieGuia.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };

                // Rellenar con ceros a la izquierda al perder el foco (Formato 0000)
                txtSerieGuia.LostFocus += (s, e) => {
                    if (int.TryParse(txtSerieGuia.Text, out int val))
                        txtSerieGuia.Text = val.ToString("D4");
                    else if (!string.IsNullOrWhiteSpace(txtSerieGuia.Text))
                        txtSerieGuia.Text = txtSerieGuia.Text.PadLeft(4, '0');
                };

                // Forzar ingreso exclusivo de números en el Número de Guía
                txtNumeroGuia.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };

                // Rellenar con ceros a la izquierda al perder el foco (Formato 0000000)
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
            txtNumeroSalida.FontStyle = FontStyles.Normal; // Desactiva cursiva
            txtNumeroSalida.Focus();
        }
        // ==========================================
        // CABECERA: NUEVO, MODIFICAR, IMPRIMIR, ANULAR
        // ==========================================
        private async void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _modoActual = ModoFormulario.Nuevo;
            grdFormularioSalida.IsEnabled = true;

            // Habilitamos todos los campos necesarios para empezar a trabajar
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

            // 🌟 CORRECCIÓN AQUÍ: Forzamos serie limpia y enmascaramos el número en la UI
            txtSerieSalida.Text = "0001"; // Serie numérica fija sin la 'S'
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

            // 🌟 Limpia y habilita la escritura limpia en la UI
            PrepararCajaBusqueda();
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            // Siempre permitir cancelar la operación para volver al estado inicial
            btnCancelar.IsEnabled = true;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para cargar y modificar.", "Modo Edición", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.BuscandoParaImprimir;

            grdFormularioSalida.IsEnabled = true;
            HabilitarCamposFormulario(false);

            // 🌟 Limpia y habilita la escritura limpia en la UI
            PrepararCajaBusqueda();
            txtSerieSalida.IsEnabled = true;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "0001";

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            // Permitir cancelar la vista de impresión para volver al inicio
            btnCancelar.IsEnabled = true;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para ver e imprimir.", "Modo Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            // Campos que siempre deben estar bloqueados en Modo Impresión
            dtpFechaDespacho.IsEnabled = habilitar;
            cboMotivoSalida.IsEnabled = habilitar;
            txtCliente.IsEnabled = habilitar;
            txtUbicacion.IsEnabled = habilitar;
            txtSerieGuia.IsEnabled = habilitar;
            txtNumeroGuia.IsEnabled = habilitar;
            txtObservacionSalida.IsEnabled = habilitar;

            // Botones de detalle
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

            // 🌟 ARREGLO: Forzar aparición del botón dinámico de inmediato
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
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC2626")), // Rojo exacto de tu paleta
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

                // 🚀 LLAMADA AL NUEVO MÉTODO DE SALIDAS EN SEGUNDO PLANO ASÍNCRONO REAL
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
                    this.Cursor = Cursors.Wait; // Encendemos reloj de espera
                    _idMovimientoActual = null;

                    var movCompleto = await _salidaService.GetMovimientoCompletoAsync(serie, numStr);

                    if (movCompleto == null || movCompleto.Movimiento == null)
                    {
                        // 🌟 LIMPIEZA INMEDIATA SI NO ENCUENTRA NADA
                        MessageBox.Show("Movimiento no encontrado o no corresponde a una Salida.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Warning);
                        EstadoInicialFormulario();
                        return;
                    }

                    if (movCompleto.Movimiento.EstadoId == 2)
                    {
                        // 🌟 Si está anulado, solo permitimos cargar para imprimir o si estamos en modo anular (verificación posterior)
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

                    // 1. Cargar datos del Cliente relacional
                    if (movCompleto.Movimiento.PersonaComercialId.HasValue)
                    {
                        _idClienteSeleccionado = movCompleto.Movimiento.PersonaComercialId;
                        try
                        {
                            using var conn = new DatabaseConnection().GetConnection();
                            await ((DbConnection)conn).OpenAsync();
                            using var cmd = ((DbConnection)conn).CreateCommand();
                            cmd.CommandText = "SELECT razon_social FROM personas_comerciales WHERE id = " + _idClienteSeleccionado;
                            var res = await cmd.ExecuteScalarAsync();
                            // Evitar disparar el TextChanged que abre el popup de búsqueda
                            txtCliente.TextChanged -= TxtCliente_TextChanged;
                            txtCliente.Text = res?.ToString() ?? "";
                            txtCliente.TextChanged += TxtCliente_TextChanged;
                            popupClientes.IsOpen = false;
                        }
                        catch { txtCliente.Text = $"ID CLIENTE: {_idClienteSeleccionado}"; }
                    }

                    // 2. Cargar datos de la Ubicación relacional
                    if (movCompleto.Movimiento.UbicacionId.HasValue)
                    {
                        _idUbicacionSeleccionada = movCompleto.Movimiento.UbicacionId;
                        try
                        {
                            using var conn = new DatabaseConnection().GetConnection();
                            await ((DbConnection)conn).OpenAsync();
                            using var cmd = ((DbConnection)conn).CreateCommand();
                            cmd.CommandText = "SELECT descripcion FROM ubicaciones WHERE id = " + _idUbicacionSeleccionada;
                            var res = await cmd.ExecuteScalarAsync();
                            // Evitar disparar el TextChanged que abre el popup de ubicaciones
                            txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;
                            txtUbicacion.Text = res?.ToString() ?? "";
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

                    // =========================================================================
                    // 🚀 EXTRAORDINARIA OPTIMIZACIÓN INDUSTRIAL: LECTURA EN BLOQUE UNIFICADA
                    // =========================================================================
                    // Traemos TODOS los códigos de todos los detalles de este movimiento en un solo viaje SQL
                    var todosLosCodigosPlanos = new List<(int DetId, int CodId, string CodString)>();

                    using (var conn = new DatabaseConnection().GetConnection())
                    {
                        await ((DbConnection)conn).OpenAsync();
                        using (var cmd = ((DbConnection)conn).CreateCommand())
                        {
                            cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                            SELECT mc.movimiento_detalle_id, cc.id, cc.codigo, c.ano, rc.categoria_producto_id
                            FROM movimiento_codigos mc 
                            INNER JOIN codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf)) ON mc.codigo_creado_id = cc.id 
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

                    // Mapeamos en un diccionario indexado de RAM rápido por DetalleId (ILookup)
                    var lookupCodigos = todosLosCodigosPlanos.ToLookup(x => x.DetId);
                    var prodService = new ProductoService();

                    // 3. Procesamos y cruzamos de forma instantánea en memoria RAM sin consultar a SQL
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

                        // Si este detalle tiene códigos asociados en nuestro Lookup de RAM, los inyectamos directo
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

                    // Refrescamos y redibujamos la UI en un solo paso limpio
                    RefrescarGrillas();

                    txtNumeroSalida.IsReadOnly = true;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;

                    if (_modoActual == ModoFormulario.BuscandoParaEditar)
                    {
                        if (_anularMode)
                        {
                            BloquearParaAnulacionVisual();
                            ShowAnularButtonNearSave();

                            // 🌟 BLOQUEO SI YA ESTÁ ANULADO: Cambiamos el texto del botón
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
                            // Siempre mantener Cancel activo en modos de edición/impresión/anulación
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
                    this.Cursor = Cursors.Arrow; // Apagamos el reloj de espera
                }
            }
        }

        private void LimpiarBotonesDinamicosCompletamente()
        {
            // Limpieza de botón de Anulación
            if (_btnAnularDefinitivoSalida != null && _btnAnularDefinitivoSalida.Parent is Panel p)
            {
                p.Children.Remove(_btnAnularDefinitivoSalida);
                _btnAnularDefinitivoSalida = null;
            }

            // Limpieza de botón de Exportar
            if (_btnExportarNearSaveSalida != null && _btnExportarNearSaveSalida.Parent is Panel p2)
            {
                p2.Children.Remove(_btnExportarNearSaveSalida);
                _btnExportarNearSaveSalida = null;
            }
        }
        private void ShowExportButtonNearSave()
        {
            // Si el botón ya fue inyectado, evitamos duplicarlo en el panel
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
        // DETALLE: AGREGAR, MODIFICAR Y ELIMINAR 
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
                var existente = _productosLista.FirstOrDefault(p => p.ProductoId == idProducto);

                var ingService = new IngresoMovimientoService();
                var listaStrings = new List<string>();

                // 🌟 CORRECCIÓN DE CONCURRENCIA: Procesamos los datos pesados en segundo plano de manera limpia
                var progressModal = new ProgressWindow("Procesando Lote Manual Despacho", "Validando correlativos y stock físico lateral...", async (progress) =>
                {
                    int totalRangos = rangosDelModal.Count;
                    for (int rIdx = 0; rIdx < totalRangos; rIdx++)
                    {
                        var rango = rangosDelModal[rIdx];
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
                    var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings);

                    int totalStrings = listaStrings.Count;
                    var codigosTemporalesLote = new List<VistaCodigoGrid>();

                    for (int sIdx = 0; sIdx < totalStrings; sIdx++)
                    {
                        string codStr = listaStrings[sIdx];
                        string norm = ingService.NormalizarCodigo(codStr);

                        if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                        {
                            // 🌟 SOLUCIÓN AL DEADLOCK: Eliminamos el .Result que congelaba el hilo principal de WPF.
                            // Ejecutamos la consulta en su propio contexto asíncrono.
                            string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                            codigosTemporalesLote.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                CodigoUnique = tup.CodigoObj.Codigo,
                                ProductoId = idProducto,
                                ColeccionTipo = tipoColeccionReal
                            });
                        }

                        int pctFinal = 40 + ((sIdx * 60) / (totalStrings == 0 ? 1 : totalStrings));
                        progress.Report(pctFinal);
                    }

                    // 🌟 VOLCADO SEGURO: Una vez procesado todo el lote en RAM, lo inyectamos al hilo principal de golpe
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var nuevoCod in codigosTemporalesLote)
                        {
                            _codigosLista.Add(nuevoCod);
                        }
                    });
                });

                progressModal.Owner = Window.GetWindow(this);

                if (progressModal.ShowDialog() == true)
                {
                    this.Cursor = Cursors.Wait;

                    // 🌟 CONTROL ASÍNCRONO: Consultamos los datos del producto sin bloquear la UI
                    var prodService = new ProductoService();
                    var prodData = Task.Run(() => prodService.ObtenerPorIdAsync(idProducto)).Result;

                    if (existente != null && modal.MergeWithExisting)
                    {
                        existente.Cantidad += (int)modal.CantidadProductoIngresada;
                        if (existente.Detalle != null) existente.Detalle.CantidadSalida += modal.CantidadProductoIngresada;
                    }
                    else
                    {
                        _productosLista.Add(new VistaProductoGrid
                        {
                            ProductoId = idProducto,
                            CodigoProducto = prodData?.Abreviatura ?? idProducto.ToString(),
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                            Detalle = new MovimientoDetalle
                            {
                                ProductoId = idProducto,
                                CantidadSalida = modal.CantidadProductoIngresada,
                                CostoUnitario = prodData?.PrecioUnitario ?? 0
                            }
                        });
                    }

                    RefrescarGrillas();
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

            // 🌟 REUTILIZACIÓN DE LA VENTANA UNIFICADA PARA EDICIÓN EN CALIENTE
            var todosLosCodigosDelProducto = _codigosLista
         .Where(c => c.ProductoId == seleccionado.ProductoId)
         .ToList();

            // 2. RECONSTRUIR RANGOS (Asegúrate de que esta función recorra 'todosLosCodigosDelProducto')
            var rangos = ReconstruirRangosDeCodigos(seleccionado.ProductoId);

            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };

            // 3. Inicializar el modal pasando la lista completa
            modal.InitializeForEdit(seleccionado, rangos);

            if (seleccionado.Detalle.CostoUnitario == 0)
            {
                var prodService = new ProductoService();
                var prodActualizado = await prodService.ObtenerPorIdAsync(seleccionado.ProductoId);
                seleccionado.Detalle.CostoUnitario = prodActualizado?.PrecioUnitario ?? 0;
            }

            seleccionado.Detalle.CantidadIngreso = seleccionado.Detalle.CantidadSalida; // Sincronización de espejo para el modal
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
                        // 🌟 CORREGIDO: Eliminamos la referencia a OriginalObj. Ahora evalúa directamente tu propiedad DesdeNum
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
                dgCodigosSalida.ItemsSource = _codigosLista.ToList(); // ToList() repinta el N°
                btnModificarDetalle.IsEnabled = false;
                btnEliminarItem.IsEnabled = false;
            }
        }

        // ==========================================
        // PROCESAR GUARDADO DE LA SALIDA (INDUSTRIAL ASYNC)
        // ==========================================
        private async void btnGrabarSalida_Click(object sender, RoutedEventArgs e)
        {
            if (_productosLista.Count == 0)
            {
                MessageBox.Show("No hay productos en la lista para procesar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(txtNumeroSalida.Text))
            {
                MessageBox.Show("El número de salida no puede estar vacío.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btnGrabarSalida.IsEnabled = false;
                btnCancelar.IsEnabled = false;
                this.Cursor = Cursors.Wait;

                var movimiento = new Movimiento
                {
                    SerieDocumento = txtSerieSalida.Text,
                    NumeroDocumento = txtNumeroSalida.Text,
                    FechaMovimiento = dtpFechaDespacho.SelectedDate.HasValue ? DateOnly.FromDateTime(dtpFechaDespacho.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Now),
                    UbicacionId = txtUbicacion.IsEnabled ? _idUbicacionSeleccionada : null,
                    PersonaComercialId = txtCliente.IsEnabled ? _idClienteSeleccionado : null,
                    MotivoProductoId = (int)cboMotivoSalida.SelectedValue,
                    Observacion = txtObservacionSalida.Text,
                    SerieGuia = txtSerieGuia.Text,
                    NumeroGuia = txtNumeroGuia.Text,
                    CreatedAt = DateTime.Now
                };

                // Encedemos la grilla de progreso nativa que pusimos abajo en tus grids
                pbCargaMasiva.Visibility = Visibility.Visible;
                lblPorcentajeCarga.Visibility = Visibility.Visible;
                pbCargaMasiva.Value = 0;
                lblPorcentajeCarga.Text = "0% Procesando despacho...";

                // 🌟 CONTROL DE PROGRESO DE FONDO SEGURIZADO (THREAD-SAFE)
                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (pbCargaMasiva != null) pbCargaMasiva.Value = percent;
                        if (lblPorcentajeCarga != null) lblPorcentajeCarga.Text = $"{percent}% Despachando Kárdex...";
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });

                // Materializar copia segura antes de ejecutar en background
                var snapshot = _productosLista.ToList();

                var detallesPlanos = snapshot.Select(p => new VistaProductoGrid
                {
                    ProductoId = p.ProductoId,
                    CodigoProducto = p.CodigoProducto,
                    Descripcion = p.Descripcion,
                    UnidadMedida = p.UnidadMedida,
                    // Asegurar que Cantidad refleje lo que realmente fue asignado al producto
                    Cantidad = p.Detalle != null ? Convert.ToDecimal(p.Detalle.CantidadSalida) : p.Cantidad,
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = p.ProductoId,
                        CantidadSalida = p.Detalle?.CantidadSalida ?? p.Cantidad,
                        CostoUnitario = p.Detalle?.CostoUnitario ?? 0,
                        // opcional: copiar Id si existe
                        Id = p.Detalle?.Id ?? 0
                    }
                }).ToList();

                bool resultado = await Task.Run(async () =>
                    await _salidaService.RegistrarSalidaCompletaAsync(
                     movimiento,
                     detallesPlanos, // Ahora enviamos la lista limpia
                     _codigosLista.ToList(),
                     idUsuarioLogueado,
                     estadoSalida,
                     _idMovimientoActual,
                     progress)
                );

                if (resultado)
                {
                    // 🌟 CAPTURA DEL CORRELATIVO REAL PERSISTIDO EN BASE DE DATOS
                    string numFinal = movimiento.NumeroDocumento; 
                    string serieFinal = movimiento.SerieDocumento; 

                    // Inyectamos el valor real con formato destacado en caliente en la UI
                    txtNumeroSalida.Text = numFinal;
                    txtNumeroSalida.Foreground = System.Windows.Media.Brushes.Black;
                    txtNumeroSalida.FontWeight = FontWeights.Bold;
                    txtNumeroSalida.FontStyle = FontStyles.Normal;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.White;

                    // Mostramos el mensaje de éxito impecable con la numeración oficial generada por SQL
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

        // ==========================================
        // IMPORTAR Y ESCANEAR
        // ==========================================
        // ==========================================
        // IMPORTAR EXCEL (MÁXIMO RENDIMIENTO CON PROGRESS WINDOW)
        // ==========================================
        private async void BtnImportarExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ImportarCodigos { Owner = Window.GetWindow(this) };
                win.EstadoPermitido = 3; // Solo códigos físicos "En Almacén"

                if (win.ShowDialog() != true) return;

                var listaRaw = win.CodigosImportados ?? new List<string>();
                if (!listaRaw.Any()) return;

                var ingService = new IngresoMovimientoService();
                var prodService = new ProductoService();

                // Lista intermedia para capturar los resultados procesados en el hilo de fondo
                var codigosProcesadosLote = new List<VistaCodigoGrid>();

                this.Cursor = Cursors.Wait;

                // 🌟 LA BARRA DE CARGA SE MOVERÁ AQUÍ MEDIANTE EL 'progress.Report()'
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
                            // Regla estricta de Salidas
                            if (tup.CodigoObj.EstadoId != 3) continue;

                            // Validación thread-safe contra duplicados en memoria
                            bool yaExisteEnGrilla = false;
                            Application.Current.Dispatcher.Invoke(() => {
                                yaExisteEnGrilla = _codigosLista.Any(c => c.MovCodigo.CodigoCreadoId == tup.CodigoObj.Id);
                            });
                            if (yaExisteEnGrilla) continue;

                            string tipoBD = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);

                            var nuevoCodigoGrid = new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                                CodigoUnique = tup.CodigoObj.Codigo,
                                ProductoId = tup.ProductoId.Value,
                                ColeccionTipo = tipoBD
                            };

                            lock (codigosProcesadosLote)
                            {
                                codigosProcesadosLote.Add(nuevoCodigoGrid);
                            }
                        }

                        // 📈 ACTUALIZACIÓN DE LA BARRA EN TIEMPO REAL
                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = pct;
                            progress.Report(pct); // Envía el progreso al ProgressWindow
                        }
                    }
                });

                progressModal.Owner = Window.GetWindow(this);

                if (progressModal.ShowDialog() == true)
                {
                    // Volcamos el lote de forma segura a la UI
                    foreach (var nuevoCod in codigosProcesadosLote)
                    {
                        _codigosLista.Add(nuevoCod);

                        var existente = _productosLista.FirstOrDefault(p => p.ProductoId == nuevoCod.ProductoId);
                        if (existente != null)
                        {
                            existente.Cantidad++;
                            existente.Detalle.CantidadSalida++;
                        }
                        else
                        {
                            var prodData = await prodService.ObtenerPorIdAsync(nuevoCod.ProductoId);
                            _productosLista.Add(new VistaProductoGrid
                            {
                                ProductoId = nuevoCod.ProductoId,
                                CodigoProducto = prodData?.Abreviatura ?? nuevoCod.ProductoId.ToString(),
                                Descripcion = prodData?.Descripcion ?? "Desconocido",
                                UnidadMedida = prodData?.UnidadMedida?.Descripcion ?? "UNIDAD",
                                Detalle = new MovimientoDetalle
                                {
                                    ProductoId = nuevoCod.ProductoId,
                                    CantidadSalida = 1,
                                    CostoUnitario = prodData?.PrecioUnitario ?? 0
                                }
                            });
                        }
                    }

                    RefrescarGrillas();
                    MessageBox.Show("Lote de Excel importado y cargado en el despacho correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
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

        // ==========================================
        // ESCANEAR POR PISTOLA LECTORA (THREAD-SAFE)
        // ==========================================
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

        // 🌟 CAMBIADO A Task<bool> PARA RESOLVER EL ERROR CS0029
        private async Task<bool> ProcesarCodigoEscaneadoAsync(LectoraResultDTO resultado)
        {
            if (resultado.EstadoId != 3)
            {
                MessageBox.Show(
                    $"El código '{resultado.CodigoCompleto}' no está en almacén.",
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

        // ==========================================
        // COMBO MOTIVO SALIDA Y CAMPOS DINÁMICOS
        // ==========================================
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

        private void CboMotivoSalida_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMotivoSalida.SelectedValue == null) return;
            int idMotivo = Convert.ToInt32(cboMotivoSalida.SelectedValue);

            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;

            // 1. Motivos de solo Cliente (5: Consignación, 6: Dev. Entregada, 7: Donación, 8: Feria, 11: Venta)
            if (idMotivo == 5 || idMotivo == 6 || idMotivo == 7 || idMotivo == 8 || idMotivo == 11)
            {
                txtCliente.IsEnabled = true;
            }
            // 2. Transferencia Entre Almacenes (10)
            else if (idMotivo == 10)
            {
                txtUbicacion.IsEnabled = true;
            }
            // 🌟 3. PROMOTORÍA / PROMOCIÓN (9) u OTROS (12): Requieren Cliente Y Ubicación
            else if (idMotivo == 9 || idMotivo == 12)
            {
                txtCliente.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
            }

            if (!txtCliente.IsEnabled) { txtCliente.Clear(); txtCodigoCliente.Clear(); txtDireccionCliente.Clear(); _idClienteSeleccionado = null; }
            if (!txtUbicacion.IsEnabled) { txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear(); _idUbicacionSeleccionada = null; }
        }

        private void ActualizarVisibilidadCampos()
        {
            var motivo = cboMotivoSalida.SelectedItem as dynamic;
            string descripcion = motivo?.Descripcion?.ToString().ToLower() ?? "";

            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;

            if (descripcion.Contains("venta") || descripcion.Contains("devolucion") || descripcion.Contains("feria") || descripcion.Contains("donacion"))
            {
                txtCliente.IsEnabled = true;
            }
            if (descripcion.Contains("transferencia"))
            {
                txtUbicacion.IsEnabled = true;
            }
            if (descripcion.Contains("otros") || descripcion.Contains("promocion"))
            {
                txtCliente.IsEnabled = true;
                txtUbicacion.IsEnabled = true;
            }

            if (!txtCliente.IsEnabled)
            {
                txtCliente.Clear(); txtCodigoCliente.Clear(); txtDireccionCliente.Clear(); _idClienteSeleccionado = null;
            }
            if (!txtUbicacion.IsEnabled)
            {
                txtUbicacion.Clear(); txtCodigoUbicacion.Clear(); txtDireccionUbicacion.Clear(); _idUbicacionSeleccionada = null;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Desea cancelar la operación actual?", "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _anularMode = false;

                // 🌟 LIMPIEZA COMPLETA DE BOTONES DINÁMICOS
                LimpiarBotonAnularDinamico();

                if (_btnExportarNearSaveSalida != null && _btnExportarNearSaveSalida.Parent is Panel panelExcel)
                {
                    panelExcel.Children.Remove(_btnExportarNearSaveSalida);
                    _btnExportarNearSaveSalida = null;
                }

                EstadoInicialFormulario();
            }
        }
        public async Task<List<PersonaComercial>> BuscarClientesAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, razon_social, direccion 
                         FROM personas_comerciales 
                         WHERE razon_social LIKE @filtro AND estado_id = 1
                         ORDER BY razon_social ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new PersonaComercial
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                RazonSocial = reader.GetString(reader.GetOrdinal("razon_social")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }
        public async Task<List<Ubicacion>> BuscarUbicacionesAsync(string filtro)
        {
            var lista = new List<Ubicacion>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, direccion 
                                 FROM ubicaciones 
                                 WHERE descripcion LIKE @filtro AND estado_id = 1
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Ubicacion
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }
        // Buscadores
        private async void TxtCliente_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtCliente.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaClientes = await BuscarClientesAsync(filtro);
                lstClientes.ItemsSource = _listaClientes;
                popupClientes.IsOpen = _listaClientes.Count > 0;
            }
            else { popupClientes.IsOpen = false; }
        }

        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtUbicacion.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaUbicaciones = await BuscarUbicacionesAsync(filtro);
                lstUbicaciones.ItemsSource = _listaUbicaciones;
                popupUbicaciones.IsOpen = _listaUbicaciones.Count > 0;
            }
            else { popupUbicaciones.IsOpen = false; }
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
                txtDireccionUbicacion.Text = string.IsNullOrWhiteSpace(ub.Direccion) ? "Sin dirección registrada" : ub.Direccion; // 🌟 CARGA DIRECCIÓN
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

                // 🌟 CORRECCIÓN: Apuntamos estrictamente al detalle logístico, jamás a la propiedad del maestro de productos
                if (producto.Detalle != null)
                {
                    producto.Detalle.CantidadSalida = count;
                }
            }

            dgProductosSalida.Items.Refresh();
            dgCodigosSalida.Items.Refresh();
        }

        private async Task<decimal> ObtenerCostoUnitarioProductoAsync(int productoId)
        {
            try
            {
                using var conn = new DatabaseConnection().GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
            SELECT costo_unitario 
            FROM productos 
            WHERE id = @productoId");

                var p = cmd.CreateParameter();
                p.ParameterName = "@productoId";
                p.Value = productoId;
                cmd.Parameters.Add(p);

                using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0))
                    {
                        return rdr.GetDecimal(0);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error obteniendo costo: {ex.Message}");
            }
            return 0m; // Retorna 0 si no encuentra el costo
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (dgProductosSalida.SelectedItem is not VistaProductoGrid productoSeleccionado) return;

            string filtro = txtBuscarCodigo.Text.Trim().ToLower();

            // 🚀 BUSQUEDA MAESTRA EN RAM: Extraemos el 100% de los códigos de la lista observable
            var todosLosCodigosDelProducto = _codigosLista
                .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                .ToList();

            if (string.IsNullOrEmpty(filtro))
            {
                // Si el buscador está vacío, volvemos a limitar la vista por rendimiento gráfico
                dgCodigosSalida.ItemsSource = todosLosCodigosDelProducto.Take(500).ToList();
                lblResumenCodigos.Text = todosLosCodigosDelProducto.Count > 500
                    ? $"500 (Viendo) / {todosLosCodigosDelProducto.Count}"
                    : $"{todosLosCodigosDelProducto.Count} / {todosLosCodigosDelProducto.Count}";
            }
            else
            {
                // 🌟 BÚSQUEDA TOTAL: Encuentra cualquier coincidencia exacta o parcial al instante
                var filtrados = todosLosCodigosDelProducto
                    .Where(c => c.CodigoUnique.ToLower().Contains(filtro))
                    .ToList();

                dgCodigosSalida.ItemsSource = filtrados;
                lblResumenCodigos.Text = $"{filtrados.Count} (Encontrados) / {todosLosCodigosDelProducto.Count}";
            }
        }



    }
}