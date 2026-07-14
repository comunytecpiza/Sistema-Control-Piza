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

namespace AplicativoDeAlmacen.Views
{
    public partial class SalidasUserControl : UserControl
    {
        private readonly SalidaMovimientoService _salidaService;
        private List<PersonaComercial> _listaClientes;
        private List<Ubicacion> _listaUbicaciones;

        int idUsuarioLogueado = 1;
        int estadoSalida = 1;

        private ObservableCollection<VistaProductoGrid> _productosLista;
        private ObservableCollection<VistaCodigoGrid> _codigosLista;

        private int? _idClienteSeleccionado;
        private int? _idUbicacionSeleccionada;
        private int? _idMovimientoActual;

        private enum ModoFormulario { Ninguno, Nuevo, BuscandoParaEditar, BuscandoParaImprimir }
        private ModoFormulario _modoActual = ModoFormulario.Ninguno;

        public SalidasUserControl()
        {
            InitializeComponent();
            _salidaService = new SalidaMovimientoService();

            _productosLista = new ObservableCollection<VistaProductoGrid>();
            _codigosLista = new ObservableCollection<VistaCodigoGrid>();

            dgProductosSalida.ItemsSource = _productosLista;
            dgCodigosSalida.ItemsSource = _codigosLista;

            EstadoInicialFormulario();
            CargarComboMotivosSalida();
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
                dgCodigosSalida.ItemsSource =
                    _codigosLista.Where(x => x.ProductoId == seleccionado.ProductoId).ToList();
            }
            else
            {
                dgCodigosSalida.ItemsSource = _codigosLista;
            }

            dgCodigosSalida.Items.Refresh();
        }

        private List<RangoCodigoItem> ReconstruirRangosDeCodigos(int productoId)
        {
            var rangos = new List<RangoCodigoItem>();
            var codigosDelProducto = _codigosLista.Where(c => c.ProductoId == productoId).ToList();
            if (!codigosDelProducto.Any()) return rangos;

            var secuenciasPorBase = new Dictionary<string, List<int>>();
            var dicColeccion = new Dictionary<int, string>();
            var ingService = new IngresoMovimientoService();

            foreach (var c in codigosDelProducto)
            {
                string norm = ingService.NormalizarCodigo(c.CodigoUnique);
                if (norm.Length >= 7 && int.TryParse(norm.Substring(norm.Length - 7), out int seq))
                {
                    string baseAbrev = norm.Substring(0, norm.Length - 7);
                    if (!secuenciasPorBase.ContainsKey(baseAbrev)) secuenciasPorBase[baseAbrev] = new List<int>();
                    secuenciasPorBase[baseAbrev].Add(seq);
                    dicColeccion[seq] = c.ColeccionTipo;
                }
            }

            foreach (var kvp in secuenciasPorBase)
            {
                var seqs = kvp.Value;
                seqs.Sort();

                int start = seqs[0];
                int end = start;

                for (int i = 1; i < seqs.Count; i++)
                {
                    if (seqs[i] == end + 1)
                    {
                        end = seqs[i];
                    }
                    else
                    {
                        rangos.Add(new RangoCodigoItem
                        {
                            productoId = productoId,
                            AbreviaturaBase = kvp.Key,
                            DesdeNum = start,
                            HastaNum = end,
                            Cantidad = (end - start + 1).ToString(),
                            Desde = $"{kvp.Key}{start:D7}",
                            Hasta = $"{kvp.Key}{end:D7}",
                            ColeccionTipo = dicColeccion.ContainsKey(start) ? dicColeccion[start] : ""
                        });
                        start = seqs[i];
                        end = start;
                    }
                }
                rangos.Add(new RangoCodigoItem
                {
                    productoId = productoId,
                    AbreviaturaBase = kvp.Key,
                    DesdeNum = start,
                    HastaNum = end,
                    Cantidad = (end - start + 1).ToString(),
                    Desde = $"{kvp.Key}{start:D7}",
                    Hasta = $"{kvp.Key}{end:D7}",
                    ColeccionTipo = dicColeccion.ContainsKey(start) ? dicColeccion[start] : ""
                });
            }
            return rangos;
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
            txtNumeroSalida.IsReadOnly = true;
            ActualizarVisibilidadCampos();

            try
            {
                string seriePorDefecto = "S001";
                var proxMov = await _salidaService.GenerarSiguienteCorrelativoAsync(seriePorDefecto);
                txtSerieSalida.Text = proxMov.SerieDocumento;
                txtNumeroSalida.Text = proxMov.NumeroDocumento;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar correlativo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.BuscandoParaEditar;

            grdFormularioSalida.IsEnabled = true;
            txtNumeroSalida.IsReadOnly = false;
            txtNumeroSalida.Background = System.Windows.Media.Brushes.White;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "S001";

            cboMotivoSalida.IsEnabled = false;
            dtpFechaDespacho.IsEnabled = false;

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para cargar y modificar.", "Modo Edición", MessageBoxButton.OK, MessageBoxImage.Information);
            txtNumeroSalida.Focus();
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            EstadoInicialFormulario();
            _modoActual = ModoFormulario.BuscandoParaImprimir;

            grdFormularioSalida.IsEnabled = true;
            txtNumeroSalida.IsReadOnly = false;
            txtNumeroSalida.Background = System.Windows.Media.Brushes.White;
            txtSerieSalida.IsReadOnly = false;
            txtSerieSalida.Text = "S001";

            cboMotivoSalida.IsEnabled = false;
            dtpFechaDespacho.IsEnabled = false;

            txtNumeroSalida.KeyDown -= txtNumeroSalida_KeyDown;
            txtNumeroSalida.KeyDown += txtNumeroSalida_KeyDown;

            MessageBox.Show("Escriba el N° de Documento y presione ENTER para ver e imprimir.", "Modo Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
            txtNumeroSalida.Focus();
        }

        private void BtnAnular_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funcionalidad de anulación en construcción.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var movCompleto = await _salidaService.GetMovimientoCompletoAsync(serie, numStr);

                    if (movCompleto == null || movCompleto.Movimiento == null)
                    {
                        MessageBox.Show("Movimiento no encontrado.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    _idMovimientoActual = movCompleto.Movimiento.Id;
                    dtpFechaDespacho.SelectedDate = movCompleto.Movimiento.FechaMovimiento?.ToDateTime(TimeOnly.MinValue);
                    cboMotivoSalida.SelectedValue = movCompleto.Movimiento.MotivoProductoId;
                    txtSerieGuia.Text = movCompleto.Movimiento.SerieGuia;
                    txtNumeroGuia.Text = movCompleto.Movimiento.NumeroGuia;
                    txtObservacionSalida.Text = movCompleto.Movimiento.Observacion;

                    // 🌟 CORRECCIÓN: Consultar el nombre real del cliente a la BD
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
                            txtCliente.Text = res?.ToString() ?? "";
                        }
                        catch { txtCliente.Text = $"ID CLIENTE: {_idClienteSeleccionado}"; }
                    }

                    // 🌟 CORRECCIÓN: Consultar el nombre real de la ubicación a la BD
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
                            txtUbicacion.Text = res?.ToString() ?? "";
                        }
                        catch { txtUbicacion.Text = $"ID UBIC: {_idUbicacionSeleccionada}"; }
                    }

                    _productosLista.Clear();
                    _codigosLista.Clear();

                    var prodService = new ProductoService();

                    foreach (var det in movCompleto.Detalles)
                    {
                        var prodData = await prodService.ObtenerPorIdAsync(det.ProductoId);

                        var vistaProd = new VistaProductoGrid
                        {
                            Detalle = det,
                            ProductoId = det.ProductoId,
                            CodigoProducto = prodData?.Abreviatura ?? prodData?.Id.ToString() ?? "",
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            Cantidad = Convert.ToInt32(det.CantidadSalida),
                            UnidadMedida = "UND"
                        };
                        _productosLista.Add(vistaProd);

                        using (var conn = new DatabaseConnection().GetConnection())
                        {
                            await ((DbConnection)conn).OpenAsync();
                            using (var cmd = ((DbConnection)conn).CreateCommand())
                            {
                                cmd.CommandText = "SELECT cc.id, cc.codigo FROM movimiento_codigos mc JOIN codigos_creados cc ON mc.codigo_creado_id = cc.id WHERE mc.movimiento_detalle_id = @detId";
                                var p = cmd.CreateParameter(); p.ParameterName = "@detId"; p.Value = det.Id; cmd.Parameters.Add(p);
                                using (var rdr = await cmd.ExecuteReaderAsync())
                                {
                                    while (await rdr.ReadAsync())
                                    {
                                        int idCodigo = rdr.GetInt32(0);
                                        string codString = rdr.GetString(1);
                                        string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(idCodigo);

                                        _codigosLista.Add(new VistaCodigoGrid
                                        {
                                            ProductoId = det.ProductoId,
                                            CodigoUnique = codString,
                                            MovCodigo = new MovimientoCodigo { CodigoCreadoId = idCodigo },
                                            ColeccionTipo = tipoColeccionReal
                                        });
                                    }
                                }
                            }
                        }
                    }

                    RefrescarGrillas();

                    txtNumeroSalida.IsReadOnly = true;
                    txtNumeroSalida.Background = System.Windows.Media.Brushes.WhiteSmoke;

                    if (_modoActual == ModoFormulario.BuscandoParaEditar)
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
                    else if (_modoActual == ModoFormulario.BuscandoParaImprimir)
                    {
                        btnCancelar.IsEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cargar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // ==========================================
        // DETALLE: AGREGAR, MODIFICAR Y ELIMINAR 
        // ==========================================

        private async void btnAgregarItem_Click(object sender, RoutedEventArgs e)
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

                if (existente != null && modal.MergeWithExisting)
                {
                    existente.Cantidad += modal.CantidadProductoIngresada;
                    existente.Detalle.CantidadSalida += modal.CantidadProductoIngresada;
                }
                else
                {
                    if (existente != null)
                    {
                        MessageBox.Show("El producto ya existe. Use modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 🌟 AQUÍ ESTÁ LA CORRECCIÓN: Declaramos e instanciamos el servicio
                    var prodService = new ProductoService();
                    var prodData = await prodService.ObtenerPorIdAsync(idProducto);

                    _productosLista.Add(new VistaProductoGrid
                    {
                        ProductoId = idProducto,
                        CodigoProducto = prodData?.Abreviatura ?? idProducto.ToString(),
                        Descripcion = prodData?.Descripcion ?? "Desconocido",
                        UnidadMedida = "UNIDAD",
                        Cantidad = (int)modal.CantidadProductoIngresada,
                        Detalle = new MovimientoDetalle
                        {
                            ProductoId = idProducto,
                            CantidadSalida = modal.CantidadProductoIngresada,
                            CostoUnitario = prodData?.PrecioUnitario ?? 0
                        }
                    });
                }

                // 🌟 CORRECCIÓN CRÍTICA: Extraer los ID Reales de la BD, NO usar 0
                var ingService = new IngresoMovimientoService();
                var listaStrings = new List<string>();
                foreach (var rango in rangosDelModal)
                {
                    for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                    {
                        listaStrings.Add($"{rango.AbreviaturaBase}-{i:D7}");
                    }
                }

                // Buscamos todos los códigos en la BD en una sola consulta rápida
                var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings);

                foreach (var codStr in listaStrings)
                {
                    string norm = ingService.NormalizarCodigo(codStr);
                    if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                    {
                        string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);
                        _codigosLista.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id }, // 🌟 ID REAL
                            CodigoUnique = tup.CodigoObj.Codigo,
                            ProductoId = idProducto,
                            ColeccionTipo = tipoColeccionReal
                        });
                    }
                }
                RefrescarGrillas();
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
            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };

            if (seleccionado.Detalle.CostoUnitario == 0)
            {
                var prodService = new ProductoService();
                var prodActualizado = await prodService.ObtenerPorIdAsync(seleccionado.ProductoId);
                seleccionado.Detalle.CostoUnitario = prodActualizado?.PrecioUnitario ?? 0;
            }
            // 🌟 TRUCO: Igualamos CantidadIngreso a CantidadSalida temporalmente 
            // para que el modal de "Entrada" sepa cuántos elementos hay
            seleccionado.Detalle.CantidadIngreso = seleccionado.Detalle.CantidadSalida;

            var rangosReconstruidos = ReconstruirRangosDeCodigos(seleccionado.ProductoId);

            modal.InitializeForEdit(seleccionado, rangosReconstruidos);
            modal.EstadoPermitido = 3;

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                // Actualizamos los datos de salida
                seleccionado.Detalle.CantidadSalida = modal.CantidadProductoIngresada;
                seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;
                seleccionado.Cantidad = modal.CantidadProductoIngresada;

                if (modal.ListaRangosAgregados != null && modal.ListaRangosAgregados.Count > 0)
                {
                    // Borramos los viejos
                    var codigosViejos = _codigosLista.Where(c => c.ProductoId == seleccionado.ProductoId).ToList();
                    foreach (var cv in codigosViejos) _codigosLista.Remove(cv);

                    // 🌟 CORRECCIÓN CRÍTICA: Extraer los ID Reales de la BD, NO usar 0
                    var ingService = new IngresoMovimientoService();
                    var listaStrings = new List<string>();
                    foreach (var rango in modal.ListaRangosAgregados)
                    {
                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        {
                            listaStrings.Add($"{rango.AbreviaturaBase}-{i:D7}");
                        }
                    }

                    var lookup = await ingService.ObtenerCodigosPorListaAsync(listaStrings);

                    foreach (var codStr in listaStrings)
                    {
                        string norm = ingService.NormalizarCodigo(codStr);
                        if (lookup.TryGetValue(norm, out var tup) && tup.CodigoObj != null)
                        {
                            string tipoColeccionReal = await ObtenerColeccionTipoBDAsync(tup.CodigoObj.Id);
                            _codigosLista.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id }, // 🌟 ID REAL
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
        // PROCESAR GUARDADO DE LA SALIDA
        // ==========================================
        private async void btnGrabarSalida_Click(object sender, RoutedEventArgs e)
        {
            if (_productosLista.Count == 0 || _codigosLista.Count == 0)
            {
                MessageBox.Show("No hay productos o códigos en la lista para procesar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                bool resultado = await _salidaService.RegistrarSalidaCompletaAsync(
                         movimiento,
                         _productosLista.ToList(),
                         _codigosLista.ToList(),
                         idUsuarioLogueado,
                         estadoSalida,
                         _idMovimientoActual
                     );

                if (resultado)
                {
                    MessageBox.Show("Salida registrada correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    EstadoInicialFormulario();
                    EventBus.NotificarMovimientosChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al grabar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnGrabarSalida.IsEnabled = true;
            }
        }

        // ==========================================
        // IMPORTAR Y ESCANEAR
        // ==========================================
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
                var lookup = await ingService.ObtenerCodigosPorListaAsync(listaRaw);
                var productosAgrupados = new Dictionary<int, int>();

                // 🌟 Obtener categorías reales masivamente (evitar consultas unitarias)
                var idsCreados = lookup.Values.Where(v => v.CodigoObj != null).Select(v => v.CodigoObj.Id).ToList();
                var dicColecciones = new Dictionary<int, string>();
                if (idsCreados.Any())
                {
                    using var conn = new DatabaseConnection().GetConnection();
                    await ((DbConnection)conn).OpenAsync();
                    using var cmd = ((DbConnection)conn).CreateCommand();
                    string idsParam = string.Join(",", idsCreados);
                    cmd.CommandText = $@"
                        SELECT cc.id, rc.categoria_producto_id, c.ano 
                        FROM codigos_creados cc
                        JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                        LEFT JOIN colecciones c ON rc.coleccion_id = c.id
                        WHERE cc.id IN ({idsParam})";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        string ano = rdr.IsDBNull(2) ? "" : rdr.GetValue(2).ToString();
                        int cat = rdr.IsDBNull(1) ? 1 : rdr.GetInt32(1);
                        string tipo = cat == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                        dicColecciones[rdr.GetInt32(0)] = string.IsNullOrEmpty(ano) ? tipo : $"C{ano} / {tipo}";
                    }
                }

                foreach (var raw in listaRaw)
                {
                    string norm = ingService.NormalizarCodigo(raw);
                    if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue) continue;

                    int codigoId = tup.CodigoObj.Id;
                    int productoId = tup.ProductoId.Value;

                    if (tup.CodigoObj.EstadoId != 3) continue;
                    if (_codigosLista.Any(c => string.Equals(c.CodigoUnique, tup.CodigoObj.Codigo, StringComparison.OrdinalIgnoreCase))) continue;

                    string colTipo = dicColecciones.ContainsKey(codigoId) ? dicColecciones[codigoId] : "LIBRO VENTA";

                    _codigosLista.Add(new VistaCodigoGrid
                    {
                        MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoId },
                        CodigoUnique = tup.CodigoObj.Codigo,
                        ProductoId = productoId,
                        ColeccionTipo = colTipo
                    });

                    if (!productosAgrupados.ContainsKey(productoId)) productosAgrupados[productoId] = 0;
                    productosAgrupados[productoId]++;
                }

                var prodService = new ProductoService();
                foreach (var kv in productosAgrupados)
                {
                    var existente = _productosLista.FirstOrDefault(p => p.ProductoId == kv.Key);
                    if (existente != null)
                    {
                        existente.Detalle.CantidadSalida += kv.Value;
                        existente.Cantidad += kv.Value;
                    }
                    else
                    {
                        // Ahora prodService ya existe en este contexto y no dará error
                        var prodData = await prodService.ObtenerPorIdAsync(kv.Key);

                        _productosLista.Add(new VistaProductoGrid
                        {
                            Detalle = new MovimientoDetalle { ProductoId = kv.Key, CantidadSalida = kv.Value, CostoUnitario = prodData?.PrecioUnitario ?? 0 },
                            ProductoId = kv.Key,
                            CodigoProducto = prodData?.Abreviatura ?? kv.Key.ToString(),
                            Descripcion = prodData?.Descripcion ?? "Desconocido",
                            Cantidad = kv.Value,
                            UnidadMedida = "UNIDAD"
                        });
                    }
                }
                RefrescarGrillas();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar códigos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            var lector = new LectorGlobalWindow(async resultado =>
            {
                bool ok = await ProcesarCodigoEscaneadoAsync(resultado);

                if (ok)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SincronizarCantidadesConCodigos();
                        RefrescarGrillas();
                    });
                }

                return ok;
            });

            lector.Owner = Window.GetWindow(this);
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

            if (_codigosLista.Any(x => x.CodigoUnique == resultado.CodigoCompleto))
                return false;

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
            ActualizarVisibilidadCampos();
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
                EstadoInicialFormulario();
            }
        }

        // Buscadores
        private async void TxtCliente_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtCliente.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaClientes = await _salidaService.BuscarClientesAsync(filtro);
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
                _listaUbicaciones = await _salidaService.BuscarUbicacionesAsync(filtro);
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
                txtDireccionUbicacion.Text = ub.Direccion;
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
                // Contamos cuántos códigos tiene este producto en la grilla derecha
                int count = _codigosLista.Count(c => c.ProductoId == producto.ProductoId);

                // Actualizamos la cantidad en el objeto VistaProductoGrid
                producto.Cantidad = count;

                // Si tienes un objeto Detalle (de Entity Framework), actualiza el campo correspondiente
                if (producto.Detalle != null)
                {
                    producto.Detalle.CantidadSalida = count;
                }
            }

            // Forzamos el refresco visual para que la columna "Cantidad" cambie de 0 a X
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
    }
}