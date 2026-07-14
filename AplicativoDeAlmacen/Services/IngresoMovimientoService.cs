using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Collections.ObjectModel;

namespace AplicativoDeAlmacen.Services
{
    public class IngresoMovimientoService
    {
        private readonly DatabaseConnection _database;

        // Resultado que devuelve la entidad Movimiento (EF) y las colecciones relacionadas
        public class MovimientoCompletoResult
        {
            public Movimiento Movimiento { get; set; }
            public List<MovimientoDetalle> Detalles { get; set; } = new List<MovimientoDetalle>();
            public List<RangoCodigoItem> Rangos { get; set; } = new List<RangoCodigoItem>();
        }

        public IngresoMovimientoService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<MovimientoCompletoResult> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var result = new MovimientoCompletoResult();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            bool numeroEsEntero = int.TryParse(numero, out int numeroIntVal);

            string query = @"
                SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
                       m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion,
                       mp.descripcion AS motivo_desc, pc.razon_social, u.descripcion AS ubicacion_desc
                FROM movimientos m
                LEFT JOIN motivo_productos mp ON mp.id = m.motivo_producto_id
                LEFT JOIN personas_comerciales pc ON pc.id = m.persona_comercial_id
                LEFT JOIN ubicaciones u ON u.id = m.ubicacion_id
                WHERE (m.numero_documento = @numero" + (numeroEsEntero ? " OR TRY_CAST(m.numero_documento AS INT) = @numeroInt" : "") + ")";

            if (!string.IsNullOrEmpty(serie)) query += " AND m.serie_documento = @serie";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@numero"; p1.Value = numero; cmd.Parameters.Add(p1);
                if (numeroEsEntero) { var pInt = cmd.CreateParameter(); pInt.ParameterName = "@numeroInt"; pInt.Value = numeroIntVal; cmd.Parameters.Add(pInt); }
                if (!string.IsNullOrEmpty(serie)) { var p2 = cmd.CreateParameter(); p2.ParameterName = "@serie"; p2.Value = serie; cmd.Parameters.Add(p2); }

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                // Mapear a la entidad Movimiento existente
                var mov = new Movimiento
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FechaMovimiento = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = reader["serie_documento"] as string,
                    NumeroDocumento = reader["numero_documento"] as string,
                    MotivoProductoId = reader.IsDBNull(reader.GetOrdinal("motivo_producto_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = reader.IsDBNull(reader.GetOrdinal("persona_comercial_id")) ? null : reader.GetInt32(reader.GetOrdinal("persona_comercial_id")),
                    SerieGuia = reader.IsDBNull(reader.GetOrdinal("serie_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("serie_guia")),
                    NumeroGuia = reader.IsDBNull(reader.GetOrdinal("numero_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("numero_guia")),
                    Observacion = reader.IsDBNull(reader.GetOrdinal("observacion")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacion")),
                    UbicacionId = reader.IsDBNull(reader.GetOrdinal("ubicacion_id")) ? null : reader.GetInt32(reader.GetOrdinal("ubicacion_id"))
                };

                result.Movimiento = mov;
            }

            // Detalles
            string qDet = @"SELECT id, producto_id, cantidad_ingreso, costo_unitario FROM movimiento_detalles WHERE movimiento_id = @movId";
            using (var cmdDet = dbConn.CreateCommand())
            {
                cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDet);
                var pdet = cmdDet.CreateParameter(); pdet.ParameterName = "@movId"; pdet.Value = result.Movimiento.Id; cmdDet.Parameters.Add(pdet);
                using var rdrDet = await cmdDet.ExecuteReaderAsync();
                while (await rdrDet.ReadAsync())
                {
                    result.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rdrDet.GetInt32(0),
                        ProductoId = rdrDet.GetInt32(1),
                        CantidadIngreso = rdrDet.GetDecimal(2),
                        CostoUnitario = rdrDet.IsDBNull(3) ? (decimal?)null : rdrDet.GetDecimal(3)
                    });
                }
            }

            // Rangos asociados por detalle
            string qRangos = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
            using (var cmdR = dbConn.CreateCommand())
            {
                cmdR.CommandText = QueryAdapter.FormatearConsulta(qRangos);
                var pr = cmdR.CreateParameter(); pr.ParameterName = "@movId"; pr.Value = result.Movimiento.Id; cmdR.Parameters.Add(pr);
                using var rdrR = await cmdR.ExecuteReaderAsync();
                while (await rdrR.ReadAsync())
                {
                    int desdeNum = rdrR.GetInt32(rdrR.GetOrdinal("desde_num"));
                    int hastaNum = rdrR.GetInt32(rdrR.GetOrdinal("hasta_num"));
                    int categoriaId = rdrR.GetInt32(rdrR.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdrR.IsDBNull(rdrR.GetOrdinal("abreviatura_base")) ? string.Empty : rdrR.GetString(rdrR.GetOrdinal("abreviatura_base"));

                    // Construir representación textual del rango (Desde/Hasta) y el tipo de colección
                    string desdeText = $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = $"{baseAbrev}-{hastaNum:D7}";
                    string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionTipo = $"C2026 / {tipoTexto}";

                    result.Rangos.Add(new RangoCodigoItem
                    {
                        MovimientoDetalleId = rdrR.IsDBNull(rdrR.GetOrdinal("movimiento_detalle_id")) ? 0 : rdrR.GetInt32(rdrR.GetOrdinal("movimiento_detalle_id")),
                        productoId = rdrR.GetInt32(rdrR.GetOrdinal("producto_id")),
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = baseAbrev,
                        DesdeNum = desdeNum,
                        HastaNum = hastaNum,
                        Cantidad = (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeText,
                        Hasta = hastaText,
                        ColeccionTipo = coleccionTipo
                    });
                }
            }

            return result;
        }

        public async Task<List<RangoCodigoItem>> GetRangosByMovimientoDetalleIdAsync(int movimientoDetalleId)
        {
            var lista = new List<RangoCodigoItem>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string q = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id
                         FROM registro_rangos WHERE movimiento_detalle_id = @detId";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                var p = cmd.CreateParameter(); p.ParameterName = "@detId"; p.Value = movimientoDetalleId; cmd.Parameters.Add(p);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int desdeNum = rdr.GetInt32(rdr.GetOrdinal("desde_num"));
                    int hastaNum = rdr.GetInt32(rdr.GetOrdinal("hasta_num"));
                    int categoriaId = rdr.GetInt32(rdr.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdr.IsDBNull(rdr.GetOrdinal("abreviatura_base")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("abreviatura_base"));

                    string desdeText = $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = $"{baseAbrev}-{hastaNum:D7}";
                    string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionTipo = $"C2026 / {tipoTexto}";

                    lista.Add(new RangoCodigoItem
                    {
                        MovimientoDetalleId = rdr.IsDBNull(rdr.GetOrdinal("movimiento_detalle_id")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("movimiento_detalle_id")),
                        productoId = rdr.GetInt32(rdr.GetOrdinal("producto_id")),
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = baseAbrev,
                        DesdeNum = desdeNum,
                        HastaNum = hastaNum,
                        Cantidad = (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeText,
                        Hasta = hastaText,
                        ColeccionTipo = coleccionTipo
                    });
                }
            }
            return lista;
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        public async Task<List<MotivoProducto>> ObtenerMotivosProductosAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, tipo_movimiento 
                                 FROM motivo_productos 
                                 WHERE tipo_movimiento = 'entrada' 
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var motivo = new MotivoProducto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                TipoMovimiento = reader.IsDBNull(reader.GetOrdinal("tipo_movimiento"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("tipo_movimiento"))
                            };
                            lista.Add(motivo);
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<Movimiento> GenerarSiguienteCorrelativoAsync(string serie)
        {
            var resultado = new Movimiento
            {
                SerieDocumento = serie,
                NumeroDocumento = "0000001"
            };

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // CAMBIO MULTI-MOTOR: ISNULL -> COALESCE
                string query = @"
                    SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 
                    FROM movimientos 
                    WHERE serie_documento = @serie";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@serie", serie);

                    object resultObj = await cmd.ExecuteScalarAsync();

                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        int siguienteNumero = Convert.ToInt32(resultObj);
                        resultado.NumeroDocumento = siguienteNumero.ToString("D7");
                    }
                }
            }
            return resultado;
        }


        public async Task<bool> RegistrarMovimientoCompletoAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> productos,
            List<RangoCodigoItem> rangos,
            int ubicacionId,
            int? existingMovimientoId = null)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using (var transaccion = dbConn.BeginTransaction())
                {
                    try
                    {
                        string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                        int movimientoIdInserted = 0;

                        // =======================================================
                        // 1. GENERACIÓN DE CORRELATIVO SEGURO (BOMBA 3 DESACTIVADA)
                        // =======================================================
                        if (!existingMovimientoId.HasValue)
                        {
                            string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "0001" : cabecera.SerieDocumento;

                            // 🌟 MAGIA ANTI-COLISIÓN: Bloqueo Pesimista
                            // SQL Server usa WITH (UPDLOCK, HOLDLOCK) y MySQL usa FOR UPDATE
                            string bloqueoConcurrencia = QueryAdapter.EsMySQL ? "FOR UPDATE" : "WITH (UPDLOCK, HOLDLOCK)";

                            using (var cmdGen = dbConn.CreateCommand())
                            {
                                cmdGen.Transaction = transaccion;

                                cmdGen.CommandText = QueryAdapter.FormatearConsulta($@"
                                    SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 
                                    FROM movimientos {bloqueoConcurrencia}
                                    WHERE serie_documento = @serie");

                                AgregarParametro(cmdGen, "@serie", serieParaGenerar);

                                object genRes = await cmdGen.ExecuteScalarAsync();
                                int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;

                                cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                                cabecera.SerieDocumento = serieParaGenerar;
                            }
                        }

                        // =======================================================
                        // 2. VALIDACIÓN PREVIA (Kardex y Estados)
                        // =======================================================
                        var codigosInvalidos = new List<string>();
                        var codigosFaltantes = new List<string>();
                        int estadoPermitido = cabecera.MotivoProductoId == 1 ? 1 : 4;

                        var existingCodigoIds = new HashSet<int>();
                        var existingSeqs = new HashSet<int>();

                        if (existingMovimientoId.HasValue)
                        {
                            using (var cmdExist = dbConn.CreateCommand())
                            {
                                cmdExist.Transaction = transaccion;
                                cmdExist.CommandText = QueryAdapter.FormatearConsulta(@"
                                    SELECT cc.id, TRY_CAST(RIGHT(cc.codigo, 7) AS INT) AS seq
                                    FROM movimiento_codigos mc
                                    JOIN codigos_creados cc ON cc.id = mc.codigo_creado_id
                                    WHERE mc.movimiento_id = @movId");
                                AgregarParametro(cmdExist, "@movId", existingMovimientoId.Value);
                                using (var rdrExist = await cmdExist.ExecuteReaderAsync())
                                {
                                    while (await rdrExist.ReadAsync())
                                    {
                                        int id = rdrExist.IsDBNull(0) ? 0 : rdrExist.GetInt32(0);
                                        int seq = rdrExist.IsDBNull(1) ? 0 : rdrExist.GetInt32(1);
                                        if (id != 0) existingCodigoIds.Add(id);
                                        if (seq != 0) existingSeqs.Add(seq);
                                    }
                                }
                            }

                            using (var cmdExist2 = dbConn.CreateCommand())
                            {
                                cmdExist2.Transaction = transaccion;
                                cmdExist2.CommandText = QueryAdapter.FormatearConsulta(@"
                                    SELECT DISTINCT cc.id, TRY_CAST(RIGHT(cc.codigo, 7) AS INT) AS seq
                                    FROM movimiento_detalles md
                                    JOIN registro_rangos rr ON rr.movimiento_detalle_id = md.id
                                    JOIN movimiento_codigos mc ON mc.movimiento_detalle_id = md.id
                                    JOIN codigos_creados cc ON cc.id = mc.codigo_creado_id
                                    WHERE md.movimiento_id = @movId");
                                AgregarParametro(cmdExist2, "@movId", existingMovimientoId.Value);
                                using (var rdrExist2 = await cmdExist2.ExecuteReaderAsync())
                                {
                                    while (await rdrExist2.ReadAsync())
                                    {
                                        int id = rdrExist2.IsDBNull(0) ? 0 : rdrExist2.GetInt32(0);
                                        int seq = rdrExist2.IsDBNull(1) ? 0 : rdrExist2.GetInt32(1);
                                        if (id != 0) existingCodigoIds.Add(id);
                                        if (seq != 0) existingSeqs.Add(seq);
                                    }
                                }
                            }
                        }

                        foreach (var itemVal in productos)
                        {
                            var rangosParaProductoVal = rangos.Where(r => r.productoId == itemVal.ProductoId).ToList();
                            foreach (var rangoVal in rangosParaProductoVal)
                            {
                                // 🌟 PASAMOS EL ID DEL PRODUCTO ACTUAL
                                    var idsCodigos = await ObtenerIdsCodigosPorRangoAsync(
                                    itemVal.ProductoId,
                                    rangoVal.AbreviaturaBase,
                                    rangoVal.CategoriaProductoId,
                                    rangoVal.DesdeNum,
                                    rangoVal.HastaNum,
                                    dbConn,
                                    transaccion);

                                    // AQUÍ ESTABA EL ERROR
                                    var foundSeqs = idsCodigos
                                        .Select(x => x.Seq)
                                        .ToHashSet();

                                    foreach (var tup in idsCodigos)
                                    {
                                        var codigoObj = tup.CodigoObj;

                                        if (existingCodigoIds.Contains(codigoObj.Id))
                                            continue;

                                        if (codigoObj.EstadoId != estadoPermitido)
                                        {
                                            codigosInvalidos.Add(
                                                $"{codigoObj.Codigo} (Producto:{itemVal.ProductoId}, Estado:{codigoObj.EstadoId})");
                                        }
                                    }

                                    for (int s = rangoVal.DesdeNum; s <= rangoVal.HastaNum; s++)
                                    {
                                        if (!foundSeqs.Contains(s) && !existingSeqs.Contains(s))
                                        {
                                            codigosFaltantes.Add($"{rangoVal.AbreviaturaBase}-{s:D7}");
                                        }
                                    }
                                }
                            }

                            if (codigosInvalidos.Any() || codigosFaltantes.Any())
                            {
                                var sb = new System.Text.StringBuilder();
                                sb.AppendLine("Validación de códigos fallida:");
                                if (codigosInvalidos.Any())
                                {
                                    sb.AppendLine($"Códigos con estado incorrecto (se requiere estado {estadoPermitido}):");
                                    foreach (var c in codigosInvalidos.Take(200)) sb.AppendLine(" - " + c);
                                    if (codigosInvalidos.Count > 200) sb.AppendLine($"... y {codigosInvalidos.Count - 200} más");
                                }
                                if (codigosFaltantes.Any())
                                {
                                    sb.AppendLine("Códigos faltantes en la base (no encontrados):");
                                    foreach (var c in codigosFaltantes.Take(200)) sb.AppendLine(" - " + c);
                                    if (codigosFaltantes.Count > 200) sb.AppendLine($"... y {codigosFaltantes.Count - 200} más");
                                }
                                throw new Exception(sb.ToString());
                            }

                            // =======================================================
                            // 3. INSERTAR / ACTUALIZAR CABECERA
                            // =======================================================
                            if (existingMovimientoId.HasValue)
                            {
                                string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                                using (var cmdUpdCab = dbConn.CreateCommand())
                                {
                                    cmdUpdCab.Transaction = transaccion;
                                    cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                                    DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
                                    AgregarParametro(cmdUpdCab, "@fecha", fechaConvertida);
                                    AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                                    AgregarParametro(cmdUpdCab, "@ubicacionId", cabecera.UbicacionId);
                                    AgregarParametro(cmdUpdCab, "@usuarioId", cabecera.UsuarioId);
                                    AgregarParametro(cmdUpdCab, "@personaId", cabecera.PersonaComercialId);
                                    AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                                    AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                                    AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                                    AgregarParametro(cmdUpdCab, "@id", existingMovimientoId.Value);
                                    await cmdUpdCab.ExecuteNonQueryAsync();
                                }
                                movimientoIdInserted = existingMovimientoId.Value;
                            }
                            else
                            {
                                string queryCabecera = $@"
                                INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                                         motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia)
                                VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId, @serieGuia, @numeroGuia);
                                {selectId}";

                                using (var cmdCab = dbConn.CreateCommand())
                                {
                                    cmdCab.Transaction = transaccion;
                                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                                    DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
                                    AgregarParametro(cmdCab, "@estadoId", 1);
                                    AgregarParametro(cmdCab, "@fecha", fechaConvertida);
                                    AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                                    AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                                    AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                                    AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId);
                                    AgregarParametro(cmdCab, "@usuarioId", cabecera.UsuarioId);
                                    AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
                                    AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                                    AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                                    AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);

                                    object resultCab = await cmdCab.ExecuteScalarAsync();
                                    if (resultCab == null || resultCab == DBNull.Value) throw new Exception("No se pudo obtener el ID de la cabecera.");
                                    movimientoIdInserted = Convert.ToInt32(resultCab);
                                }
                            }

                            // =======================================================
                            // 4. SINCRONIZACIÓN DE DETALLES (UPSERT)
                            // =======================================================
                            var idsDetallesActivos = new List<int>();

                            foreach (var item in productos)
                            {
                                int detalleId = 0;

                                // 4.1 ¿El detalle ya existe?
                                using (var cmdCheck = dbConn.CreateCommand())
                                {
                                    cmdCheck.Transaction = transaccion;
                                    cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND producto_id = @prodId");
                                    AgregarParametro(cmdCheck, "@movId", movimientoIdInserted);
                                    AgregarParametro(cmdCheck, "@prodId", item.ProductoId);
                                    object resDet = await cmdCheck.ExecuteScalarAsync();
                                    if (resDet != null && resDet != DBNull.Value) detalleId = Convert.ToInt32(resDet);
                                }

                                if (detalleId > 0)
                                {
                                    // Hacer UPDATE si ya existe
                                    using (var cmdUpd = dbConn.CreateCommand())
                                    {
                                        cmdUpd.Transaction = transaccion;
                                        cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_ingreso = @cant, costo_unitario = @costo WHERE id = @detId");
                                        AgregarParametro(cmdUpd, "@cant", item.Detalle?.CantidadIngreso ?? 0);
                                        AgregarParametro(cmdUpd, "@costo", item.Detalle?.CostoUnitario ?? 0);
                                        AgregarParametro(cmdUpd, "@detId", detalleId);
                                        await cmdUpd.ExecuteNonQueryAsync();
                                    }

                                    // Limpiamos sus rangos y códigos asociados para rehacerlos (Es seguro porque no tienen dependencias)
                                    using (var cmdClean = dbConn.CreateCommand())
                                    {
                                        cmdClean.Transaction = transaccion;
                                        cmdClean.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_detalle_id = @detId; DELETE FROM registro_rangos WHERE movimiento_detalle_id = @detId;");
                                        AgregarParametro(cmdClean, "@detId", detalleId);
                                        await cmdClean.ExecuteNonQueryAsync();
                                    }
                                }
                                else
                                {
                                    // Hacer INSERT si es nuevo
                                    string qDet = $@"INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at) VALUES (@movId, @prodId, @cant, 0, @costo, GETDATE()); {selectId}";
                                    using (var cmdIns = dbConn.CreateCommand())
                                    {
                                        cmdIns.Transaction = transaccion;
                                        cmdIns.CommandText = QueryAdapter.FormatearConsulta(qDet);
                                        AgregarParametro(cmdIns, "@movId", movimientoIdInserted);
                                        AgregarParametro(cmdIns, "@prodId", item.ProductoId);
                                        AgregarParametro(cmdIns, "@cant", item.Detalle?.CantidadIngreso ?? 0);
                                        AgregarParametro(cmdIns, "@costo", item.Detalle?.CostoUnitario ?? 0);
                                        detalleId = Convert.ToInt32(await cmdIns.ExecuteScalarAsync());
                                    }
                                }

                                idsDetallesActivos.Add(detalleId);

                                // 4.2 Insertar los nuevos Rangos y Códigos para este Detalle
                                string qRango = $@"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, usuario_id) VALUES (@prodId, @catId, @abrev, @desde, @hasta, @detId, @usr); {selectId}";

                                var rangosPorProd = rangos?.Where(r => r.productoId == item.ProductoId).ToList() ?? new List<RangoCodigoItem>();

                                foreach (var r in rangosPorProd)
                                {
                                    using (var cmdR = dbConn.CreateCommand())
                                    {
                                        cmdR.Transaction = transaccion;
                                        cmdR.CommandText = QueryAdapter.FormatearConsulta(qRango);
                                        AgregarParametro(cmdR, "@prodId", r.productoId);
                                        AgregarParametro(cmdR, "@catId", r.CategoriaProductoId);
                                        AgregarParametro(cmdR, "@abrev", r.AbreviaturaBase);
                                        AgregarParametro(cmdR, "@desde", r.DesdeNum);
                                        AgregarParametro(cmdR, "@hasta", r.HastaNum);
                                        AgregarParametro(cmdR, "@detId", detalleId);
                                        AgregarParametro(cmdR, "@usr", cabecera.UsuarioId);
                                        await cmdR.ExecuteNonQueryAsync();
                                    }

                                    var ids = await ObtenerIdsCodigosPorRangoAsync(
                                    r.productoId,
                                    r.AbreviaturaBase,
                                    r.CategoriaProductoId,
                                    r.DesdeNum,
                                    r.HastaNum,
                                    dbConn,
                                    transaccion);

                                    // Refrescar estados
                                    var idsParaRefrescar = ids.Where(t => t.CodigoObj != null).Select(t => t.CodigoObj.Id).Distinct().ToList();
                                    if (idsParaRefrescar.Any())
                                    {
                                        var paramNamesRef = new List<string>();
                                        for (int pi = 0; pi < idsParaRefrescar.Count; pi++) paramNamesRef.Add("@rp" + pi);
                                        string qRef = $"SELECT id, estado_id FROM codigos_creados WHERE id IN ({string.Join(',', paramNamesRef)})";
                                        using var cmdRef = dbConn.CreateCommand();
                                        cmdRef.Transaction = transaccion;
                                        cmdRef.CommandText = QueryAdapter.FormatearConsulta(qRef);
                                        for (int pi = 0; pi < idsParaRefrescar.Count; pi++)
                                        {
                                            var p = cmdRef.CreateParameter(); p.ParameterName = "@rp" + pi; p.Value = idsParaRefrescar[pi]; cmdRef.Parameters.Add(p);
                                        }
                                        using var rdrRef = await cmdRef.ExecuteReaderAsync();
                                        var estadoMap = new Dictionary<int, int>();
                                        while (await rdrRef.ReadAsync())
                                        {
                                            int id = rdrRef.IsDBNull(0) ? 0 : rdrRef.GetInt32(0);
                                            int est = rdrRef.IsDBNull(1) ? 0 : rdrRef.GetInt32(1);
                                            if (id > 0) estadoMap[id] = est;
                                        }
                                        foreach (var t in ids)
                                        {
                                            if (t.CodigoObj != null && estadoMap.TryGetValue(t.CodigoObj.Id, out int est)) t.CodigoObj.EstadoId = est;
                                        }
                                    }

                                    foreach (var tupCod in ids)
                                    {
                                        var codigoObj = tupCod.CodigoObj;
                                        bool perteneceAlMovimiento = existingCodigoIds.Contains(codigoObj.Id);

                                        if (!perteneceAlMovimiento && codigoObj.EstadoId != estadoPermitido)
                                        {
                                            throw new Exception($"El código {codigoObj.Codigo} (ID:{codigoObj.Id}) tiene estado {codigoObj.EstadoId} y no cumple el estado requerido ({estadoPermitido}) para el motivo seleccionado.");
                                        }

                                        using (var cmdMovCod = dbConn.CreateCommand())
                                        {
                                            cmdMovCod.Transaction = transaccion;
                                            cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(@"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES (@movId, @detId, @codId, 1, 0, GETDATE());");
                                            AgregarParametro(cmdMovCod, "@movId", movimientoIdInserted);
                                            AgregarParametro(cmdMovCod, "@detId", detalleId);
                                            AgregarParametro(cmdMovCod, "@codId", codigoObj.Id);
                                            await cmdMovCod.ExecuteNonQueryAsync();
                                        }

                                        if (!existingMovimientoId.HasValue || (!perteneceAlMovimiento && codigoObj.EstadoId == 1))
                                        {
                                            using (var cmdUpd = dbConn.CreateCommand())
                                            {
                                                cmdUpd.Transaction = transaccion;
                                                cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 3 WHERE id = @id");
                                                AgregarParametro(cmdUpd, "@id", codigoObj.Id);
                                                await cmdUpd.ExecuteNonQueryAsync();
                                            }
                                        }
                                    }
                                }
                            }

                            // =======================================================
                            // 5. BARRIDO DE HUÉRFANOS
                            // Si el usuario eliminó un producto al editar, borramos su detalle.
                            // =======================================================
                            if (existingMovimientoId.HasValue && idsDetallesActivos.Any())
                            {
                                var paramNames = new List<string>();
                                for (int i = 0; i < idsDetallesActivos.Count; i++) paramNames.Add("@act" + i);

                                string qLimpiar = $@"
                                    DELETE FROM movimiento_codigos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)}));
                                    DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)}));
                                    DELETE FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)});
                                ";

                                using (var cmdLimp = dbConn.CreateCommand())
                                {
                                    cmdLimp.Transaction = transaccion;
                                    cmdLimp.CommandText = QueryAdapter.FormatearConsulta(qLimpiar);
                                    AgregarParametro(cmdLimp, "@movId", movimientoIdInserted);
                                    for (int i = 0; i < idsDetallesActivos.Count; i++)
                                    {
                                        AgregarParametro(cmdLimp, "@act" + i, idsDetallesActivos[i]);
                                    }
                                    await cmdLimp.ExecuteNonQueryAsync();
                                }
                            }

                            transaccion.Commit();
                            return true;
                        }
                        catch (Exception)
                        {
                            transaccion.Rollback();
                            throw;
                        }
                    }
                }
        }
        private async Task<List<(CodigoCreado CodigoObj, int Seq)>> ObtenerIdsCodigosPorRangoAsync(
        int productoId,
        string baseLimpia,
        int categoriaId,
        int desde,
        int hasta,
        DbConnection conn,
        DbTransaction trans)
        {
            var resultados = new List<(CodigoCreado CodigoObj, int Seq)>();

            string prefijo = (baseLimpia ?? "").Length >= 3
                ? baseLimpia.Substring(0, 3) + "%"
                : "%";

            string query = @"
            SELECT
                cc.id,
                cc.registro_codigo_id,
                cc.codigo,
                cc.es_manual,
                cc.estado_id
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc
                ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id=@productoId
            AND rc.categoria_producto_id=@categoriaId
            AND cc.codigo LIKE @prefijo";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@productoId", productoId);
                AgregarParametro(cmd, "@categoriaId", categoriaId);
                AgregarParametro(cmd, "@prefijo", prefijo);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string codigoRaw = reader.IsDBNull(2) ? "" : reader.GetString(2);

                        string codigoNorm = NormalizarCodigo(codigoRaw);

                        System.Diagnostics.Debug.WriteLine("RAW  : " + codigoRaw);
                        System.Diagnostics.Debug.WriteLine("NORM : " + codigoNorm);

                        if (codigoNorm.Length >= 7)
                        {
                            string colaStr = codigoNorm.Substring(codigoNorm.Length - 7);

                            System.Diagnostics.Debug.WriteLine("COLA : " + colaStr);

                            if (int.TryParse(colaStr, out int seq))
                            {
                                System.Diagnostics.Debug.WriteLine("SEQ  : " + seq);

                                if (seq >= desde && seq <= hasta)
                                {
                                    resultados.Add((
                                        new CodigoCreado
                                        {
                                            Id = reader.GetInt32(0),
                                            RegistroCodigoId = reader.GetInt32(1),
                                            Codigo = codigoRaw,
                                            EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                                            EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                                        },
                                        seq));
                                }
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("TOTAL = " + resultados.Count);
                }
            }

            return resultados;
        }

        // Normaliza un código: elimina espacios y comillas, y pasa a mayúsculas
        public string NormalizarCodigo(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;
            // quitar espacios, comillas simples/dobles, apóstrofes tipográficos y distintas variedades de guion/dash
            var s = codigo.Replace(" ", "")
                          .Replace("'", "")
                          .Replace("\"", "")
                          .Replace("\u2019", "")
                          .Replace("\u2018", "")
                          .Replace("-", "")
                          .Replace("\u2010", "")
                          .Replace("\u2011", "")
                          .Replace("\u2012", "")
                          .Replace("\u2013", "")
                          .Replace("\u2014", "")
                          .Replace("\u2015", "")
                          .Replace("\u200B", ""); // zero-width space
            return s.ToUpperInvariant();
        }

        public async Task<Dictionary<string, (CodigoCreado CodigoObj, int? ProductoId)>> ObtenerCodigosPorListaAsync(IEnumerable<string> codigos)
        {
            var resultado = new Dictionary<string, (CodigoCreado, int?)>(StringComparer.OrdinalIgnoreCase);

            var listaRaw = codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (!listaRaw.Any()) return resultado;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            // 🌟 1. Preparamos las metas en la memoria caché
            var targetNorms = new HashSet<string>(listaRaw.Select(NormalizarCodigo), StringComparer.OrdinalIgnoreCase);
            var targetSeqs = new HashSet<int>();

            foreach (var code in targetNorms)
            {
                if (code.Length >= 7 && int.TryParse(code.Substring(code.Length - 7), out int seq))
                {
                    targetSeqs.Add(seq);
                }
            }

            // Procesar en lotes para evitar error de exceso de parámetros en SQL
            const int batchSize = 1000;
            for (int i = 0; i < targetSeqs.Count; i += batchSize)
            {
                var batchSeqs = targetSeqs.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();
                for (int j = 0; j < batchSeqs.Count; j++) paramNames.Add("@s" + j);

                // 🌟 2. Consulta simplificada sin múltiples REPLACE
                string queryExact = $@"
                SELECT cc.Id, cc.registro_codigo_id, cc.Codigo, cc.es_manual, cc.estado_id, rc.producto_id
                FROM codigos_creados cc
                LEFT JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
                WHERE TRY_CAST(RIGHT(cc.Codigo, 7) AS INT) IN ({string.Join(',', paramNames)})";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryExact);
                    for (int j = 0; j < batchSeqs.Count; j++)
                    {
                        AgregarParametro(cmd, "@s" + j, batchSeqs[j]);
                    }

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string codigoRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        string codNorm = NormalizarCodigo(codigoRaw);

                        // 🌟 3. Cruce veloz en memoria RAM
                        if (targetNorms.Contains(codNorm) && !resultado.ContainsKey(codNorm))
                        {
                            var obj = new CodigoCreado
                            {
                                Id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                                RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                Codigo = codigoRaw,
                                EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                                EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                            };
                            int? prodId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);

                            resultado[codNorm] = (obj, prodId);
                        }
                    }
                }
            }

            return resultado;
        }

        // Comprueba en la tabla movimiento_codigos qué códigos (por id) ya están asociados a algún movimiento.
        public async Task<HashSet<int>> ObtenerCodigosEnMovimientoAsync(IEnumerable<int> codigoIds)
        {
            var set = new HashSet<int>();
            var ids = codigoIds?.Distinct().ToList();
            if (ids == null || !ids.Any()) return set;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            const int batchSize = 1000;
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();
                for (int j = 0; j < batch.Count; j++) paramNames.Add("@p" + j);

                string q = $@"SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE codigo_creado_id IN ({string.Join(',', paramNames)})";
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                for (int j = 0; j < batch.Count; j++)
                {
                    var p = cmd.CreateParameter(); p.ParameterName = "@p" + j; p.Value = batch[j]; cmd.Parameters.Add(p);
                }

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0)) set.Add(rdr.GetInt32(0));
                }
            }

            return set;
        }

        // Registra códigos importados como un movimiento de ingreso (similar a RegistrarMovimientoCompletoAsync)
        // codigosImportados: lista de tuplas (CodigoCreadoId, ProductoId)
        // ------------------------------------------------------------------
        // MÉTODO OPCIONAL: Registrar ingreso básico SIN cambiar estados de códigos
        // Inserta cabecera, detalles, rangos y movimiento_codigos pero no actualiza
        // la columna estado_id en codigos_creados. Útil cuando quieres posponer
        // la actualización de estado hasta otro proceso (por ejemplo salida).
        public async Task<bool> RegistrarIngresoBasicoAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> productos,
            List<RangoCodigoItem> rangos,
            int usuarioId,
            int? existingMovimientoId = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaccion = dbConn.BeginTransaction();
            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                int movimientoIdInserted = 0;

                if (existingMovimientoId.HasValue)
                {
                    // Actualizar cabecera mínima
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using var cmdUpd = dbConn.CreateCommand();
                    cmdUpd.Transaction = transaccion;
                    cmdUpd.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                    DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
                    AgregarParametro(cmdUpd, "@fecha", fechaConvertida);
                    AgregarParametro(cmdUpd, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdUpd, "@ubicacionId", cabecera.UbicacionId);
                    AgregarParametro(cmdUpd, "@usuarioId", usuarioId);
                    AgregarParametro(cmdUpd, "@personaId", cabecera.PersonaComercialId ?? (object)DBNull.Value);
                    AgregarParametro(cmdUpd, "@observacion", cabecera.Observacion ?? (object)DBNull.Value);
                    AgregarParametro(cmdUpd, "@serieGuia", cabecera.SerieGuia ?? (object)DBNull.Value);
                    AgregarParametro(cmdUpd, "@numeroGuia", cabecera.NumeroGuia ?? (object)DBNull.Value);
                    AgregarParametro(cmdUpd, "@id", existingMovimientoId.Value);
                    await cmdUpd.ExecuteNonQueryAsync();

                    movimientoIdInserted = existingMovimientoId.Value;

                    // Limpiar datos antiguos para reinsertar
                    using var cmdDel = dbConn.CreateCommand();
                    cmdDel.Transaction = transaccion;
                    cmdDel.CommandText = QueryAdapter.FormatearConsulta(@"DELETE FROM movimiento_codigos WHERE movimiento_id = @movId; DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId); DELETE FROM movimiento_detalles WHERE movimiento_id = @movId;");
                    AgregarParametro(cmdDel, "@movId", movimientoIdInserted);
                    await cmdDel.ExecuteNonQueryAsync();
                }
                else
                {
                    string queryCabecera = $@"
                    INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia, created_at)
                    VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId, @serieGuia, @numeroGuia, GETDATE());
                    {selectId}";

                    using var cmdCab = dbConn.CreateCommand();
                    cmdCab.Transaction = transaccion;
                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                    DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
                    AgregarParametro(cmdCab, "@estadoId", 1);
                    AgregarParametro(cmdCab, "@fecha", fechaConvertida);
                    AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                    AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                    AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId);
                    AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                    AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId ?? (object)DBNull.Value);
                    AgregarParametro(cmdCab, "@observacion", cabecera.Observacion ?? (object)DBNull.Value);
                    AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia ?? (object)DBNull.Value);
                    AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia ?? (object)DBNull.Value);

                    object res = await cmdCab.ExecuteScalarAsync();
                    movimientoIdInserted = Convert.ToInt32(res);
                }

                // Insertar detalles y rangos y relaciones (sin actualizar estado de codigos)
                string qDet = $@"INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at) VALUES (@movId, @prodId, @cantIngreso, 0, @costo, GETDATE()); {selectId}";
                string qRango = $@"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, usuario_id) VALUES (@prodId, @catId, @abrev, @desde, @hasta, @detId, @usr); {selectId}";

                foreach (var item in productos)
                {
                    int detalleId = 0;
                    using var cmdDet = dbConn.CreateCommand();
                    cmdDet.Transaction = transaccion;
                    cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDet);
                    AgregarParametro(cmdDet, "@movId", movimientoIdInserted);
                    AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                    AgregarParametro(cmdDet, "@cantIngreso", item.Detalle?.CantidadIngreso ?? 0);
                    AgregarParametro(cmdDet, "@costo", item.Detalle?.CostoUnitario ?? 0);
                    detalleId = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());

                    var rangosPorProd = rangos?.Where(r => r.productoId == item.ProductoId).ToList() ?? new List<RangoCodigoItem>();
                    foreach (var r in rangosPorProd)
                    {
                        int rangoId = 0;
                        using var cmdR = dbConn.CreateCommand();
                        cmdR.Transaction = transaccion;
                        cmdR.CommandText = QueryAdapter.FormatearConsulta(qRango);
                        AgregarParametro(cmdR, "@prodId", r.productoId);
                        AgregarParametro(cmdR, "@catId", r.CategoriaProductoId);
                        AgregarParametro(cmdR, "@abrev", r.AbreviaturaBase);
                        AgregarParametro(cmdR, "@desde", r.DesdeNum);
                        AgregarParametro(cmdR, "@hasta", r.HastaNum);
                        AgregarParametro(cmdR, "@detId", detalleId);
                        AgregarParametro(cmdR, "@usr", usuarioId);
                        rangoId = Convert.ToInt32(await cmdR.ExecuteScalarAsync());

                        // Asociar códigos encontrados al detalle
                        var ids = await ObtenerIdsCodigosPorRangoAsync(
                        r.productoId,
                        r.AbreviaturaBase,  
                        r.CategoriaProductoId,
                        r.DesdeNum,
                        r.HastaNum,
                        dbConn,
                        transaccion);
                        foreach (var t in ids)
                        {
                            if (t.CodigoObj == null) continue;
                            using var cmdMovCod = dbConn.CreateCommand();
                            cmdMovCod.Transaction = transaccion;
                            cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(@"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES (@movId, @detId, @codId, 1, 0, GETDATE());");
                            AgregarParametro(cmdMovCod, "@movId", movimientoIdInserted);
                            AgregarParametro(cmdMovCod, "@detId", detalleId);
                            AgregarParametro(cmdMovCod, "@codId", t.CodigoObj.Id);
                            await cmdMovCod.ExecuteNonQueryAsync();
                        }
                    }
                }

                await transaccion.CommitAsync();
                return true;
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> RegistrarCodigosImportadosAsync(
            Movimiento cabecera,
            List<(int CodigoCreadoId, int ProductoId)> codigosImportados,
            int usuarioId,
            int? existingMovimientoId = null)
        {
            if (codigosImportados == null || !codigosImportados.Any()) return false;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaccion = dbConn.BeginTransaction();
            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                int movimientoIdInserted = 0;

                if (existingMovimientoId.HasValue)
                {
                    // Actualizar cabecera
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using (var cmdUpdCab = dbConn.CreateCommand())
                    {
                        cmdUpdCab.Transaction = transaccion;
                        cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                        DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue
                            ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                            : DateTime.Today;
                        AgregarParametro(cmdUpdCab, "@fecha", fechaConvertida);
                        AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdUpdCab, "@ubicacionId", cabecera.UbicacionId);
                        AgregarParametro(cmdUpdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdUpdCab, "@personaId", cabecera.PersonaComercialId);
                        AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                        AgregarParametro(cmdUpdCab, "@id", existingMovimientoId.Value);
                        await cmdUpdCab.ExecuteNonQueryAsync();
                    }

                    movimientoIdInserted = existingMovimientoId.Value;

                    // Limpiar detalles antiguos para reinsertar
                    using (var cmdDel = dbConn.CreateCommand())
                    {
                        cmdDel.Transaction = transaccion;
                        cmdDel.CommandText = QueryAdapter.FormatearConsulta(@"
                            DELETE FROM movimiento_codigos WHERE movimiento_id = @movId;
                            DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId);
                            DELETE FROM movimiento_detalles WHERE movimiento_id = @movId;
                        ");
                        AgregarParametro(cmdDel, "@movId", movimientoIdInserted);
                        await cmdDel.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    string queryCabecera = $@"
                    INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                            motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia, created_at)
                    VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId, @serieGuia, @numeroGuia, GETDATE());
                    {selectId}";

                    using (var cmdCab = dbConn.CreateCommand())
                    {
                        cmdCab.Transaction = transaccion;
                        cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);

                        DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue
                            ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                            : DateTime.Today;

                        AgregarParametro(cmdCab, "@estadoId", 1);
                        AgregarParametro(cmdCab, "@fecha", fechaConvertida);
                        AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                        AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                        AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId);
                        AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
                        AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);

                        object resultCab = await cmdCab.ExecuteScalarAsync();
                        if (resultCab == null || resultCab == DBNull.Value) throw new Exception("No se pudo obtener el ID de la cabecera.");
                        movimientoIdInserted = Convert.ToInt32(resultCab);
                    }
                }

                // Agrupar códigos por producto para crear detalles
                var grupos = codigosImportados.GroupBy(x => x.ProductoId);

                foreach (var grupo in grupos)
                {
                    int productoId = grupo.Key;
                    int cantidad = grupo.Count();

                    // Insertar detalle
                    int detalleIdInserted = 0;
                    string queryDetalle = $@"
                        INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                        VALUES (@movimientoId, @productoId, @cantidad, 0, 0, GETDATE());
                        {selectId}";

                    using (var cmdDet = dbConn.CreateCommand())
                    {
                        cmdDet.Transaction = transaccion;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                        AgregarParametro(cmdDet, "@movimientoId", movimientoIdInserted);
                        AgregarParametro(cmdDet, "@productoId", productoId);
                        AgregarParametro(cmdDet, "@cantidad", cantidad);
                        object resultDet = await cmdDet.ExecuteScalarAsync();
                        detalleIdInserted = Convert.ToInt32(resultDet);
                    }

                    // Para cada código del grupo insertar movimiento_codigos y actualizar estado del código
                    foreach (var item in grupo)
                    {
                        int codigoId = item.CodigoCreadoId;
                        using (var cmdMovCod = dbConn.CreateCommand())
                        {
                            cmdMovCod.Transaction = transaccion;
                            cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(@"
                                INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at)
                                VALUES (@movId, @detId, @codId, 1, 0, GETDATE());");

                            AgregarParametro(cmdMovCod, "@movId", movimientoIdInserted);
                            AgregarParametro(cmdMovCod, "@detId", detalleIdInserted);
                            AgregarParametro(cmdMovCod, "@codId", codigoId);
                            await cmdMovCod.ExecuteNonQueryAsync();
                        }

                        // Actualizar estado del código a 3 (En almacén) para ingresos
                        // Comportamiento: si estamos creando un movimiento nuevo, actualizar siempre.
                        // Si estamos editando, actualizar SOLO si el código tenía estado 1 (existente pero no ingresado)
                        // y no estaba previamente asociado al movimiento. Para ello consultamos el estado actual.
                        bool shouldUpdateState = false;
                        if (!existingMovimientoId.HasValue)
                        {
                            shouldUpdateState = true;
                        }
                        else
                        {
                            try
                            {
                                using var cmdCheck = dbConn.CreateCommand();
                                cmdCheck.Transaction = transaccion;
                                cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT estado_id FROM codigos_creados WHERE id = @id");
                                AgregarParametro(cmdCheck, "@id", codigoId);
                                object st = await cmdCheck.ExecuteScalarAsync();
                                int estadoActual = st == null || st == DBNull.Value ? 0 : Convert.ToInt32(st);
                                if (estadoActual == 1)
                                {
                                    // No comprobamos pertenencia aquí porque, al insertar la relación
                                    // acabamos de añadirlo al movimiento; asumimos que si estaba en estado 1
                                    // es un código "nuevo" y debe marcarse como ingresado.
                                    shouldUpdateState = true;
                                }
                            }
                            catch { shouldUpdateState = false; }
                        }

                        if (shouldUpdateState)
                        {
                            using (var cmdUpd = dbConn.CreateCommand())
                            {
                                cmdUpd.Transaction = transaccion;
                                cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 3 WHERE id = @id");
                                AgregarParametro(cmdUpd, "@id", codigoId);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }

                transaccion.Commit();
                return true;
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }


        private void NormalizarRangosImportados(ObservableCollection<RangoCodigoItem> lista)
        {
            foreach (var item in lista)
            {
                // 1. Asegurar que las descripciones de texto coincidan con los números
                if (string.IsNullOrEmpty(item.Desde) && item.DesdeNum > 0)
                    item.Desde = item.DesdeNum.ToString(); // Ajusta según tu formato

                if (string.IsNullOrEmpty(item.Hasta) && item.HastaNum > 0)
                    item.Hasta = item.HastaNum.ToString();

                // 2. Si el objeto no tiene ColeccionTipo pero tiene otros datos, infiérelo
                if (string.IsNullOrEmpty(item.ColeccionTipo))
                    item.ColeccionTipo = "Importado - N/A";
            }
        }
    }
}