#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Core;
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

        private ObservableCollection<ItemGridDTO> _itemsGrid = new ObservableCollection<ItemGridDTO>();
        private List<SerieDocumento> _todasLasSeries = new List<SerieDocumento>();
        private bool _isUpdatingFicha = false;
        private int _idComprobanteActual = 0;

        private enum ModoFormulario
        {
            Ninguno,
            Nuevo,
            BuscandoParaEditar,
            BuscandoParaImprimir
        }

        private ModoFormulario _modoActual = ModoFormulario.Ninguno;

        public RegistroComprobantesUserControl()
        {
            InitializeComponent();
            _facturacionService = new FacturacionService();
            _serieService = new SerieDocumentoService();
            _ubicacionService = new UbicacionService();
            _personaService = new PersonaComercialService();
            _reporteService = new ReporteExcelService();

            DgItems.ItemsSource = _itemsGrid;
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
            try
            {
                await CargarTodasLasSeries();
                FiltrarSeriesPorTipoDocumento();

                _modoActual = ModoFormulario.Ninguno;
                PanelFormulario.IsEnabled = false;
                LimpiarFormulario();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar módulo de facturación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (TxtNumero == null || TxtPuntoVenta == null) return;

            if (CmbSerie.SelectedItem is SerieDocumento s)
            {
                var todasSedes = await _ubicacionService.ObtenerTodasAsync();
                var sede = todasSedes.FirstOrDefault(u => u.Id == s.UbicacionId);

                TxtPuntoVenta.Text = sede?.Descripcion ?? "Ubicación Desconocida";
                TxtPuntoVenta.Tag = sede?.Id;

                ActualizarNumeroCorrelativo();
            }
            else
            {
                TxtPuntoVenta.Text = string.Empty;
                if (_modoActual == ModoFormulario.Nuevo) TxtNumero.Text = string.Empty;
            }
        }

        private void CmbTipoDocu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FiltrarSeriesPorTipoDocumento();
        }

        // ==========================================
        // BOTONES DE CONTROL SUPERIOR
        // ==========================================
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
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
            LimpiarFormulario();
            _idComprobanteActual = 0;
            _modoActual = ModoFormulario.BuscandoParaImprimir;
            BtnImprimirExcel.IsEnabled = false;

            ConfigurarModoBusqueda();
            TxtNumero.Focus();

            MessageBox.Show("Seleccione la Serie, escriba el número de comprobante y presione ENTER para cargarlo en modo Vista Previa. Luego use 'Imprimir Excel'.",
                "Modo Vista Previa", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            _modoActual = ModoFormulario.Ninguno;
            _idComprobanteActual = 0;

            BtnImprimirExcel.IsEnabled = false;
            PanelFormulario.IsEnabled = false;
            LimpiarFormulario();
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null) parentWindow.Close();
        }

        // ==========================================
        // GRABAR / ACTUALIZAR (CON AUDITORÍA REAL)
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

            int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

            var cabecera = new FacturacionCabecera
            {
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
                ImporteTotal = decimal.Parse(TxtTotal.Text),
                PorcentajeIgv = 18.00m,
                EstadoRegistro = true,
                UsuarioId = usuarioActivoId
            };

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
                    await _facturacionService.GuardarComprobanteAsync(cabecera, serieId);

                    await CargarTodasLasSeries();
                    FiltrarSeriesPorTipoDocumento();
                    ActualizarNumeroCorrelativo();

                    MessageBox.Show("Comprobante guardado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LimpiarFormulario();
                _idComprobanteActual = 0;
                PanelFormulario.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al procesar comprobante: {ex.Message}", "Error de Facturación", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (_modoActual != ModoFormulario.BuscandoParaEditar && _modoActual != ModoFormulario.BuscandoParaImprimir) return;

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

                // 🛑 VALIDACIÓN DE PLAZO DE AUDITORÍA AL EDITAR
                if (_modoActual == ModoFormulario.BuscandoParaEditar)
                {
                    int rolUsuarioActivo = SesionSistema.UsuarioActual?.RolUsuarioId ?? SesionSistema.UsuarioActual?.Rol?.Id ?? 0;
                    if (!AuditoriaPoliticas.ValidarPlazoEdicion(comprobante.FechaRegistro, rolUsuarioActivo, out string mensajeBloqueo))
                    {
                        MessageBox.Show(mensajeBloqueo, "Acceso Restringido por Auditoría", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (!comprobante.EstadoRegistro)
                {
                    MessageBox.Show("¡ATENCIÓN! Este comprobante se encuentra ANULADO.", "Comprobante Anulado", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                if (_modoActual == ModoFormulario.BuscandoParaEditar)
                {
                    HabilitarTodoElFormulario();
                }
                else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
                {
                    PanelFormulario.IsEnabled = true;
                    ConfigurarModoBusqueda();
                    BtnImprimirExcel.IsEnabled = true;
                }
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        // ==========================================
        // EXPORTAR EXCEL CON NOMBRE DE OPERADOR REAL
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

        private async void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (_idComprobanteActual == 0)
            {
                MessageBox.Show("Primero debe cargar un comprobante usando el botón 'Modificar'.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "¿Está seguro de ANULAR este comprobante? El registro quedará marcado como anulado.",
                "Confirmar Anulación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
                    await _facturacionService.AnularComprobanteAsync(_idComprobanteActual, usuarioActivoId, "ANULACIÓN POR USUARIO");
                    MessageBox.Show("El comprobante ha sido anulado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                    _modoActual = ModoFormulario.Ninguno;
                    _idComprobanteActual = 0;
                    BtnImprimirExcel.IsEnabled = false;
                    LimpiarFormulario();
                    PanelFormulario.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error al Anular", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            TxtNumero.Text = string.Empty;
            TxtNumero.Background = System.Windows.Media.Brushes.White;

            TxtRazonSocialBuscador.IsEnabled = false;
            TxtDniRuc.IsEnabled = false;
            CmbTipoIdentidad.IsEnabled = false;
            TxtDireccionPagador.IsEnabled = false;
            TxtClienteBuscador.IsEnabled = false;
            TxtDireccionColegio.IsEnabled = false;
            TxtLocalidad.IsEnabled = false;
            TxtObservacion.IsEnabled = false;
            DpFecha.IsEnabled = false;

            BtnAgregarItem.IsEnabled = false;
            BtnModificarItem.IsEnabled = false;
            BtnEliminarItem.IsEnabled = false;
            BtnLector.IsEnabled = false;
            DgItems.IsEnabled = false;
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

            TxtNumero.IsEnabled = true;
            TxtNumero.IsReadOnly = true;
            TxtNumero.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F8FAFC");
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

            _itemsGrid.Clear();
            ActualizarTotales();
            _isUpdatingFicha = false;
        }

        private void ActualizarTotales()
        {
            TxtOpGravadas.Text = "0.00";
            TxtOpExoneradas.Text = "0.00";
            TxtIgv.Text = "0.00";
            TxtTotal.Text = "0.00";
        }

        private void RecalcularTotales()
        {
            decimal subTotal = _itemsGrid.Sum(x => x.ImpTota);
            TxtOpExoneradas.Text = subTotal.ToString("N2");
            TxtOpGravadas.Text = "0.00";
            TxtIgv.Text = "0.00";
            TxtTotal.Text = subTotal.ToString("N2");
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
            if (texto.Length < 2) { PopRazonSocial.IsOpen = false; return; }

            var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
            LstRazonSocial.ItemsSource = resultados.Take(10).ToList();
            PopRazonSocial.IsOpen = resultados != null && resultados.Any();
        }

        private async void TxtClienteBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;
            string texto = TxtClienteBuscador.Text.Trim();
            if (texto.Length < 2) { PopCliente.IsOpen = false; return; }

            var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
            LstCliente.ItemsSource = resultados.Take(10).ToList();
            PopCliente.IsOpen = resultados != null && resultados.Any();
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
            TxtNumero.Background = TxtNumero.IsReadOnly ? System.Windows.Media.Brushes.WhiteSmoke : System.Windows.Media.Brushes.White;
            if (!TxtNumero.IsReadOnly) TxtNumero.Focus();
        }

        private void DgItems_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    }
}