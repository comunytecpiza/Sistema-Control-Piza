using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Documentos;
using AplicativoDeAlmacen.Services.facturaciòn;
using AplicativoDeAlmacen.Services.Ubicaciones;
using Notification.Wpf;



namespace AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante
{
    public partial class RegistroComprobantesUserControl : UserControl
    {



        private readonly FacturacionService _facturacionService;
        private readonly SerieDocumentoService _serieService;
        private readonly UbicacionService _ubicacionService;
        private readonly PersonaComercialService _personaService;

        private ObservableCollection<ItemGridDTO> _itemsGrid = new ObservableCollection<ItemGridDTO>();
        private List<SerieDocumento> _todasLasSeries = new List<SerieDocumento>();
        private bool _isUpdatingFicha = false;
        private int _idComprobanteActual = 0;

        // 🌟 Máquina de estados del formulario.
        // Ninguno              -> formulario bloqueado, sin nada cargado.
        // Nuevo                -> formulario totalmente habilitado para crear un comprobante.
        // BuscandoParaEditar   -> solo Tipo Doc / Serie / N° habilitados; ENTER carga TODO editable.
        // BuscandoParaImprimir -> solo Tipo Doc / Serie / N° habilitados; ENTER carga TODO en solo lectura
        //                         y habilita el botón "Imprimir Excel" de la fila inferior.
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

            DgItems.ItemsSource = _itemsGrid;
            Loaded += async (s, e) => await InicializarModulo();
        }
        private void ActualizarNumeroCorrelativo()
        {
            if (_modoActual != ModoFormulario.Nuevo)
                return;

            if (CmbSerie.SelectedItem is not SerieDocumento s)
                return;

            string tipo = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            int correlativo =
                tipo == "01" ? s.CorrelativoFactura :
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

                // Iniciamos con el formulario bloqueado y limpio
                _modoActual = ModoFormulario.Ninguno;
                PanelFormulario.IsEnabled = false;
                LimpiarFormulario();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarTodasLasSeries()
        {
            _todasLasSeries.Clear();
            var ubicaciones = _ubicacionService.ObtenerTodas();
            foreach (var u in ubicaciones)
            {
                var seriesSede = await _serieService.ObtenerSeriesPorUbicacionAsync(u.Id);
                _todasLasSeries.AddRange(seriesSede);
            }
        }

        // ==========================================
        // LÓGICA DE BUSCADORES PREDICTIVOS
        // ==========================================

        private async void TxtRazonSocialBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;
            string texto = TxtRazonSocialBuscador.Text.Trim();
            if (texto.Length < 2) { PopRazonSocial.IsOpen = false; return; }

            var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
            LstRazonSocial.ItemsSource = resultados.Take(10).ToList();
            PopRazonSocial.IsOpen = resultados.Any();
        }

        private async void TxtClienteBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFicha) return;
            string texto = TxtClienteBuscador.Text.Trim();
            if (texto.Length < 2) { PopCliente.IsOpen = false; return; }

            var resultados = await _personaService.BuscarPorRazonSocialAsync(texto);
            LstCliente.ItemsSource = resultados.Take(10).ToList();
            PopCliente.IsOpen = resultados.Any();
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial p)
            {
                PopRazonSocial.IsOpen = false;
                LlenarFichaCliente(p, esRazonSocial: true); // Llena bloque Naranja
            }
        }

        private void LstCliente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstCliente.SelectedItem is PersonaComercial p)
            {
                PopCliente.IsOpen = false;
                LlenarFichaCliente(p, esRazonSocial: false); // Llena bloque Rojo
            }
        }

        private void LlenarFichaCliente(PersonaComercial cliente, bool esRazonSocial)
        {
            if (cliente == null) return;
            _isUpdatingFicha = true;

            if (esRazonSocial)
            {
                // RECUADRO NARANJA (Pagador Legal)
                TxtRazonSocialBuscador.Text = cliente.RazonSocial ?? $"{cliente.Nombres} {cliente.ApellidoPaterno}";
                TxtRazonSocialId.Text = cliente.Id.ToString("D6");
                TxtDireccionPagador.Text = cliente.Direccion;
                TxtDniRuc.Text = cliente.Ruc ?? cliente.Dni;
            }
            else
            {
                // RECUADRO ROJO (Colegio / Destino)
                TxtClienteBuscador.Text = cliente.RazonSocial ?? $"{cliente.Nombres} {cliente.ApellidoPaterno}";
                TxtClienteId.Text = cliente.Id.ToString("D6");
                TxtDireccionColegio.Text = cliente.Direccion;

                string localidad = cliente.Localidad?.Nombre ?? "";
                string zona = cliente.ZonaPromotoria?.Descripcion ?? "";
                TxtLocalidad.Text = $"{localidad} / {zona}".Trim();
            }

            _isUpdatingFicha = false;
        }

        // ==========================================
        // CASCADA SERIE / DOCUMENTO
        // ==========================================
        private void CmbTipoDocu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FiltrarSeriesPorTipoDocumento();
        }

        private void FiltrarSeriesPorTipoDocumento()
        {
            if (CmbSerie == null || _todasLasSeries == null) return;

            if (CmbTipoDocu.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag.ToString();
                var series = _todasLasSeries.Where(s =>
                    !string.IsNullOrEmpty(s.NumeroSerie) &&
                    s.NumeroSerie.StartsWith(tag == "01" ? "F" : tag == "02" ? "B" : "R")).ToList();

                CmbSerie.ItemsSource = series;
                CmbSerie.DisplayMemberPath = "NumeroSerie";
                if (series.Any()) CmbSerie.SelectedIndex = 0;
            }
        }

        private void CmbSerie_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtNumero == null || TxtPuntoVenta == null)
                return;

            if (CmbSerie.SelectedItem is SerieDocumento s)
            {
                var sede = _ubicacionService.ObtenerTodas()
                    .FirstOrDefault(u => u.Id == s.UbicacionId);

                TxtPuntoVenta.Text = sede?.Descripcion ?? "Ubicación Desconocida";
                TxtPuntoVenta.Tag = sede?.Id;

                ActualizarNumeroCorrelativo();
            }
            else
            {
                TxtPuntoVenta.Text = string.Empty;

                if (_modoActual == ModoFormulario.Nuevo)
                    TxtNumero.Text = string.Empty;
            }
        }

        // ==========================================
        // BOTONES DE ACCIÓN PRINCIPALES (BARRA SUPERIOR)
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

            // 1. Forzamos el filtrado para que el combo tenga series
            FiltrarSeriesPorTipoDocumento();

            // 2. Si el combo tiene series, seleccionamos la primera y forzamos el cálculo
            if (CmbSerie.Items.Count > 0)
            {
                CmbSerie.SelectedIndex = 0;
                ActualizarNumeroCorrelativo(); // Esto ya usa el s.Correlativo+1
            }

            TxtRazonSocialBuscador.Focus();
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            // Si ya estábamos en modo búsqueda para editar, solo devolvemos el foco al N°.
            if (_modoActual == ModoFormulario.BuscandoParaEditar)
            {
                HabilitarTodoElFormulario();
                BtnGrabar.IsEnabled = true;
                BtnImprimirExcel.IsEnabled = false;
            }

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
            // Si ya estábamos en modo búsqueda para imprimir, solo devolvemos el foco al N°.
            if (_modoActual == ModoFormulario.BuscandoParaImprimir)
            {
                TxtNumero.Focus();
                return;
            }

            LimpiarFormulario();
            _idComprobanteActual = 0;
            _modoActual = ModoFormulario.BuscandoParaImprimir;
            BtnImprimirExcel.IsEnabled = false;

            ConfigurarModoBusqueda();
            TxtNumero.Focus();

            MessageBox.Show("Seleccione la Serie, escriba el número de comprobante y presione ENTER para cargarlo en modo Vista Previa (solo lectura). Luego use el botón 'Imprimir Excel'.",
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
            // Cierra la ventana que contiene a este UserControl
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Close();
            }
        }

        // ==========================================
        // HELPERS DE HABILITADO / BLOQUEO DE CAMPOS
        // ==========================================

        /// <summary>
        /// Deja habilitados únicamente Tipo Documento, Serie y N° Documento.
        /// Todo lo demás (datos del cliente, items, botones de grilla) queda bloqueado
        /// hasta que se cargue un comprobante por número.
        /// </summary>
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

        /// <summary>
        /// Habilita todos los campos del formulario (modo edición completa).
        /// </summary>
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

            // Limpiamos Bloque Naranja
            TxtRazonSocialBuscador.Text = string.Empty;
            TxtRazonSocialId.Text = string.Empty;
            TxtDniRuc.Text = string.Empty;
            TxtDireccionPagador.Text = string.Empty;
            if (CmbTipoIdentidad.Items.Count > 0) CmbTipoIdentidad.SelectedIndex = 0;

            // Limpiamos Bloque Rojo
            TxtClienteBuscador.Text = string.Empty;
            TxtClienteId.Text = string.Empty;
            TxtDireccionColegio.Text = string.Empty;
            TxtLocalidad.Text = string.Empty;

            // Otros campos
            TxtObservacion.Text = string.Empty;
            DpFecha.SelectedDate = null;
            TxtNumero.Text = string.Empty;

            // Limpiamos grilla y totales
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

        // ==========================================
        // ACCIONES DE LA GRILLA (ITEMS)
        // ==========================================

        private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            AgregarItemWindow modal = new AgregarItemWindow();
            modal.Owner = Window.GetWindow(this);

            if (modal.ShowDialog() == true) // Si el usuario presionó Grabar
            {
                if (modal.NuevoItem != null)
                {
                    _itemsGrid.Add(modal.NuevoItem);
                    RecalcularTotales();
                }
            }
        }

        private void RecalcularTotales()
        {
            decimal subTotal = _itemsGrid.Sum(x => x.ImpTota);

            // Asignamos todo a exonerado por defecto para que se guarde correctamente en BD.
            // Si algún día venden algo con IGV, el contador tendrá que editar el TextBox a mano antes de darle a Grabar.
            TxtOpExoneradas.Text = subTotal.ToString("N2");
            TxtOpGravadas.Text = "0.00";
            TxtIgv.Text = "0.00";
            TxtTotal.Text = subTotal.ToString("N2");
        }

        private void BtnActivarLector_Click(object sender, RoutedEventArgs e)
        {
            // Le pasamos tu _itemsGrid por referencia.
            // Lo que el modal cambie, se reflejará aquí al instante.
            LectorWindow lectorModal = new LectorWindow(_itemsGrid);
            lectorModal.Owner = Window.GetWindow(this);
            lectorModal.ShowDialog();

            // Cuando el usuario termine y cierre la ventana, recalculamos los totales abajo
            RecalcularTotales();
        }

        private async void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validaciones previas
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

            // 🌟 2. Construir la cabecera (Mapeo actualizado con Nombres Completos)
            var cabecera = new FacturacionCabecera
            {
                TipoDocumento = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "01",
                SerieDocumento = (CmbSerie.SelectedItem as SerieDocumento)?.NumeroSerie,
                NumeroDocumento = TxtNumero.Text,
                FechaEmision = DpFecha.SelectedDate ?? DateTime.Now,
                PuntoVentaId = TxtPuntoVenta.Tag != null ? (int)TxtPuntoVenta.Tag : 0,
                CompradorId = int.TryParse(TxtRazonSocialId.Text, out int idClie) ? idClie : (int?)null,
                InstitucionId = int.TryParse(TxtClienteId.Text, out int idInst) ? idInst : (int?)null,
                Observacion = TxtObservacion.Text,
                TotalGravado = decimal.Parse(TxtOpGravadas.Text),
                TotalExonerado = decimal.Parse(TxtOpExoneradas.Text),
                TotalIgv = decimal.Parse(TxtIgv.Text),
                ImporteTotal = decimal.Parse(TxtTotal.Text),
                PorcentajeIgv = 18.00m, // Ajusta según tu configuración
                EstadoRegistro = true,
                UsuarioId = 1 // 🌟 CÁMBIALO POR EL ID DE TU USUARIO LOGUEADO
            };

            // 🌟 3. Mapear detalles y códigos desde _itemsGrid (Nombres Completos)
            foreach (var item in _itemsGrid)
            {
                var detalle = new FacturacionDetalle
                {
                    ProductoId = item.ProductoId,
                    Cantidad = item.CanProd, // Se mapea desde la propiedad de tu DTO
                    PrecioUnitario = item.PreUnit,
                    ImporteTotal = item.ImpTota,
                    MovimientoId = item.MovimientoId, // Tu candado de integridad
                    Codigos = item.Codigos.Select(c => new FacturacionDetalleCodigos
                    {
                        CodigoCreadoId = c.CodigoCreadoId
                    }).ToList()
                };
                cabecera.Detalles.Add(detalle);
            }

            try
            {
                if (_idComprobanteActual > 0)
                {
                    // 🌟 MODO ACTUALIZAR
                    cabecera.Id = _idComprobanteActual;
                    await _facturacionService.ActualizarComprobanteAsync(cabecera);
                    MessageBox.Show("Comprobante actualizado correctamente.");
                }
                else
                {
                    int serieId = (CmbSerie.SelectedItem as SerieDocumento).Id;

                    await _facturacionService.GuardarComprobanteAsync(cabecera, serieId);

                    // Recargar correlativos reales desde BD
                    await CargarTodasLasSeries();
                    var prueba = _todasLasSeries
                    .FirstOrDefault(x => x.Id == serieId);

                    MessageBox.Show(
                        $"Serie: {prueba.NumeroSerie}\nFactura actual BD: {prueba.CorrelativoFactura}"
                    );
                    FiltrarSeriesPorTipoDocumento();

                    ActualizarNumeroCorrelativo();

                    MessageBox.Show("Comprobante guardado correctamente.");
                }

                LimpiarFormulario();
                _idComprobanteActual = 0;
                PanelFormulario.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
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

        private void BtnModificarItem_Click(object sender, RoutedEventArgs e)
        {
            if (DgItems.SelectedItem is not ItemGridDTO itemSeleccionado)
            {
                MessageBox.Show("Debe seleccionar un ítem de la lista para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var modal = new AgregarItemWindow(itemSeleccionado)
            {
                Owner = Window.GetWindow(this)
            };

            if (modal.ShowDialog() == true && modal.NuevoItem != null)
            {
                int index = _itemsGrid.IndexOf(itemSeleccionado);
                if (index >= 0)
                {
                    // Conservamos el número de línea original
                    modal.NuevoItem.NumLine = itemSeleccionado.NumLine;

                    // Reemplazamos el ítem completo en la misma posición
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

            var confirmacion = MessageBox.Show(
                $"¿Está seguro de eliminar el ítem \"{itemSeleccionado.DescripcionProducto}\"?",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmacion == MessageBoxResult.Yes)
            {
                _itemsGrid.Remove(itemSeleccionado);
                RecalcularTotales();
            }
        }

        // ==========================================================
        // CARGA DE COMPROBANTE POR NÚMERO (ENTER en TxtNumero)
        // Se dispara tanto en modo "BuscandoParaEditar" como en
        // "BuscandoParaImprimir"; la diferencia es qué se hace DESPUÉS
        // de cargar los datos.
        // ==========================================================
        private async void TxtNumero_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await CargarComprobantePorNumero();
            }
        }

        private async Task CargarComprobantePorNumero()
        {
            // Si el ENTER se presiona fuera de un modo de búsqueda válido, no hacemos nada.
            if (_modoActual != ModoFormulario.BuscandoParaEditar && _modoActual != ModoFormulario.BuscandoParaImprimir)
                return;

            if (CmbSerie.SelectedItem is not SerieDocumento serieSeleccionada)
            {
                MessageBox.Show("Seleccione una serie primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_modoActual == ModoFormulario.BuscandoParaImprimir)
            {
                // Dejamos el panel habilitado.
                // Luego bloquearemos únicamente los controles de edición.
                PanelFormulario.IsEnabled = true;
            }

            string inputNumero = TxtNumero.Text.Trim();
            if (!int.TryParse(inputNumero, out int numeroInt))
            {
                MessageBox.Show("El número de comprobante debe ser un valor numérico.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🌟 FORMATEO A 7 DÍGITOS
            string numeroFormateado = numeroInt.ToString("D7");
            TxtNumero.Text = numeroFormateado; // Actualizamos el UI para que el usuario vea que se completó

            // 1. Traer la cabecera completa desde la BD
            var comprobante = await _facturacionService.ObtenerComprobantePorNumeroAsync(serieSeleccionada.NumeroSerie, numeroFormateado);

            if (comprobante == null)
            {
                // 🌟 CORRECCIÓN: Usamos numeroFormateado para el mensaje de error
                MessageBox.Show($"No se encontró el comprobante {serieSeleccionada.NumeroSerie}-{numeroFormateado}.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!comprobante.EstadoRegistro)
            {
                MessageBox.Show("¡ATENCIÓN! Este comprobante se encuentra ANULADO.", "Comprobante Anulado", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 2. Llenar la Cabecera Visual
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

            // 3. Llenar la Grilla de Detalles
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

            // 4. Totales
            RecalcularTotales();
            TxtOpGravadas.Text = comprobante.TotalGravado.ToString("N2");
            TxtOpExoneradas.Text = comprobante.TotalExonerado.ToString("N2");
            TxtIgv.Text = comprobante.TotalIgv.ToString("N2");
            TxtTotal.Text = comprobante.ImporteTotal.ToString("N2");

            // 5. Comportamiento final
            if (_modoActual == ModoFormulario.BuscandoParaEditar)
            {
                HabilitarTodoElFormulario();
            }
            else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
            {
                // El panel permanece habilitado.
                PanelFormulario.IsEnabled = true;

                // Bloqueamos únicamente la edición.
                TxtRazonSocialBuscador.IsEnabled = false;
                TxtDniRuc.IsEnabled = false;
                CmbTipoIdentidad.IsEnabled = false;
                TxtDireccionPagador.IsEnabled = false;
                TxtClienteBuscador.IsEnabled = false;
                TxtDireccionColegio.IsEnabled = false;
                TxtLocalidad.IsEnabled = false;
                TxtObservacion.IsEnabled = false;
                DpFecha.IsEnabled = false;

                DgItems.IsEnabled = false;

                BtnAgregarItem.IsEnabled = false;
                BtnModificarItem.IsEnabled = false;
                BtnEliminarItem.IsEnabled = false;
                BtnLector.IsEnabled = false;
                BtnGrabar.IsEnabled = false;

                // ESTE SÍ debe quedar habilitado
                BtnImprimirExcel.IsEnabled = true;

                // Buscador sigue funcionando
                CmbTipoDocu.IsEnabled = true;
                CmbSerie.IsEnabled = true;

                TxtNumero.IsEnabled = true;
                TxtNumero.IsReadOnly = false;
            }
        }

        // ==========================================================
        // BOTÓN "IMPRIMIR EXCEL" (fila inferior) - NUEVO
        // Solo hace el export; nace deshabilitado y se activa recién
        // cuando se cargó un comprobante en modo Vista Previa.
        // ==========================================================
        private void BtnImprimirExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsGrid.Count == 0)
            {
                MessageBox.Show("No hay datos cargados para imprimir. Use el botón 'Imprimir' de la parte superior para buscar un comprobante primero.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var reporteService = new AplicativoDeAlmacen.Services.Reportes.ReporteExcelService();

                string tipoDoc = (CmbTipoDocu.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "FACTURA";
                string serieNumero = $"{CmbSerie.Text}-{TxtNumero.Text}";
                string fecha = DpFecha.SelectedDate?.ToString("dd/MM/yyyy") ?? "";

                decimal.TryParse(TxtOpGravadas.Text, out decimal gravadas);
                decimal.TryParse(TxtOpExoneradas.Text, out decimal exoneradas);
                decimal.TryParse(TxtIgv.Text, out decimal igv);
                decimal.TryParse(TxtTotal.Text, out decimal total);

                reporteService.ExportarComprobanteImpresion(
                   tipoDoc, serieNumero, fecha,
                    TxtRazonSocialBuscador.Text, TxtDniRuc.Text,
                    TxtClienteBuscador.Text, TxtLocalidad.Text,
                    TxtObservacion.Text, "USUARIO_SISTEMA",
                    _itemsGrid.ToList(),
                    gravadas, exoneradas, igv, total
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al generar el Excel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================================
        // BOTÓN ANULAR (No elimina, solo cambia estado_registro a 0)
        // ==========================================================
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
                    await _facturacionService.AnularComprobanteAsync(_idComprobanteActual);
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

        private void DgItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}