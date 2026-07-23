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
        public int EstadoPermitido { get; set; } = 1; // 1 = COMPRA, 4 = DEVOLUCIÓN/VENTA

        public bool IsEdit { get; set; } = false;
        public int? OriginalProductoId { get; set; } = null;
        public bool MergeWithExisting { get; private set; } = false;

        public int CantidadProductoIngresada { get; set; }
        public decimal CostoUnitarioIngresado { get; set; }
        public bool FueGrabado { get; private set; } = false;

        private readonly ProductoService _productoService;
        private readonly DatabaseConnection _database;
        public Producto _productoSeleccionado = null;
        public ObservableCollection<RangoCodigoItem> ListaRangosAgregados { get; private set; }
        public List<VistaProductoGrid> ListaProductosExistentesEnPadre { get; set; }

        // Copia de respaldo para comparar los códigos originales antes de editar
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

            txtBuscarRangoInterno.TextChanged += (s, e) =>
            {
                string filtro = txtBuscarRangoInterno.Text.Trim().ToLower();
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

        private async Task CargarStockActualProductoAsync(int productoId)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 CAPTURAMOS EL ALMACÉN DE LA SESIÓN DEL USUARIO ACTUAL (ej. Lima = 2)
                int almacenActualId = SesionSistema.AlmacenActual?.Id ?? 1;

                // 🌟 CONSULTA DIRECTA A LA TABLA MULTI-ALMACÉN 'stock_almacen'
                string query = @"
            SELECT ISNULL(stock_actual, 0)
            FROM stock_almacen WITH (NOLOCK)
            WHERE producto_id = @productoId AND almacen_id = @almacenId";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var pProd = cmd.CreateParameter(); pProd.ParameterName = "@productoId"; pProd.Value = productoId; cmd.Parameters.Add(pProd);
                var pAlm = cmd.CreateParameter(); pAlm.ParameterName = "@almacenId"; pAlm.Value = almacenActualId; cmd.Parameters.Add(pAlm);

                object result = await cmd.ExecuteScalarAsync();
                int stockLocal = (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;

                txtStockDisponible.Text = stockLocal.ToString();
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
                PrecioUnitario = item.Detalle?.CostoUnitario,
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
            txtCUnitario.Text = (_productoSeleccionado.PrecioUnitario ?? 0m).ToString("F2");

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

                    // Guardamos una copia exacta para saber qué teníamos al iniciar la edición
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

                txtCUnitario.Text = producto.PrecioUnitario.HasValue
                    ? producto.PrecioUnitario.Value.ToString("F2")
                    : "0.00";

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
                        ListaRangosAgregados.Add(nuevoRango);
                    }

                    dgDetalleCodigos.Items.Refresh();
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

        // 🌟 CONSULTA SQL: VERIFICA SI LOS CÓDIGOS QUITADOS REGISTRAN MOVIMIENTOS POSTERIORES
        // 🌟 CONSULTA SQL CORREGIDA: SOLO DETECTA SALIDAS O DESPACHOS REALES
        private List<string> ObtenerCodigosConMovimientosPosteriores(int productoId, List<string> codigosQuitados)
        {
            var conflictos = new List<string>();
            if (codigosQuitados == null || !codigosQuitados.Any()) return conflictos;

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                foreach (string codStr in codigosQuitados)
                {
                    // 🛡️ REGLA: Un código retirado solo se considera en conflicto si:
                    // 1. Su estado_id actual es 4 (VENDIDO/DESPACHADO) o 5 (EN TRÁNSITO)
                    // 2. O si participa en un movimiento de SALIDA (tipo_movimiento_id = 2) que no esté anulado
                    string query = @"
                SELECT COUNT(*)
                FROM codigos_creados cc WITH (NOLOCK)
                LEFT JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                LEFT JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
                LEFT JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                WHERE cc.codigo = @codigoExacto
                  AND (
                      cc.estado_id IN (4, 5) -- Vendido o En Tránsito
                      OR (m.estado_id = 1 AND mp.tipo_movimiento_id = 2) -- Registrado en Salidas activas
                  )";

                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    var p = cmd.CreateParameter();
                    p.ParameterName = "@codigoExacto";
                    p.Value = codStr;
                    cmd.Parameters.Add(p);

                    object res = cmd.ExecuteScalar();
                    int count = (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;

                    if (count > 0)
                    {
                        conflictos.Add(codStr);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar trazabilidad: {ex.Message}");
            }

            return conflictos;
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

            if (!decimal.TryParse(txtCUnitario.Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal costoValido))
            {
                MessageBox.Show("Ingrese un costo unitario válido.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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

                // 🌟 BARRERA DE SEGURIDAD AL GUARDAR ÍTEM:
                // Evaluamos los códigos que estaban al inicio de la edición vs los que quedaron ahora en ListaRangosAgregados
                if (IsEdit && _rangosOriginalesEdicion.Any())
                {
                    var codigosOriginales = new HashSet<string>();
                    foreach (var r in _rangosOriginalesEdicion)
                    {
                        string separador = r.AbreviaturaBase.EndsWith("-V") || r.AbreviaturaBase.EndsWith("-G") ? "-" : (r.CategoriaProductoId == 1 ? "-G-" : "-V-");
                        for (int i = r.DesdeNum; i <= r.HastaNum; i++)
                        {
                            codigosOriginales.Add($"{r.AbreviaturaBase}{separador}{i:D7}");
                        }
                    }

                    var codigosActuales = new HashSet<string>();
                    foreach (var r in ListaRangosAgregados)
                    {
                        string separador = r.AbreviaturaBase.EndsWith("-V") || r.AbreviaturaBase.EndsWith("-G") ? "-" : (r.CategoriaProductoId == 1 ? "-G-" : "-V-");
                        for (int i = r.DesdeNum; i <= r.HastaNum; i++)
                        {
                            codigosActuales.Add($"{r.AbreviaturaBase}{separador}{i:D7}");
                        }
                    }

                    // Obtenemos únicamente los códigos que el usuario retiró al editar o fraccionar
                    var codigosQuitados = codigosOriginales.Where(c => !codigosActuales.Contains(c)).ToList();

                    if (codigosQuitados.Any())
                    {
                        // Consultamos a la BD si alguno de los retirados tiene devoluciones/movimientos posteriores
                        var conflictos = ObtenerCodigosConMovimientosPosteriores(this._productoSeleccionado.Id, codigosQuitados);

                        if (conflictos.Any())
                        {
                            MessageBox.Show(
                                $"⚠️ Operación Rechazada por Seguridad de Kárdex:\n\n" +
                                $"No se pueden quitar los siguientes códigos del producto porque ya registran reingresos o devoluciones posteriores:\n\n" +
                                $"{string.Join("\n", conflictos.Select(c => $"• {c}"))}\n\n" +
                                $"Si necesita retirar estos códigos, primero debe anular o modificar las devoluciones asociadas.",
                                "Restricción de Kárdex",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            return; // 🛑 Impide cerrar o guardar la ventana azul
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
                        ListaRangosAgregados[index] = rangoModificado;
                    }

                    dgDetalleCodigos.Items.Refresh();
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