#nullable enable

using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Transferencias;
using ClosedXML.Excel;
using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Services.Importaciones
{
    public class ImportacionExcelService
    {
        private readonly DataConnection.DatabaseConnection _database;
        private readonly AplicativoDeAlmacen.Services.facturaciòn.FacturacionService _facturacionService;

        public ImportacionExcelService()
        {
            _database = new DataConnection.DatabaseConnection();
            _facturacionService = new AplicativoDeAlmacen.Services.facturaciòn.FacturacionService();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =========================================================================
        // MÉTODOS ORIGINALES (RegistroCodigosWindow.xaml.cs y Transferencias)
        // INTACTOS - NO SE TOCAN
        // =========================================================================

        public async Task<List<string>> LeerCodigosDesdeExcelAsync(string ruta)
        {
            return await Task.Run(() =>
            {
                using var workbook = new XLWorkbook(ruta);
                var lista = new List<string>(5000);

                var cabecerasIgnorar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "CODIGO", "CÓDIGO", "SERIES", "SERIE", "ID", "LISTA", "CODIGOS", "CÓDIGOS"
                };

                foreach (var ws in workbook.Worksheets)
                {
                    var used = ws.RangeUsed();
                    if (used == null) continue;

                    foreach (var row in used.Rows())
                    {
                        foreach (var cell in row.Cells())
                        {
                            string valorRaw = cell.GetString();
                            if (string.IsNullOrWhiteSpace(valorRaw)) continue;

                            string valorLimpio = Regex.Replace(valorRaw, @"[\u200B-\u200D\uFEFF\u00A0]", "").Trim();
                            if (string.IsNullOrWhiteSpace(valorLimpio)) continue;

                            if (cabecerasIgnorar.Contains(valorLimpio)) continue;

                            string codigoFinal = valorLimpio
                                .Replace("'", "-")
                                .Replace("\u2019", "-")
                                .Replace("\u2018", "-");

                            lista.Add(codigoFinal);
                        }
                    }
                }

                return lista;
            });
        }

        public async Task<List<string>> ObtenerCodigosDuplicadosAsync(List<string> codigosExcel)
        {
            var duplicados = new List<string>();

            if (codigosExcel == null || !codigosExcel.Any())
                return duplicados;

            var codigosUnicosParaRevisar = codigosExcel
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            const int batchSize = 1000;

            for (int i = 0; i < codigosUnicosParaRevisar.Count; i += batchSize)
            {
                var loteActual = codigosUnicosParaRevisar.Skip(i).Take(batchSize).ToList();

                using var cmd = dbConn.CreateCommand();

                var paramNames = loteActual.Select((_, idx) => $"@p{idx}").ToList();
                string clausulaIn = string.Join(",", paramNames);

                string tablaQuery = QueryAdapter.EsMySQL ? "codigos_creados cc" : "codigos_creados cc WITH (NOLOCK)";

                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                    SELECT cc.codigo 
                    FROM {tablaQuery} 
                    WHERE cc.codigo IN ({clausulaIn})");

                for (int j = 0; j < loteActual.Count; j++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@p{j}";
                    p.Value = loteActual[j];
                    cmd.Parameters.Add(p);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        duplicados.Add(reader.GetString(0));
                    }
                }
            }

            return duplicados;
        }

        public async Task GuardarCodigosImportadosTransactionAsync(
            int coleccionId,
            int productoId,
            int categoriaId,
            List<string> codigosAprobados,
            int usuarioId,
            int almacenId,
            string nombreArchivoOrigen,
            IProgress<int>? progress = null)
        {
            if (codigosAprobados == null || !codigosAprobados.Any()) return;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaction = dbConn.BeginTransaction())
                {
                    try
                    {
                        int cantidad = codigosAprobados.Count;

                        string codigoDesde = codigosAprobados.First().Replace("'", "-");
                        string codigoHasta = codigosAprobados.Last().Replace("'", "-");

                        int lastDashDesde = codigoDesde.LastIndexOf('-');
                        int desdeNum = (lastDashDesde >= 0 && int.TryParse(codigoDesde.Substring(lastDashDesde + 1), out int dNum)) ? dNum : 0;

                        int lastDashHasta = codigoHasta.LastIndexOf('-');
                        int hastaNum = (lastDashHasta >= 0 && int.TryParse(codigoHasta.Substring(lastDashHasta + 1), out int hNum)) ? hNum : 0;

                        string abreviaturaBase = lastDashDesde >= 0 ? codigoDesde.Substring(0, lastDashDesde) : codigoDesde;
                        string origenRegistro = $"EXCEL: {nombreArchivoOrigen}";

                        string funcionFecha = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                        string queryRegistro = $@"
                            INSERT INTO registro_codigos 
                            (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id, usuario_id, origen_registro, created_at) 
                            VALUES 
                            (@cId, @pId, @cant, @des, @has, @catId, @uId, @origen, {funcionFecha});";

                        string selectId = QueryAdapter.EsMySQL ? " SELECT LAST_INSERT_ID();" : " SELECT SCOPE_IDENTITY();";

                        int registroId;
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryRegistro + selectId);

                            AgregarParametro(cmd, "@cId", coleccionId);
                            AgregarParametro(cmd, "@pId", productoId);
                            AgregarParametro(cmd, "@cant", cantidad);
                            AgregarParametro(cmd, "@des", codigoDesde);
                            AgregarParametro(cmd, "@has", codigoHasta);
                            AgregarParametro(cmd, "@catId", categoriaId);
                            AgregarParametro(cmd, "@uId", usuarioId > 0 ? usuarioId : 1);
                            AgregarParametro(cmd, "@origen", origenRegistro);

                            registroId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        string queryRango = $@"
                            INSERT INTO registro_rangos 
                            (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at, usuario_id) 
                            VALUES 
                            (@pId, @catId, @abrev, @dNum, @hNum, NULL, {funcionFecha}, @uId);";

                        using (var cmdRango = dbConn.CreateCommand())
                        {
                            cmdRango.Transaction = transaction;
                            cmdRango.CommandText = QueryAdapter.FormatearConsulta(queryRango);

                            AgregarParametro(cmdRango, "@pId", productoId);
                            AgregarParametro(cmdRango, "@catId", categoriaId);
                            AgregarParametro(cmdRango, "@abrev", abreviaturaBase);
                            AgregarParametro(cmdRango, "@dNum", desdeNum);
                            AgregarParametro(cmdRango, "@hNum", hastaNum);
                            AgregarParametro(cmdRango, "@uId", usuarioId > 0 ? usuarioId : 1);

                            await cmdRango.ExecuteNonQueryAsync();
                        }

                        int batchSize = 1000;
                        for (int i = 0; i < cantidad; i += batchSize)
                        {
                            int currentBatch = Math.Min(batchSize, cantidad - i);
                            var queryBuilder = new System.Text.StringBuilder($@"
                                INSERT INTO codigos_creados 
                                (registro_codigo_id, codigo, estado_id, condicion_id, almacen_id, usuario_id, origen_creacion, created_at) 
                                VALUES ");

                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = transaction;

                                for (int j = 0; j < currentBatch; j++)
                                {
                                    int idx = i + j;
                                    string paramCod = $"@cod{idx}";
                                    string codigoGenerado = codigosAprobados[idx].Replace("'", "-");

                                    queryBuilder.Append($"({registroId}, {paramCod}, 1, 1, @almId, @usrId, 'EXCEL', {funcionFecha})");
                                    if (j < currentBatch - 1) queryBuilder.Append(", ");

                                    AgregarParametro(cmd, paramCod, codigoGenerado);
                                }

                                AgregarParametro(cmd, "@almId", almacenId);
                                AgregarParametro(cmd, "@usrId", usuarioId > 0 ? usuarioId : 1);

                                cmd.CommandText = QueryAdapter.FormatearConsulta(queryBuilder.ToString());
                                await cmd.ExecuteNonQueryAsync();
                            }

                            int pct = ((i + currentBatch) * 100) / cantidad;
                            progress?.Report(pct);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task<List<TransaccionHeaderDTO>> ObtenerHistorialTransferenciasAsync(int miAlmacenId, DateTime? desde = null, DateTime? hasta = null, string? estadoFiltro = "TODOS")
        {
            var lista = new List<TransaccionHeaderDTO>();

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                string coalesce = "COALESCE";

                string query = $@"
                    SELECT 
                        m.id,
                        CONCAT(m.serie_documento, '-', m.numero_documento) AS serie_numero,
                        {coalesce}(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS guia_remision,
                        m.fecha_movimiento,
                        {coalesce}(m.almacen_origen_id, 1) AS almacen_origen_id,
                        {coalesce}(ao.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS origen_nombre,
                        {coalesce}(m.almacen_destino_id, 1) AS almacen_destino_id,
                        {coalesce}(ad.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS destino_nombre,
                        {coalesce}(u.nombres, 'SISTEMA') AS emisor_nombre,
                        {coalesce}(mp.descripcion, 'TRANSFERENCIA') AS motivo_desc,
                        {coalesce}(m.observacion, '') AS observacion,
                        COUNT(DISTINCT md.producto_id) AS total_productos,
                        COUNT(mc.id) AS total_codigos,
                        MAX(CASE WHEN cc.estado_id = 5 THEN 1 ELSE 0 END) AS es_pendiente
                    FROM movimientos m {nolock}
                    LEFT JOIN almacenes ao {nolock} ON m.almacen_origen_id = ao.id
                    LEFT JOIN almacenes ad {nolock} ON m.almacen_destino_id = ad.id
                    LEFT JOIN usuarios u {nolock} ON m.usuario_id = u.id
                    LEFT JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                    INNER JOIN movimiento_detalles md {nolock} ON md.movimiento_id = m.id
                    INNER JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
                    INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
                    WHERE m.estado_id = 1
                      AND (m.motivo_producto_id IN (4, 10))
                      AND (m.almacen_origen_id = @miAlmacen OR m.almacen_destino_id = @miAlmacen)";

                if (desde.HasValue) query += " AND m.fecha_movimiento >= @desde";
                if (hasta.HasValue) query += " AND m.fecha_movimiento <= @hasta";

                query += @"
                    GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                             m.fecha_movimiento, m.almacen_origen_id, ao.nombre, m.almacen_destino_id, 
                             ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
                    ORDER BY m.created_at DESC";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);
                if (desde.HasValue) AgregarParametro(cmd, "@desde", desde.Value);
                if (hasta.HasValue) AgregarParametro(cmd, "@hasta", hasta.Value);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    bool esPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1;

                    if (estadoFiltro == "PENDIENTES" && !esPendiente) continue;
                    if (estadoFiltro == "RECEPCIONADOS" && esPendiente) continue;

                    lista.Add(new TransaccionHeaderDTO
                    {
                        MovimientoId = rdr.GetInt32(0),
                        SerieNumero = rdr.GetString(1),
                        GuiaRemision = rdr.GetString(2),
                        FechaMovimiento = rdr.IsDBNull(3) ? DateTime.Today : rdr.GetDateTime(3),
                        AlmacenOrigenId = rdr.GetInt32(4),
                        AlmacenOrigenNombre = rdr.GetString(5),
                        AlmacenDestinoId = rdr.GetInt32(6),
                        AlmacenDestinoNombre = rdr.GetString(7),
                        UsuarioEmisorNombre = rdr.GetString(8),
                        MotivoDescripcion = rdr.GetString(9),
                        Observacion = rdr.GetString(10),
                        TotalProductos = Convert.ToInt32(rdr.GetValue(11)),
                        TotalCodigos = Convert.ToInt32(rdr.GetValue(12)),
                        EsPendiente = esPendiente
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar historial de transferencias: {ex.Message}");
            }

            return lista;
        }

        // =========================================================================
        // NUEVOS MÉTODOS MEJORADOS (IMPORTACIÓN DE VENTAS NISIRA)
        // =========================================================================

        public async Task<List<ImportacionCabeceraDTO>> LeerExcelVentasAgrupadoAsync(string rutaArchivo)
        {
            var cabecerasAgrupadas = new List<ImportacionCabeceraDTO>();

            await Task.Run(() =>
            {
                using var stream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                // 1. Localizar dinámicamente la fila de cabeceras
                IXLRow? filaCabecera = null;
                for (int r = 1; r <= 5; r++)
                {
                    var row = ws.Row(r);
                    var textos = row.CellsUsed().Select(c => c.GetString().Trim().ToUpperInvariant()).ToList();
                    if (textos.Contains("SERIE") && (textos.Contains("NUMERO") || textos.Contains("NRODOCUMENTO")))
                    {
                        filaCabecera = row;
                        break;
                    }
                }

                if (filaCabecera == null) filaCabecera = ws.Row(2);

                // 2. Índices de columnas predeterminados
                int colDocTipo = 2;
                int colSerie = 3;
                int colNumero = 4;
                int colDocIdentidad = 6;
                int colRazonSocial = 7;
                int colMoneda = 8;
                int colFecha = 9;
                int colExonerado = 10;
                int colImporte = 11;
                int colProducto = 12;
                int colPrecio = 13;
                int colInstitucion = 14;
                int colCodigoInterno = 16;
                int colCantidad = 17;
                int colCondicion = 18;
                int colEmpresa = -1;

                var canalesPagoColumnas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Mapeo automático de columnas
                foreach (var cell in filaCabecera.CellsUsed())
                {
                    string h = cell.GetString().Trim().ToUpperInvariant();
                    int cNum = cell.Address.ColumnNumber;

                    if (h.Equals("DOCUMENTO") || h.Equals("TIPO DOC")) colDocTipo = cNum;
                    else if (h.Equals("SERIE")) colSerie = cNum;
                    else if (h.Equals("NUMERO") || h.Equals("NRO")) colNumero = cNum;
                    else if (h.Equals("NRODOCUMENTO") || h.Equals("DNI/RUC") || h.Equals("RUC/DNI")) colDocIdentidad = cNum;
                    else if (h.Equals("RAZONSOCIAL") || h.Equals("CLIENTE") || h.Equals("PAGADOR")) colRazonSocial = cNum;
                    else if (h.Equals("MONEDA")) colMoneda = cNum;
                    else if (h.Equals("FECHA") || h.Equals("FEC EMISION")) colFecha = cNum;
                    else if (h.Equals("EXONERADO")) colExonerado = cNum;
                    else if (h.Equals("IMPORTE") || h.Equals("TOTAL")) colImporte = cNum;
                    else if (h.Equals("PRODUCTO") || h.Equals("DESCRIPCION")) colProducto = cNum;
                    else if (h.Equals("PRECIO") || h.Equals("P.UNITARIO")) colPrecio = cNum;
                    else if (h.Equals("INSTITUCIÓN") || h.Equals("INSTITUCION") || h.Equals("COLEGIO")) colInstitucion = cNum;
                    else if (h.Contains("CODIGO") || h.Contains("CÓDIGO") || h.Contains("INTERNO")) colCodigoInterno = cNum;
                    else if (h.Equals("CANTIDAD") || h.Equals("CANT")) colCantidad = cNum;
                    else if (h.Equals("CONDICION") || h.Equals("CONDICIÓN")) colCondicion = cNum;
                    else if (h.Equals("EMPRESA")) colEmpresa = cNum;
                    else if (h.Contains("EFECTIVO") || h.Contains("YAPE") || h.Contains("PLIN") ||
                             h.Contains("TRANSF") || h.Contains("DEPOSITO") || h.Contains("TIENDA") ||
                             h.Contains("CULQUI") || h.Contains("DELIVERY"))
                    {
                        canalesPagoColumnas[h] = cNum;
                    }
                }

                var agrupador = new Dictionary<string, (ImportacionCabeceraDTO Cabecera, Dictionary<string, ImportacionDetalleDTO> DetDict)>(StringComparer.OrdinalIgnoreCase);

                // 3. Recorrer filas de datos
                foreach (var row in ws.RowsUsed().Skip(filaCabecera.RowNumber()))
                {
                    try
                    {
                        string serie = row.Cell(colSerie).GetString().Trim().ToUpperInvariant();
                        string numeroRaw = row.Cell(colNumero).GetString().Trim();

                        if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numeroRaw)) continue;

                        string numeroFinal = int.TryParse(numeroRaw, out int numVal) ? numVal.ToString("D7") : numeroRaw.PadLeft(7, '0');
                        string claveDoc = $"{serie}-{numeroFinal}";

                        string docTipo = row.Cell(colDocTipo).GetString().Trim().ToUpperInvariant();
                        DateTime fecha = row.Cell(colFecha).TryGetValue(out DateTime dtVal) ? dtVal : DateTime.Today;

                        string rzExcel = row.Cell(colRazonSocial).GetString().Trim();
                        string colExcel = row.Cell(colInstitucion).GetString().Trim();
                        string monedaStr = row.Cell(colMoneda).GetString().Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(monedaStr)) monedaStr = "SOLES";

                        string condicionStr = row.Cell(colCondicion).GetString().Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(condicionStr)) condicionStr = "EFECTIVO";

                        string empresaExcel = (colEmpresa > 0) ? row.Cell(colEmpresa).GetString().Trim() : string.Empty;

                        decimal precio = row.Cell(colPrecio).TryGetValue(out decimal prVal) ? prVal : 0m;
                        decimal importeFila = row.Cell(colImporte).TryGetValue(out decimal impVal) ? impVal : 0m;
                        string prodDesc = row.Cell(colProducto).GetString().Trim();

                        int cantFila = 1;
                        if (row.Cell(colCantidad).TryGetValue(out int cVal) && cVal > 0) cantFila = cVal;

                        string codInternoRaw = row.Cell(colCodigoInterno).GetString().Trim();

                        // Inicializar cabecera si es nueva
                        if (!agrupador.TryGetValue(claveDoc, out var paquete))
                        {
                            // Detectar si es FACTURA, RECIBO o BOLETA (por columna o por letra de serie)
                            string tipoDocIdentificado;
                            if (docTipo.Contains("FACT") || serie.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                            {
                                tipoDocIdentificado = "FACTURA";
                            }
                            else if (docTipo.Contains("REC") || serie.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                            {
                                tipoDocIdentificado = "RECIBO";
                            }
                            else
                            {
                                tipoDocIdentificado = "BOLETA";
                            }

                            var cab = new ImportacionCabeceraDTO
                            {
                                DocumentoExcel = tipoDocIdentificado,
                                Serie = serie,
                                Numero = numeroFinal,
                                Fecha = fecha,
                                RazonSocialExcel = rzExcel,
                                ClienteExcel = colExcel,
                                Moneda = monedaStr,
                                CondicionPagoNombre = condicionStr,
                                EmpresaNombre = !string.IsNullOrWhiteSpace(empresaExcel) ? empresaExcel : "[ ASIGNANDO... ]",
                                Detalles = new List<ImportacionDetalleDTO>(),
                                PagosDesglosados = new List<ImportacionPagoDetalleDTO>()
                            };

                            paquete = (cab, new Dictionary<string, ImportacionDetalleDTO>(StringComparer.OrdinalIgnoreCase));
                                agrupador[claveDoc] = paquete;
                        }

                        // 🌟 LECTURA Y ACUMULACIÓN MULTICANAL DE PAGOS (Para todas las filas del comprobante)
                        foreach (var canal in canalesPagoColumnas)
                        {
                            if (row.Cell(canal.Value).TryGetValue(out decimal montoCanal) && montoCanal > 0)
                            {
                                if (canal.Key.Contains("DELIVERY"))
                                {
                                    paquete.Cabecera.MontoDelivery += montoCanal;
                                }

                                var pagoExistente = paquete.Cabecera.PagosDesglosados
                                    .FirstOrDefault(p => p.MedioPagoNombre.Equals(canal.Key, StringComparison.OrdinalIgnoreCase));

                                if (pagoExistente != null)
                                {
                                    pagoExistente.Monto += montoCanal;
                                }
                                else
                                {
                                    paquete.Cabecera.PagosDesglosados.Add(new ImportacionPagoDetalleDTO
                                    {
                                        MedioPagoNombre = canal.Key,
                                        Monto = montoCanal
                                    });
                                }
                            }
                        }

                        paquete.Cabecera.Total += importeFila;
                        paquete.Cabecera.Exonerado += importeFila;

                        // Agrupar Detalles por Producto
                        if (!paquete.DetDict.TryGetValue(prodDesc, out var det))
                        {
                            det = new ImportacionDetalleDTO
                            {
                                Linea = paquete.DetDict.Count + 1,
                                DescripcionExcel = prodDesc,
                                Cantidad = 0,
                                PrecioUnitario = precio,
                                Importe = 0,
                                Codigos = new List<ImportacionCodigoDTO>()
                            };
                            paquete.DetDict[prodDesc] = det;
                        }

                        det.Cantidad += cantFila;
                        det.Importe += importeFila;

                        // Extracción de correlativo numérico
                        int correlativoNum = 0;
                        if (!string.IsNullOrWhiteSpace(codInternoRaw))
                        {
                            int posUltimoGuion = codInternoRaw.LastIndexOf('-');
                            string parteNum = posUltimoGuion >= 0 ? codInternoRaw.Substring(posUltimoGuion + 1) : codInternoRaw;
                            string soloDigitos = new string(parteNum.Where(char.IsDigit).ToArray());
                            int.TryParse(soloDigitos, out correlativoNum);
                        }

                        det.Codigos.Add(new ImportacionCodigoDTO
                        {
                            CodigoExcel = codInternoRaw,
                            CorrelativoExtraido = correlativoNum,
                            Cantidad = cantFila,
                            MensajeValidacion = "PENDIENTE DE VERIFICAR"
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error al leer fila de Excel: {ex.Message}");
                    }
                }

                // 🌟 ARMAR LA LISTA DE CONDICIONES VISIBLES (YAPE, TRANSFERENCIA, EFECTIVO)
                foreach (var paquete in agrupador.Values)
                {
                    paquete.Cabecera.Detalles = paquete.DetDict.Values.ToList();

                    if (paquete.Cabecera.PagosDesglosados.Any())
                    {
                        // Muestra la lista de condiciones separadas por coma (ej: "EFECTIVO, YAPE, TRANSF. BCP")
                        var nombresUnicos = paquete.Cabecera.PagosDesglosados
                            .Where(p => p.Monto > 0)
                            .Select(p => p.MedioPagoNombre.Trim())
                            .Distinct()
                            .ToList();

                        if (nombresUnicos.Any())
                        {
                            paquete.Cabecera.CondicionPagoNombre = string.Join(", ", nombresUnicos);
                        }
                    }

                    cabecerasAgrupadas.Add(paquete.Cabecera);
                }
            });

            return cabecerasAgrupadas;
        }

        public async Task ValidarDatosImportacionAsync(List<ImportacionCabeceraDTO> comprobantes, int almacenId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            // A. Series y Empresas vinculadas
            var mapaSeriesEmpresa = new Dictionary<string, (int EmpresaId, string RazonSocial)>(StringComparer.OrdinalIgnoreCase);
            cmd.CommandText = QueryAdapter.FormatearConsulta(@"
        SELECT s.num_seri, s.empresa_id, COALESCE(e.razon_social, 'EMPRESA NO ASIGNADA')
        FROM series_documentos s
        LEFT JOIN empresas e ON s.empresa_id = e.id;");
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    string numSeri = rdr.GetString(0).Trim();
                    int empId = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
                    string empNombre = rdr.GetString(2).Trim();
                    mapaSeriesEmpresa[numSeri] = (empId, empNombre);
                }
            }

            // B. Clientes Comodín (7: CLIENTES VARIOS / 8: CLIENTES VARIOS FACTURACION)
            int clienteBoletaId = 7;
            int clienteFacturaId = 8;
            cmd.CommandText = QueryAdapter.FormatearConsulta(@"
        SELECT id, COALESCE(dni, ''), COALESCE(ruc, '') 
        FROM personas_comerciales 
        WHERE dni = '00000000' OR ruc = '00000000000';");
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    int id = rdr.GetInt32(0);
                    string dni = rdr.GetString(1).Trim();
                    string ruc = rdr.GetString(2).Trim();
                    if (dni == "00000000") clienteBoletaId = id;
                    if (ruc == "00000000000") clienteFacturaId = id;
                }
            }

            // C. Medios de Pago
            var mapaMedios = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM medios_pago;");
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    mapaMedios[rdr.GetString(1).Trim()] = rdr.GetInt32(0);
                }
            }

            // D. Monedas
            var mapaMonedas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion, codigo_sunat FROM monedas;");
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    int id = rdr.GetInt32(0);
                    mapaMonedas[rdr.GetString(1).Trim()] = id;
                    mapaMonedas[rdr.GetString(2).Trim()] = id;
                }
            }

            // E. Catálogo de Productos y Abreviaturas
            var listaProductos = new List<(int Id, string Descripcion, string Abreviatura)>();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion, COALESCE(abreviatura, '') FROM productos;");
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    listaProductos.Add((rdr.GetInt32(0), rdr.GetString(1).Trim(), rdr.GetString(2).Trim()));
                }
            }

            string queryComprobanteRegistrado = QueryAdapter.EsMySQL
                ? "SELECT 1 FROM facturacion_cabecera WHERE serie_documento = @s AND numero_documento = @n AND estado_registro = 1 LIMIT 1;"
                : "SELECT TOP 1 1 FROM facturacion_cabecera WITH (NOLOCK) WHERE serie_documento = @s AND numero_documento = @n AND estado_registro = 1;";

            // 🌟 CANDADO MULTI-SEDE: Se evalúa almacen_id para asegurar que el código pertenezca a la sede
            string queryKardexCodigo = QueryAdapter.EsMySQL
                ? @"SELECT cc.id, cc.codigo, cc.estado_id, cc.almacen_id, COALESCE(mc.movimiento_id, 0) AS mov_id
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
            LEFT JOIN movimiento_codigos mc ON mc.codigo_creado_id = cc.id
            WHERE rc.producto_id = @ProdId
              AND (cc.codigo LIKE @patronNumero OR cc.codigo = @codExacto)
            ORDER BY cc.id DESC LIMIT 1;"
                : @"SELECT TOP 1 cc.id, cc.codigo, cc.estado_id, cc.almacen_id, ISNULL(mc.movimiento_id, 0) AS mov_id
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
            LEFT JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            WHERE rc.producto_id = @ProdId
              AND (cc.codigo LIKE @patronNumero OR cc.codigo = @codExacto)
            ORDER BY cc.id DESC;";

            string queryFacturado = QueryAdapter.EsMySQL
                ? @"SELECT fc.serie_documento, fc.numero_documento
            FROM facturacion_detalle_codigos fdc
            INNER JOIN facturacion_detalle fd ON fdc.facturacion_detalle_id = fd.id
            INNER JOIN facturacion_cabecera fc ON fd.facturacion_cabecera_id = fc.id
            WHERE fdc.codigo_creado_id = @CodId AND fc.estado_registro = 1 LIMIT 1;"
                : @"SELECT TOP 1 fc.serie_documento, fc.numero_documento
            FROM facturacion_detalle_codigos fdc WITH (NOLOCK)
            INNER JOIN facturacion_detalle fd WITH (NOLOCK) ON fdc.facturacion_detalle_id = fd.id
            INNER JOIN facturacion_cabecera fc WITH (NOLOCK) ON fd.facturacion_cabecera_id = fc.id
            WHERE fdc.codigo_creado_id = @CodId AND fc.estado_registro = 1;";

            foreach (var cab in comprobantes)
            {
                cab.EsValido = true;
                cab.MensajeError = string.Empty;

                // 1. Empresa
                if (mapaSeriesEmpresa.TryGetValue(cab.Serie, out var empInfo))
                {
                    cab.EmpresaId = empInfo.EmpresaId > 0 ? empInfo.EmpresaId : null;
                    cab.EmpresaNombre = empInfo.RazonSocial;

                    if (!cab.EmpresaId.HasValue)
                    {
                        cab.EsValido = false;
                        cab.MensajeError += $"La serie '{cab.Serie}' no tiene Empresa asignada. ";
                    }
                }
                else
                {
                    cab.EsValido = false;
                    cab.EmpresaNombre = "[ SERIE NO REGISTRADA ]";
                    cab.MensajeError += $"La serie '{cab.Serie}' no está registrada en 'series_documentos'. ";
                }

                // 2. Cliente Comodín
                if (cab.DocumentoExcel == "FACTURA" || cab.Serie.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                {
                    cab.CompradorId = clienteFacturaId;
                    cab.RazonSocialSistema = "CLIENTES VARIOS FACTURACION";
                    cab.ClienteNumeroDoc = "00000000000";
                }
                else
                {
                    cab.CompradorId = clienteBoletaId;
                    cab.RazonSocialSistema = "CLIENTES VARIOS";
                    cab.ClienteNumeroDoc = "00000000";
                }

                // 3. Moneda y Medios de Pago
                cab.MonedaId = mapaMonedas.TryGetValue(cab.Moneda, out int mId) ? mId : 1;

                foreach (var p in cab.PagosDesglosados)
                {
                    string nombreLimpio = p.MedioPagoNombre.Trim();

                    if (mapaMedios.TryGetValue(nombreLimpio, out int medId))
                    {
                        p.MedioPagoId = medId;
                    }
                    else
                    {
                        var matchAprox = mapaMedios.FirstOrDefault(m =>
                            m.Key.Replace(" ", "").Equals(nombreLimpio.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                            nombreLimpio.Contains(m.Key, StringComparison.OrdinalIgnoreCase) ||
                            m.Key.Contains(nombreLimpio, StringComparison.OrdinalIgnoreCase));

                        p.MedioPagoId = matchAprox.Value > 0 ? matchAprox.Value : 1;
                    }
                }

                if (cab.PagosDesglosados.Any())
                {
                    cab.CondicionPagoId = cab.PagosDesglosados.First().MedioPagoId;
                }
                else if (mapaMedios.TryGetValue(cab.CondicionPagoNombre, out int cId))
                {
                    cab.CondicionPagoId = cId;
                    cab.PagosDesglosados.Add(new ImportacionPagoDetalleDTO
                    {
                        MedioPagoId = cId,
                        MedioPagoNombre = cab.CondicionPagoNombre,
                        Monto = cab.Total
                    });
                }
                else
                {
                    cab.CondicionPagoId = 1;
                }

                // 4. Duplicidad
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryComprobanteRegistrado);
                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@s", cab.Serie);
                AgregarParametro(cmd, "@n", cab.Numero);

                if (await cmd.ExecuteScalarAsync() != null)
                {
                    cab.EsValido = false;
                    cab.MensajeError += "¡Comprobante ya registrado en el sistema! ";
                }

                // 5. Validación de Productos y Códigos por Sede
                foreach (var det in cab.Detalles)
                {
                    det.EsValido = true;

                    var matchProd = listaProductos.FirstOrDefault(p =>
                        p.Descripcion.Equals(det.DescripcionExcel, StringComparison.OrdinalIgnoreCase) ||
                        p.Descripcion.Replace(" ", "").Contains(det.DescripcionExcel.Replace(" ", "")) ||
                        det.DescripcionExcel.Replace(" ", "").Contains(p.Descripcion.Replace(" ", "")));

                    if (matchProd.Id > 0)
                    {
                        det.ProductoSistemaId = matchProd.Id;
                        det.DescripcionSistema = matchProd.Descripcion;
                        det.AbreviaturaOficial = matchProd.Abreviatura;

                        foreach (var cod in det.Codigos)
                        {
                            if (cod.CorrelativoExtraido <= 0)
                            {
                                cod.EsValido = false;
                                cod.CodigoSistema = "[ CORRELATIVO INVÁLIDO ]";
                                cod.MensajeValidacion = $"⛔ Correlativo inválido ({cod.CodigoExcel})";
                                det.EsValido = false;
                                cab.EsValido = false;
                                continue;
                            }

                            string numD7 = cod.CorrelativoExtraido.ToString("D7");
                            string patronNumero = $"%-{numD7}";
                            string codExactoEsperado = !string.IsNullOrEmpty(det.AbreviaturaOficial) ? $"{det.AbreviaturaOficial}-{numD7}" : cod.CodigoExcel;

                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryKardexCodigo);
                            cmd.Parameters.Clear();
                            AgregarParametro(cmd, "@ProdId", det.ProductoSistemaId.Value);
                            AgregarParametro(cmd, "@patronNumero", patronNumero);
                            AgregarParametro(cmd, "@codExacto", codExactoEsperado);

                            int codigoCreadoId = 0;
                            string codigoRealBD = string.Empty;
                            int estadoId = 0;
                            int codigoAlmacenId = 0;
                            int movId = 0;
                            bool existe = false;

                            using (var rdrCod = await cmd.ExecuteReaderAsync())
                            {
                                if (await rdrCod.ReadAsync())
                                {
                                    existe = true;
                                    codigoCreadoId = rdrCod.GetInt32(0);
                                    codigoRealBD = rdrCod.GetString(1);
                                    estadoId = rdrCod.GetInt32(2);
                                    codigoAlmacenId = rdrCod.GetInt32(3);
                                    movId = rdrCod.GetInt32(4);
                                }
                            }

                            if (!existe)
                            {
                                cod.EsValido = false;
                                cod.CodigoCreadoId = null;
                                cod.CodigoSistema = "[ NO EXISTE EN KÁRDEX ]";
                                cod.MensajeValidacion = $"⛔ NO EXISTE EN KÁRDEX (Esperado: {det.AbreviaturaOficial}-{numD7})";
                                det.EsValido = false;
                                cab.EsValido = false;
                                cab.MensajeError += $"Código '{cod.CodigoExcel}' no existe en Kárdex. ";
                                continue;
                            }

                            cod.CodigoCreadoId = codigoCreadoId;
                            cod.CodigoSistema = codigoRealBD;
                            cod.MovimientoKardexId = movId;

                            // 🛑 🌟 CANDADO MULTI-SEDE: Validar que el código pertenezca a la sede activa
                            if (codigoAlmacenId != almacenId)
                            {
                                cod.EsValido = false;
                                cod.MensajeValidacion = $"⛔ ERROR: PERTENECE A OTRA SEDE (Almacén ID: {codigoAlmacenId})";
                                det.EsValido = false;
                                cab.EsValido = false;
                                cab.MensajeError += $"Código {codigoRealBD} está en otra sede (Almacén {codigoAlmacenId}). ";
                                continue;
                            }

                            // 🛑 Candado: Verificar si ya fue facturado
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryFacturado);
                            cmd.Parameters.Clear();
                            AgregarParametro(cmd, "@CodId", codigoCreadoId);

                            string docFacturado = string.Empty;
                            using (var rdrF = await cmd.ExecuteReaderAsync())
                            {
                                if (await rdrF.ReadAsync())
                                {
                                    docFacturado = $"{rdrF.GetString(0)}-{rdrF.GetString(1)}";
                                }
                            }

                            if (!string.IsNullOrEmpty(docFacturado))
                            {
                                cod.EsValido = false;
                                cod.MensajeValidacion = $"⛔ YA FACTURADO EN [{docFacturado}]";
                                det.EsValido = false;
                                cab.EsValido = false;
                                cab.MensajeError += $"Código {codigoRealBD} ya facturado en {docFacturado}. ";
                                continue;
                            }

                            // 🛑 CANDADO DE ESTADO: SOLO ESTADO 4 ES VÁLIDO
                            switch (estadoId)
                            {
                                case 4:
                                    cod.EsValido = true;
                                    cod.MensajeValidacion = "✓ LISTO (SALIDA NETA / DESPACHADO)";
                                    break;

                                case 3:
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = "⛔ ERROR: CÓDIGO EN ALMACÉN (SIN SALIDA PREVIA)";
                                    det.EsValido = false;
                                    cab.EsValido = false;
                                    cab.MensajeError += $"Código {codigoRealBD} figura en almacén. ";
                                    break;

                                case 5:
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = "⛔ ERROR: CÓDIGO EN TRÁNSITO";
                                    det.EsValido = false;
                                    cab.EsValido = false;
                                    cab.MensajeError += $"Código {codigoRealBD} en tránsito. ";
                                    break;

                                default:
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = $"⛔ ERROR: ESTADO {estadoId} NO APTO";
                                    det.EsValido = false;
                                    cab.EsValido = false;
                                    cab.MensajeError += $"Código {codigoRealBD} en estado {estadoId}. ";
                                    break;
                            }
                        }
                    }
                    else
                    {
                        det.EsValido = false;
                        det.DescripcionSistema = "[ PRODUCTO NO REGISTRADO ]";
                        det.MensajeError = "Producto no encontrado en catálogo";
                        cab.EsValido = false;
                        cab.MensajeError += $"Producto '{det.DescripcionExcel}' desconocido. ";

                        foreach (var cod in det.Codigos)
                        {
                            cod.EsValido = false;
                            cod.CodigoSistema = "[ NO EXISTE ]";
                            cod.MensajeValidacion = "⛔ PRODUCTO NO REGISTRADO EN EL SISTEMA";
                        }
                    }
                }
            }
        }

        public async Task<int> TransferirComprobantesValidosAsync(List<ImportacionCabeceraDTO> comprobantesValidos, int idUsuario, int almacenId)
        {
            var procesables = comprobantesValidos.Where(c => c.EsValido).ToList();
            if (!procesables.Any()) return 0;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var trans = await dbConn.BeginTransactionAsync();
            int countExito = 0;
            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            try
            {
                foreach (var cab in procesables)
                {
                    string tipoDocSunat;
                    if (cab.DocumentoExcel.Contains("FACT") || cab.Serie.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                    {
                        tipoDocSunat = "01"; // Factura
                    }
                    else if (cab.DocumentoExcel.Contains("REC") || cab.Serie.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                    {
                        tipoDocSunat = "03"; // Recibo
                    }
                    else
                    {
                        tipoDocSunat = "02"; // Boleta
                    }

                    // 🌟 Se asigna @almId con el almacén de la sesión activa
                    string sqlCab = $@"
                INSERT INTO facturacion_cabecera (
                    empresa_id, tipo_documento, serie_documento, numero_documento,
                    fecha_emision, punto_venta_id, almacen_id, comprador_id,
                    observacion, total_gravado, total_inafecto, total_exonerado,
                    moneda_id, condicion_pago_id, total_igv, importe_total,
                    monto_delivery, porcentaje_igv, fecha_registro, usuario_id, estado_registro
                ) VALUES (
                    @empId, @tipoDoc, @serie, @numero,
                    @fEmision, 1, @almId, @compradorId,
                    'IMPORTADO DESDE NISIRA', @grav, 0.00, @exon,
                    @monId, @condId, @igv, @total,
                    @delivery, 0.00, NOW(), @usrId, 1
                ); {selectId}";

                    int cabeceraId;
                    using (var cmdCab = dbConn.CreateCommand())
                    {
                        cmdCab.Transaction = trans;
                        cmdCab.CommandText = QueryAdapter.FormatearConsulta(sqlCab);

                        AgregarParametro(cmdCab, "@empId", cab.EmpresaId);
                        AgregarParametro(cmdCab, "@tipoDoc", tipoDocSunat);
                        AgregarParametro(cmdCab, "@serie", cab.Serie);
                        AgregarParametro(cmdCab, "@numero", cab.Numero);
                        AgregarParametro(cmdCab, "@fEmision", cab.Fecha);
                        AgregarParametro(cmdCab, "@almId", almacenId); // 👈 ID de la sede activa
                        AgregarParametro(cmdCab, "@compradorId", cab.CompradorId);
                        AgregarParametro(cmdCab, "@grav", cab.Afecto);
                        AgregarParametro(cmdCab, "@exon", cab.Exonerado);
                        AgregarParametro(cmdCab, "@monId", cab.MonedaId);
                        AgregarParametro(cmdCab, "@condId", cab.CondicionPagoId);
                        AgregarParametro(cmdCab, "@igv", cab.IGV);
                        AgregarParametro(cmdCab, "@total", cab.Total);
                        AgregarParametro(cmdCab, "@delivery", cab.MontoDelivery);
                        AgregarParametro(cmdCab, "@usrId", idUsuario);

                        var resCab = await cmdCab.ExecuteScalarAsync();
                        cabeceraId = Convert.ToInt32(resCab);
                    }

                    // 2. Pagos Desglosados en facturacion_pagos_detalle
                    foreach (var pago in cab.PagosDesglosados)
                    {
                        if (pago.Monto <= 0) continue;

                        int medioIdFinal = pago.MedioPagoId > 0 ? pago.MedioPagoId : 1;

                        string sqlPago = @"
                    INSERT INTO facturacion_pagos_detalle (
                        facturacion_cabecera_id, medio_pago_id, monto, observacion, created_at
                    ) VALUES (
                        @cabId, @medioId, @monto, @obs, NOW()
                    );";

                        using var cmdPago = dbConn.CreateCommand();
                        cmdPago.Transaction = trans;
                        cmdPago.CommandText = QueryAdapter.FormatearConsulta(sqlPago);
                        AgregarParametro(cmdPago, "@cabId", cabeceraId);
                        AgregarParametro(cmdPago, "@medioId", medioIdFinal);
                        AgregarParametro(cmdPago, "@monto", pago.Monto);
                        AgregarParametro(cmdPago, "@obs", pago.MedioPagoNombre);
                        await cmdPago.ExecuteNonQueryAsync();
                    }

                    // 3. Detalles de productos
                    int numLinea = 1;
                    foreach (var det in cab.Detalles)
                    {
                        if (!det.ProductoSistemaId.HasValue) continue;

                        int movIdDetectado = 0;
                        if (det.Codigos.Any(c => c.MovimientoKardexId.HasValue && c.MovimientoKardexId.Value > 0))
                        {
                            movIdDetectado = det.Codigos.First(c => c.MovimientoKardexId.HasValue).MovimientoKardexId!.Value;
                        }

                        string sqlDet = $@"
                    INSERT INTO facturacion_detalle (
                        facturacion_cabecera_id, movimiento_id, producto_id, numero_linea,
                        cantidad, precio_unitario, valor_gravado, valor_inafecto,
                        valor_exonerado, valor_igv, importe_total
                    ) VALUES (
                        @cabId, @movId, @prodId, @linea,
                        @cant, @precio, 0.00, 0.00,
                        @total, 0.00, @total
                    ); {selectId}";

                        int detalleId;
                        using (var cmdDet = dbConn.CreateCommand())
                        {
                            cmdDet.Transaction = trans;
                            cmdDet.CommandText = QueryAdapter.FormatearConsulta(sqlDet);
                            AgregarParametro(cmdDet, "@cabId", cabeceraId);
                            AgregarParametro(cmdDet, "@movId", movIdDetectado);
                            AgregarParametro(cmdDet, "@prodId", det.ProductoSistemaId.Value);
                            AgregarParametro(cmdDet, "@linea", numLinea++);
                            AgregarParametro(cmdDet, "@cant", det.Cantidad);
                            AgregarParametro(cmdDet, "@precio", det.PrecioUnitario);
                            AgregarParametro(cmdDet, "@total", det.Importe);

                            var resDet = await cmdDet.ExecuteScalarAsync();
                            detalleId = Convert.ToInt32(resDet);
                        }

                        // 4. Códigos Físicos y Actualización a Estado 4 (VENDIDO)
                        foreach (var cod in det.Codigos)
                        {
                            if (!cod.CodigoCreadoId.HasValue || cod.CodigoCreadoId.Value <= 0) continue;

                            string sqlCod = @"
                        INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id)
                        VALUES (@detId, @codId);";

                            using var cmdCod = dbConn.CreateCommand();
                            cmdCod.Transaction = trans;
                            cmdCod.CommandText = QueryAdapter.FormatearConsulta(sqlCod);
                            AgregarParametro(cmdCod, "@detId", detalleId);
                            AgregarParametro(cmdCod, "@codId", cod.CodigoCreadoId.Value);
                            await cmdCod.ExecuteNonQueryAsync();

                            string sqlUpdKardex = "UPDATE codigos_creados SET estado_id = 4 WHERE id = @codId;";
                            using var cmdKardex = dbConn.CreateCommand();
                            cmdKardex.Transaction = trans;
                            cmdKardex.CommandText = QueryAdapter.FormatearConsulta(sqlUpdKardex);
                            AgregarParametro(cmdKardex, "@codId", cod.CodigoCreadoId.Value);
                            await cmdKardex.ExecuteNonQueryAsync();
                        }
                    }

                    countExito++;
                }

                await trans.CommitAsync();
                return countExito;
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                throw new Exception($"Falla durante la transferencia transaccional: {ex.Message}", ex);
            }
        }
    }
}