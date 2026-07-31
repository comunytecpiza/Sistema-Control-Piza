using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
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
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarItemWindow : Window
    {
        public bool IsAddAction { get; set; } = false;

        // 🌟 1 = COMPRA (Costo editable / Inicia en 0.00)
        // 🌟 3 = SALIDA (Costo OCULTO completamente)
        // 🌟 4 = DEVOLUCIÓN/REINGRESO (Costo jala de BD / BLOQUEADO Y SOMBREADO)
        public int EstadoPermitido { get; set; } = 1;

        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;
        public int? MovimientoIdActual { get; set; } = null;
        public bool MergeWithExisting { get; private set; } = false;

        public int CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        private readonly DatabaseConnection _database;
        public Producto _productoSeleccionado = null;
        public ObservableCollection<RangoCodigoItem> ListaRangosAgregados { get; private set; }
        public List<VistaProductoGrid> ListaProductosExistentesEnPadre { get; set; }

        private List<RangoCodigoItem> _rangosOriginalesEdicion = new List<RangoCodigoItem>();

        public AgregarItemWindow()
        {
            InitializeComponent();

            _productoService = new ProductoService();
            _database = new DatabaseConnection();
            ListaRangosAgregados = new ObservableCollection<RangoCodigoItem>();

            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
            dgDetalleCodigos.MouseDoubleClick += DgDetalleCodigos_MouseDoubleClick;

            txtProducto.TextChanged += TxtProducto_TextChanged;
            lstSugerenciasProductos.SelectionChanged += LstSugerenciasProductos_SelectionChanged;

            txtCantidad.IsReadOnly = false;

            Loaded += (s, e) => { AplicarReglasVisualesCosto(); };

            txtBuscarRangoInterno.TextChanged += (s, e) =>
            {
                string filtro = txtBuscarRangoInterno.Text.Trim().ToLower();
                dgDetalleCodigos.ItemsSource = null;

                if (string.IsNullOrEmpty(filtro))
                {
                    dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
                }
                else
                {
                    dgDetalleCodigos.ItemsSource = ListaRangosAgregados
                        .Where(r => (r.Desde != null && r.Desde.ToLower().Contains(filtro)) ||
                                    (r.Hasta != null && r.Hasta.ToLower().Contains(filtro)) ||
                                    (r.ColeccionTipo != null && r.ColeccionTipo.ToLower().Contains(filtro)))
                        .ToList();
                }
            };
        }

        // 🌟 MÉTODO CENTRALIZADO DE REGLAS DE COSTO
        private void AplicarReglasVisualesCosto()
        {
            bool esSalida = (this.EstadoPermitido == 3);
            bool esCompraOOtros = (this.EstadoPermitido == 1);

            if (esSalida)
            {
                // 🚫 EN SALIDAS: OCULTAR CAMPO DE COSTO Y SU ETIQUETA
                txtCUnitario.Visibility = Visibility.Collapsed;
                if (FindName("lblCUnitario") is TextBlock lbl) lbl.Visibility = Visibility.Collapsed;
            }
            else if (!esCompraOOtros)
            {
                // 🔒 EN REINGRESOS / DEVOLUCIONES / TRANSFERENCIAS: MOSTRAR, PERO BLOQUEAR Y SOMBREADO
                txtCUnitario.Visibility = Visibility.Visible;
                if (FindName("lblCUnitario") is TextBlock lbl) lbl.Visibility = Visibility.Visible;

                txtCUnitario.IsReadOnly = true;
                txtCUnitario.Background = System.Windows.Media.Brushes.WhiteSmoke;
                txtCUnitario.Foreground = System.Windows.Media.Brushes.Gray;
                txtCUnitario.Focusable = false;
            }
            else
            {
                // ✏️ EN COMPRAS Y OTROS: MOSTRAR Y EDITABLE
                txtCUnitario.Visibility = Visibility.Visible;
                if (FindName("lblCUnitario") is TextBlock lbl) lbl.Visibility = Visibility.Visible;

                txtCUnitario.IsReadOnly = false;
                txtCUnitario.Background = System.Windows.Media.Brushes.White;
                txtCUnitario.Foreground = System.Windows.Media.Brushes.Black;
                txtCUnitario.Focusable = true;
            }
        }

        private async Task CargarStockActualProductoAsync(int productoId)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                int almacenActualId = SesionSistema.AlmacenActual?.Id ?? 1;

                string query = QueryAdapter.EsMySQL
                    ? @"SELECT IFNULL(stock_actual, 0)
                        FROM stock_almacen
                        WHERE producto_id = @productoId AND almacen_id = @almacenId;"
                    : @"SELECT ISNULL(stock_actual, 0)
                        FROM stock_almacen WITH (NOLOCK)
                        WHERE producto_id = @productoId AND almacen_id = @almacenId";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var pProd = cmd.CreateParameter(); pProd.ParameterName = "@productoId"; pProd.Value = productoId; cmd.Parameters.Add(pProd);
                var pAlm = cmd.CreateParameter(); pAlm.ParameterName = "@almacenId"; pAlm.Value = almacenActualId; cmd.Parameters.Add(pAlm);

                object result = await cmd.ExecuteScalarAsync();
                int stockLocal = (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;

                txtStockDisponible.Text = stockLocal.ToString("N0");
            }
            catch
            {
                txtStockDisponible.Text = "0";
            }
        }

        private void RecalcularCantidadTotalEnVivo()
        {
            if (int.TryParse(txtCantidad.Text, out int actual) && actual > 0)
            {
                return;
            }

            if (ListaRangosAgregados != null && ListaRangosAgregados.Any())
            {
                int sumaTotal = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
                txtCantidad.Text = sumaTotal.ToString();
            }
        }

        public void InitializeForEdit(VistaProductoGrid item, List<RangoCodigoItem> rangos)
        {
            if (item == null) return;

            IsEdit = true;
            OriginalProductoId = item.ProductoId;

            _productoSeleccionado = new Producto
            {
                Id = item.ProductoId,
                Descripcion = item.Descripcion,
                PrecioUnitario = item.Detalle?.CostoUnitario ?? 0m,
                UnidadMedida = new UnidadMedida { Descripcion = item.UnidadMedida }
            };

            if (rangos != null && rangos.Any())
            {
                var primera = rangos.FirstOrDefault();
                if (primera != null && !string.IsNullOrWhiteSpace(primera.AbreviaturaBase))
                {
                    _productoSeleccionado.Abreviatura = primera.AbreviaturaBase;
                }
            }

            txtProducto.Text = _productoSeleccionado.Descripcion;
            txtUMedida.Text = !string.IsNullOrWhiteSpace(item.UnidadMedida) ? item.UnidadMedida.ToUpperInvariant() : "UNIDAD";

            decimal costoRealMovimiento = item.Detalle?.CostoUnitario ?? 0m;
            txtCUnitario.Text = costoRealMovimiento.ToString("F2");

            AplicarReglasVisualesCosto();

            dgDetalleCodigos.ItemsSource = null;
            ListaRangosAgregados.Clear();
            _rangosOriginalesEdicion.Clear();

            if (rangos != null)
            {
                foreach (var r in rangos)
                {
                    string txtDesde, txtHasta;
                    int desdeN = r.DesdeNum;
                    int hastaN = r.HastaNum;
                    string abrev = string.IsNullOrEmpty(r.AbreviaturaBase) ? (_productoSeleccionado?.Abreviatura ?? "COD") : r.AbreviaturaBase;
                    int cantCalcular = (desdeN == -1) ? 1 : (hastaN - desdeN + 1);

                    if (desdeN == -1)
                    {
                        txtDesde = abrev;
                        txtHasta = abrev;
                    }
                    else
                    {
                        txtDesde = string.IsNullOrEmpty(r.Desde) ? $"{abrev}-{desdeN:D7}" : r.Desde;
                        txtHasta = string.IsNullOrEmpty(r.Hasta) ? $"{abrev}-{hastaN:D7}" : r.Hasta;
                    }

                    int categoriaId = r.CategoriaProductoId == 0 ? (EstadoPermitido == 1 ? 1 : 2) : r.CategoriaProductoId;
                    string tipoTexto = (categoriaId == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";

                    string textoColeccionFinal = $"C26 / {tipoTexto}";
                    if (!string.IsNullOrEmpty(r.ColeccionTipo) && r.ColeccionTipo.Contains("/"))
                    {
                        var partesColeccion = r.ColeccionTipo.Split('/');
                        textoColeccionFinal = $"{partesColeccion[0].Trim()} / {tipoTexto}";
                    }

                    string txtCantidadRango = string.IsNullOrEmpty(r.Cantidad) || r.Cantidad == "0" ? cantCalcular.ToString() : r.Cantidad;

                    var nuevoRangoItem = new RangoCodigoItem
                    {
                        Cantidad = txtCantidadRango,
                        Desde = txtDesde,
                        Hasta = txtHasta,
                        ColeccionTipo = textoColeccionFinal,
                        DesdeNum = desdeN,
                        HastaNum = hastaN,
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = abrev,
                        productoId = r.productoId == 0 ? item.ProductoId : r.productoId
                    };

                    ListaRangosAgregados.Add(nuevoRangoItem);

                    _rangosOriginalesEdicion.Add(new RangoCodigoItem
                    {
                        DesdeNum = desdeN,
                        HastaNum = hastaN,
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = abrev,
                        productoId = nuevoRangoItem.productoId
                    });
                }
            }

            dgDetalleCodigos.ItemsSource = ListaRangosAgregados;

            decimal cantidadOriginal = item.Detalle != null ? (item.Detalle.CantidadIngreso > 0 ? item.Detalle.CantidadIngreso : item.Detalle.CantidadSalida) : item.Cantidad;
            txtCantidad.Text = cantidadOriginal > 0 ? Convert.ToInt32(cantidadOriginal).ToString() : ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int cant) ? cant : 0).ToString();

            txtCantidad.IsReadOnly = false;
            txtProducto.IsEnabled = false;
        }

        #region BUSCADOR DE PRODUCTOS

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string textoBusqueda = txtProducto.Text.Trim();

            if (textoBusqueda.Length < 2)
            {
                popupProductos.IsOpen = false;
                return;
            }

            try
            {
                if (_productoSeleccionado == null || _productoSeleccionado.Descripcion != txtProducto.Text)
                {
                    List<Producto> listaFiltrada = await _productoService.BuscarProductosPorTextoAsync(textoBusqueda);

                    if (listaFiltrada.Count > 0)
                    {
                        lstSugerenciasProductos.ItemsSource = listaFiltrada;
                        popupProductos.IsOpen = true;
                    }
                    else
                    {
                        popupProductos.IsOpen = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al buscar el producto:\n{ex.Message}", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void LstSugerenciasProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasProductos.SelectedItem is Producto producto)
            {
                _productoSeleccionado = producto;
                txtProducto.Text = producto.Descripcion;

                txtUMedida.Text = (producto.UnidadMedida != null && !string.IsNullOrWhiteSpace(producto.UnidadMedida.Descripcion))
                    ? producto.UnidadMedida.Descripcion.ToUpperInvariant()
                    : "UNIDAD";

                // 🌟 ASIGNACIÓN Y CONTROL ESTRICTO DE COSTO SEGÚN MOTIVO
                bool esCompraOOtros = (this.EstadoPermitido == 1);

                if (esCompraOOtros)
                {
                    txtCUnitario.Text = "0.00"; // Inicia en 0 para ingresar costo de compra
                }
                else
                {
                    txtCUnitario.Text = producto.PrecioUnitario.HasValue ? producto.PrecioUnitario.Value.ToString("F2") : "0.00";
                }

                AplicarReglasVisualesCosto();

                await CargarStockActualProductoAsync(producto.Id);

                if (string.IsNullOrWhiteSpace(producto.Abreviatura))
                {
                    ListaRangosAgregados.Clear();
                    dgDetalleCodigos.IsEnabled = false;
                    txtBuscarRangoInterno.IsEnabled = false;
                }
                else
                {
                    dgDetalleCodigos.IsEnabled = true;
                    txtBuscarRangoInterno.IsEnabled = true;
                }

                popupProductos.IsOpen = false;
                lstSugerenciasProductos.SelectedIndex = -1;
            }
        }

        #endregion

        #region ASIGNACIÓN Y MODIFICACIÓN DE RANGOS

        private void BtnAgregarRangoCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto antes de agregar códigos.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura))
            {
                MessageBox.Show("Este producto es genérico (sin series de códigos).", "Producto Genérico", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(txtCantidad.Text, out int cantidadCodigosEsperados) || cantidadCodigosEsperados <= 0)
            {
                MessageBox.Show("Ingrese primero la cantidad de unidades arriba.", "Cantidad Vacía", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCantidad.Focus();
                return;
            }

            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);
            int totalCodigosYaAgregados = listaDeRangosActual.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
            int cantidadFaltantePorAsignar = cantidadCodigosEsperados - totalCodigosYaAgregados;

            if (cantidadFaltantePorAsignar <= 0)
            {
                MessageBox.Show($"Ya ha asignado el total de la cantidad digitada ({cantidadCodigosEsperados} unidades).", "Lote Completo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                AsignarCodigoWindow ventanaCodigo = new AsignarCodigoWindow(
                    listaDeRangosActual,
                    _productoSeleccionado.Abreviatura,
                    _productoSeleccionado.Id,
                    cantidadFaltantePorAsignar
                )
                {
                    EstadoPermitido = this.EstadoPermitido,
                    Owner = this
                };

                if (ventanaCodigo.ShowDialog() == true && ventanaCodigo.FueConfirmado)
                {
                    this.Cursor = Cursors.Wait;
                    btnGrabar.IsEnabled = false;

                    RangoCodigoItem nuevoRango = ventanaCodigo.RangoProcesado;
                    if (nuevoRango != null)
                    {
                        dgDetalleCodigos.ItemsSource = null;
                        ListaRangosAgregados.Add(nuevoRango);
                        dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
                    }

                    RecalcularCantidadTotalEnVivo();

                    btnGrabar.IsEnabled = true;
                    this.Cursor = Cursors.Arrow;
                }
            }
            catch (Exception ex)
            {
                this.Cursor = Cursors.Arrow;
                btnGrabar.IsEnabled = true;
                MessageBox.Show($"Error al abrir la ventana de códigos:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<string> ObtenerCodigosConMovimientosPosteriores(int productoId, List<string> codigosQuitados)
        {
            var conflictos = new List<string>();
            if (codigosQuitados == null || !codigosQuitados.Any()) return conflictos;

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                bool esModoSalida = (this.EstadoPermitido == 3);
                bool esModoDevolucion = (this.EstadoPermitido == 4);
                bool esModoCompraInicial = (this.EstadoPermitido == 1);

                if (esModoDevolucion) return conflictos;

                foreach (string codStr in codigosQuitados)
                {
                    string query = string.Empty;

                    if (esModoSalida)
                    {
                        query = QueryAdapter.EsMySQL
                            ? @"SELECT COUNT(*)
                        FROM codigos_creados cc
                        INNER JOIN movimiento_codigos mc ON mc.codigo_creado_id = cc.id
                        INNER JOIN movimientos m ON mc.movimiento_id = m.id
                        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        WHERE cc.codigo = @codigoExacto
                          AND m.estado_id = 1
                          AND (@movIdActual IS NULL OR m.id != @movIdActual)
                          AND cc.estado_id IN (4, 5)
                          AND mp.tipo_movimiento_id = 2
                          AND m.fecha_movimiento > CURRENT_DATE();"
                            : @"SELECT COUNT(*)
                        FROM codigos_creados cc WITH (NOLOCK)
                        INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                        INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        WHERE cc.codigo = @codigoExacto
                          AND m.estado_id = 1
                          AND (@movIdActual IS NULL OR m.id != @movIdActual)
                          AND cc.estado_id IN (4, 5)
                          AND mp.tipo_movimiento_id = 2
                          AND m.fecha_movimiento > GETDATE();";
                    }
                    else if (esModoCompraInicial)
                    {
                        query = QueryAdapter.EsMySQL
                            ? @"SELECT COUNT(*)
                        FROM codigos_creados cc
                        INNER JOIN movimiento_codigos mc ON mc.codigo_creado_id = cc.id
                        INNER JOIN movimientos m ON mc.movimiento_id = m.id
                        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        WHERE cc.codigo = @codigoExacto
                          AND m.estado_id = 1
                          AND (@movIdActual IS NULL OR m.id != @movIdActual)
                          AND cc.estado_id IN (4, 5)
                          AND mp.tipo_movimiento_id = 2;"
                            : @"SELECT COUNT(*)
                        FROM codigos_creados cc WITH (NOLOCK)
                        INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                        INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        WHERE cc.codigo = @codigoExacto
                          AND m.estado_id = 1
                          AND (@movIdActual IS NULL OR m.id != @movIdActual)
                          AND cc.estado_id IN (4, 5)
                          AND mp.tipo_movimiento_id = 2;";
                    }

                    if (string.IsNullOrEmpty(query)) continue;

                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    var pCod = cmd.CreateParameter(); pCod.ParameterName = "@codigoExacto"; pCod.Value = codStr; cmd.Parameters.Add(pCod);
                    var pMov = cmd.CreateParameter(); pMov.ParameterName = "@movIdActual"; pMov.Value = (object?)this.MovimientoIdActual ?? DBNull.Value; cmd.Parameters.Add(pMov);

                    object res = cmd.ExecuteScalar();
                    int count = (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;

                    if (count > 0) conflictos.Add(codStr);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar trazabilidad de Kárdex: {ex.Message}");
            }

            return conflictos;
        }

        // 🌟 VALIDACIÓN ESTRICTA DE ALMACÉN Y ESTADO
        private List<string> ValidarPertenenciaYCondicionAlmacen(List<RangoCodigoItem> rangosAgregados)
        {
            var codigosIncompatibles = new List<string>();
            if (rangosAgregados == null || !rangosAgregados.Any()) return codigosIncompatibles;

            int almacenSesionId = SesionSistema.AlmacenActual?.Id ?? 1;
            bool esModoSalida = (this.EstadoPermitido == 3);        // ➡️ SALIDA
            bool esModoCompra = (this.EstadoPermitido == 1);        // ➡️ COMPRA NUEVA

            var codigosOriginalesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (IsEdit && _rangosOriginalesEdicion.Any())
            {
                foreach (var r in _rangosOriginalesEdicion)
                {
                    string separador = r.AbreviaturaBase.EndsWith("-V") || r.AbreviaturaBase.EndsWith("-G") ? "-" : (r.CategoriaProductoId == 1 ? "-G-" : "-V-");
                    if (r.DesdeNum == -1) codigosOriginalesSet.Add(r.AbreviaturaBase);
                    else { for (int i = r.DesdeNum; i <= r.HastaNum; i++) codigosOriginalesSet.Add($"{r.AbreviaturaBase}{separador}{i:D7}"); }
                }
            }

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                foreach (var rango in rangosAgregados)
                {
                    var listaCodigosRango = new List<string>();
                    if (rango.DesdeNum == -1) listaCodigosRango.Add(rango.AbreviaturaBase);
                    else
                    {
                        string separador = rango.AbreviaturaBase.EndsWith("-V") || rango.AbreviaturaBase.EndsWith("-G") ? "-" : (rango.CategoriaProductoId == 1 ? "-G-" : "-V-");
                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++) listaCodigosRango.Add($"{rango.AbreviaturaBase}{separador}{i:D7}");
                    }

                    foreach (var codStr in listaCodigosRango)
                    {
                        if (IsEdit && codigosOriginalesSet.Contains(codStr)) continue;

                        string query = QueryAdapter.EsMySQL
                            ? @"SELECT cc.almacen_id, cc.estado_id, COALESCE(cond.permitir_salida, 0) AS permitir_salida 
                                FROM codigos_creados cc 
                                LEFT JOIN condiciones_codigo cond ON cc.condicion_id = cond.id
                                WHERE cc.codigo = @codigoExacto;"
                            : @"SELECT cc.almacen_id, cc.estado_id, ISNULL(cond.permitir_salida, 0) AS permitir_salida 
                                FROM codigos_creados cc WITH (NOLOCK) 
                                LEFT JOIN condiciones_codigo cond WITH (NOLOCK) ON cc.condicion_id = cond.id
                                WHERE cc.codigo = @codigoExacto;";

                        using var cmd = dbConn.CreateCommand();
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        var p = cmd.CreateParameter(); p.ParameterName = "@codigoExacto"; p.Value = codStr; cmd.Parameters.Add(p);

                        using var rdr = cmd.ExecuteReader();
                        if (rdr.Read())
                        {
                            int almCod = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
                            int estadoCod = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
                            bool permitirSalida = !rdr.IsDBNull(2) && Convert.ToBoolean(rdr.GetValue(2));

                            if (esModoSalida)
                            {
                                // 🛡️ EN SALIDAS: Debe pertenecer al almacén actual + Estado 3 + Operativo
                                if (almCod != almacenSesionId || estadoCod != 3 || !permitirSalida)
                                {
                                    codigosIncompatibles.Add(codStr);
                                }
                            }
                            else if (!esModoCompra)
                            {
                                // 🛡️ EN ENTRADAS / DEVOLUCIONES / REINGRESOS: El código DEBE pertenecer a esta sede/almacén
                                if (almCod != almacenSesionId)
                                {
                                    codigosIncompatibles.Add(codStr);
                                }
                            }
                        }
                        else
                        {
                            // Si no es Compra Nueva y el código NO existe en BD, rebotar
                            if (!esModoCompra) codigosIncompatibles.Add(codStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al validar pertenencia de almacén: {ex.Message}");
            }

            return codigosIncompatibles;
        }

        private void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null || string.IsNullOrWhiteSpace(txtProducto.Text))
            {
                MessageBox.Show("Por favor, seleccione un producto antes de guardar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtCantidad.Text.Trim(), out int cantidadDeclarada) || cantidadDeclarada <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida mayor a 0.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal costoValido = 0m;
            if (this.EstadoPermitido != 3)
            {
                if (!decimal.TryParse(txtCUnitario.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out costoValido))
                {
                    MessageBox.Show("Ingrese un costo unitario válido.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                costoValido = _productoSeleccionado.PrecioUnitario ?? 0m;
            }

            bool esProductoSinCodigo = string.IsNullOrWhiteSpace(_productoSeleccionado.Abreviatura);

            if (!esProductoSinCodigo)
            {
                if (ListaRangosAgregados.Count == 0)
                {
                    MessageBox.Show("Este producto utiliza códigos únicos. Debe presionar '➕ Agregar Rango' para asignar los códigos.", "Códigos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int totalCodigosUnicosRegistrados = ListaRangosAgregados.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);

                if (cantidadDeclarada != totalCodigosUnicosRegistrados)
                {
                    string detalleDiferencia = cantidadDeclarada < totalCodigosUnicosRegistrados
                        ? $"Tiene MÁS CÓDIGOS en la lista ({totalCodigosUnicosRegistrados}) de los indicados en la Cantidad General ({cantidadDeclarada})."
                        : $"Tiene MENOS CÓDIGOS en la lista ({totalCodigosUnicosRegistrados}) de los indicados en la Cantidad General ({cantidadDeclarada}).";

                    MessageBox.Show(
                        $"⚠️ Descuadre en la Cantidad General:\n\n" +
                        $"{detalleDiferencia}\n\n" +
                        $"• Cantidad General arriba: {cantidadDeclarada} unidades.\n" +
                        $"• Suma de códigos abajo: {totalCodigosUnicosRegistrados} códigos.\n\n" +
                        $"Por favor, corrija la Cantidad General arriba o ajuste las series en la lista para que coincidan exactamente.",
                        "Inconsistencia de Unidades",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                // 🌟 VALIDACIÓN ESTRICTA DE ALMACÉN DE SESIÓN Y ESTADO
                var incompatibles = ValidarPertenenciaYCondicionAlmacen(ListaRangosAgregados.ToList());
                if (incompatibles.Any())
                {
                    string nombreAlmacenSesion = SesionSistema.AlmacenActual?.Nombre ?? "tu almacén actual";
                    int totalProblematicos = incompatibles.Count;

                    string mensajeAlerta = (this.EstadoPermitido == 3)
                        ? $"Se encontraron {totalProblematicos} código(s) que NO pertenecen a {nombreAlmacenSesion}, no están disponibles o están marcados como Dañados/Perdidos:\n\n"
                        : $"Se encontraron {totalProblematicos} código(s) que NO pertenecen a {nombreAlmacenSesion}:\n\n";

                    MessageBox.Show(
                        $"⚠️ Código(s) No Permitidos:\n\n" +
                        mensajeAlerta +
                        $"{string.Join("\n", incompatibles.Take(5).Select(c => $"• {c}"))}\n" +
                        $"{(totalProblematicos > 5 ? $"... y {totalProblematicos - 5} códigos más." : "")}\n\n" +
                        $"Por favor, retira estos códigos antes de continuar.",
                        "Restricción de Inventario / Sede",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);

                    return;
                }

                if (IsEdit && _rangosOriginalesEdicion.Any())
                {
                    var codigosOriginales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in _rangosOriginalesEdicion)
                    {
                        string separador = r.AbreviaturaBase.EndsWith("-V") || r.AbreviaturaBase.EndsWith("-G") ? "-" : (r.CategoriaProductoId == 1 ? "-G-" : "-V-");
                        if (r.DesdeNum == -1) codigosOriginales.Add(r.AbreviaturaBase);
                        else { for (int i = r.DesdeNum; i <= r.HastaNum; i++) codigosOriginales.Add($"{r.AbreviaturaBase}{separador}{i:D7}"); }
                    }

                    var codigosActuales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in ListaRangosAgregados)
                    {
                        string separador = r.AbreviaturaBase.EndsWith("-V") || r.AbreviaturaBase.EndsWith("-G") ? "-" : (r.CategoriaProductoId == 1 ? "-G-" : "-V-");
                        if (r.DesdeNum == -1) codigosActuales.Add(r.AbreviaturaBase);
                        else { for (int i = r.DesdeNum; i <= r.HastaNum; i++) codigosActuales.Add($"{r.AbreviaturaBase}{separador}{i:D7}"); }
                    }

                    var codigosQuitados = codigosOriginales.Where(c => !codigosActuales.Contains(c)).ToList();

                    if (codigosQuitados.Any())
                    {
                        var conflictos = ObtenerCodigosConMovimientosPosteriores(this._productoSeleccionado.Id, codigosQuitados);

                        if (conflictos.Any())
                        {
                            MessageBox.Show(
                                $"⚠️ Operación Rechazada por Seguridad de Kárdex:\n\n" +
                                $"No puede guardar el producto sin los siguientes códigos porque ya registran despachos o salidas posteriores en el sistema:\n\n" +
                                $"{string.Join("\n", conflictos.Take(5).Select(c => $"• {c}"))}\n\n" +
                                $"{(conflictos.Count > 5 ? $"... y {conflictos.Count - 5} códigos más." : "")}\n\n" +
                                $"Debe volver a incluir estos códigos en la lista para poder guardar.",
                                "Restricción de Kárdex",
                                MessageBoxButton.OK,
                                MessageBoxImage.Stop);

                            return;
                        }
                    }
                }
            }

            CantidadProductoIngresada = cantidadDeclarada;
            CostoUnitarioIngresado = costoValido;

            FueGrabado = true;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnEliminarRango_Click(object sender, RoutedEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem rangoSeleccionado)
            {
                if (MessageBox.Show("¿Desea quitar este rango de la lista?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ListaRangosAgregados.Remove(rangoSeleccionado);
                }
            }
            else
            {
                MessageBox.Show("Seleccione la fila que desea eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            FueGrabado = false;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnModificarRango_Click(object sender, RoutedEventArgs e) { EditarRangoSeleccionado(); }

        private void DgDetalleCodigos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgDetalleCodigos.SelectedItem is RangoCodigoItem) EditarRangoSeleccionado();
        }

        private void EditarRangoSeleccionado()
        {
            if (dgDetalleCodigos.SelectedItem is not RangoCodigoItem rangoSeleccionado)
            {
                MessageBox.Show("Seleccione el rango que desea modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int.TryParse(txtCantidad.Text, out int cantidadTotalArriba);
            List<RangoCodigoItem> listaDeRangosActual = new List<RangoCodigoItem>(ListaRangosAgregados);

            int sumaOtrosRangos = listaDeRangosActual.Where(r => r != rangoSeleccionado).Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
            int disponibleParaEsteRango = cantidadTotalArriba - sumaOtrosRangos;

            try
            {
                AsignarCodigoWindow ventanaEdicion = new AsignarCodigoWindow(listaDeRangosActual, rangoSeleccionado, disponibleParaEsteRango > 0 ? disponibleParaEsteRango : 0)
                {
                    EstadoPermitido = this.EstadoPermitido,
                    Owner = this
                };

                if (ventanaEdicion.ShowDialog() == true && ventanaEdicion.FueConfirmado)
                {
                    this.Cursor = Cursors.Wait;
                    btnGrabar.IsEnabled = false;

                    RangoCodigoItem rangoModificado = ventanaEdicion.RangoProcesado;
                    int index = ListaRangosAgregados.IndexOf(rangoSeleccionado);
                    if (index >= 0)
                    {
                        dgDetalleCodigos.ItemsSource = null;
                        ListaRangosAgregados[index] = rangoModificado;
                        dgDetalleCodigos.ItemsSource = ListaRangosAgregados;
                    }

                    RecalcularCantidadTotalEnVivo();

                    btnGrabar.IsEnabled = true;
                    this.Cursor = Cursors.Arrow;
                }
            }
            catch (Exception ex)
            {
                this.Cursor = Cursors.Arrow;
                btnGrabar.IsEnabled = true;
                MessageBox.Show($"Error al editar el rango:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}