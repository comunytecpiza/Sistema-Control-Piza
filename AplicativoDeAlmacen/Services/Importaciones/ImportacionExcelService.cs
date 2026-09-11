#nullable enable

using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Facturación.AplicativoDeAlmacen.Models.Facturación;
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

        public async Task<List<ImportacionCabeceraDTO>> LeerExcelVentasAgrupadoAsync(string rutaArchivo)
        {
            var cabecerasAgrupadas = new List<ImportacionCabeceraDTO>();

            await Task.Run(() =>
            {
                using var stream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                // 🌟 1. DETECCIÓN INTELIGENTE DE COLUMNAS POR NOMBRE DE CABECERA
                var primeraFilaHeaders = ws.Row(1);
                int colCodigoInterno = 17; // Valor por defecto
                int colCantidad = 18;

                foreach (var cell in primeraFilaHeaders.CellsUsed())
                {
                    string headerText = cell.GetString().Trim().ToUpperInvariant();
                    if (headerText.Contains("CODIGO") || headerText.Contains("CÓDIGO") || headerText.Contains("INTERNO"))
                    {
                        colCodigoInterno = cell.Address.ColumnNumber;
                    }
                    else if (headerText.Equals("CANTIDAD") || headerText.Equals("CANT"))
                    {
                        colCantidad = cell.Address.ColumnNumber;
                    }
                }

                var filasRaw = new List<FilaPlanaExcel>();

                foreach (var row in ws.RowsUsed().Skip(1))
                {
                    try
                    {
                        string docTipo = row.Cell(1).GetString().Trim();
                        string serie = row.Cell(2).GetString().Trim();
                        string numero = row.Cell(3).GetString().Trim();

                        if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) continue;

                        decimal afecto = row.Cell(9).TryGetValue(out decimal afVal) ? afVal : 0m;
                        decimal igv = row.Cell(10).TryGetValue(out decimal igvVal) ? igvVal : 0m;
                        decimal exonerado = row.Cell(11).TryGetValue(out decimal exVal) ? exVal : 0m;
                        decimal importe = row.Cell(12).TryGetValue(out decimal impVal) ? impVal : 0m;
                        string prodDesc = row.Cell(13).GetString().Trim();
                        decimal precio = row.Cell(14).TryGetValue(out decimal prVal) ? prVal : 0m;
                        string instDesc = row.Cell(15).GetString().Trim();

                        // 🌟 2. EXTRACCIÓN SEGURA DEL CÓDIGO PLANO (Ej: "483" o "406")
                        string codExcel = string.Empty;
                        var celdaCod = row.Cell(colCodigoInterno);
                        if (!celdaCod.IsEmpty())
                        {
                            // Limpieza de caracteres y formato texto/número
                            codExcel = celdaCod.GetString().Trim();
                            if (string.IsNullOrEmpty(codExcel) && celdaCod.TryGetValue(out double dVal))
                            {
                                codExcel = ((long)dVal).ToString();
                            }
                        }

                        int cantFila = 1;
                        if (row.Cell(colCantidad).TryGetValue(out int cVal) && cVal > 0)
                        {
                            cantFila = cVal;
                        }

                        filasRaw.Add(new FilaPlanaExcel
                        {
                            Documento = docTipo,
                            Serie = serie,
                            Numero = numero,
                            RazonSocial = row.Cell(6).GetString().Trim(),
                            Moneda = row.Cell(7).GetString().Trim(),
                            Fecha = row.Cell(8).GetDateTime(),
                            Afecto = afecto,
                            IGV = igv,
                            Exonerado = exonerado,
                            Importe = importe,
                            Producto = prodDesc,
                            Precio = precio,
                            Institucion = instDesc,
                            CodigoInterno = codExcel,
                            Cantidad = cantFila
                        });
                    }
                    catch { }
                }

                cabecerasAgrupadas = filasRaw
                    .GroupBy(c => new { c.Documento, c.Serie, c.Numero })
                    .Select(gCab =>
                    {
                        var f = gCab.First();
                        var cab = new ImportacionCabeceraDTO
                        {
                            DocumentoExcel = gCab.Key.Documento,
                            Serie = gCab.Key.Serie,
                            Numero = gCab.Key.Numero,
                            Fecha = f.Fecha,
                            RazonSocialExcel = f.RazonSocial,
                            ClienteExcel = f.Institucion,
                            Moneda = f.Moneda,
                            Afecto = gCab.Sum(x => x.Afecto),
                            Exonerado = gCab.Sum(x => x.Exonerado),
                            IGV = gCab.Sum(x => x.IGV),
                            Total = gCab.Sum(x => x.Importe)
                        };

                        cab.Detalles = gCab
                            .GroupBy(d => new { d.Producto, d.Precio })
                            .Select(gDet =>
                            {
                                var codigosValidos = gDet
                                    .Where(c => !string.IsNullOrEmpty(c.CodigoInterno))
                                    .Select(c => new ImportacionCodigoDTO
                                    {
                                        CodigoExcel = c.CodigoInterno, // Número plano (ej: "483")
                                        Cantidad = 1,
                                        Error = "PENDIENTE DE VERIFICAR"
                                    }).ToList();

                                int cantidadFinal = codigosValidos.Any() ? codigosValidos.Count : gDet.Sum(x => x.Cantidad);
                                decimal importeFinal = gDet.Sum(x => x.Importe);

                                return new ImportacionDetalleDTO
                                {
                                    DescripcionExcel = gDet.Key.Producto,
                                    PrecioUnitario = gDet.Key.Precio,
                                    Cantidad = cantidadFinal > 0 ? cantidadFinal : 1,
                                    Importe = importeFinal,
                                    Codigos = codigosValidos
                                };
                            }).ToList();

                        return cab;
                    }).ToList();
            });

            return cabecerasAgrupadas;
        }

        public async Task ValidarDatosImportacionAsync(List<ImportacionCabeceraDTO> comprobantes)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            string queryExisteComprobante = QueryAdapter.EsMySQL
                ? "SELECT 1 FROM facturacion_cabecera WHERE serie_documento = @serie AND numero_documento = @numero AND estado_registro = 1 LIMIT 1;"
                : "SELECT TOP 1 1 FROM facturacion_cabecera WITH (NOLOCK) WHERE serie_documento = @serie AND numero_documento = @numero AND estado_registro = 1;";

            string queryPersona = QueryAdapter.EsMySQL
                ? @"SELECT id, COALESCE(razon_social, nombres) AS nombre_mostrar 
            FROM personas_comerciales 
            WHERE COALESCE(razon_social, '') LIKE @filtro 
               OR CONCAT(COALESCE(nombres, ''), ' ', COALESCE(apellido_paterno, '')) LIKE @filtro
               OR COALESCE(nombre_comercial, '') LIKE @filtro
            LIMIT 1;"
                : @"SELECT TOP 1 id, ISNULL(razon_social, nombres) AS nombre_mostrar 
            FROM personas_comerciales WITH (NOLOCK)
            WHERE ISNULL(razon_social, '') LIKE @filtro 
               OR LTRIM(RTRIM(ISNULL(nombres, '') + ' ' + ISNULL(apellido_paterno, ''))) LIKE @filtro
               OR ISNULL(nombre_comercial, '') LIKE @filtro;";

            string queryProducto = QueryAdapter.EsMySQL
                ? "SELECT id, descripcion, abreviatura FROM productos WHERE descripcion = @prod LIMIT 1;"
                : "SELECT TOP 1 id, descripcion, abreviatura FROM productos WITH (NOLOCK) WHERE descripcion = @prod;";

            // 🌟 Búsqueda del código amarrada estrictamente al producto
            string queryKardexCodigo = QueryAdapter.EsMySQL
                ? @"SELECT cc.id, cc.codigo, cc.estado_id, COALESCE(mc.movimiento_id, 0) AS movimiento_id
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
            LEFT JOIN movimiento_codigos mc ON mc.codigo_creado_id = cc.id
            WHERE rc.producto_id = @ProductoId
              AND (cc.codigo = @codExacto OR cc.codigo LIKE @codLike OR cc.codigo = @codPlano)
            ORDER BY cc.id DESC LIMIT 1;"
                : @"SELECT TOP 1 cc.id, cc.codigo, cc.estado_id, ISNULL(mc.movimiento_id, 0) AS movimiento_id
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
            LEFT JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            WHERE rc.producto_id = @ProductoId
              AND (cc.codigo = @codExacto OR cc.codigo LIKE @codLike OR cc.codigo = @codPlano)
            ORDER BY cc.id DESC;";

            // 🌟 Candado: Detectar si el código ya pertenece a algún comprobante emitido
            string queryFacturado = QueryAdapter.EsMySQL
                ? @"SELECT fc.serie_documento, fc.numero_documento, fc.tipo_documento
            FROM facturacion_detalle_codigos fdc
            INNER JOIN facturacion_detalle fd ON fdc.facturacion_detalle_id = fd.id
            INNER JOIN facturacion_cabecera fc ON fd.facturacion_cabecera_id = fc.id
            WHERE fdc.codigo_creado_id = @CodigoId AND fc.estado_registro = 1
            LIMIT 1;"
                : @"SELECT TOP 1 fc.serie_documento, fc.numero_documento, fc.tipo_documento
            FROM facturacion_detalle_codigos fdc WITH (NOLOCK)
            INNER JOIN facturacion_detalle fd WITH (NOLOCK) ON fdc.facturacion_detalle_id = fd.id
            INNER JOIN facturacion_cabecera fc WITH (NOLOCK) ON fd.facturacion_cabecera_id = fc.id
            WHERE fdc.codigo_creado_id = @CodigoId AND fc.estado_registro = 1;";

            foreach (var cabecera in comprobantes)
            {
                // 0. Duplicidad de Comprobante (Se marca pero NO se hace continue para procesar sus códigos)
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryExisteComprobante);
                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@serie", cabecera.Serie);
                AgregarParametro(cmd, "@numero", cabecera.Numero.PadLeft(7, '0'));

                if (await cmd.ExecuteScalarAsync() != null)
                {
                    cabecera.EsValido = false;
                    cabecera.MensajeError = "¡Comprobante ya registrado previamente en el sistema! ";
                }

                // 1. Pagador
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryPersona);
                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@filtro", "%" + cabecera.RazonSocialExcel.Trim() + "%");
                using (var rdr = await cmd.ExecuteReaderAsync())
                {
                    if (await rdr.ReadAsync())
                    {
                        cabecera.PagadorSistemaId = Convert.ToInt32(rdr["id"]);
                        cabecera.RazonSocialSistema = rdr["nombre_mostrar"]?.ToString() ?? cabecera.RazonSocialExcel;
                    }
                    else
                    {
                        cabecera.EsValido = false;
                        cabecera.MensajeError += "Razón Social no encontrada. ";
                    }
                }

                // 2. Institución
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryPersona);
                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@filtro", "%" + cabecera.ClienteExcel.Trim() + "%");
                using (var rdr = await cmd.ExecuteReaderAsync())
                {
                    if (await rdr.ReadAsync())
                    {
                        cabecera.ColegioSistemaId = Convert.ToInt32(rdr["id"]);
                        cabecera.ClienteSistema = rdr["nombre_mostrar"]?.ToString() ?? cabecera.ClienteExcel;
                    }
                    else
                    {
                        cabecera.EsValido = false;
                        cabecera.MensajeError += "Colegio no encontrado. ";
                    }
                }

                // 3. Productos
                foreach (var detalle in cabecera.Detalles)
                {
                    string abreviaturaProd = string.Empty;

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryProducto);
                    cmd.Parameters.Clear();
                    AgregarParametro(cmd, "@prod", detalle.DescripcionExcel.Trim());
                    using (var rdr = await cmd.ExecuteReaderAsync())
                    {
                        if (await rdr.ReadAsync())
                        {
                            detalle.ProductoSistemaId = Convert.ToInt32(rdr["id"]);
                            detalle.DescripcionSistema = rdr["descripcion"]?.ToString() ?? "";
                            abreviaturaProd = rdr["abreviatura"]?.ToString() ?? "";
                        }
                        else
                        {
                            detalle.EsValido = false;
                            cabecera.EsValido = false;
                            cabecera.MensajeError += $"Producto '{detalle.DescripcionExcel}' no encontrado. ";
                        }
                    }

                    // 4. Búsqueda y Validación de Códigos Físicos
                    if (detalle.ProductoSistemaId.HasValue && detalle.Codigos.Any())
                    {
                        foreach (var cod in detalle.Codigos)
                        {
                            string raw = cod.CodigoExcel.Trim();
                            int num = int.TryParse(raw, out int n) ? n : 0;
                            string numeroFormateado = num > 0 ? num.ToString("D7") : raw;

                            string codExacto = string.IsNullOrEmpty(abreviaturaProd) ? raw : $"{abreviaturaProd}-{numeroFormateado}";
                            string codLike = string.IsNullOrEmpty(abreviaturaProd) ? $"%-{numeroFormateado}" : $"{abreviaturaProd}%{numeroFormateado}";

                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryKardexCodigo);
                            cmd.Parameters.Clear();
                            AgregarParametro(cmd, "@ProductoId", detalle.ProductoSistemaId.Value);
                            AgregarParametro(cmd, "@codExacto", codExacto);
                            AgregarParametro(cmd, "@codLike", codLike);
                            AgregarParametro(cmd, "@codPlano", raw);

                            int codigoId = 0;
                            string codigoRealBD = string.Empty;
                            int estadoId = 0;
                            int movId = 0;
                            bool existe = false;

                            using (var rdrCod = await cmd.ExecuteReaderAsync())
                            {
                                if (await rdrCod.ReadAsync())
                                {
                                    existe = true;
                                    codigoId = Convert.ToInt32(rdrCod["id"]);
                                    codigoRealBD = rdrCod["codigo"]?.ToString() ?? codExacto;
                                    estadoId = Convert.ToInt32(rdrCod["estado_id"]);
                                    movId = Convert.ToInt32(rdrCod["movimiento_id"]);
                                }
                            }

                            if (!existe)
                            {
                                cod.EsValido = false;
                                string codigoEsperado = !string.IsNullOrEmpty(abreviaturaProd) ? $"{abreviaturaProd}-...-{numeroFormateado}" : raw;
                                cod.CodigoSistema = codigoEsperado;
                                cod.CodigoExcel = raw;
                                cod.MensajeValidacion = $"⛔ NO EXISTE EN KÁRDEX ({abreviaturaProd})";
                                cod.Error = cod.MensajeValidacion;

                                detalle.EsValido = false;
                                cabecera.EsValido = false;
                                cabecera.MensajeError += $"Código '{raw}' no pertenece a '{detalle.DescripcionExcel}'. ";
                                continue;
                            }

                            cod.CodigoCreadoId = codigoId;
                            cod.CodigoSistema = codigoRealBD;
                            cod.CodigoExcel = codigoRealBD;
                            cod.MovimientoKardexId = movId;

                            // 🛑 CANDADO FISCAL: Verificar si ya existe en facturacion_detalle_codigos
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryFacturado);
                            cmd.Parameters.Clear();
                            AgregarParametro(cmd, "@CodigoId", codigoId);

                            string docFacturado = string.Empty;
                            using (var rdrFact = await cmd.ExecuteReaderAsync())
                            {
                                if (await rdrFact.ReadAsync())
                                {
                                    string sDoc = rdrFact.GetString(0);
                                    string nDoc = rdrFact.GetString(1);
                                    docFacturado = $"{sDoc}-{nDoc}";
                                }
                            }

                            if (!string.IsNullOrEmpty(docFacturado))
                            {
                                cod.EsValido = false;
                                string msgError = $"⛔ YA FACTURADO (Doc: {docFacturado})";
                                cod.MensajeValidacion = msgError;
                                cod.Error = msgError;

                                detalle.EsValido = false;
                                cabecera.EsValido = false;
                                cabecera.MensajeError += $"Código {codigoRealBD} ya registrado en {docFacturado}. ";
                                continue;
                            }

                            // 🛑 EVALUACIÓN DE ESTADOS (Solo Estado 4 permitido para emitir comprobante)
                            switch (estadoId)
                            {
                                case 4: // Salida neta aprobada
                                    cod.EsValido = true;
                                    cod.MensajeValidacion = "✓ LISTO PARA TRANSFERIR (SALIDA NETA)";
                                    cod.Error = cod.MensajeValidacion;
                                    break;

                                case 3: // En almacén
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = "⛔ ERROR: CÓDIGO EN ALMACÉN (SIN SALIDA)";
                                    cod.Error = cod.MensajeValidacion;
                                    detalle.EsValido = false;
                                    cabecera.EsValido = false;
                                    cabecera.MensajeError += $"Código {codigoRealBD} figura en almacén. ";
                                    break;

                                case 5: // En tránsito
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = "⛔ ERROR: CÓDIGO EN TRÁNSITO";
                                    cod.Error = cod.MensajeValidacion;
                                    detalle.EsValido = false;
                                    cabecera.EsValido = false;
                                    cabecera.MensajeError += $"Código {codigoRealBD} está en tránsito. ";
                                    break;

                                case 1: // No registrado en stock
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = "⛔ ERROR: NO REGISTRADO EN STOCK";
                                    cod.Error = cod.MensajeValidacion;
                                    detalle.EsValido = false;
                                    cabecera.EsValido = false;
                                    cabecera.MensajeError += $"Código {codigoRealBD} sin stock inicial. ";
                                    break;

                                default:
                                    cod.EsValido = false;
                                    cod.MensajeValidacion = $"⛔ ERROR: ESTADO {estadoId} NO PERMITIDO";
                                    cod.Error = cod.MensajeValidacion;
                                    detalle.EsValido = false;
                                    cabecera.EsValido = false;
                                    cabecera.MensajeError += $"Código {codigoRealBD} con estado {estadoId}. ";
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public async Task<int> TransferirComprobantesValidosAsync(List<ImportacionCabeceraDTO> comprobantesValidos, int idUsuario)
        {
            int countExito = 0;
            var procesables = comprobantesValidos.Where(c => c.EsValido).ToList();

            foreach (var excelCab in procesables)
            {
                string tipoDocSunat = "03";
                string docUpper = excelCab.DocumentoExcel.ToUpperInvariant();
                if (docUpper.Contains("FACTURA") || excelCab.Serie.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                {
                    tipoDocSunat = "01";
                }
                else if (docUpper.Contains("BOLETA") || excelCab.Serie.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                {
                    tipoDocSunat = "02";
                }

                var cabeceraDB = new FacturacionCabecera
                {
                    TipoDocumento = tipoDocSunat,
                    SerieDocumento = excelCab.Serie,
                    NumeroDocumento = excelCab.Numero.PadLeft(7, '0'),
                    FechaEmision = excelCab.Fecha,
                    PuntoVentaId = 1,
                    CompradorId = excelCab.PagadorSistemaId,
                    InstitucionId = excelCab.ColegioSistemaId,
                    Observacion = "Importado desde Plantilla Excel Nisira",
                    TotalGravado = excelCab.Afecto,
                    TotalExonerado = excelCab.Exonerado,
                    TotalIgv = excelCab.IGV,
                    ImporteTotal = excelCab.Total,
                    PorcentajeIgv = 18.00m,
                    EstadoRegistro = true,
                    UsuarioId = idUsuario
                };

                int lineIndex = 1;

                foreach (var excelDet in excelCab.Detalles)
                {
                    decimal proporcion = excelCab.Total > 0 ? excelDet.Importe / excelCab.Total : 0m;

                    int? movIdDetectado = null;
                    if (excelDet.Codigos != null && excelDet.Codigos.Any(c => c.MovimientoKardexId.HasValue && c.MovimientoKardexId.Value > 0))
                    {
                        movIdDetectado = excelDet.Codigos.First(c => c.MovimientoKardexId.HasValue).MovimientoKardexId!.Value;
                    }

                    var detalleDB = new FacturacionDetalle
                    {
                        ProductoId = excelDet.ProductoSistemaId!.Value,
                        Cantidad = excelDet.Cantidad,
                        PrecioUnitario = excelDet.PrecioUnitario,
                        ImporteTotal = excelDet.Importe,
                        NumeroLinea = lineIndex++,
                        ValorGravado = Math.Round(excelCab.Afecto * proporcion, 2),
                        ValorExonerado = Math.Round(excelCab.Exonerado * proporcion, 2),
                        ValorIgv = Math.Round(excelCab.IGV * proporcion, 2),
                        ValorInafecto = 0m,
                        MovimientoId = movIdDetectado ?? 0
                    };

                    if (excelDet.Codigos != null)
                    {
                        foreach (var excelCod in excelDet.Codigos)
                        {
                            if (excelCod.CodigoCreadoId.HasValue && excelCod.CodigoCreadoId.Value > 0)
                            {
                                detalleDB.Codigos.Add(new FacturacionDetalleCodigos
                                {
                                    CodigoCreadoId = excelCod.CodigoCreadoId.Value
                                });
                            }
                        }
                    }

                    cabeceraDB.Detalles.Add(detalleDB);
                }

                try
                {
                    int serieId = await ObtenerIdSerieAsync(cabeceraDB.SerieDocumento);
                    await _facturacionService.GuardarComprobanteAsync(cabeceraDB, serieId);
                    countExito++;
                }
                catch (Exception ex)
                {
                    excelCab.EsValido = false;
                    excelCab.MensajeError = "Error al Transferir: " + ex.Message;
                    throw new Exception($"Falló la inserción del documento {cabeceraDB.SerieDocumento}-{cabeceraDB.NumeroDocumento}. Detalle técnico: {ex.Message}");
                }
            }

            return countExito;
        }

        private async Task<int> ObtenerIdSerieAsync(string numeroSerie)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            string querySerie = QueryAdapter.EsMySQL
                ? "SELECT id FROM series_documentos WHERE num_seri = @serie LIMIT 1;"
                : "SELECT TOP 1 id FROM series_documentos WITH (NOLOCK) WHERE num_seri = @serie;";

            cmd.CommandText = QueryAdapter.FormatearConsulta(querySerie);
            AgregarParametro(cmd, "@serie", numeroSerie);

            object? result = await cmd.ExecuteScalarAsync();
            if (result == null) throw new Exception($"La serie '{numeroSerie}' no está registrada en el sistema.");

            return Convert.ToInt32(result);
        }
    }
}