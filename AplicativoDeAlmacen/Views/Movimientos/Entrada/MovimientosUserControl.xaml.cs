    using AplicativoDeAlmacen.Models;
    using AplicativoDeAlmacen.Models.Models;
    using AplicativoDeAlmacen.Services;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Text.RegularExpressions;

    using System.Data.Common;
    using AplicativoDeAlmacen.Data;
    using static AplicativoDeAlmacen.Data.DataConnection;

    using AplicativoDeAlmacen.Services.Ubicaciones;
    using AplicativoDeAlmacen.Views.Movimientos.Lectora;


    namespace AplicativoDeAlmacen.Views
    {
        public partial class MovimientosUserControl : UserControl
        {
            private int? _currentMovimientoId = null;
            private List<MovimientoDetalle> ListaProductosAgregados = new List<MovimientoDetalle>();
            private List<RangoCodigoItem> ListaTodosLosCodigosDelMovimiento = new List<RangoCodigoItem>();
            private readonly PersonaComercialService _service;
            private readonly IngresoMovimientoService _serviceMovimiento;
            private readonly UbicacionService _ubicacionService;

            private List<VistaProductoGrid> _productosGridList;
            private List<VistaCodigoGrid> _codigosGridList;
            private List<RangoCodigoItem> _rangosProcesadosGlobal;

            private bool _isUpdatingFromSelection = false;
            private const string SERIE_POR_DEFECTO = "0001";
            private int? _personaComercialIdSeleccionada = null;
            private const int UBICACION_ID_SELECCIONADA = 1; // ID Fijo de Almacén
            private bool _printMode = false;
            private Button _btnPrintNearSave = null;

            public MovimientosUserControl()
            {

                _productosGridList = new List<VistaProductoGrid>();
                _codigosGridList = new List<VistaCodigoGrid>();
                _rangosProcesadosGlobal = new List<RangoCodigoItem>();
                _service = new PersonaComercialService();
                _serviceMovimiento = new IngresoMovimientoService();
                _ubicacionService = new UbicacionService();

                InitializeComponent(); // ¡Primero se inicializa todo el XAML!

                ConfigurarEventosIniciales();
                EstablecerEstadoInicial();
            }

            // Fusiona entradas duplicadas en _productosGridList que tienen el mismo ProductoId.
            // Mantiene la descripción/um/costo de la primera entrada y suma las cantidades.
            private void MergeDuplicateProducts()
            {
                try
                {
                    var grouped = _productosGridList
                        .GroupBy(p => p.ProductoId)
                        .Select(g => new VistaProductoGrid
                        {
                            ProductoId = g.Key,
                            CodigoProducto = g.First().CodigoProducto,
                            Descripcion = g.First().Descripcion,
                            UnidadMedida = g.First().UnidadMedida,
                            Detalle = new MovimientoDetalle
                            {
                                Id = g.Select(x => x.Detalle?.Id ?? 0).FirstOrDefault(id => id > 0),
                                ProductoId = g.Key,
                                CantidadIngreso = g.Sum(x => x.Detalle?.CantidadIngreso ?? 0),
                                CostoUnitario = g.First().Detalle?.CostoUnitario ?? 0
                            }
                        })
                        .ToList();

                    // Reemplazar lista y reconstruir códigos basados en rangos
                    _productosGridList = grouped;

                    // Reconstruir códigos desde rangos globales para asegurar consistencia
                    RebuildCodigosGridList();
                }
                catch { }
            }

            // Completa la información de ColeccionTipo y CodigoCreadoId para las filas de _codigosGridList
            private async Task FillColeccionInfoAsync()
            {
                try
                {
                    var codesToCheck = _codigosGridList
                        .Where(c => string.IsNullOrWhiteSpace(c.ColeccionTipo) || c.MovCodigo == null || c.MovCodigo.CodigoCreadoId == 0)
                        .Select(c => c.CodigoUnique)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!codesToCheck.Any()) return;

                    // Normalizar claves
                    var normMap = codesToCheck.ToDictionary(k => _serviceMovimiento.NormalizarCodigo(k), v => v, StringComparer.OrdinalIgnoreCase);

                    using var conn = _dbConnHelper.GetConnection();
                    var dbConn = (DbConnection)conn;
                    await dbConn.OpenAsync();

                    const int batchSize = 500;
                    var found = new Dictionary<string, (int Id, string Codigo, int? Categoria, string Ano)>(StringComparer.OrdinalIgnoreCase);

                    var allNorms = normMap.Keys.ToList();
                    for (int i = 0; i < allNorms.Count; i += batchSize)
                    {
                        var batch = allNorms.Skip(i).Take(batchSize).ToList();
                        var paramNames = new List<string>();
                        for (int j = 0; j < batch.Count; j++) paramNames.Add("@p" + j);

                        string q = $@"
                            SELECT cc.id, cc.codigo, rc.categoria_producto_id, c.ano,
                                   UPPER(REPLACE(REPLACE(REPLACE(cc.codigo,' ',''),'-',''),CHAR(39),'')) AS norm
                            FROM codigos_creados cc
                            LEFT JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
                            LEFT JOIN colecciones c ON c.id = rc.coleccion_id
                            WHERE UPPER(REPLACE(REPLACE(REPLACE(cc.codigo,' ',''),'-',''),CHAR(39),'')) IN ({string.Join(',', paramNames)})";

                        using var cmd = dbConn.CreateCommand();
                        cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                        for (int j = 0; j < batch.Count; j++)
                        {
                            var p = cmd.CreateParameter(); p.ParameterName = "@p" + j; p.Value = batch[j]; cmd.Parameters.Add(p);
                        }

                        using var rdr = await cmd.ExecuteReaderAsync();
                        while (await rdr.ReadAsync())
                        {
                            int id = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
                            string codigo = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                            int? categoria = rdr.IsDBNull(2) ? (int?)null : rdr.GetInt32(2);
                            string ano = rdr.IsDBNull(3) ? string.Empty : rdr.GetValue(3)?.ToString();
                            string norm = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4);
                            if (!string.IsNullOrWhiteSpace(norm) && !found.ContainsKey(norm)) found[norm] = (id, codigo, categoria, ano);
                        }
                    }

                    // Aplicar resultados a la lista en memoria
                    foreach (var item in _codigosGridList)
                    {
                        try
                        {
                            var norm = _serviceMovimiento.NormalizarCodigo(item.CodigoUnique);
                            if (string.IsNullOrWhiteSpace(norm)) continue;
                            if (found.TryGetValue(norm, out var info))
                            {
                                if (item.MovCodigo == null) item.MovCodigo = new MovimientoCodigo();
                                if (item.MovCodigo.CodigoCreadoId == 0) item.MovCodigo.CodigoCreadoId = info.Id;
                                if (string.IsNullOrWhiteSpace(item.ColeccionTipo))
                                {
                                    if (!string.IsNullOrWhiteSpace(info.Ano))
                                    {
                                        string tipoTexto = (info.Categoria.HasValue && info.Categoria.Value == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
                                        item.ColeccionTipo = $"C{info.Ano} / {tipoTexto}";
                                    }
                                    else
                                    {
                                        item.ColeccionTipo = info.Codigo;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // Refrescar UI en hilo de Dispatcher
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        dgCodigos.ItemsSource = null;
                        dgCodigos.ItemsSource = _codigosGridList;
                    }));
                }
                catch { }
            }

            private async void BtnImportar_Click(object sender, RoutedEventArgs e)
            {
                try
                {
                    var win = new ImportarCodigos { Owner = Window.GetWindow(this) };
                    // Validamos estado 1 para ingresos nuevos
                    try { win.EstadoPermitido = (cboMotivo.SelectedValue is int mv && mv == 1) ? 1 : 4; } catch { win.EstadoPermitido = 1; }

                    if (win.ShowDialog() != true) return;

                    var listaRaw = win.CodigosImportados ?? new List<string>();
                    if (!listaRaw.Any()) return;

                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(listaRaw);

                    // Agrupar códigos validados por ProductoId
                    var codigosFisicosAgrupados = new Dictionary<int, List<string>>();

                    foreach (var raw in listaRaw)
                    {
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);

                        if (!lookup.TryGetValue(norm, out var tup) || tup.CodigoObj == null || !tup.ProductoId.HasValue)
                            continue;

                        int productoId = tup.ProductoId.Value;

                        int categoriaReal = await ObtenerCategoriaDesdeBDAsync(tup.CodigoObj.Id);

                        if (tup.CodigoObj.EstadoId != win.EstadoPermitido)
                            continue;

                        if (_codigosGridList.Any(c => string.Equals(c.CodigoUnique, tup.CodigoObj.Codigo, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Agregamos a la lista de códigos físicos
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { CodigoCreadoId = tup.CodigoObj.Id },
                            CodigoUnique = tup.CodigoObj.Codigo,
                            ProductoId = productoId,
                            ColeccionTipo = "Importado"
                        });

                        if (!codigosFisicosAgrupados.ContainsKey(productoId))
                            codigosFisicosAgrupados[productoId] = new List<string>();

                        codigosFisicosAgrupados[productoId].Add(tup.CodigoObj.Codigo);
                    }

                    var prodService = new ProductoService();

                    // 🌟 CREACIÓN AUTOMÁTICA DE PRODUCTOS Y RANGOS
                    // 🌟 CREACIÓN AUTOMÁTICA DE PRODUCTOS Y RANGOS
                    foreach (var kvp in codigosFisicosAgrupados)
                    {
                        int productoId = kvp.Key;
                        var listaDeCodigos = kvp.Value;
                        int cantidadTotal = listaDeCodigos.Count;

                        // 1. Crear o actualizar producto en grilla
                        var prodGridExistente = _productosGridList.FirstOrDefault(p => p.ProductoId == productoId);
                        if (prodGridExistente == null)
                        {
                            var prodData = await prodService.ObtenerPorIdAsync(productoId);
                            prodGridExistente = new VistaProductoGrid
                            {
                                ProductoId = productoId,
                                CodigoProducto = prodData?.Abreviatura ?? productoId.ToString(),
                                Descripcion = prodData?.Descripcion ?? "Desconocido",
                                UnidadMedida = "UNIDAD",
                                Cantidad = cantidadTotal,
                                Detalle = new MovimientoDetalle { ProductoId = productoId, CantidadIngreso = cantidadTotal, CostoUnitario = prodData?.PrecioUnitario ?? 0 }
                            };
                            _productosGridList.Add(prodGridExistente);
                        }
                        else
                        {
                            prodGridExistente.Cantidad += cantidadTotal;
                            prodGridExistente.Detalle.CantidadIngreso += cantidadTotal;
                        }

                        // 2. 🌟 DECLARACIÓN CORRECTA DE CATEGORÍA
                        // Obtenemos la categoría del primer código que encontramos para este grupo
                        int categoriaReal = await ObtenerCategoriaDesdeBDAsync(lookup.Values.First(t => t.ProductoId == productoId).CodigoObj.Id);

                        // 3. ALGORITMO PARA CREAR RANGOS (Dentro del mismo bucle para tener acceso a categoriaReal)
                        var secuencias = new List<int>();
                        string baseAbrev = prodGridExistente.CodigoProducto;

                        foreach (var codigo in listaDeCodigos)
                        {
                            string norm = _serviceMovimiento.NormalizarCodigo(codigo);
                            if (norm.Length >= 7 && int.TryParse(norm.Substring(norm.Length - 7), out int seq))
                            {
                                secuencias.Add(seq);
                                baseAbrev = norm.Substring(0, norm.Length - 7);
                            }
                        }

                        secuencias.Sort();

                        if (secuencias.Any())
                        {
                            int start = secuencias[0];
                            int end = start;

                            for (int i = 1; i < secuencias.Count; i++)
                            {
                                if (secuencias[i] == end + 1) end = secuencias[i];
                                else
                                {
                                    // Guardar bloque actual usando categoriaReal
                                    _rangosProcesadosGlobal.Add(new RangoCodigoItem
                                    {
                                        productoId = productoId,
                                        CategoriaProductoId = categoriaReal,
                                        AbreviaturaBase = baseAbrev,
                                        DesdeNum = start,
                                        HastaNum = end,
                                        Cantidad = (end - start + 1).ToString(),
                                        Desde = $"{baseAbrev}{start:D7}",
                                        Hasta = $"{baseAbrev}{end:D7}",
                                        ColeccionTipo = categoriaReal == 1 ? "LIBRO GUÍA" : "LIBRO VENTA"
                                    });
                                    start = secuencias[i]; end = start;
                                }
                            }
                            // Guardar último bloque
                            _rangosProcesadosGlobal.Add(new RangoCodigoItem
                            {
                                productoId = productoId,
                                CategoriaProductoId = categoriaReal,
                                AbreviaturaBase = baseAbrev,
                                DesdeNum = start,
                                HastaNum = end,
                                Cantidad = (end - start + 1).ToString(),
                                Desde = $"{baseAbrev}{start:D7}",
                                Hasta = $"{baseAbrev}{end:D7}",
                                ColeccionTipo = categoriaReal == 1 ? "LIBRO GUÍA" : "LIBRO VENTA"
                            });
                        }
                    }

                    dgProductos.ItemsSource = null;
                    dgProductos.ItemsSource = _productosGridList;
                    dgCodigos.ItemsSource = null;
                    dgCodigos.ItemsSource = _codigosGridList;

                    MessageBox.Show($"Importación finalizada. Se procesaron {_codigosGridList.Count} códigos correctamente agrupados en rangos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al importar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }



            private void BtnEditar_Click(object sender, RoutedEventArgs e)
            {
                // Habilitar formulario para edición y permitir que el usuario escriba el número de documento
                // Mantener campos del formulario deshabilitados hasta que se cargue el movimiento
                HabilitarCamposFormulario(false);
                grdFormulario.IsEnabled = true;
                // Solo permitimos editar el número de documento (txtNumDocumento). NO habilitar la serie.
                if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = false; txtNumDocumento.IsEnabled = true; }
                if (txtNumSerie != null) { txtNumSerie.IsReadOnly = true; txtNumSerie.IsEnabled = false; }
                txtNumDocumento.Focus();

                // Suscribir evento Enter para cargar registro
                txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
                txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

                // Aseguramos que los botones y grillas estén listos para edición
                // Bloquear los botones principales (Nuevo/Editar/Imprimir/Anular) hasta que se confirme con Enter
                GestionarBotonesPrincipales(enEdicion: true);

                // Mantener las acciones de producto y grabado deshabilitadas hasta cargar el registro
                if (dgProductos != null) dgProductos.IsEnabled = false;
                if (dgCodigos != null) dgCodigos.IsEnabled = false;
                if (btnGrabar != null) btnGrabar.IsEnabled = false;
                if (btnCancelar != null) btnCancelar.IsEnabled = true; // permitir cancelar la operación
            }

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
                        await LoadMovimientoBySerieNumeroAsync(serie, numero);
                        // Si estamos en modo imprimir, bloquear todo excepto los botones superiores Imprimir y Cancelar
                        if (_printMode)
                        {
                            // Mostrar el botón persistente de imprimir al lado de Guardar (si no existe)
                            ShowPrintButtonNearSave();

                            // Bloquear edición de grillas y controles
                            if (dgProductos != null) dgProductos.IsReadOnly = true;
                            if (dgCodigos != null) dgCodigos.IsReadOnly = true;

                            // Deshabilitar la mayoría de botones
                            if (btnAgregar != null) btnAgregar.IsEnabled = false;
                            if (btnModificar != null) btnModificar.IsEnabled = false;
                            if (btnEliminar != null) btnEliminar.IsEnabled = false;
                            if (btnImportar != null) btnImportar.IsEnabled = false;
                            if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = false;

                            // Mantener habilados sólo los botones superiores Cancelar.
                            // Bloqueamos el botón superior Imprimir para evitar reentradas desde la barra superior.
                            if (btnImprimir != null) btnImprimir.IsEnabled = false;
                            if (btnCancelar != null) btnCancelar.IsEnabled = true;

                            // Deshabilitar el botón Guardar mientras estamos en modo imprimir
                            if (btnGrabar != null) btnGrabar.IsEnabled = false;

                            // Asegurar que los campos del formulario permanezcan deshabilitados en modo impresión
                            if (dtpFechaRecepcion != null) dtpFechaRecepcion.IsEnabled = false;
                            if (cboMotivo != null) cboMotivo.IsEnabled = false;
                            if (txtRazonSocial != null) txtRazonSocial.IsEnabled = false;
                            if (txtUbicacion != null) txtUbicacion.IsEnabled = false;
                            if (txtSerieGuia != null) txtSerieGuia.IsEnabled = false;
                            if (txtNumeroGuia != null) txtNumeroGuia.IsEnabled = false;
                            if (txtObservacion != null) txtObservacion.IsEnabled = false;

                            // Cerrar popups de búsqueda si están abiertos
                            try { popupSugerencias.IsOpen = false; } catch { }
                            try { popupUbicacion.IsOpen = false; } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error al cargar movimiento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            private async Task LoadMovimientoBySerieNumeroAsync(string serie, string numero)
            {
                // Limpieza previa
                LimpiarFormulario();
                // Use IngresoMovimientoService to fetch movement data instead of embedding SQL in the UI
                var movimientoComp = await _serviceMovimiento.GetMovimientoCompletoAsync(serie, numero);
                if (movimientoComp == null)
                {
                    MessageBox.Show("No se encontró el movimiento especificado.", "No encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var movimiento = movimientoComp.Movimiento;
                _currentMovimientoId = movimiento.Id;
                if (movimiento.FechaMovimiento.HasValue) dtpFechaRecepcion.SelectedDate = movimiento.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue);
                txtNumSerie.Text = movimiento.SerieDocumento;
                txtNumDocumento.Text = movimiento.NumeroDocumento;
                cboMotivo.SelectedValue = movimiento.MotivoProductoId;
                try { CboMotivo_SelectionChanged(cboMotivo, null); } catch { }

                // Configurar UI para edición del movimiento cargado (solo si no estamos en modo impresión)
                if (!_printMode)
                {
                    // Habilitar campos generales editables
                    if (dtpFechaRecepcion != null) dtpFechaRecepcion.IsEnabled = true;
                    if (txtSerieGuia != null) txtSerieGuia.IsEnabled = true;
                    if (txtNumeroGuia != null) txtNumeroGuia.IsEnabled = true;
                    if (txtObservacion != null) txtObservacion.IsEnabled = true;

                    // Habilitar grillas para interacción/edición de productos
                    if (dgProductos != null) { dgProductos.IsEnabled = true; dgProductos.IsReadOnly = false; }
                    if (dgCodigos != null) { dgCodigos.IsEnabled = true; dgCodigos.IsReadOnly = true; }

                    // Habilitar botones de acción pertinentes
                    if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = true;
                    if (btnModificar != null) btnModificar.IsEnabled = (_productosGridList != null && _productosGridList.Count > 0);
                    if (btnEliminar != null) btnEliminar.IsEnabled = (_productosGridList != null && _productosGridList.Count > 0);
                    if (btnImportar != null) btnImportar.IsEnabled = true;
                    if (btnGrabar != null) btnGrabar.IsEnabled = true;
                    if (btnCancelar != null) btnCancelar.IsEnabled = true;

                    // Bloquear los botones principales mientras editamos
                    GestionarBotonesPrincipales(enEdicion: true);
                }
                // Razon social y descripcion ubicacion vienen en el objeto compuesto
                txtRazonSocial.Text = movimientoComp.Movimiento.PersonaComercialId.HasValue ? movimientoComp.Movimiento.PersonaComercialId.ToString() : string.Empty;
                txtCodigoRazonSocial.Text = movimientoComp.Movimiento.PersonaComercialId?.ToString() ?? string.Empty;
                txtUbicacion.Text = movimientoComp.Rangos != null ? movimientoComp.Rangos.FirstOrDefault()?.ColeccionTipo ?? string.Empty : string.Empty; // fallback
                txtSerieGuia.Text = movimiento.SerieGuia ?? string.Empty;
                txtNumeroGuia.Text = movimiento.NumeroGuia ?? string.Empty;
                txtObservacion.Text = movimiento.Observacion ?? string.Empty;

                _productosGridList.Clear();
                _codigosGridList.Clear();
                _rangosProcesadosGlobal.Clear();

                // Map detalles
                foreach (var det in movimientoComp.Detalles)
                {
                    string descripcionProducto = string.Empty;
                    try
                    {
                        using var cmdProd = _dbConnHelper.GetConnection().CreateCommand();
                        var dbCmd = (DbCommand)cmdProd;
                        dbCmd.CommandText = QueryAdapter.FormatearConsulta("SELECT descripcion FROM productos WHERE id = @id");
                        var pprod = dbCmd.CreateParameter(); pprod.ParameterName = "@id"; pprod.Value = det.ProductoId; dbCmd.Parameters.Add(pprod);
                        dbCmd.Connection.Open();
                        var res = dbCmd.ExecuteScalar();
                        dbCmd.Connection.Close();
                        if (res != null && res != DBNull.Value) descripcionProducto = res.ToString();
                    }
                    catch
                    {
                        descripcionProducto = string.Empty;
                    }

                    var vp = new VistaProductoGrid
                    {
                        ProductoId = det.ProductoId,
                        CodigoProducto = det.ProductoId.ToString(),
                        Descripcion = descripcionProducto,
                        UnidadMedida = "UNIDAD",
                        Detalle = new MovimientoDetalle { Id = det.Id, ProductoId = det.ProductoId, CantidadIngreso = det.CantidadIngreso, CostoUnitario = det.CostoUnitario }
                    };

                    // Rangos asociados
                    var rangosForDet = movimientoComp.Rangos.Where(r => r.MovimientoDetalleId == det.Id).ToList();
                    foreach (var r in rangosForDet)
                    {
                        // r ya es RangoCodigoItem, lo podemos usar directamente
                        var rango = new RangoCodigoItem
                        {
                            MovimientoDetalleId = r.MovimientoDetalleId,
                            productoId = r.productoId,
                            CategoriaProductoId = r.CategoriaProductoId,
                            AbreviaturaBase = r.AbreviaturaBase ?? string.Empty,
                            DesdeNum = r.DesdeNum,
                            HastaNum = r.HastaNum,
                            Cantidad = (r.HastaNum - r.DesdeNum + 1).ToString()
                        };

                        _rangosProcesadosGlobal.Add(rango);
                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        {
                            _codigosGridList.Add(new VistaCodigoGrid { MovCodigo = new MovimientoCodigo { MovimientoDetalleId = det.Id }, CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}", ColeccionTipo = rango.ColeccionTipo ?? string.Empty, ProductoId = rango.productoId });
                        }
                    }

                    _productosGridList.Add(vp);
                }

                // Unificar productos duplicados (mismo ProductoId) antes de mostrar
                MergeDuplicateProducts();

                // Asignar a grillas
                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
                dgCodigos.ItemsSource = null;
                dgCodigos.ItemsSource = _codigosGridList;
                // Asegurar que los campos de ColeccionTipo estén completos consultando la BD
                try { _ = FillColeccionInfoAsync(); } catch { }
            }

            private async void BtnImprimir_Click(object sender, RoutedEventArgs e)
            {
                // Si ya estamos en modo imprimir no hacemos nada (la impresión real se realiza
                // con el botón 'Imprimir Registro' que aparece junto a Guardar).
                if (_printMode) return;

                // Entrar en modo impresión: habilitar solo el campo número, esperar Enter para cargar
                _printMode = true;
                // Bloquear formulario inicialmente
                HabilitarCamposFormulario(false);
                grdFormulario.IsEnabled = true;
                // Solo permitir escribir el número de documento
                if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = false; txtNumDocumento.IsEnabled = true; txtNumDocumento.Focus(); }
                if (txtNumSerie != null) { txtNumSerie.IsReadOnly = true; txtNumSerie.IsEnabled = false; }

                // Deshabilitar la mayoría de botones hasta que se confirme el registro
                if (btnAgregar != null) btnAgregar.IsEnabled = false;
                if (btnModificar != null) btnModificar.IsEnabled = false;
                if (btnEliminar != null) btnEliminar.IsEnabled = false;
                if (btnImportar != null) btnImportar.IsEnabled = false;
                if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = false;

                // Mantener habilitados los botones superiores Imprimir y Cancelar para permitir la operación
                if (btnImprimir != null) btnImprimir.IsEnabled = true;
                if (btnCancelar != null) btnCancelar.IsEnabled = true;

                // Aseguramos que la tecla Enter al cargar dispare la carga (ya conectado en editar)
                txtNumDocumento.KeyDown -= TxtNumDocumento_KeyDown;
                txtNumDocumento.KeyDown += TxtNumDocumento_KeyDown;

                // Informar brevemente al usuario
                MessageBox.Show("Modo Imprimir: ingrese el número de documento y presione Enter. Se bloqueará la UI excepto Imprimir y Cancelar.", "Imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            private void GenerateExcelFromCurrentLoadedMovement()
            {
                try
                {
                    // Usar ClosedXML para generar reporte con estilo similar al ejemplo
                    using var wb = new ClosedXML.Excel.XLWorkbook();
                    var ws = wb.Worksheets.Add("Ingreso de Productos");

                    // Título
                    ws.Range("A1:E1").Merge();
                    ws.Cell(1, 1).Value = "INGRESO DE PRODUCTOS - ALMACEN CENTRAL";
                    ws.Cell(1, 1).Style.Font.Bold = true;
                    ws.Cell(1, 1).Style.Font.FontSize = 14;
                    ws.Cell(1, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    // Cabecera de datos (key/value) con bordes
                    int r = 3;
                    void PutKV(string key, string val)
                    {
                        ws.Cell(r, 1).Value = key;
                        ws.Cell(r, 2).Value = val;
                        var rng = ws.Range(r, 1, r, 5);
                        rng.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                        rng.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
                        r++;
                    }

                    PutKV("# Registro", $"{txtNumSerie.Text}-{txtNumDocumento.Text}");
                    PutKV("Fecha", dtpFechaRecepcion.SelectedDate?.ToString("dd/MM/yyyy") ?? "");
                    PutKV("Motivo", (cboMotivo.SelectedItem as dynamic)?.Descripcion ?? "");
                    PutKV("Razón Social", txtRazonSocial.Text);
                    PutKV("Dirección", txtDireccion.Text);
                    PutKV("Ubicación", txtUbicacion.Text);
                    PutKV("# Guía", $"{txtSerieGuia.Text}-{txtNumeroGuia.Text}");
                    PutKV("Observación", txtObservacion.Text);

                    // Encabezado tabla productos
                    int headerRow = r + 1;
                    ws.Cell(headerRow, 1).Value = "Producto";
                    ws.Cell(headerRow, 2).Value = "U. Medida";
                    ws.Cell(headerRow, 3).Value = "Cantidad";
                    ws.Cell(headerRow, 4).Value = "C. Unitario";
                    ws.Range(headerRow, 1, headerRow, 4).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FFE699");
                    ws.Range(headerRow, 1, headerRow, 4).Style.Font.Bold = true;
                    ws.Range(headerRow, 1, headerRow, 4).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                    ws.Range(headerRow, 1, headerRow, 4).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;

                    int fila = headerRow + 1;

                    foreach (var p in _productosGridList)
                    {
                        // Producto principal en una fila
                        ws.Cell(fila, 1).Value = p.Descripcion ?? p.CodigoProducto;
                        ws.Cell(fila, 2).Value = p.UnidadMedida;
                        ws.Cell(fila, 3).Value = p.Detalle?.CantidadIngreso ?? 0;
                        ws.Cell(fila, 4).Value = p.Detalle?.CostoUnitario ?? 0;
                        ws.Range(fila, 1, fila, 4).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                        fila++;

                        // Listado de códigos individuales para este producto
                        var codigos = _codigosGridList.Where(c => c.ProductoId == p.ProductoId).ToList();
                        // Si no se llenó _codigosGridList por alguna razón, intentar generar desde rangos
                        if ((codigos == null || codigos.Count == 0) && _rangosProcesadosGlobal != null)
                        {
                            var rangosFallback = _rangosProcesadosGlobal.Where(rg => rg.productoId == p.ProductoId).ToList();
                            foreach (var rg in rangosFallback)
                            {
                                for (int seq = rg.DesdeNum; seq <= rg.HastaNum; seq++)
                                {
                                    codigos.Add(new VistaCodigoGrid { CodigoUnique = $"{rg.AbreviaturaBase}-{seq:D7}", ColeccionTipo = rg.ColeccionTipo, ProductoId = rg.productoId });
                                }
                            }
                        }

                        foreach (var code in codigos)
                        {
                            ws.Cell(fila, 1).Value = ""; // dejar primera columna vacía para códigos (alinear debajo del producto)
                            ws.Cell(fila, 2).Value = code.CodigoUnique;
                            ws.Cell(fila, 3).Value = code.ColeccionTipo ?? string.Empty;
                            // estilo para códigos
                            ws.Range(fila, 2, fila, 3).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#333333");
                            fila++;
                        }

                        // Espacio entre productos
                        fila++;
                    }

                    // Ajustes finales
                    ws.Columns(1, 4).AdjustToContents();
                    ws.Rows().AdjustToContents();

                    // Establecer anchos y alturas similares a ejemplo
                    ws.Column(1).Width = 70; // Producto
                    ws.Column(2).Width = 15; // U. Medida
                    ws.Column(3).Width = 12; // Cantidad
                    ws.Column(4).Width = 14; // C. Unitario

                    // Guardar archivo temporal
                    string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"IngresoProductos_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    wb.SaveAs(ruta);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generando Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private void ShowPrintButtonNearSave()
            {
                try
                {
                    if (_btnPrintNearSave != null) return; // ya creado

                    // Encontrar el panel que contiene btnGrabar (asumimos StackPanel)
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
                        // CLICK: abrir el Excel, pero NO salir del modo impresión ni borrar la información.
                        _btnPrintNearSave.Click += (s, e) =>
                        {
                            try
                            {
                                // Evitar múltiples clicks que puedan cambiar estados o generar trabajo duplicado
                                _btnPrintNearSave.IsEnabled = false;
                                GenerateExcelFromCurrentLoadedMovement();
                            }
                            catch
                            {
                                // ignorar excepciones aquí para no romper el modo impresión
                            }
                            // no llamamos a CleanupPrintMode() aquí; el modo impresión permanece hasta Cancelar
                        };

                        parentPanel.Children.Insert(parentPanel.Children.IndexOf(btnGrabar) + 1, _btnPrintNearSave);
                        // Aseguramos que el botón Guardar quede deshabilitado mientras el botón persistente exista
                        if (btnGrabar != null) btnGrabar.IsEnabled = false;
                    }
                }
                catch
                {
                    // no crítico
                }
            }

            private void CleanupPrintMode()
            {
                _printMode = false;
                if (_btnPrintNearSave != null)
                {
                    if (_btnPrintNearSave.Parent is Panel p) p.Children.Remove(_btnPrintNearSave);
                    _btnPrintNearSave = null;
                }

                // Restablecer estado de campos
                HabilitarCamposFormulario(false);
                // Restaurar botones al estado inicial
                ApplyPrintModeButtonStates(enable: false);
                EstablecerEstadoInicial();

                // Restaurar cursor si fue modificado
                if (Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;
            }

            private void ApplyPrintModeButtonStates(bool enable)
            {
                // Cuando enable = true significa que estamos en modo imprimir y queremos "desbloquear"
                // la mayoría de botones para visualizar/editar, excepto el botón Imprimir superior
                // y el botón Cancelar (según la solicitud). Cuando enable = false revertimos.

                try
                {
                    if (enable)
                    {
                        // Habilitar acciones en el formulario
                        if (btnAgregar != null) btnAgregar.IsEnabled = true;
                        if (btnModificar != null) btnModificar.IsEnabled = true;
                        if (btnEliminar != null) btnEliminar.IsEnabled = true;
                        if (btnImportar != null) btnImportar.IsEnabled = true;
                        if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = true;
                        if (btnModificar != null) btnModificar.IsEnabled = true;

                        // Deshabilitar el botón superior Imprimir (no queremos que se vuelva a pulsar)
                        if (btnImprimir != null) btnImprimir.IsEnabled = false;

                        // Deshabilitar Cancelar según request
                        if (btnCancelar != null) btnCancelar.IsEnabled = false;
                    }
                    else
                    {
                        // Revertir a estado por defecto (usar EstablecerEstadoInicial para consistencia)
                        // Aquí solo intentamos dejar botones en estado no-imprimir
                        if (btnAgregar != null) btnAgregar.IsEnabled = true;
                        if (btnModificar != null) btnModificar.IsEnabled = true;
                        if (btnEliminar != null) btnEliminar.IsEnabled = true;
                        if (btnImportar != null) btnImportar.IsEnabled = true;
                        if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = false;
                        if (btnImprimir != null) btnImprimir.IsEnabled = true;
                        if (btnCancelar != null) btnCancelar.IsEnabled = true;
                    }
                }
                catch
                {
                    // No crítico
                }
            }

            private string ShowInputDialog(string text, string caption)
            {
                var win = new Window
                {
                    Title = caption,
                    Width = 480,
                    Height = 140,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = Window.GetWindow(this)
                };

                var panel = new StackPanel { Margin = new Thickness(10) };
                panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 8) });
                var txt = new TextBox { Height = 28 };
                panel.Children.Add(txt);
                var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
                var cancel = new Button { Content = "Cancelar", Width = 80, IsCancel = true };
                btns.Children.Add(ok);
                btns.Children.Add(cancel);
                panel.Children.Add(btns);

                string result = null;
                ok.Click += (s, e) => { result = txt.Text; win.DialogResult = true; };
                win.Content = panel;
                if (win.ShowDialog() == true) return result;
                return null;
            }

            // Helper para acceso a BD
            private readonly DatabaseConnection _dbConnHelper = new DatabaseConnection();
            private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
            {
                // Permitir solo dígitos
                e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
            }

            private void OnPasteNumeric(object sender, DataObjectPastingEventArgs e)
            {
                if (e.DataObject.GetDataPresent(typeof(string)))
                {
                    var text = (string)e.DataObject.GetData(typeof(string));
                    if (!Regex.IsMatch(text, "^\\d+$"))
                    {
                        e.CancelCommand();
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            }

            public void ConfigurarEventosIniciales()
            {
                // Primero limpiamos cualquier asignación previa por seguridad
                txtRazonSocial.TextChanged -= TxtRazonSocial_TextChanged;
                lstSugerencias.SelectionChanged -= LstSugerencias_SelectionChanged;
                cboMotivo.SelectionChanged -= CboMotivo_SelectionChanged;
                this.PreviewMouseDown -= MovimientosUserControl_PreviewMouseDown;
                Loaded -= MovimientosUserControl_Loaded;
                btnAgregar.Click -= BtnAgregar_Click;
                btnAgregarProducto.Click -= BtnAgregarItem_Click;
                btnCancelar.Click -= BtnCancelar_Click;
                btnGrabar.Click -= RegistrarMovimientoCompleto;
                // 🔥 COLÓCALO AQUÍ ABAJO (Para des-registrar con seguridad):
                dgProductos.SelectionChanged -= DgProductos_SelectionChanged;

                // Ahora los asignamos con la certeza de que serán únicos
                txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
                lstSugerencias.SelectionChanged += LstSugerencias_SelectionChanged;
                cboMotivo.SelectionChanged += CboMotivo_SelectionChanged;
                lstSugerenciasUbicacion.SelectionChanged += LstSugerenciasUbicacion_SelectionChanged;
                this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
                this.PreviewMouseMove -= MovimientosUserControl_PreviewMouseMove;
                this.PreviewMouseMove += MovimientosUserControl_PreviewMouseMove;
                Loaded += MovimientosUserControl_Loaded;
                // Intercept mouse to block actions en modo impresión
                this.PreviewMouseDown -= MovimientosUserControl_PreviewMouseDown;
                this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
                btnAgregar.Click += BtnAgregar_Click;
                btnEditar.Click += BtnEditar_Click;
                btnAgregarProducto.Click += BtnAgregarItem_Click;
                btnModificar.Click -= BtnModificar_Click;
                btnEliminar.Click -= BtnEliminar_Click;
                btnModificar.Click += BtnModificar_Click;
                btnEliminar.Click += BtnEliminar_Click;
                btnCancelar.Click += BtnCancelar_Click;
                btnGrabar.Click += RegistrarMovimientoCompleto;
                btnImprimir.Click += BtnImprimir_Click;
                btnImportar.Click += BtnImportar_Click;
                // 🔥 COLÓCALO AQUÍ ABAJO (Para registrar el evento):
                dgProductos.SelectionChanged += DgProductos_SelectionChanged;
                dgProductos.MouseDoubleClick -= DgProductos_MouseDoubleClick;
                dgProductos.MouseDoubleClick += DgProductos_MouseDoubleClick;

                // Manejar rueda del ratón para que el DataGrid interno haga scroll cuando hay un ScrollViewer padre
                if (dgProductos != null)
                {
                    dgProductos.PreviewMouseWheel -= DataGrid_PreviewMouseWheel;
                    dgProductos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
                }

                if (dgCodigos != null)
                {
                    dgCodigos.PreviewMouseWheel -= DataGrid_PreviewMouseWheel;
                    dgCodigos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
                }

            }

            private void RebuildCodigosGridList()
            {
                _codigosGridList.Clear();
                int contadorFila = 1;

                foreach (var producto in _productosGridList)
                {
                    var rangosProducto = _rangosProcesadosGlobal.Where(r => r.productoId == producto.ProductoId).ToList();
                    foreach (var rango in rangosProducto)
                    {
                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        {
                            _codigosGridList.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                ColeccionTipo = rango.ColeccionTipo,
                                ProductoId = producto.ProductoId
                            });
                        }
                    }
                }
                // Actualizar cantidades de productos basadas en los códigos reconstruidos
                try
                {
                    var counts = _codigosGridList.GroupBy(c => c.ProductoId).ToDictionary(g => g.Key, g => g.Count());
                    foreach (var p in _productosGridList)
                    {
                        if (counts.TryGetValue(p.ProductoId, out int cnt))
                        {
                            p.Detalle = p.Detalle ?? new MovimientoDetalle { ProductoId = p.ProductoId };
                            p.Detalle.CantidadIngreso = Convert.ToDecimal(cnt);
                        }
                    }

                    // NOTE: do not call MergeDuplicateProducts() here to avoid recursion
                    // MergeDuplicateProducts is invoked explicitly where needed (e.g. on load or after edits).
                }
                catch { }
            }

            private async void BtnModificar_Click(object sender, RoutedEventArgs e)
            {
                await EditSelectedProductAsync();
            }

            private async void DgProductos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
                // Ignorar si doble click se produce fuera de una fila
                if (dgProductos.SelectedItem is VistaProductoGrid)
                {
                    await EditSelectedProductAsync();
                }
            }

            private async Task EditSelectedProductAsync()
            {
                if (dgProductos.SelectedItem is not VistaProductoGrid seleccionado) return;

                List<RangoCodigoItem> rangosExistentes = null;

                // Si el movimiento está guardado en BD, obtener rangos desde la BD usando movimiento_detalle_id
                try
                {
                    if (_currentMovimientoId.HasValue && seleccionado.Detalle != null && seleccionado.Detalle.Id > 0)
                    {
                        rangosExistentes = await _serviceMovimiento.GetRangosByMovimientoDetalleIdAsync(seleccionado.Detalle.Id);
                    }
                }
                catch
                {
                    // ignore DB fetch errors y fallback
                    rangosExistentes = null;
                }

                // Si no obtuvimos nada desde BD, usamos los rangos procesados en memoria (nuevos movimientos)
                if (rangosExistentes == null || rangosExistentes.Count == 0)
                {
                    rangosExistentes = _rangosProcesadosGlobal.Where(r => r.productoId == seleccionado.ProductoId).ToList();
                }

                var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
                // Propagar estado permitido según el motivo seleccionado (1 = COMPRA, otro = 4)
                modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
                modal.ListaProductosExistentesEnPadre = _productosGridList;
                modal.InitializeForEdit(seleccionado, rangosExistentes);

                if (modal.ShowDialog() == true && modal.FueGrabado)
                {
                    // Actualizar detalle del producto
                    seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                    seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                    // Reemplazar rangos globales para este producto
                    _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);
                    foreach (var r in modal.ListaRangosAgregados)
                    {
                        r.productoId = seleccionado.ProductoId;
                        _rangosProcesadosGlobal.Add(r);
                    }

                    // Reconstruir la lista de códigos y refrescar UI
                    RebuildCodigosGridList();
                    dgProductos.ItemsSource = null;
                    dgProductos.ItemsSource = _productosGridList;
                    dgProductos.SelectedItem = seleccionado;
                    DgProductos_SelectionChanged(dgProductos, new SelectionChangedEventArgs(DataGrid.SelectionChangedEvent, new List<object>(), new List<object>()));
                }
            }

            private void BtnEliminar_Click(object sender, RoutedEventArgs e)
            {
                if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
                {
                    var confirmacion = MessageBox.Show($"¿Está seguro de eliminar el producto \"{productoSeleccionado.Descripcion}\" y todos sus códigos asociados?",
                                                       "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirmacion == MessageBoxResult.Yes)
                    {
                        // 1. Borramos el producto de la grilla principal
                        _productosGridList.Remove(productoSeleccionado);

                        // 2. Borramos todos los códigos físicos asociados a este producto
                        var codigosAEliminar = _codigosGridList.Where(c => c.ProductoId == productoSeleccionado.ProductoId).ToList();
                        foreach (var c in codigosAEliminar)
                        {
                            _codigosGridList.Remove(c);
                        }

                        // 3. Borramos todos los rangos asociados a este producto
                        var rangosAEliminar = _rangosProcesadosGlobal.Where(r => r.productoId == productoSeleccionado.ProductoId).ToList();
                        foreach (var r in rangosAEliminar)
                        {
                            _rangosProcesadosGlobal.Remove(r);
                        }

                        // Refrescamos las grillas
                        dgProductos.ItemsSource = null;
                        dgProductos.ItemsSource = _productosGridList;
                        dgCodigos.ItemsSource = null;
                        dgCodigos.ItemsSource = _codigosGridList;
                    }
                }
                else
                {
                    MessageBox.Show("Debe seleccionar un producto de la lista para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            private void CboMotivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                if (cboMotivo.SelectedItem == null)
                    return;

                // Obtén la descripción del motivo
                dynamic motivo = cboMotivo.SelectedItem;
                string descripcion = motivo.Descripcion?.Trim().ToUpper() ?? "";

                // Primero deshabilitamos todo
                txtRazonSocial.IsEnabled = false;
                txtUbicacion.IsEnabled = false;

                // Limpiamos valores opcionalmente
                txtRazonSocial.Clear();
                txtCodigoRazonSocial.Clear();
                txtDireccion.Clear();

                txtUbicacion.Clear();
                txtCodigoUbicacion.Clear();
                txtDireccionUbicacion.Clear();

                switch (descripcion)
                {
                    case "COMPRA":
                        txtRazonSocial.IsEnabled = true;
                        break;

                    case "DEVOLUCION RECIBIDA":
                        txtRazonSocial.IsEnabled = true;
                        break;

                    case "OTROS":
                        txtRazonSocial.IsEnabled = true;
                        txtUbicacion.IsEnabled = true;
                        break;

                    case "PROMOCION/PROMOTORIA":
                        // En PROMOCION/PROMOTORIA solo se habilita la ubicación (no razón social)
                        txtRazonSocial.IsEnabled = false;
                        txtUbicacion.IsEnabled = true;
                        break;

                    case "TRANSFERENCIA ENTRE ALMACENES":
                        txtUbicacion.IsEnabled = true;
                        break;
                }
            }
            private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
            {
                if (!txtRazonSocial.IsEnabled)
                    return;

                if (_isUpdatingFromSelection)
                    return;


                if (_isUpdatingFromSelection) return;
                string textoBusqueda = txtRazonSocial.Text.Trim();

                if (textoBusqueda.Length >= 2)
                {
                    try
                    {
                        List<PersonaComercial> sugerencias = await _service.BuscarPorRazonSocialAsync(textoBusqueda);
                        if (sugerencias != null && sugerencias.Count > 0)
                        {
                            lstSugerencias.ItemsSource = sugerencias;
                            popupSugerencias.IsOpen = true;
                        }
                        else
                        {
                            popupSugerencias.IsOpen = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error al consultar sugerencias: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    popupSugerencias.IsOpen = false;
                }
            }

            private void LstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                if (lstSugerencias.SelectedItem is PersonaComercial personaSeleccionada)
                {
                    _isUpdatingFromSelection = true;

                    _personaComercialIdSeleccionada = personaSeleccionada.Id;
                    txtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                        ? personaSeleccionada.RazonSocial
                        : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";

                    txtCodigoRazonSocial.Text = personaSeleccionada.Id.ToString("D6");
                    txtDireccion.Text = personaSeleccionada.Direccion ?? "Sin dirección registrada";

                    popupSugerencias.IsOpen = false;
                    lstSugerencias.SelectedIndex = -1;

                    _isUpdatingFromSelection = false;
                }
            }

            private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
            {
                if (!txtUbicacion.IsEnabled)
                    return;


                string busqueda = txtUbicacion.Text;

                if (string.IsNullOrWhiteSpace(busqueda))
                {
                    popupUbicacion.IsOpen = false;
                    return;
                }

                // Aquí llamas a tu servicio de base de datos
                // Supongamos que tienes un método 'BuscarUbicaciones(string criterio)'
                var resultados = _ubicacionService.BuscarUbicaciones(busqueda);

                if (resultados != null && resultados.Count > 0)
                {
                    lstSugerenciasUbicacion.ItemsSource = resultados;
                    popupUbicacion.IsOpen = true;
                }
                else
                {
                    popupUbicacion.IsOpen = false;
                }
            }

            private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado)
                {
                    txtUbicacion.Text = itemSeleccionado.Descripcion;
                    txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString(); // Autocompleta el código
                    popupUbicacion.IsOpen = false;
                }
            }

            private void MovimientosUserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            {
                // Si estamos en modo imprimir, interceptamos clicks en botones y bloqueamos todo excepto Imprimir y Cancelar
                if (_printMode)
                {
                    var dep = e.OriginalSource as DependencyObject;
                    while (dep != null)
                    {
                        if (dep is Button btn)
                        {
                            // permitir acciones en botones: superior Imprimir, Cancelar y el botón persistente junto a Guardar
                            if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave)
                            {
                                // permitir estas acciones
                                break;
                            }

                            // Bloquear cualquier otro botón silenciosamente
                            e.Handled = true;
                            return;
                        }
                        dep = VisualTreeHelper.GetParent(dep);
                    }
                }

                // Comportamiento original: cerrar popup de sugerencias si se clickea fuera
                if (!txtRazonSocial.IsMouseOver && !popupSugerencias.IsMouseOver)
                {
                    popupSugerencias.IsOpen = false;
                }
            }

            private void MovimientosUserControl_PreviewMouseMove(object sender, MouseEventArgs e)
            {
                // Durante modo imprimir, cuando el cursor está sobre botones no permitidos
                // queremos mostrar cursor por defecto (no mano) para indicar que no se puede clicar.
                if (!_printMode)
                {
                    if (Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;
                    return;
                }

                var dep = e.OriginalSource as DependencyObject;
                bool overBlockedButton = false;

                while (dep != null)
                {
                    if (dep is Button btn)
                    {
                        // Allow pointer for top Imprimir, Cancelar and the side persistent print button
                        if (btn == btnImprimir || btn == btnCancelar || btn == _btnPrintNearSave)
                        {
                            overBlockedButton = false;
                            break;
                        }
                        overBlockedButton = true;
                        break;
                    }
                    dep = VisualTreeHelper.GetParent(dep);
                }

                if (overBlockedButton)
                {
                    Mouse.OverrideCursor = Cursors.Arrow; // cursor normal
                }
                else
                {
                    Mouse.OverrideCursor = null; // restaurar
                }
            }

            private async void MovimientosUserControl_Loaded(object sender, RoutedEventArgs e)
            {
                // Configurar virtualización y columnas adaptables antes de cargar datos
                ConfigurarDataGridsParaVirtualizacion();

                await CargarMotivosAsync();
            }

            private void ConfigurarDataGridsParaVirtualizacion()
            {
                // Habilitar virtualización y reciclaje para evitar instanciar todos los elementos visuales
                if (dgCodigos != null)
                {
                    VirtualizingPanel.SetIsVirtualizing(dgCodigos, true);
                    VirtualizingPanel.SetVirtualizationMode(dgCodigos, VirtualizationMode.Recycling);
                    dgCodigos.EnableRowVirtualization = true;
                    dgCodigos.EnableColumnVirtualization = false;
                    ScrollViewer.SetCanContentScroll(dgCodigos, true);
                }

                if (dgProductos != null)
                {
                    VirtualizingPanel.SetIsVirtualizing(dgProductos, true);
                    VirtualizingPanel.SetVirtualizationMode(dgProductos, VirtualizationMode.Recycling);
                    dgProductos.EnableRowVirtualization = true;
                    dgProductos.EnableColumnVirtualization = false;
                    ScrollViewer.SetCanContentScroll(dgProductos, true);
                    // Si no existen columnas definidas en XAML, crear columnas por código.
                    if (dgProductos.Columns == null || dgProductos.Columns.Count == 0)
                    {
                        dgProductos.AutoGenerateColumns = false;

                        dgProductos.Columns.Add(new DataGridTextColumn
                        {
                            Header = "N°",
                            Binding = new System.Windows.Data.Binding("ProductoId"),
                            Width = new DataGridLength(60)
                        });

                        dgProductos.Columns.Add(new DataGridTextColumn
                        {
                            Header = "CÓDIGO",
                            Binding = new System.Windows.Data.Binding("CodigoProducto"),
                            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                        });

                        dgProductos.Columns.Add(new DataGridTextColumn
                        {
                            Header = "DESCRIPCIÓN",
                            Binding = new System.Windows.Data.Binding("Descripcion"),
                            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
                        });

                        dgProductos.Columns.Add(new DataGridTextColumn
                        {
                            Header = "CANT.",
                            Binding = new System.Windows.Data.Binding("Detalle.CantidadIngreso") { StringFormat = "N2" },
                            Width = new DataGridLength(0.7, DataGridLengthUnitType.Star)
                        });

                        dgProductos.Columns.Add(new DataGridTextColumn
                        {
                            Header = "COSTO UNIT.",
                            Binding = new System.Windows.Data.Binding("Detalle.CostoUnitario") { StringFormat = "N2" },
                            Width = new DataGridLength(0.9, DataGridLengthUnitType.Star)
                        });
                    }
                }
            }

            private async Task CargarMotivosAsync()
            {
                try
                {
                    this.Cursor = Cursors.Wait;
                    cboMotivo.ItemsSource = await _serviceMovimiento.ObtenerMotivosProductosAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cargar los motivos de productos: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }

            private async void BtnAgregar_Click(object sender, RoutedEventArgs e)
            {
                try
                {
                    this.Cursor = Cursors.Wait;
                    LimpiarFormulario();

                    // No generamos correlativo en este punto para evitar condiciones de carrera.
                    // Mostramos el número de registro (visible) en modo lectura para que el usuario lo vea,
                    // pero no le permitimos editarlo hasta que el correlativo se genere.
                    txtNumSerie.Text = string.Empty;
                    txtNumDocumento.Text = string.Empty;
                    txtNumSerie.Visibility = Visibility.Visible;
                    txtNumDocumento.Visibility = Visibility.Visible;
                    if (txtNumSerie != null) { txtNumSerie.IsReadOnly = true; txtNumSerie.IsEnabled = false; }
                    if (txtNumDocumento != null) { txtNumDocumento.IsReadOnly = true; txtNumDocumento.IsEnabled = false; }

                    dtpFechaRecepcion.SelectedDate = DateTime.Today;

                    // NO habilitamos campos dependientes del motivo (razón social / ubicación) aquí.
                    // Mantenerlos deshabilitados hasta que el usuario seleccione un motivo y
                    // CboMotivo_SelectionChanged aplique las reglas específicas.
                    HabilitarCamposFormulario(false);

                    // Habilitar elementos imprescindibles para iniciar el ingreso:
                    if (dtpFechaRecepcion != null) dtpFechaRecepcion.IsEnabled = true;
                    if (cboMotivo != null) cboMotivo.IsEnabled = true;
                    if (txtObservacion != null) txtObservacion.IsEnabled = true;
                    if (txtSerieGuia != null) txtSerieGuia.IsEnabled = true;
                    if (txtNumeroGuia != null) txtNumeroGuia.IsEnabled = true;

                    // Habilitar acciones relacionadas a agregar productos/importar y control de guardado
                    if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = true;
                    if (btnImportar != null) btnImportar.IsEnabled = true;
                    if (btnGrabar != null) btnGrabar.IsEnabled = true;
                    if (btnCancelar != null) btnCancelar.IsEnabled = true;

                    // Permitir editar/seleccionar filas en la grilla de productos mientras se registra un movimiento
                    if (dgProductos != null) { dgProductos.IsEnabled = true; dgProductos.IsReadOnly = false; }
                    // Permitir seleccionar códigos en la grilla derecha
                    if (dgCodigos != null) { dgCodigos.IsEnabled = true; dgCodigos.IsReadOnly = true; }

                    // Bloquear botones principales (Nuevo/Editar/Imprimir/Anular)
                    GestionarBotonesPrincipales(enEdicion: true);

                    // Intentar preseleccionar COMPRA por defecto y dejar el foco en el combo motivo
                    try { cboMotivo.SelectedValue = 1; } catch { }
                    cboMotivo.Focus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al inicializar el nuevo registro: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }

            private void BtnCancelar_Click(object sender, RoutedEventArgs e)
            {
                MessageBoxResult resultado = MessageBox.Show("¿Está seguro que desea cancelar la operación actual? Se perderán los datos no guardados.",
                                                             "Confirmar Cancelación", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (resultado == MessageBoxResult.Yes)
                {
                    // Si estábamos en modo impresión, limpiamos ese modo primero
                    if (_printMode)
                    {
                        try { CleanupPrintMode(); }
                        catch { _printMode = false; }
                    }

                    // Limpiar y restaurar estado por defecto
                    LimpiarFormulario();
                    HabilitarCamposFormulario(false);
                    GestionarBotonesPrincipales(enEdicion: false);

                    // Aseguramos explícitamente que los botones superiores queden habilitados
                    if (btnAgregar != null) btnAgregar.IsEnabled = true;
                    if (btnEditar != null) btnEditar.IsEnabled = true;
                    if (btnImprimir != null) btnImprimir.IsEnabled = true;
                    if (btnAnular != null) btnAnular.IsEnabled = true;

                    // El botón cancelar puede quedar deshabilitado hasta que se entre en un flujo
                    if (btnCancelar != null) btnCancelar.IsEnabled = false;
                }
            }

            private async void RegistrarMovimientoCompleto(object sender, RoutedEventArgs e)
            {
                // 1. Validaciones iniciales
                if (_productosGridList == null || _productosGridList.Count == 0)
                {
                    MessageBox.Show("Debe agregar al menos un producto a la lista antes de guardar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (cboMotivo.SelectedValue == null)
                {
                    MessageBox.Show("Por favor, seleccione el motivo del movimiento.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // 2. APAGAR EL BOTÓN INMEDIATAMENTE para evitar el doble envío
                    btnGrabar.IsEnabled = false;
                    this.Cursor = Cursors.Wait;

                    Movimiento nuevaCabecera = new Movimiento
                    {
                        FechaMovimiento = dtpFechaRecepcion.SelectedDate != null ? DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Today),
                        SerieDocumento = txtNumSerie.Text.Trim(),
                        NumeroDocumento = txtNumDocumento.Text.Trim(),
                        MotivoProductoId = Convert.ToInt32(cboMotivo.SelectedValue),
                        UbicacionId = UBICACION_ID_SELECCIONADA,
                        UsuarioId = 1,
                        PersonaComercialId = _personaComercialIdSeleccionada,
                        SerieGuia = txtSerieGuia.Text.Trim(),
                        NumeroGuia = txtNumeroGuia.Text.Trim(),
                        Observacion = txtObservacion.Text.Trim()
                    };

                    // PRE-VALIDACIÓN DE CÓDIGOS (consistente con la validación en el servicio)
                    // Recolectar códigos tal como aparecen en la grilla final y desde los rangos procesados
                    var allCodesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // códigos explícitos en la lista de códigos
                    foreach (var c in _codigosGridList)
                    {
                        if (!string.IsNullOrWhiteSpace(c.CodigoUnique)) allCodesSet.Add(_serviceMovimiento.NormalizarCodigo(c.CodigoUnique));
                    }
                    // expandir rangos procesados (por si hay rangos que no están representados en _codigosGridList)
                    foreach (var rg in _rangosProcesadosGlobal)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(rg.AbreviaturaBase)) continue;
                            for (int seq = rg.DesdeNum; seq <= rg.HastaNum; seq++)
                            {
                                var code = $"{rg.AbreviaturaBase}-{seq:D7}";
                                allCodesSet.Add(_serviceMovimiento.NormalizarCodigo(code));
                            }
                        }
                        catch { }
                    }

                    var allCodes = allCodesSet.ToList();

                    // Mapear contra la BD
                    var lookupMap = await _serviceMovimiento.ObtenerCodigosPorListaAsync(allCodes);

                    int estadoPermitido = (cboMotivo.SelectedValue is int mvv && mvv == 1) ? 1 : 4;

                    var codigosFaltantes = new List<string>();
                    var codigosInvalidos = new List<string>();

                    // Si estamos editando, obtener ids ya asociados al movimiento para permitirlos aun si estado difiere
                    var existingCodigoIds = new System.Collections.Generic.HashSet<int>();
                    if (_currentMovimientoId.HasValue)
                    {
                        try
                        {
                            using var connExist = _dbConnHelper.GetConnection();
                            var dbConnExist = (System.Data.Common.DbConnection)connExist;
                            await dbConnExist.OpenAsync();
                            using var cmdExist = dbConnExist.CreateCommand();
                            cmdExist.CommandText = QueryAdapter.FormatearConsulta(@"SELECT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId");
                            var p = cmdExist.CreateParameter(); p.ParameterName = "@movId"; p.Value = _currentMovimientoId.Value; cmdExist.Parameters.Add(p);
                            using var rdrExist = await cmdExist.ExecuteReaderAsync();
                            while (await rdrExist.ReadAsync())
                            {
                                if (!rdrExist.IsDBNull(0)) existingCodigoIds.Add(rdrExist.GetInt32(0));
                            }
                        }
                        catch
                        {
                            // si falla la consulta, continuamos sin excepcionar; no permitiremos excepciones de validación aquí
                            existingCodigoIds.Clear();
                        }
                    }

                    foreach (var code in allCodes)
                    {
                        var key = _serviceMovimiento.NormalizarCodigo(code);
                        if (!lookupMap.TryGetValue(key, out var tup) || tup.CodigoObj == null)
                        {
                            codigosFaltantes.Add(code);
                            continue;
                        }

                        var codigoObj = tup.CodigoObj;
                        if (existingCodigoIds.Contains(codigoObj.Id))
                        {
                            // permitir código ya asociado
                            continue;
                        }

                        if (codigoObj.EstadoId != estadoPermitido)
                        {
                            codigosInvalidos.Add(code + $" (estado:{codigoObj.EstadoId})");
                        }
                    }

                    // Si hay problemas, preguntar al usuario cómo proceder
                    if (codigosFaltantes.Any() || codigosInvalidos.Any())
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("Validación previa de códigos:");
                        if (codigosInvalidos.Any())
                        {
                            sb.AppendLine($"Códigos con estado inválido (se requiere estado {estadoPermitido}): {codigosInvalidos.Count}");
                            foreach (var s in codigosInvalidos.Take(200)) sb.AppendLine(" - " + s);
                            if (codigosInvalidos.Count > 200) sb.AppendLine($"... y {codigosInvalidos.Count - 200} más");
                        }
                        if (codigosFaltantes.Any())
                        {
                            sb.AppendLine($"Códigos no encontrados en la base: {codigosFaltantes.Count}");
                            foreach (var s in codigosFaltantes.Take(200)) sb.AppendLine(" - " + s);
                            if (codigosFaltantes.Count > 200) sb.AppendLine($"... y {codigosFaltantes.Count - 200} más");
                        }
                        sb.AppendLine();
                        sb.AppendLine("Seleccione 'Sí' para continuar y omitir estos códigos (se eliminarán de la lista antes de guardar).\nSeleccione 'No' para cancelar y corregir los códigos.");

                        var resp = MessageBox.Show(sb.ToString(), "Validación de códigos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (resp != MessageBoxResult.Yes)
                        {
                            btnGrabar.IsEnabled = true;
                            return; // cancelar guardado para que el usuario corrija
                        }

                        // El usuario eligió continuar: eliminar códigos inválidos/faltantes de _codigosGridList
                        _codigosGridList.RemoveAll(c => codigosFaltantes.Contains(c.CodigoUnique) || codigosInvalidos.Any(inv => inv.StartsWith(c.CodigoUnique)));

                        // Reconstruir rangos a partir de códigos restantes
                        _rangosProcesadosGlobal.Clear();
                        // Agrupar por producto para reconstruir rangos
                        var byProduct = _codigosGridList.GroupBy(c => c.ProductoId);
                        foreach (var grp in byProduct)
                        {
                            int prodId = grp.Key;
                            // extraer secuencias válidas por base
                            var seqsByBase = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
                            foreach (var item in grp)
                            {
                                var norm = _serviceMovimiento.NormalizarCodigo(item.CodigoUnique);
                                if (norm.Length >= 7 && int.TryParse(norm.Substring(norm.Length - 7), out int seq))
                                {
                                    string baseStr = norm.Length > 7 ? norm.Substring(0, norm.Length - 7) : "";
                                    if (!seqsByBase.ContainsKey(baseStr)) seqsByBase[baseStr] = new System.Collections.Generic.List<int>();
                                    seqsByBase[baseStr].Add(seq);
                                }
                                else
                                {
                                    // no tiene secuencia; lo tratamos como rango unitario usando el texto como base y seq 0
                                    string baseStr = norm;
                                    if (!seqsByBase.ContainsKey(baseStr)) seqsByBase[baseStr] = new System.Collections.Generic.List<int>();
                                    seqsByBase[baseStr].Add(0);
                                }
                            }

                            // convertir listas de secuencias en rangos
                            foreach (var kv in seqsByBase)
                            {
                                var listSeq = kv.Value.Distinct().OrderBy(x => x).ToList();
                                if (!listSeq.Any()) continue;
                                int start = listSeq[0];
                                int end = start;
                                for (int i = 1; i < listSeq.Count; i++)
                                {
                                    int cur = listSeq[i];
                                    if (cur == end + 1)
                                    {
                                        end = cur;
                                    }
                                    else
                                    {
                                        _rangosProcesadosGlobal.Add(new RangoCodigoItem { productoId = prodId, AbreviaturaBase = kv.Key, DesdeNum = start, HastaNum = end, CategoriaProductoId = 1, Cantidad = (end - start + 1).ToString() });
                                        start = cur; end = cur;
                                    }
                                }
                                _rangosProcesadosGlobal.Add(new RangoCodigoItem { productoId = prodId, AbreviaturaBase = kv.Key, DesdeNum = start, HastaNum = end, CategoriaProductoId = 1, Cantidad = (end - start + 1).ToString() });
                            }
                        }

                        // Recalcular cantidades por producto desde la lista de códigos restante
                        foreach (var p in _productosGridList)
                        {
                            int cnt = _codigosGridList.Count(c => c.ProductoId == p.ProductoId);
                            if (cnt > 0) p.Detalle.CantidadIngreso = Convert.ToDecimal(cnt);
                        }

                        // Refrescar UI
                        dgProductos.ItemsSource = null; dgProductos.ItemsSource = _productosGridList;
                        dgCodigos.ItemsSource = null; dgCodigos.ItemsSource = _codigosGridList;
                    }


                var listaDetalles = _productosGridList .Select(p => p.Detalle).ToList();

                // Asegurar que el movimientoID esté en cada detalle (si es nuevo registro)
                foreach (var d in listaDetalles)
                {
                    if (d.MovimientoId == 0 && _currentMovimientoId.HasValue)
                        d.MovimientoId = _currentMovimientoId.Value;
                }

                foreach (var p in _productosGridList)
                {
                    if (p.Detalle == null)
                        p.Detalle = new MovimientoDetalle { ProductoId = p.ProductoId };

                    // Sincroniza cantidad del Grid al detalle antes de guardar
                    p.Detalle.CantidadIngreso = p.Cantidad;
                }


                // Ejecutar transacción
                bool isEditSave = _currentMovimientoId.HasValue;

                _rangosProcesadosGlobal.Clear();

                foreach (var grupo in _codigosGridList.GroupBy(x => x.ProductoId))
                {
                    int productoId = grupo.Key;

                    var gruposBase = grupo
                        .Select(x =>
                        {
                            string codigo = x.CodigoUnique;
                            int pos = codigo.LastIndexOf('-');
                            return new
                            {
                                Base = codigo.Substring(0, pos),
                                Seq = int.Parse(codigo.Substring(pos + 1))
                            };
                        })
                        .GroupBy(x => x.Base);

                    foreach (var g in gruposBase)
                    {
                        var lista = g.Select(x => x.Seq).OrderBy(x => x).ToList();

                        int inicio = lista[0];
                        int fin = inicio;

                        for (int i = 1; i < lista.Count; i++)
                        {
                            if (lista[i] == fin + 1)
                            {
                                fin = lista[i];
                            }
                            else
                            {
                                _rangosProcesadosGlobal.Add(new RangoCodigoItem
                                {
                                    productoId = productoId,
                                    AbreviaturaBase = g.Key,
                                    DesdeNum = inicio,
                                    HastaNum = fin,
                                    CategoriaProductoId = await ObtenerCategoriaDesdeBDAsync(
                                        grupo.First().MovCodigo.CodigoCreadoId)
                                });

                                inicio = fin = lista[i];
                            }
                        }

                        _rangosProcesadosGlobal.Add(new RangoCodigoItem
                        {
                            productoId = productoId,
                            AbreviaturaBase = g.Key,
                            DesdeNum = inicio,
                            HastaNum = fin,
                            CategoriaProductoId = await ObtenerCategoriaDesdeBDAsync(
                                grupo.First().MovCodigo.CodigoCreadoId)
                        });
                    }
                }

                bool exito = await _serviceMovimiento.RegistrarMovimientoCompletoAsync(
                    nuevaCabecera,
                    _productosGridList,
                    _rangosProcesadosGlobal,
                    UBICACION_ID_SELECCIONADA,
                    _currentMovimientoId
                );

                if (exito)
                    {
                        if (isEditSave)
                        {
                            MessageBox.Show("Registro actualizado correctamente.", "Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
                            // limpiar estado de edición
                            _currentMovimientoId = null;
                            LimpiarFormulario();
                            HabilitarCamposFormulario(false);
                            GestionarBotonesPrincipales(enEdicion: false);
                        }
                        else
                        {
                            // El servicio ya asignó el correlativo definitivo en 'nuevaCabecera'
                            string correlativoFinal = $"{nuevaCabecera.SerieDocumento}-{nuevaCabecera.NumeroDocumento}";

                            // Limpiar formulario interno
                            LimpiarFormulario();

                            // Mostrar modal de confirmación con correlativo y botón para confirmar
                            var confirmWin = new Window
                            {
                                Owner = Window.GetWindow(this),
                                Title = "Registro creado",
                                Width = 420,
                                Height = 240,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                ResizeMode = ResizeMode.NoResize,
                                Content = new System.Windows.Controls.Border
                                {
                                    Padding = new Thickness(16),
                                    Child = new System.Windows.Controls.StackPanel
                                    {
                                        Orientation = System.Windows.Controls.Orientation.Vertical,
                                        Children =
                                        {
                                            new System.Windows.Controls.TextBlock
                                            {
                                                Text = "Se ha registrado correctamente",
                                                FontSize = 18,
                                                FontWeight = FontWeights.SemiBold,
                                                Margin = new Thickness(0,0,0,8),
                                                HorizontalAlignment = HorizontalAlignment.Center,
                                                TextAlignment = TextAlignment.Center
                                            },
                                            new System.Windows.Controls.TextBlock
                                            {
                                                Text = correlativoFinal,
                                                FontSize = 28,
                                                FontWeight = FontWeights.Bold,
                                                Margin = new Thickness(0,0,0,12),
                                                HorizontalAlignment = HorizontalAlignment.Center,
                                                TextAlignment = TextAlignment.Center
                                            },
                                            new System.Windows.Controls.Button
                                            {
                                                Content = "Confirmar",
                                                Width = 120,
                                                Height = 36,
                                                HorizontalAlignment = HorizontalAlignment.Center,
                                                IsDefault = true
                                            }
                                        }
                                    }
                                }
                            };

                            // Asociar cierre del diálogo al botón
                            if (confirmWin.Content is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.StackPanel sp && sp.Children[2] is System.Windows.Controls.Button btn)
                            {
                                btn.Click += (s, ev) => { confirmWin.DialogResult = true; };
                            }

                            // Mostrar modal (el usuario confirma)
                            confirmWin.ShowDialog();

                            // Guardar correlativo en memoria pero mantener los campos ocultos por seguridad
                            txtNumSerie.Text = nuevaCabecera.SerieDocumento;
                            txtNumDocumento.Text = nuevaCabecera.NumeroDocumento;
                            txtNumSerie.Visibility = Visibility.Hidden;
                            txtNumDocumento.Visibility = Visibility.Hidden;

                            // Preparar UI para ingresar otro registro (campos editables, correlativos ocultos)
                            HabilitarCamposFormulario(true);
                            btnGrabar.IsEnabled = true;
                            GestionarBotonesPrincipales(enEdicion: true);

                            cboMotivo.Focus();
                        }
                        // Si estábamos en modo imprimir, generar el Excel después de guardar
                        if (_printMode)
                        {
                            try
                            {
                                GenerateExcelFromCurrentLoadedMovement();
                            }
                            catch { }
                            finally
                            {
                                CleanupPrintMode();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error crítico en la transacción de inventario: {ex.Message}", "Error al Guardar", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Si falló, volvemos a encender el botón para que puedan intentar corregir/guardar de nuevo
                    btnGrabar.IsEnabled = true;
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }

            private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
            {
                AgregarItemWindow modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
                // Indicar al modal que fue abierto desde la acción "Agregar Ítem" (nuevo producto)
                modal.IsAddAction = true;
                // Propagar estado permitido según el motivo seleccionado (1 = COMPRA, otro = 4)
                modal.EstadoPermitido = (cboMotivo.SelectedValue is int mid && mid == 1) ? 1 : 4;
                modal.ListaProductosExistentesEnPadre = _productosGridList;
                if (modal.ShowDialog() == true && modal.FueGrabado)
                {
                    var productoSelected = modal._productoSeleccionado;
                    var rangosDelModal = modal.ListaRangosAgregados ?? new System.Collections.ObjectModel.ObservableCollection<RangoCodigoItem>();
                    if (productoSelected == null)
                    {
                        MessageBox.Show("No se seleccionó un producto válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    int idProducto = productoSelected.Id;

                    // VALIDACIÓN DE SOLAPAMIENTO DE RANGOS: se aplica siempre sobre los rangos propuestos
                    foreach (var nuevoRango in rangosDelModal)
                    {
                        var solapamiento = _rangosProcesadosGlobal.FirstOrDefault(r =>
                            r.productoId == idProducto &&
                            (nuevoRango.DesdeNum <= r.HastaNum && nuevoRango.HastaNum >= r.DesdeNum)
                        );

                        if (solapamiento != null)
                        {
                            MessageBox.Show($"Conflicto de rangos para el producto: {productoSelected.Descripcion}.\n" +
                                            $"El rango propuesto ({nuevoRango.DesdeNum}-{nuevoRango.HastaNum}) " +
                                            $"se solapa con el rango ya registrado ({solapamiento.DesdeNum}-{solapamiento.HastaNum}).",
                                            "Error de Rangos", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    // Si el modal indica que se debe MERGEAR con un producto ya existente, hacemos la fusión
                    if (modal.MergeWithExisting)
                    {
                        var existente = _productosGridList.FirstOrDefault(p => p.ProductoId == idProducto);
                        if (existente != null)
                        {
                            // Actualizar cantidad y costo (sumar cantidades de los rangos añadidos)
                            decimal sumaNuevos = modal.CantidadProductoIngresada;
                            existente.Detalle = existente.Detalle ?? new MovimientoDetalle { ProductoId = existente.ProductoId };
                            existente.Detalle.CantidadIngreso += sumaNuevos;
                            existente.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                            // Agregar rangos y códigos
                            int contadorFila = _codigosGridList.Count + 1;
                            foreach (var rango in rangosDelModal)
                            {
                                rango.productoId = idProducto;
                                _rangosProcesadosGlobal.Add(rango);
                                for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                                {
                                    _codigosGridList.Add(new VistaCodigoGrid
                                    {
                                        MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                        CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                        ColeccionTipo = rango.ColeccionTipo,
                                        ProductoId = idProducto
                                    });
                                }
                            }

                            // Refrescar UI
                            dgProductos.ItemsSource = null;
                            dgProductos.ItemsSource = _productosGridList;
                            dgProductos.SelectedItem = existente;
                            dgCodigos.ItemsSource = null;
                            dgCodigos.ItemsSource = _codigosGridList;
                            return;
                        }
                        // si no existe ya, proceder como nuevo (caída elegida)
                    }

                    // Si no se indica merge y el producto ya existe -> rechazar la creación aquí
                    bool yaExiste = _productosGridList.Any(p => p.ProductoId == idProducto);
                    if (yaExiste)
                    {
                        MessageBox.Show("Este producto ya existe.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // --- Agregar como nuevo producto ---
                    var nuevoProductoGrid = new VistaProductoGrid
                    {
                        Detalle = new MovimientoDetalle
                        {
                            ProductoId = idProducto,
                            CantidadIngreso = modal.CantidadProductoIngresada,
                            CostoUnitario = modal.CostoUnitarioIngresado
                        },
                        CodigoProducto = idProducto.ToString(),
                        Descripcion = productoSelected.Descripcion,
                        UnidadMedida = "UNIDAD",
                        ProductoId = idProducto
                    };
                    _productosGridList.Add(nuevoProductoGrid);

                    if (rangosDelModal != null)
                    {
                        int contadorFila = _codigosGridList.Count + 1;
                        foreach (var rango in rangosDelModal)
                        {
                            rango.productoId = idProducto;
                            _rangosProcesadosGlobal.Add(rango);

                            for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                            {
                                _codigosGridList.Add(new VistaCodigoGrid
                                {
                                    MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                    CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                    ColeccionTipo = rango.ColeccionTipo,
                                    ProductoId = idProducto
                                });
                            }
                        }
                    }

                    dgProductos.ItemsSource = null;
                    dgProductos.ItemsSource = _productosGridList;
                    dgProductos.SelectedItem = nuevoProductoGrid;
                }
            }

            private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                // 1. Verificamos si realmente se seleccionó algo en la grilla izquierda
                if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
                {
                    // 2. LIMPIEZA: Rompemos el origen de datos para limpiar la grilla de la derecha de forma segura
                    dgCodigos.ItemsSource = null;

                    // 3. FILTRADO: Buscamos en la lista global '_codigosGridList' los códigos que tengan el mismo ProductoId
                    var codigosDelProducto = _codigosGridList
                                             .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                                             .ToList();

                    // 4. CARGA: Asignamos la lista filtrada directamente al ItemsSource
                    dgCodigos.ItemsSource = codigosDelProducto;
                    // Habilitar acciones contextuales cuando hay un producto seleccionado
                    if (btnModificar != null) btnModificar.IsEnabled = true;
                    if (btnEliminar != null) btnEliminar.IsEnabled = true;
                    if (btnImportar != null) btnImportar.IsEnabled = true; // permitir importar cuando hay selección
                }
                else
                {
                    // Si no hay ningún producto seleccionado, la tabla de la derecha se queda vacía
                    dgCodigos.ItemsSource = null;
                    // Deshabilitar acciones contextuales
                    if (btnModificar != null) btnModificar.IsEnabled = false;
                    if (btnEliminar != null) btnEliminar.IsEnabled = false;
                    // Importar queda habilitado si hay productos en la lista global (permitir import masivo)
                    if (btnImportar != null) btnImportar.IsEnabled = (_productosGridList != null && _productosGridList.Count > 0);
                }
            }
            private void LimpiarFormulario()
            {
                _isUpdatingFromSelection = true;

                // 1. Limpieza de datos en memoria (CRÍTICO)
                _productosGridList.Clear();
                _codigosGridList.Clear();
                _rangosProcesadosGlobal.Clear();

                // 2. Limpieza de controles visuales
                txtNumSerie.Clear();
                txtNumDocumento.Clear();
                dtpFechaRecepcion.SelectedDate = null;
                cboMotivo.SelectedIndex = -1;
                txtRazonSocial.Clear();
                txtCodigoRazonSocial.Clear();
                txtDireccion.Clear();
                txtUbicacion.Clear();
                txtCodigoUbicacion.Clear();
                txtDireccionUbicacion.Clear();
                txtObservacion.Clear();
                txtSerieGuia.Clear();
                txtNumeroGuia.Clear();

                // 3. Limpieza de las tablas visuales
                dgProductos.ItemsSource = null;
                dgCodigos.ItemsSource = null;

                _personaComercialIdSeleccionada = null;
                _isUpdatingFromSelection = false;
            }

            private void HabilitarCamposFormulario(bool habilitar)
            {
                txtNumSerie.IsEnabled = false;
                txtNumDocumento.IsEnabled = false;
                txtCodigoRazonSocial.IsEnabled = false;
                txtDireccion.IsEnabled = false;

                dtpFechaRecepcion.IsEnabled = habilitar;
                cboMotivo.IsEnabled = habilitar;
                txtRazonSocial.IsEnabled = habilitar;
                txtObservacion.IsEnabled = habilitar;
                txtSerieGuia.IsEnabled = habilitar;
                txtNumeroGuia.IsEnabled = habilitar;

                if (btnModificar != null) btnModificar.IsEnabled = habilitar;
                if (btnEliminar != null) btnEliminar.IsEnabled = habilitar;
                if (btnImportar != null) btnImportar.IsEnabled = habilitar;
                if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = habilitar;
                // Control de acciones de guardado/cancelación y grillas
                if (btnGrabar != null) btnGrabar.IsEnabled = habilitar;
                if (btnCancelar != null) btnCancelar.IsEnabled = habilitar;
                if (dgProductos != null)
                {
                    dgProductos.IsEnabled = habilitar;
                    // Cuando el formulario está habilitado permitimos editar la grilla de productos
                    dgProductos.IsReadOnly = !habilitar;
                }
                if (dgCodigos != null)
                {
                    dgCodigos.IsEnabled = habilitar;
                    // Mantener la grilla de códigos en lectura (selección) incluso cuando el formulario está habilitado
                    dgCodigos.IsReadOnly = true;
                }
            }

            private void GestionarBotonesPrincipales(bool enEdicion)
            {
                btnAgregar.IsEnabled = !enEdicion;
                btnEditar.IsEnabled = !enEdicion;
                btnImprimir.IsEnabled = !enEdicion;
                btnAnular.IsEnabled = !enEdicion;
            }

            private void EstablecerEstadoInicial()
            {
                LimpiarFormulario();
                HabilitarCamposFormulario(false);
                GestionarBotonesPrincipales(enEdicion: false);
            }

            private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            {
                // Cuando el control está dentro de un ScrollViewer padre, el evento rueda lo captura el padre.
                // Aquí forzamos que la grilla interna se desplace verticalmente.
                if (sender is DependencyObject dep)
                {
                    var sv = FindVisualChild<ScrollViewer>(dep);
                    if (sv != null)
                    {
                        // Delta positivo -> hacia arriba
                        double newOffset = sv.VerticalOffset - e.Delta / 3.0; // ajuste de sensibilidad
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


            private async Task<int> ObtenerCategoriaDesdeBDAsync(int codigoId)
            {
                try
                {
                    using var conn = _dbConnHelper.GetConnection();
                    var dbConn = (System.Data.Common.DbConnection)conn;
                    await dbConn.OpenAsync();
                    using var cmd = dbConn.CreateCommand();
                    // Asumiendo que la categoría está en registro_codigos
                    cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT rc.categoria_producto_id 
                FROM registro_codigos rc 
                JOIN codigos_creados cc ON cc.registro_codigo_id = rc.id 
                WHERE cc.id = @id");

                    var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = codigoId; cmd.Parameters.Add(p);
                    var res = await cmd.ExecuteScalarAsync();
                    return res != null ? Convert.ToInt32(res) : 1; // Default a 1 (Guía)
                }
                catch { return 1; }
            }

            // ==========================================
            // MÓDULO DE LECTORA PARA ENTRADAS (CORREGIDO)
            // ==========================================

            private void BtnEscanear_Click(object sender, RoutedEventArgs e)
            {
                var lector = new LectorGlobalWindow(async resultado =>
                {
                    // Ahora devuelve true o false de forma correcta
                    bool seAgregoConExito = await ProcesarCodigoEscaneadoAsync(resultado);

                    if (seAgregoConExito)
                    {
                        Application.Current.Dispatcher.Invoke(() => {
                            RefrescarGrillas(); // 🌟 LLAMAMOS AL NUEVO MÉTODO AQUÍ
                        });
                    }

                    return seAgregoConExito; // Le respondemos al modal inteligente
                });

                lector.Owner = Window.GetWindow(this);
                lector.ShowDialog();

                RefrescarGrillas(); // 🌟 Y TAMBIÉN AQUÍ AL CERRAR LA VENTANA
            }

            private void RefrescarGrillas()
            {
                SincronizarCantidadesConCodigos();

                // 1. Refrescar productos
                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;

                // 2. Refrescar códigos
                dgCodigos.ItemsSource = null;
                if (dgProductos.SelectedItem is VistaProductoGrid seleccionado)
                {
                    var filtrados = _codigosGridList.Where(c => c.ProductoId == seleccionado.ProductoId).ToList();
                    dgCodigos.ItemsSource = filtrados;
                }
                else
                {
                    dgCodigos.ItemsSource = _codigosGridList.ToList();
                }
            }

            private async Task<bool> ProcesarCodigoEscaneadoAsync(AplicativoDeAlmacen.Models.Facturación.LectoraResultDTO resultado)
            {
                // Validación para Entradas
                if (resultado.EstadoId == 3)
                {
                    MessageBox.Show($"El código '{resultado.CodigoCompleto}' YA ESTÁ EN ALMACÉN. No puede ingresarlo de nuevo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // ✅ CORREGIDO: Usamos _codigosGridList que es el nombre real en tu clase
                if (_codigosGridList.Any(x => x.CodigoUnique == resultado.CodigoCompleto))
                {
                    return false; // Retorna false si es repetido
                }

                string tipoBD = await ObtenerColeccionTipoBDAsync(resultado.CodigoCreadoId);

                // ✅ CORREGIDO: Usamos _codigosGridList
                _codigosGridList.Add(new VistaCodigoGrid
                {
                    ProductoId = resultado.ProductoId,
                    CodigoUnique = resultado.CodigoCompleto,
                    ColeccionTipo = tipoBD,
                    MovCodigo = new MovimientoCodigo { CodigoCreadoId = resultado.CodigoCreadoId }
                });

                // ✅ CORREGIDO: Usamos _productosGridList
                var producto = _productosGridList.FirstOrDefault(p => p.ProductoId == resultado.ProductoId);

                if (producto != null)
                {
                    producto.Cantidad++;
                    if (producto.Detalle != null)
                        producto.Detalle.CantidadIngreso++;
                }
                else
                {
                    // ✅ CORREGIDO: Usamos _productosGridList
                    _productosGridList.Add(new VistaProductoGrid
                    {
                        ProductoId = resultado.ProductoId,
                        CodigoProducto = resultado.ProductoId.ToString(),
                        Descripcion = resultado.DescripcionProducto,
                        UnidadMedida = "UNIDAD",
                        Cantidad = 1,
                        Detalle = new MovimientoDetalle
                        {
                            ProductoId = resultado.ProductoId,
                            CantidadIngreso = 1,
                            CostoUnitario = resultado.PrecioUnitario
                        }
                    });
                }

                return true;
            }

            private void SincronizarCantidadesConCodigos()
            {
                int i = 1;
                foreach (var codigo in _codigosGridList)
                {
                    codigo.NumeroFila = i++;
                }

                foreach (var producto in _productosGridList)
                {
                    int count = _codigosGridList.Count(c => c.ProductoId == producto.ProductoId);
                    producto.Cantidad = count;
                    if (producto.Detalle != null)
                    {
                        producto.Detalle.CantidadIngreso = count;
                    }
                }
            }

            private async Task<string> ObtenerColeccionTipoBDAsync(int codigoCreadoId)
            {
                try
                {
                    // ✅ SOLUCIÓN AL ERROR CS0234: Reutilizamos tu variable global '_dbConnHelper'
                    using var conn = _dbConnHelper.GetConnection();
                    var dbConn = (System.Data.Common.DbConnection)conn;
                    if (dbConn.State != System.Data.ConnectionState.Open)
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
        }    
    }