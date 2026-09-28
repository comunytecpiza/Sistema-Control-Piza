#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Documentos;
using AplicativoDeAlmacen.Services.facturaciòn;
using AplicativoDeAlmacen.Services.Politicas;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante
{
    public partial class RegistroComprobantesUserControl : UserControl
    {
        private readonly FacturacionService _facturacionService;
        private readonly SerieDocumentoService _serieService;
        private readonly UbicacionService _ubicacionService;
        private readonly PersonaComercialService _personaService;
        private readonly ReporteExcelService _reporteService;
        private bool _moduloIniciado = false;
        private readonly ObservableCollection<PagoManualDTO> _pagosManuales = new ObservableCollection<PagoManualDTO>();
        private readonly AplicativoDeAlmacen.Services.Facturación.MedioPagoService _medioPagoService = new();
        private ObservableCollection<ItemGridDTO> _itemsGrid = new ObservableCollection<ItemGridDTO>();
        private readonly Dictionary<int, string> _mapaEmpresas = new Dictionary<int, string>();
        private List<SerieDocumento> _todasLasSeries = new List<SerieDocumento>();
        private bool _isUpdatingFicha = false;
        private bool _isInitializing = false;
        private int _idComprobanteActual = 0;

        private enum ModoFormulario
        {
            Ninguno,
            Nuevo,
            BuscandoParaEditar,
            BuscandoParaImprimir,
            BuscandoParaAnular
        }

        private ModoFormulario _modoActual = ModoFormulario.Ninguno;
        private Button? _btnAnularDefinitivo = null;

        public RegistroComprobantesUserControl()
        {
            InitializeComponent();
            _facturacionService = new FacturacionService();
            _serieService = new SerieDocumentoService();
            _ubicacionService = new UbicacionService();
            _personaService = new PersonaComercialService();
            _reporteService = new ReporteExcelService();

            DgItems.ItemsSource = _itemsGrid;
            DgPagosManuales.ItemsSource = _pagosManuales;
            Loaded += async (s, e) => await InicializarModulo();
        }

        private void ActualizarNumeroCorrelativo()
        {
            if (_modoActual != ModoFormulario.Nuevo) return;
            if (CmbSerie.SelectedItem is not SerieDocumento s) return;

            string tipo = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "01";

            int correlativo = tipo == "01" ? s.CorrelativoFactura :
                              tipo == "02" ? s.CorrelativoBoleta :
                                             s.CorrelativoRecibo;

            TxtNumero.Text = (correlativo + 1).ToString("D7");
        }

        private async Task InicializarModulo()
        {
            if (_moduloIniciado || _modoActual == ModoFormulario.BuscandoParaImprimir || _idComprobanteActual > 0)
            {
                return;
            }

            try
            {
                _isInitializing = true;
                _moduloIniciado = true;

                // 🌟 1. Cargar Catálogo de Empresas en memoria
                await CargarCatalogoEmpresas();

                // 2. Cargar Series de la BD
                await CargarTodasLasSeries();
                FiltrarSeriesPorTipoDocumento();

                // 3. Cargar Medios de Pago
                var medios = await _medioPagoService.ObtenerMediosPagoAsync(soloActivos: true);
                CmbMedioPagoManual.ItemsSource = medios;
                if (medios.Any()) CmbMedioPagoManual.SelectedIndex = 0;

                if (_modoActual == ModoFormulario.Ninguno && _idComprobanteActual == 0)
                {
                    PanelFormulario.IsEnabled = false;
                    LimpiarFormulario();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar módulo de facturación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        // 🌟 Método auxiliar para tener en memoria ID -> Razón Social de Empresa
        private async Task CargarCatalogoEmpresas()
        {
            _mapaEmpresas.Clear();
            var db = new DataConnection.DatabaseConnection();
            using var conn = db.GetConnection();
            var dbConn = (System.Data.Common.DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = Data.QueryAdapter.FormatearConsulta("SELECT id, razon_social FROM empresas;");

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                int id = rdr.GetInt32(0);
                string rz = rdr.GetString(1).Trim();
                _mapaEmpresas[id] = rz;
            }
        }
    

        private async Task CargarTodasLasSeries()
        {
            _todasLasSeries.Clear();
            var ubicaciones = await _ubicacionService.ObtenerTodasAsync();
            foreach (var u in ubicaciones)
            {
                var seriesSede = await _serieService.ObtenerSeriesPorUbicacionAsync(u.Id);
                _todasLasSeries.AddRange(seriesSede);
            }
        }

        private void FiltrarSeriesPorTipoDocumento()
        {
            if (CmbSerie == null || _todasLasSeries == null) return;

            if (CmbTipoDocu.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag.ToString() ?? "01";
                var series = _todasLasSeries.Where(s =>
                    !string.IsNullOrEmpty(s.NumeroSerie) &&
                    s.NumeroSerie.StartsWith(tag == "01" ? "F" : tag == "02" ? "B" : "R")).ToList();

                CmbSerie.ItemsSource = series;
                CmbSerie.DisplayMemberPath = "NumeroSerie";
                if (series.Any()) CmbSerie.SelectedIndex = 0;
            }
        }

        private async void CmbSerie_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (TxtNumero == null || TxtPuntoVenta == null || TxtEmpresaTitular == null) return;

            if (CmbSerie.SelectedItem is SerieDocumento s)
            {
                // 1. Obtener Sede
                var todasSedes = await _ubicacionService.ObtenerTodasAsync();
                var sede = todasSedes.FirstOrDefault(u => u.Id == s.UbicacionId);

                TxtPuntoVenta.Text = sede?.Descripcion ?? "Ubicación Desconocida";
                TxtPuntoVenta.Tag = sede?.Id;

                // 🌟 2. Resolver Empresa Titular por Serie
                int empresaIdDetectada = s.EmpresaId ?? s.Empresa?.Id ?? 0;

                // Si el objeto en memoria no traía el EmpresaId, consultarlo directamente a la BD
                if (empresaIdDetectada == 0 && !string.IsNullOrWhiteSpace(s.NumeroSerie))
                {
                    empresaIdDetectada = await ObtenerEmpresaIdPorSerieAsync(s.NumeroSerie);
                }

                if (empresaIdDetectada > 0 && _mapaEmpresas.TryGetValue(empresaIdDetectada, out string? razonSocialEmpresa))
                {
                    TxtEmpresaTitular.Text = razonSocialEmpresa;
                    TxtEmpresaTitular.Tag = empresaIdDetectada;
                }
                else if (s.Empresa != null && !string.IsNullOrWhiteSpace(s.Empresa.RazonSocial))
                {
                    TxtEmpresaTitular.Text = s.Empresa.RazonSocial;
                    TxtEmpresaTitular.Tag = s.Empresa.Id;
                }
                else
                {
                    TxtEmpresaTitular.Text = "[ SIN EMPRESA ASIGNADA ]";
                    TxtEmpresaTitular.Tag = null;
                }

                ActualizarNumeroCorrelativo();
            }
            else
            {
                TxtPuntoVenta.Text = string.Empty;
                TxtEmpresaTitular.Text = "[ SELECCIONE SERIE ]";
                TxtEmpresaTitular.Tag = null;
                if (_modoActual == ModoFormulario.Nuevo) TxtNumero.Text = string.Empty;
            }
        }

        // 🌟 Consulta de respaldo rápida por si el modelo SerieDocumento no traía el ID mapeado
        private async Task<int> ObtenerEmpresaIdPorSerieAsync(string numSeri)
        {
            try
            {
                var db = new DataConnection.DatabaseConnection();
                using var conn = db.GetConnection();
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = Data.QueryAdapter.FormatearConsulta(@"
            SELECT COALESCE(empresa_id, 0) 
            FROM series_documentos 
            WHERE num_seri = @serie LIMIT 1;");

                var p = cmd.CreateParameter();
                p.ParameterName = "@serie";
                p.Value = numSeri.Trim();
                cmd.Parameters.Add(p);

                object? res = await cmd.ExecuteScalarAsync();
                return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void CmbTipoDocu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            FiltrarSeriesPorTipoDocumento();
        }

        // ==========================================
        // BOTONES DE CONTROL SUPERIOR
        // ==========================================
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LimpiarBotonAnularDinamico();
            _modoActual = ModoFormulario.Nuevo;
            _idComprobanteActual = 0;

            PanelFormulario.IsEnabled = true;
            LimpiarFormulario();
            HabilitarTodoElFormulario();
            BtnImprimirExcel.IsEnabled = false;

            DpFecha.SelectedDate = DateTime.Now;
            FiltrarSeriesPorTipoDocumento();

            if (CmbSerie.Items.Count > 0)
            {
                CmbSerie.SelectedIndex = 0;
                ActualizarNumeroCorrelativo();
            }

            TxtRazonSocialBuscador.Focus();
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LimpiarBotonAnularDinamico();
            LimpiarFormulario();
            _idComprobanteActual = 0;
            _modoActual = ModoFormulario.BuscandoParaEditar;

            BtnImprimirExcel.IsEnabled = false;
            PanelFormulario.IsEnabled = true;
            ConfigurarModoBusqueda();
            TxtNumero.Focus();

            MessageBox.Show("Seleccione la Serie, escriba el número de comprobante y presione ENTER para cargarlo y poder editarlo.",
                "Modo Búsqueda - Editar", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LimpiarBotonAnularDinamico();
            LimpiarFormulario();
            _idComprobanteActual = 0;
            _modoActual = ModoFormulario.BuscandoParaImprimir;

            BtnImprimirExcel.IsEnabled = false;
            PanelFormulario.IsEnabled = true;
            ConfigurarModoBusqueda();

            BtnGrabar.IsEnabled = false; // 👈 Asegurado en falso desde el inicio
            TxtNumero.Focus();

            MessageBox.Show("Seleccione la Serie, escriba el número de comprobante y presione ENTER para cargarlo en modo Vista Previa. Luego use 'Exportar Excel'.",
                "Modo Vista Previa", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LimpiarBotonAnularDinamico();
            LimpiarFormulario();
            _idComprobanteActual = 0;
            _modoActual = ModoFormulario.BuscandoParaAnular;

            BtnImprimirExcel.IsEnabled = false;
            PanelFormulario.IsEnabled = true;
            ConfigurarModoBusqueda();

            BtnGrabar.IsEnabled = false; // 👈 Asegurado en falso desde el inicio
            TxtNumero.Focus();

            MessageBox.Show("Modo Anulación activado.\n\nSeleccione la Serie, escriba el número de comprobante y presione ENTER para revisar su contenido antes de confirmar.",
                "Preparando Anulación", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            _modoActual = ModoFormulario.Ninguno;
            _idComprobanteActual = 0;

            LimpiarBotonAnularDinamico();
            BtnImprimirExcel.IsEnabled = false;
            PanelFormulario.IsEnabled = false;
            LimpiarFormulario();
        }

        // ==========================================
        // BOTÓN ROJO DINÁMICO DE CONFIRMAR ANULACIÓN
        // ==========================================
        private void MostrarBotonAnularDinamico()
        {
            if (_btnAnularDefinitivo != null) return;

            if (BtnGrabar?.Parent is Panel parentPanel)
            {
                _btnAnularDefinitivo = new Button
                {
                    Content = "💥 CONFIRMAR ANULACIÓN",
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
                    Foreground = Brushes.White,
                    Cursor = Cursors.Hand,
                    Margin = BtnGrabar.Margin,
                    Padding = BtnGrabar.Padding,
                    MinWidth = 180,
                    Height = BtnGrabar.Height > 0 ? BtnGrabar.Height : 32,
                    FontSize = BtnGrabar.FontSize,
                    FontWeight = FontWeights.Bold
                };

                _btnAnularDefinitivo.Click += EjecutarAnulacionDefinitiva_Click;

                BtnGrabar.Visibility = Visibility.Collapsed;
                parentPanel.Children.Insert(parentPanel.Children.IndexOf(BtnGrabar) + 1, _btnAnularDefinitivo);
            }
        }

        private void LimpiarBotonAnularDinamico()
        {
            if (_btnAnularDefinitivo != null && _btnAnularDefinitivo.Parent is Panel p)
            {
                p.Children.Remove(_btnAnularDefinitivo);
                _btnAnularDefinitivo = null;
            }

            if (BtnGrabar != null)
            {
                BtnGrabar.Visibility = Visibility.Visible;
                BtnGrabar.IsEnabled = true;
            }
        }

        private async void EjecutarAnulacionDefinitiva_Click(object sender, RoutedEventArgs e)
        {
            if (_idComprobanteActual == 0) return;

            string docIdent = $"{CmbSerie.Text}-{TxtNumero.Text}";
            var result = MessageBox.Show(
                $"⚠️ ¿Está absolutamente seguro de ANULAR el comprobante {docIdent}?\n\nEl registro quedará marcado formalmente como anulado.",
                "Confirmar Reversión",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    this.Cursor = Cursors.Wait;
                    if (_btnAnularDefinitivo != null) _btnAnularDefinitivo.IsEnabled = false;

                    int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
                    await _facturacionService.AnularComprobanteAsync(_idComprobanteActual, usuarioActivoId, "ANULACIÓN DE COMPROBANTE");

                    MessageBox.Show("El comprobante ha sido anulado exitosamente.", "Operación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);

                    _modoActual = ModoFormulario.Ninguno;
                    _idComprobanteActual = 0;
                    LimpiarBotonAnularDinamico();
                    LimpiarFormulario();
                    PanelFormulario.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error al Anular", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (_btnAnularDefinitivo != null) _btnAnularDefinitivo.IsEnabled = true;
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        // ==========================================
        // GRABAR / ACTUALIZAR
        // ==========================================
        private async void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsGrid.Count == 0)
            {
                MessageBox.Show("No hay ítems para facturar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CmbSerie.SelectedItem == null)
            {
                MessageBox.Show("Seleccione una serie de documento.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            decimal.TryParse(TxtTotal.Text, out decimal totalVenta);
            decimal.TryParse(TxtMontoDelivery.Text.Trim(), out decimal delivery);

            // Si no agregó pagos a la grilla inferior, asignar Efectivo por defecto
            if (!_pagosManuales.Any())
            {
                _pagosManuales.Add(new PagoManualDTO
                {
                    MedioPagoId = 1,
                    MedioPagoNombre = "EFECTIVO",
                    Monto = totalVenta
                });
            }

            int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

            var cabecera = new FacturacionCabecera
            {
                EmpresaId = TxtEmpresaTitular.Tag != null ? Convert.ToInt32(TxtEmpresaTitular.Tag) : (int?)null,
                TipoDocumento = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "01",
                SerieDocumento = (CmbSerie.SelectedItem as SerieDocumento)?.NumeroSerie ?? "",
                NumeroDocumento = TxtNumero.Text.Trim(),
                FechaEmision = DpFecha.SelectedDate ?? DateTime.Now,
                PuntoVentaId = TxtPuntoVenta.Tag != null ? Convert.ToInt32(TxtPuntoVenta.Tag) : 0,
                AlmacenId = miAlmacenId,
                CompradorId = int.TryParse(TxtRazonSocialId.Text, out int idClie) ? idClie : (int?)null,
                InstitucionId = int.TryParse(TxtClienteId.Text, out int idInst) ? idInst : (int?)null,
                Observacion = TxtObservacion.Text.Trim(),
                TotalGravado = decimal.Parse(TxtOpGravadas.Text),
                TotalExonerado = decimal.Parse(TxtOpExoneradas.Text),
                TotalIgv = decimal.Parse(TxtIgv.Text),
                ImporteTotal = totalVenta,
                MontoDelivery = delivery,
                PorcentajeIgv = 18.00m,
                EstadoRegistro = true,
                UsuarioId = usuarioActivoId,
                CondicionPagoId = _pagosManuales.First().MedioPagoId
            };

            // 🌟 Pasar los pagos a la cabecera para que se guarden dentro de la misma transacción
            foreach (var p in _pagosManuales)
            {
                cabecera.Pagos.Add(new FacturacionPagoDetalle
                {
                    MedioPagoId = p.MedioPagoId,
                    Monto = p.Monto,
                    NumeroOperacion = p.NumeroOperacion
                });
            }

            foreach (var item in _itemsGrid)
            {
                var detalle = new FacturacionDetalle
                {
                    ProductoId = item.ProductoId,
                    Cantidad = item.CanProd,
                    PrecioUnitario = item.PreUnit,
                    ImporteTotal = item.ImpTota,
                    MovimientoId = item.MovimientoId,
                    Codigos = item.Codigos.Select(c => new FacturacionDetalleCodigos
                    {
                        CodigoCreadoId = c.CodigoCreadoId
                    }).ToList()
                };
                cabecera.Detalles.Add(detalle);
            }

            try
            {
                this.Cursor = Cursors.Wait;

                if (_idComprobanteActual > 0)
                {
                    cabecera.Id = _idComprobanteActual;
                    await _facturacionService.ActualizarComprobanteAsync(cabecera, usuarioActivoId);
                    MessageBox.Show("Comprobante actualizado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    int serieId = ((SerieDocumento)CmbSerie.SelectedItem).Id;
                    int nuevoId = await _facturacionService.GuardarComprobanteAsync(cabecera, serieId);

                    await CargarTodasLasSeries();
                    FiltrarSeriesPorTipoDocumento();
                    ActualizarNumeroCorrelativo();

                    MessageBox.Show($"¡Comprobante N° {cabecera.SerieDocumento}-{cabecera.NumeroDocumento} guardado exitosamente con sus pagos!", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LimpiarFormulario();
                _idComprobanteActual = 0;
                PanelFormulario.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar comprobante: {ex.Message}", "Error de Facturación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        // ==========================================
        // CARGA POR NÚMERO Y CANDADO DE AUDITORÍA
        // ==========================================
        private async void TxtNumero_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await CargarComprobantePorNumero();
            }
        }

        private async Task CargarComprobantePorNumero()
        {
            if (_modoActual != ModoFormulario.BuscandoParaEditar &&
                _modoActual != ModoFormulario.BuscandoParaImprimir &&
                _modoActual != ModoFormulario.BuscandoParaAnular) return;

            if (CmbSerie.SelectedItem is not SerieDocumento serieSeleccionada)
            {
                MessageBox.Show("Seleccione una serie primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string inputNumero = TxtNumero.Text.Trim();
            if (!int.TryParse(inputNumero, out int numeroInt))
            {
                MessageBox.Show("El número de comprobante debe ser numérico.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string numeroFormateado = numeroInt.ToString("D7");
            TxtNumero.Text = numeroFormateado;

            try
            {
                this.Cursor = Cursors.Wait;
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                var comprobante = await _facturacionService.ObtenerComprobantePorNumeroAsync(serieSeleccionada.NumeroSerie, numeroFormateado, miAlmacenId);

                if (comprobante == null)
                {
                    MessageBox.Show($"No se encontró el comprobante {serieSeleccionada.NumeroSerie}-{numeroFormateado}.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_modoActual == ModoFormulario.BuscandoParaEditar)
                {
                    int rolUsuarioActivo = SesionSistema.UsuarioActual?.RolUsuarioId ?? SesionSistema.UsuarioActual?.Rol?.Id ?? 0;
                    if (!AuditoriaPoliticas.ValidarPlazoEdicion(comprobante.FechaRegistro, rolUsuarioActivo, out string mensajeBloqueo))
                    {
                        MessageBox.Show(mensajeBloqueo, "Acceso Restringido por Auditoría", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                _idComprobanteActual = comprobante.Id;
                DpFecha.SelectedDate = comprobante.FechaEmision;
                TxtObservacion.Text = comprobante.Observacion;

                if (comprobante.CompradorId.HasValue)
                {
                    var cliente = await _personaService.ObtenerPorIdAsync(comprobante.CompradorId.Value);
                    LlenarFichaCliente(cliente, esRazonSocial: true);
                }

                if (comprobante.InstitucionId.HasValue)
                {
                    var colegio = await _personaService.ObtenerPorIdAsync(comprobante.InstitucionId.Value);
                    LlenarFichaCliente(colegio, esRazonSocial: false);
                }

                _itemsGrid.Clear();
                var productoService = new ProductoService();

                foreach (var det in comprobante.Detalles)
                {
                    var producto = await productoService.ObtenerPorIdAsync(det.ProductoId);

                    _itemsGrid.Add(new ItemGridDTO
                    {
                        ProductoId = det.ProductoId,
                        MovimientoId = det.MovimientoId,
                        DescripcionProducto = producto?.Descripcion ?? "PRODUCTO NO ENCONTRADO",
                        UnidadMedida = producto?.UnidadMedida?.Descripcion ?? "UND",
                        CanProd = det.Cantidad,
                        PreUnit = det.PrecioUnitario,
                        ImpTota = det.ImporteTotal,
                        Codigos = det.Codigos.Select(c => new CodigoLeidoDTO
                        {
                            CodigoCreadoId = c.CodigoCreadoId,
                            CodigoString = c.CodigoTexto,
                            Cantidad = 1,
                            Coleccion = "Kardex Recuperado"
                        }).ToList()
                    });
                }

                RecalcularTotales();
                TxtOpGravadas.Text = comprobante.TotalGravado.ToString("N2");
                TxtOpExoneradas.Text = comprobante.TotalExonerado.ToString("N2");
                TxtIgv.Text = comprobante.TotalIgv.ToString("N2");
                TxtTotal.Text = comprobante.ImporteTotal.ToString("N2");

                TxtNumero.IsReadOnly = true;
                TxtNumero.Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC");

                if (_modoActual == ModoFormulario.BuscandoParaEditar)
                {
                    if (!comprobante.EstadoRegistro)
                    {
                        MessageBox.Show("Este comprobante se encuentra ANULADO (Modo solo lectura).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                        ConfigurarModoBusqueda();
                        BtnGrabar.IsEnabled = false;
                        return;
                    }

                    // En edición: se habilitan campos, ítems y el botón guardar
                    HabilitarTodoElFormulario();
                    BtnGrabar.IsEnabled = true;
                    BtnImprimirExcel.IsEnabled = false;
                }
                else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
                {
                    if (!comprobante.EstadoRegistro)
                    {
                        MessageBox.Show("¡ATENCIÓN! Este comprobante se encuentra ANULADO.", "Comprobante Anulado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // En impresión: bloqueo estricto de edición e inserción
                    ConfigurarModoBusqueda();
                    BtnGrabar.IsEnabled = false;            // 👈 Bloqueado: No permite sobreguardar al imprimir
                    BtnImprimirExcel.IsEnabled = true;     // 👈 Habilita únicamente la exportación
                }
                else if (_modoActual == ModoFormulario.BuscandoParaAnular)
                {
                    ConfigurarModoBusqueda();

                    if (!comprobante.EstadoRegistro)
                    {
                        MessageBox.Show("Este comprobante ya se encuentra previamente ANULADO.", "Comprobante Anulado", MessageBoxButton.OK, MessageBoxImage.Information);
                        LimpiarBotonAnularDinamico();
                        BtnGrabar.IsEnabled = false;
                        return;
                    }

                    // En anulación: el botón verde desaparece y se muestra el rojo
                    BtnGrabar.IsEnabled = false;
                    MostrarBotonAnularDinamico();
                }

            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        // ==========================================
        // EXPORTAR EXCEL
        // ==========================================
        private void BtnImprimirExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsGrid.Count == 0)
            {
                MessageBox.Show("No hay datos cargados para imprimir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string tipoDoc = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "FACTURA";
                string serieNumero = $"{CmbSerie.Text}-{TxtNumero.Text}";
                string fecha = DpFecha.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
                string operador = SesionSistema.UsuarioActual?.Nombres ?? "SISTEMA";

                decimal.TryParse(TxtOpGravadas.Text, out decimal gravadas);
                decimal.TryParse(TxtOpExoneradas.Text, out decimal exoneradas);
                decimal.TryParse(TxtIgv.Text, out decimal igv);
                decimal.TryParse(TxtTotal.Text, out decimal total);

                _reporteService.ExportarComprobanteImpresion(
                    tipoDoc, serieNumero, fecha,
                    TxtRazonSocialBuscador.Text, TxtDniRuc.Text,
                    TxtClienteBuscador.Text, TxtLocalidad.Text,
                    TxtObservacion.Text, operador,
                    _itemsGrid.ToList(),
                    gravadas, exoneradas, igv, total
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al generar el Excel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // HELPERS Y AUTOCOMPLETADOS
        // ==========================================
        private void ConfigurarModoBusqueda()
        {
            PanelFormulario.IsEnabled = true;
            CmbTipoDocu.IsEnabled = true;
            CmbSerie.IsEnabled = true;

            TxtNumero.IsEnabled = true;
            TxtNumero.IsReadOnly = false;
            TxtNumero.Background = Brushes.White;

            // Campos de cabecera bloqueados
            TxtRazonSocialBuscador.IsEnabled = false;
            TxtDniRuc.IsEnabled = false;
            CmbTipoIdentidad.IsEnabled = false;
            TxtDireccionPagador.IsEnabled = false;
            TxtClienteBuscador.IsEnabled = false;
            TxtDireccionColegio.IsEnabled = false;
            TxtLocalidad.IsEnabled = false;
            TxtObservacion.IsEnabled = false;
            DpFecha.IsEnabled = false;

            // Botones de acciones de ítems bloqueados
            BtnAgregarItem.IsEnabled = false;
            BtnModificarItem.IsEnabled = false;
            BtnEliminarItem.IsEnabled = false;
            BtnLector.IsEnabled = false;

            // Grilla en modo solo lectura para inspección con doble clic
            DgItems.IsEnabled = true;
            DgItems.IsReadOnly = true;

            // 🛑 CANDADO DE MODO: Si NO es "Nuevo" ni "BuscandoParaEditar", apagar botón de Guardar
            if (BtnGrabar != null)
            {
                BtnGrabar.IsEnabled = (_modoActual == ModoFormulario.Nuevo || _modoActual == ModoFormulario.BuscandoParaEditar);
            }
        }

        private void HabilitarTodoElFormulario()
        {
            CmbTipoDocu.IsEnabled = true;
            CmbSerie.IsEnabled = true;
            TxtRazonSocialBuscador.IsEnabled = true;
            TxtDniRuc.IsEnabled = true;
            CmbTipoIdentidad.IsEnabled = true;
            TxtDireccionPagador.IsEnabled = true;
            TxtClienteBuscador.IsEnabled = true;
            TxtDireccionColegio.IsEnabled = true;
            TxtLocalidad.IsEnabled = true;
            TxtObservacion.IsEnabled = true;
            DpFecha.IsEnabled = true;

            BtnAgregarItem.IsEnabled = true;
            BtnModificarItem.IsEnabled = true;
            BtnEliminarItem.IsEnabled = true;
            BtnLector.IsEnabled = true;
            DgItems.IsEnabled = true;
            DgItems.IsReadOnly = false;

            TxtNumero.IsEnabled = true;
            TxtNumero.IsReadOnly = true;
            TxtNumero.Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC");
        }

        private void LimpiarFormulario()
        {
            _isUpdatingFicha = true;
            TxtRazonSocialBuscador.Text = string.Empty;
            TxtRazonSocialId.Text = string.Empty;
            TxtDniRuc.Text = string.Empty;
            TxtDireccionPagador.Text = string.Empty;
            if (CmbTipoIdentidad.Items.Count > 0) CmbTipoIdentidad.SelectedIndex = 0;

            TxtClienteBuscador.Text = string.Empty;
            TxtClienteId.Text = string.Empty;
            TxtDireccionColegio.Text = string.Empty;
            TxtLocalidad.Text = string.Empty;

            TxtObservacion.Text = string.Empty;
            DpFecha.SelectedDate = null;
            TxtNumero.Text = string.Empty;

            _pagosManuales.Clear();
            if (ChkTieneDelivery != null) ChkTieneDelivery.IsChecked = false;
            if (TxtMontoDelivery != null)
            {
                TxtMontoDelivery.IsEnabled = false;
                TxtMontoDelivery.Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9");
                TxtMontoDelivery.Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B");
                TxtMontoDelivery.Text = "0.00";
            }
            if (ChkTieneDelivery != null) ChkTieneDelivery.IsChecked = false;
            RecalcularBalanceCobro();

            _itemsGrid.Clear();
            ActualizarTotales();
            _isUpdatingFicha = false;
        }

        private void ActualizarTotales()
        {
            TxtOpGravadas.Text = "0.00";
            TxtOpExoneradas.Text = "0.00";
            TxtIgv.Text = "0.00";
            TxtTotalDelivery.Text = "0.00";
            TxtTotal.Text = "0.00";
        }

        private void RecalcularTotales()
        {
            // 🛡️ Candado de nulidad: Si la interfaz aún se está dibujando, no ejecutar
            if (TxtOpExoneradas == null || TxtOpGravadas == null || TxtIgv == null ||
                TxtTotal == null || TxtTotalDelivery == null || TxtMontoDelivery == null || _itemsGrid == null)
            {
                return;
            }

            decimal subTotal = _itemsGrid.Sum(x => x.ImpTota);
            decimal.TryParse(TxtMontoDelivery.Text.Trim(), out decimal delivery);

            TxtOpExoneradas.Text = subTotal.ToString("N2");
            TxtOpGravadas.Text = "0.00";
            TxtIgv.Text = "0.00";
            TxtTotalDelivery.Text = delivery.ToString("N2");

            decimal totalFinal = subTotal + delivery;
            TxtTotal.Text = totalFinal.ToString("N2");

            RecalcularBalanceCobro();
        }
        private void LlenarFichaCliente(PersonaComercial? cliente, bool esRazonSocial)
        {
            if (cliente == null) return;
            _isUpdatingFicha = true;

            if (esRazonSocial)
            {
                TxtRazonSocialBuscador.Text = cliente.RazonSocial ?? $"{cliente.Nombres} {cliente.ApellidoPaterno}";
                TxtRazonSocialId.Text = cliente.Id.ToString("D6");
                TxtDireccionPagador.Text = cliente.Direccion ?? "";
                TxtDniRuc.Text = cliente.Ruc ?? cliente.Dni ?? "";
            }
            else
            {
                TxtClienteBuscador.Text = cliente.RazonSocial ?? $"{cliente.Nombres} {cliente.ApellidoPaterno}";
                TxtClienteId.Text = cliente.Id.ToString("D6");
                TxtDireccionColegio.Text = cliente.Direccion ?? "";
                string localidad = cliente.Localidad?.Nombre ?? "";
                string zona = cliente.ZonaPromotoria?.Descripcion ?? "";
                TxtLocalidad.Text = $"{localidad} / {zona}".Trim();
            }

            _isUpdatingFicha = false;
        }

        private async void TxtRazonSocialBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;

            string texto = TxtRazonSocialBuscador.Text.Trim();
            if (texto.Length < 2)
            {
                PopRazonSocial.IsOpen = false;
                return;
            }

            try
            {
                var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
                var lista = resultados?.Take(12).ToList();

                if (lista != null && lista.Any())
                {
                    LstRazonSocial.ItemsSource = lista;
                    PopRazonSocial.IsOpen = false; // Reset para refrescar posición si hubo scroll
                    PopRazonSocial.IsOpen = true;
                }
                else
                {
                    PopRazonSocial.IsOpen = false;
                }
            }
            catch
            {
                PopRazonSocial.IsOpen = false;
            }
        }

        private async void TxtClienteBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;

            string texto = TxtClienteBuscador.Text.Trim();
            if (texto.Length < 2)
            {
                PopCliente.IsOpen = false;
                return;
            }

            try
            {
                var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
                var lista = resultados?.Take(12).ToList();

                if (lista != null && lista.Any())
                {
                    LstCliente.ItemsSource = lista;
                    PopCliente.IsOpen = false;
                    PopCliente.IsOpen = true;
                }
                else
                {
                    PopCliente.IsOpen = false;
                }
            }
            catch
            {
                PopCliente.IsOpen = false;
            }
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial p)
            {
                PopRazonSocial.IsOpen = false;
                LlenarFichaCliente(p, esRazonSocial: true);
            }
        }

        private void LstCliente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstCliente.SelectedItem is PersonaComercial p)
            {
                PopCliente.IsOpen = false;
                LlenarFichaCliente(p, esRazonSocial: false);
            }
        }

        private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            if (modal.ShowDialog() == true && modal.NuevoItem != null)
            {
                _itemsGrid.Add(modal.NuevoItem);
                RecalcularTotales();
            }
        }

        private void BtnModificarItem_Click(object sender, RoutedEventArgs e)
        {
            if (DgItems.SelectedItem is not ItemGridDTO itemSeleccionado)
            {
                MessageBox.Show("Debe seleccionar un ítem de la lista para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var modal = new AgregarItemWindow(itemSeleccionado) { Owner = Window.GetWindow(this) };
            if (modal.ShowDialog() == true && modal.NuevoItem != null)
            {
                int index = _itemsGrid.IndexOf(itemSeleccionado);
                if (index >= 0)
                {
                    modal.NuevoItem.NumLine = itemSeleccionado.NumLine;
                    _itemsGrid[index] = modal.NuevoItem;
                    RecalcularTotales();
                }
            }
        }

        private void BtnEliminarItem_Click(object sender, RoutedEventArgs e)
        {
            if (DgItems.SelectedItem is not ItemGridDTO itemSeleccionado)
            {
                MessageBox.Show("Debe seleccionar un ítem de la lista para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"¿Está seguro de eliminar el ítem \"{itemSeleccionado.DescripcionProducto}\"?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _itemsGrid.Remove(itemSeleccionado);
                RecalcularTotales();
            }
        }

        private void BtnActivarLector_Click(object sender, RoutedEventArgs e)
        {
            var lectorModal = new LectorWindow(_itemsGrid) { Owner = Window.GetWindow(this) };
            lectorModal.ShowDialog();
            RecalcularTotales();
        }

        private void BtnSaltarNumero_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funcionalidad para saltar número de comprobante.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnModificarNumero_Click(object sender, RoutedEventArgs e)
        {
            TxtNumero.IsReadOnly = !TxtNumero.IsReadOnly;
            TxtNumero.Background = TxtNumero.IsReadOnly ? (Brush)new BrushConverter().ConvertFromString("#F8FAFC") : Brushes.White;
            if (!TxtNumero.IsReadOnly) TxtNumero.Focus();
        }

        private void DgItems_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void DgItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgItems.SelectedItem is not ItemGridDTO itemSeleccionado) return;

            bool esSoloLectura = _modoActual == ModoFormulario.BuscandoParaImprimir ||
                                 _modoActual == ModoFormulario.BuscandoParaAnular ||
                                 _modoActual == ModoFormulario.Ninguno;

            var modal = new AgregarItemWindow(itemSeleccionado, esSoloLectura)
            {
                Owner = Window.GetWindow(this)
            };

            if (!esSoloLectura && modal.ShowDialog() == true && modal.NuevoItem != null)
            {
                int index = _itemsGrid.IndexOf(itemSeleccionado);
                if (index >= 0)
                {
                    modal.NuevoItem.NumLine = itemSeleccionado.NumLine;
                    _itemsGrid[index] = modal.NuevoItem;
                    RecalcularTotales();
                }
            }
            else if (esSoloLectura)
            {
                modal.ShowDialog();
            }
        }

        public async Task CargarComprobanteParaConsultaAsync(string serie, string numero)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                _moduloIniciado = true; // 👈 Evita que el evento Loaded posterior ejecute LimpiarFormulario()
                _modoActual = ModoFormulario.BuscandoParaImprimir;

                // 1. Asegurar catálogo de series en memoria sin alertas
                if (_todasLasSeries == null || !_todasLasSeries.Any())
                {
                    await CargarTodasLasSeries();
                }

                // 2. Normalizar número a 7 dígitos (ej: 1 -> 0000001)
                string numeroNormalizado = int.TryParse(numero, out int nVal) ? nVal.ToString("D7") : numero.Trim();
                string seriePura = serie.Trim().ToUpper();

                // 3. Obtener el comprobante directo usando el servicio de facturación
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                var comprobante = await _facturacionService.ObtenerComprobantePorNumeroAsync(seriePura, numeroNormalizado, miAlmacenId);

                if (comprobante == null)
                {
                    MessageBox.Show($"No se encontró el comprobante {seriePura}-{numeroNormalizado} en esta sede.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 4. Limpiar e inicializar formulario
                LimpiarFormulario();
                _idComprobanteActual = comprobante.Id;

                // Tipo de Comprobante
                foreach (ComboBoxItem item in CmbTipoDocu.Items)
                {
                    if (item.Tag?.ToString() == comprobante.TipoDocumento)
                    {
                        CmbTipoDocu.SelectedItem = item;
                        break;
                    }
                }

                FiltrarSeriesPorTipoDocumento();

                // Serie
                var serieObj = _todasLasSeries.FirstOrDefault(s => string.Equals(s.NumeroSerie?.Trim(), seriePura, StringComparison.OrdinalIgnoreCase));
                if (serieObj != null)
                {
                    CmbSerie.SelectedItem = serieObj;
                }
                else
                {
                    var serieAux = new SerieDocumento { NumeroSerie = seriePura };
                    var listSeries = CmbSerie.ItemsSource as List<SerieDocumento> ?? new List<SerieDocumento>();
                    listSeries.Add(serieAux);
                    CmbSerie.ItemsSource = null;
                    CmbSerie.ItemsSource = listSeries;
                    CmbSerie.SelectedItem = serieAux;
                }

                TxtNumero.Text = comprobante.NumeroDocumento;
                DpFecha.SelectedDate = comprobante.FechaEmision;
                TxtObservacion.Text = comprobante.Observacion;

                // Cargar Personas Comerciales
                if (comprobante.CompradorId.HasValue)
                {
                    var cliente = await _personaService.ObtenerPorIdAsync(comprobante.CompradorId.Value);
                    LlenarFichaCliente(cliente, esRazonSocial: true);
                }

                if (comprobante.InstitucionId.HasValue)
                {
                    var colegio = await _personaService.ObtenerPorIdAsync(comprobante.InstitucionId.Value);
                    LlenarFichaCliente(colegio, esRazonSocial: false);
                }

                
                // Cargar Ítems y Códigos asegurando la coherencia aritmética
                _itemsGrid.Clear();
                var prodService = new ProductoService();

                foreach (var det in comprobante.Detalles)
                {
                    var producto = await prodService.ObtenerPorIdAsync(det.ProductoId);

                    // 1. Determinar la cantidad real: si tiene códigos asignados, manda el conteo de códigos
                    int cantidadReal = (det.Codigos != null && det.Codigos.Any())
                        ? det.Codigos.Count
                        : (int)det.Cantidad;

                    decimal precioUnit = det.PrecioUnitario;

                    // 2. Corregir el importe total de la línea (Cantidad * Precio)
                    decimal importeCalculado = Math.Round(cantidadReal * precioUnit, 2);

                    _itemsGrid.Add(new ItemGridDTO
                    {
                        NumLine = det.NumeroLinea,
                        ProductoId = det.ProductoId,
                        MovimientoId = det.MovimientoId,
                        DescripcionProducto = producto?.Descripcion ?? "PRODUCTO",
                        UnidadMedida = producto?.UnidadMedida?.Descripcion ?? "UND",
                        CanProd = cantidadReal,
                        PreUnit = precioUnit,
                        ImpTota = importeCalculado, // 👈 3 * 180.00 = 540.00 asegurado
                        Codigos = det.Codigos.Select(c => new CodigoLeidoDTO
                        {
                            CodigoCreadoId = c.CodigoCreadoId,
                            CodigoString = c.CodigoTexto,
                            Cantidad = 1,
                            Coleccion = "Kardex"
                        }).ToList()
                    });
                }

                // 3. Recalcular la cabecera con los valores consistentes de la grilla
                RecalcularTotales();

                // Cargar Totales
                TxtOpGravadas.Text = comprobante.TotalGravado.ToString("N2");
                TxtOpExoneradas.Text = comprobante.TotalExonerado.ToString("N2");
                TxtIgv.Text = comprobante.TotalIgv.ToString("N2");
                TxtTotal.Text = comprobante.ImporteTotal.ToString("N2");

                // 5. Aplicar bloqueo visual para lectura
                BloquearParaImpresionContable();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar comprobante: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }

        }

        private void TxtMontoDelivery_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalcularTotales();
        }

        private void ChkTieneDelivery_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtMontoDelivery == null) return;

            if (ChkTieneDelivery.IsChecked == true)
            {
                TxtMontoDelivery.IsEnabled = true;
                TxtMontoDelivery.Background = Brushes.White;
                TxtMontoDelivery.Foreground = (Brush)new BrushConverter().ConvertFromString("#1E293B");
                if (TxtMontoDelivery.Text.Trim() == "0.00" || string.IsNullOrWhiteSpace(TxtMontoDelivery.Text))
                {
                    TxtMontoDelivery.Text = "0.00";
                    TxtMontoDelivery.SelectAll();
                }
                TxtMontoDelivery.Focus();
            }
            else
            {
                TxtMontoDelivery.IsEnabled = false;
                TxtMontoDelivery.Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9");
                TxtMontoDelivery.Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B");
                TxtMontoDelivery.Text = "0.00";
            }

            RecalcularTotales();
        }

        private void TxtSoloNumerosDecimales_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Solo permite dígitos y un único punto decimal
            var tb = sender as TextBox;
            string textoCompleto = (tb?.Text ?? "") + e.Text;
            bool esValido = System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]+$");

            if (!esValido || (e.Text == "." && (tb?.Text.Contains(".") ?? false)))
            {
                e.Handled = true; // Bloquea caracteres no numéricos
            }
        }

        private void BtnAgregarPago_Click(object sender, RoutedEventArgs e)
        {
            if (CmbMedioPagoManual.SelectedItem is not AplicativoDeAlmacen.Models.Facturación.MedioPago mp)
            {
                MessageBox.Show("Seleccione un medio de pago.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(TxtMontoPagoManual.Text.Trim(), out decimal monto) || monto <= 0)
            {
                MessageBox.Show("Ingrese un monto válido mayor a 0.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMontoPagoManual.Focus();
                return;
            }

            var existente = _pagosManuales.FirstOrDefault(p => p.MedioPagoId == mp.Id);
            if (existente != null)
            {
                existente.Monto += monto;
            }
            else
            {
                _pagosManuales.Add(new PagoManualDTO
                {
                    MedioPagoId = mp.Id,
                    MedioPagoNombre = mp.Nombre,
                    Monto = monto,
                    NumeroOperacion = TxtOperacionPagoManual.Text.Trim()
                });
            }

            DgPagosManuales.Items.Refresh();
            TxtMontoPagoManual.Text = "0.00";
            TxtOperacionPagoManual.Clear();

            RecalcularBalanceCobro();
        }

        private void BtnQuitarPago_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PagoManualDTO pago)
            {
                _pagosManuales.Remove(pago);
                RecalcularBalanceCobro();
            }
        }

        private void RecalcularBalanceCobro()
        {
            // 🛡️ Candado de nulidad para las etiquetas del balance multi-pago
            if (LblTotalCobrado == null || LblDiferenciaCobro == null || TxtTotal == null || _pagosManuales == null)
            {
                return;
            }

            decimal.TryParse(TxtTotal.Text, out decimal totalVenta);
            decimal totalCobrado = _pagosManuales.Sum(p => p.Monto);
            decimal diferencia = totalVenta - totalCobrado;

            LblTotalCobrado.Text = $"S/ {totalCobrado:N2}";

            if (diferencia == 0)
            {
                LblDiferenciaCobro.Text = "S/ 0.00 (CUADRADO)";
                LblDiferenciaCobro.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
            }
            else if (diferencia > 0)
            {
                LblDiferenciaCobro.Text = $"Falta: S/ {diferencia:N2}";
                LblDiferenciaCobro.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            }
            else
            {
                LblDiferenciaCobro.Text = $"Vuelto / Exceso: S/ {Math.Abs(diferencia):N2}";
                LblDiferenciaCobro.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
            }
        }
        private void BloquearParaImpresionContable()
        {
            // 🛡️ 1. Deshabilitar los 4 botones de la barra superior (Nuevo, Modificar, Imprimir, Anular)
            if (PanelFormulario?.Parent is FrameworkElement root)
            {
                // Si PanelFormulario está dentro del Grid general, buscamos los botones superiores
                if (VisualTreeHelper.GetParent(PanelFormulario) is FrameworkElement parentGrid)
                {
                    DeshabilitarBotonesSuperiores(this);
                }
            }

            // 🛡️ 2. Configurar el formulario y campos en solo lectura
            PanelFormulario.IsEnabled = true;

            CmbTipoDocu.IsEnabled = false;
            CmbSerie.IsEnabled = false;
            TxtNumero.IsReadOnly = true;
            TxtNumero.Background = System.Windows.Media.Brushes.WhiteSmoke;

            TxtRazonSocialBuscador.IsEnabled = false;
            TxtDniRuc.IsEnabled = false;
            CmbTipoIdentidad.IsEnabled = false;
            TxtDireccionPagador.IsEnabled = false;
            TxtClienteBuscador.IsEnabled = false;
            TxtDireccionColegio.IsEnabled = false;
            TxtLocalidad.IsEnabled = false;
            TxtObservacion.IsEnabled = false;
            DpFecha.IsEnabled = false;

            // 🛡️ 3. Grilla de ítems en modo inspección (seleccionable pero no editable)
            DgItems.IsEnabled = true;
            DgItems.IsReadOnly = true;

            // 🛡️ 4. Bloquear botones de edición de detalle
            if (BtnGrabar != null) BtnGrabar.IsEnabled = false;
            if (BtnAgregarItem != null) BtnAgregarItem.IsEnabled = false;
            if (BtnModificarItem != null) BtnModificarItem.IsEnabled = false;
            if (BtnEliminarItem != null) BtnEliminarItem.IsEnabled = false;
            if (BtnLector != null) BtnLector.IsEnabled = false;

            // 🌟 5. Habilitar únicamente la exportación a Excel
            if (BtnImprimirExcel != null) BtnImprimirExcel.IsEnabled = true;
        }

        // Helper para deshabilitar automáticamente los botones sin x:Name (Nuevo, Modificar, Imprimir, Anular, Saltar N°, Editar N°)
        private void DeshabilitarBotonesSuperiores(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn)
                {
                    string texto = btn.Content?.ToString() ?? string.Empty;
                    if (texto.Contains("Nuevo") ||
                        texto.Contains("Modificar") ||
                        texto.Contains("Imprimir") ||
                        texto.Contains("Anular") ||
                        texto.Contains("Saltar N°") ||
                        texto.Contains("Editar N°"))
                    {
                        btn.IsEnabled = false;
                    }
                }
                else
                {
                    DeshabilitarBotonesSuperiores(child);
                }
            }
        }
    }
}