#nullable enable

using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Motivo_y_Movimientos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class SalidaMovimientoService
    {
        private readonly DatabaseConnection _database;

        public SalidaMovimientoService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        // 🌟 CORRECCIÓN: Cambia el tipo de retorno de Movimiento a MovimientoCompletoDTO
        public async Task<MovimientoCompletoDTO> GenerarSiguienteCorrelativoAsync(string seriePorDefecto)
        {
            var resultado = new MovimientoCompletoDTO
            {
                Movimiento = new Movimiento
                {
                    SerieDocumento = seriePorDefecto,
                    NumeroDocumento = "0000001"
                }
            };

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 1. Buscamos cuál es la ÚLTIMA serie de salida registrada en la base de datos
                string queryUltimaSerie = @"
            SELECT TOP 1 serie_documento 
            FROM movimientos 
            WHERE motivo_producto_id IN (SELECT id FROM motivo_productos WHERE tipo_movimiento = 'salida')
            ORDER BY id DESC";

                string serieActual = seriePorDefecto;
                using (var cmdSerie = dbConn.CreateCommand())
                {
                    cmdSerie.CommandText = QueryAdapter.FormatearConsulta(queryUltimaSerie);
                    var resSerie = await cmdSerie.ExecuteScalarAsync();
                    if (resSerie != null && resSerie != DBNull.Value)
                    {
                        serieActual = resSerie.ToString();
                    }
                }

                // 2. Obtenemos el número máximo registrado para esa serie de salida
                string queryMaxNum = @"
            SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0)
            FROM movimientos 
            WHERE serie_documento = @serie";

                int ultimoNumero = 0;
                using (var cmdNum = dbConn.CreateCommand())
                {
                    cmdNum.CommandText = QueryAdapter.FormatearConsulta(queryMaxNum);
                    AgregarParametro(cmdNum, "@serie", serieActual);
                    object? resultObj = await cmdNum.ExecuteScalarAsync();
                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        ultimoNumero = Convert.ToInt32(resultObj);
                    }
                }

                // 3. 🌟 REGLA DE ORO: EVALUACIÓN DE DESBORDE ALFANUMÉRICO (S001 -> S002)
                if (ultimoNumero >= 9999999)
                {
                    // Extraemos la parte numérica de la serie alfanumérica (Ej: "S001" -> "001")
                    string parteNumericaSerie = serieActual.Substring(1);

                    if (int.TryParse(parteNumericaSerie, out int numeroSerieVal))
                    {
                        int siguienteSerieInt = numeroSerieVal + 1;

                        // Reconstruimos la serie manteniendo el prefijo fijo 'S' (Ej: "S" + "002")
                        resultado.Movimiento.SerieDocumento = "S" + siguienteSerieInt.ToString("D3");
                        resultado.Movimiento.NumeroDocumento = "0000001"; // Se reinicia el conteo
                    }
                    else
                    {
                        resultado.Movimiento.SerieDocumento = serieActual;
                        resultado.Movimiento.NumeroDocumento = "0000001";
                    }
                }
                else
                {
                    // Flujo estándar consecutivo
                    resultado.Movimiento.SerieDocumento = serieActual;
                    resultado.Movimiento.NumeroDocumento = (ultimoNumero + 1).ToString("D7");
                }
            }
            return resultado;
        }

        public async Task<List<MotivoProducto>> ObtenerMotivosSalidaAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, tipo_movimiento 
                                 FROM motivo_productos 
                                 WHERE tipo_movimiento = 'salida' 
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new MotivoProducto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                TipoMovimiento = reader.IsDBNull(reader.GetOrdinal("tipo_movimiento"))
                                    ? null : reader.GetString(reader.GetOrdinal("tipo_movimiento"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<PersonaComercial>> BuscarClientesAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, razon_social, direccion 
                         FROM personas_comerciales 
                         WHERE razon_social LIKE @filtro 
                         ORDER BY razon_social ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new PersonaComercial
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                // 🌟 CORREGIDO: "rawon_social" cambiado por "razon_social"
                                RazonSocial = reader.GetString(reader.GetOrdinal("razon_social")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Ubicacion>> BuscarUbicacionesAsync(string filtro)
        {
            var lista = new List<Ubicacion>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, direccion 
                                 FROM ubicaciones 
                                 WHERE descripcion LIKE @filtro 
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Ubicacion
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<CodigoCreado?> ObtenerCodigoLimpiadoAsync(string codigoBruto)
        {
            if (string.IsNullOrWhiteSpace(codigoBruto)) return null;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string codigoLimpio = codigoBruto.Replace("'", "-").Trim();

            string query = @"
            SELECT TOP 1 cc.id, cc.codigo, cc.estado_id, rc.producto_id 
            FROM codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf))
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE cc.codigo = @CodigoLimpio";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@CodigoLimpio", codigoLimpio);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new CodigoCreado
                        {
                            Id = reader.GetInt32(0),
                            Codigo = reader.GetString(1),
                            EstadoId = reader.GetInt32(2),
                            RegistroCodigoId = reader.GetInt32(3)
                        };
                    }
                }
            }
            return null;
        }

        public async Task<HashSet<int>> ObtenerCodigosEnMovimientoAsync(IEnumerable<int> movIds, DbConnection conn, DbTransaction trans)
        {
            var set = new HashSet<int>();
            var ids = movIds?.Distinct().ToList();
            if (ids == null || !ids.Any()) return set;

            const int batchSize = 1000;
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();
                for (int j = 0; j < batch.Count; j++) paramNames.Add("@p" + j);

                string q = $@"SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id IN ({string.Join(',', paramNames)})";
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                for (int j = 0; j < batch.Count; j++)
                {
                    AgregarParametro(cmd, "@p" + j, batch[j]);
                }

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0)) set.Add(rdr.GetInt32(0));
                }
            }
            return set;
        }

        public async Task<bool> TieneMovimientosPosterioresAsync(int codigoId, DateTime fechaEdicion, DbConnection conn, DbTransaction trans)
        {
            string query = @"
            SELECT COUNT(*) 
            FROM movimiento_codigos mc
            JOIN movimientos m ON mc.movimiento_id = m.id
            WHERE mc.codigo_creado_id = @codId 
            AND m.fecha_movimiento > @fechaEdicion";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@codId", codigoId);
            AgregarParametro(cmd, "@fechaEdicion", fechaEdicion);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && Convert.ToInt32(result) > 0;
        }

        public async Task<int> ObtenerEstadoAnteriorAsync(int codigoId, int movimientoActualId, DbConnection conn, DbTransaction trans)
        {
            return 3; // En Almacén por defecto seguro para salidas revertidas
        }

        private async Task ActualizarEstadoCodigo(int codigoId, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = @estado WHERE id = @id");
            AgregarParametro(cmd, "@estado", nuevoEstadoId);
            AgregarParametro(cmd, "@id", codigoId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertarMovimientoCodigosSalidaMasivoAsync(int movId, int detId, List<int> codigosIds, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var sb = new System.Text.StringBuilder();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            sb.Append("INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES ");

            for (int i = 0; i < codigosIds.Count; i++)
            {
                sb.Append($"(@movId, @detId, @c{i}, 0, 1, GETDATE())");
                if (i < codigosIds.Count - 1) sb.Append(",");
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@detId", detId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ActualizarEstadoCodigosMasivoAsync(List<int> codigosIds, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var paramNames = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            for (int i = 0; i < codigosIds.Count; i++)
            {
                paramNames.Add("@c" + i);
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta($"UPDATE codigos_creados SET estado_id = @estado WHERE id IN ({string.Join(",", paramNames)})");
            AgregarParametro(cmd, "@estado", nuevoEstadoId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task EliminarMovimientoCodigoAsync(int movId, int codId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_id = @movId AND codigo_creado_id = @codId");
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@codId", codId);
            await cmd.ExecuteNonQueryAsync();
        }

        // =========================================================================
        // 🌟 4. REGISTRAR SALIDA MASIVA OPTIMIZADA Y BLINDADA ANTI-DUPLICADOS
        // =========================================================================
        public async Task<bool> RegistrarSalidaCompletaAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> listaProductos,
            List<VistaCodigoGrid> listaCodigos,
            int usuarioId,
            int estadoId,
            int? existingMovimientoId = null,
            IProgress<int>? progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                int movimientoIdInserted = 0;

                // 🌟 [CANDADO 1: DETECTOR DE DUPLICADOS INTERNOS EN LA MISMA GRILLA/SOLICITUD]
                var detectorDuplicadosInternos = new HashSet<int>();
                foreach (var cod in listaCodigos)
                {
                    if (!detectorDuplicadosInternos.Add(cod.MovCodigo.CodigoCreadoId))
                    {
                        throw new Exception($"Error de Operación: El código único con ID {cod.MovCodigo.CodigoCreadoId} se encuentra duplicado en la grilla actual de despacho. Elimine el código repetido antes de guardar.");
                    }
                }

                // Generar Cabecera Anti-Colisión por Concurrencia
                if (!existingMovimientoId.HasValue)
                {
                    string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "S001" : cabecera.SerieDocumento;

                    // 🌟 Cambiado UPDLOCK por TABLOCKX para asegurar el bloqueo estricto si MAX devuelve 0 rows
                    string bloqueoConcurrencia = QueryAdapter.EsMySQL ? "FOR UPDATE" : "WITH (TABLOCKX, HOLDLOCK)";

                    using (var cmdGen = dbConn.CreateCommand())
                    {
                        cmdGen.Transaction = transaccion;
                        cmdGen.CommandText = QueryAdapter.FormatearConsulta($@"SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 FROM movimientos {bloqueoConcurrencia} WHERE serie_documento = @serie");
                        AgregarParametro(cmdGen, "@serie", serieParaGenerar);
                        object? genRes = await cmdGen.ExecuteScalarAsync();
                        int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;

                        cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                        cabecera.SerieDocumento = serieParaGenerar;
                    }

                    string queryCabecera = $@"INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, serie_guia, numero_guia, observacion, estado_id, created_at)
                                             VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @serieGuia, @numeroGuia, @observacion, @estadoId, GETDATE()); {selectId}";

                    using var cmdCab = dbConn.CreateCommand();
                    cmdCab.Transaction = transaccion;
                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                    AgregarParametro(cmdCab, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now);
                    AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                    AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                    AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId > 0 ? (object)cabecera.UbicacionId : DBNull.Value);
                    AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                    AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId > 0 ? (object)cabecera.PersonaComercialId : DBNull.Value);
                    AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                    AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);
                    AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                    AgregarParametro(cmdCab, "@estadoId", estadoId);

                    try
                    {
                        movimientoIdInserted = Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
                    }
                    catch (DbException ex) when (ex.Message.Contains("PRIMARY KEY") || ex.Message.Contains("UNIQUE") || ex.ErrorCode == 2627)
                    {
                        throw new Exception("Colisión de Red: Otro terminal generó la misma numeración de salida al mismo tiempo. Por favor, vuelva a presionar 'Guardar Salida' para recalcular el correlativo.");
                    }
                }
                else
                {
                    movimientoIdInserted = existingMovimientoId.Value;
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using var cmdUpdCab = dbConn.CreateCommand();
                    cmdUpdCab.Transaction = transaccion;
                    cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                    AgregarParametro(cmdUpdCab, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now);
                    AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdUpdCab, "@ubicacionId", cabecera.UbicacionId > 0 ? (object)cabecera.UbicacionId : DBNull.Value);
                    AgregarParametro(cmdUpdCab, "@usuarioId", usuarioId);
                    AgregarParametro(cmdUpdCab, "@personaId", cabecera.PersonaComercialId > 0 ? (object)cabecera.PersonaComercialId : DBNull.Value);
                    AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                    AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                    AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                    AgregarParametro(cmdUpdCab, "@id", movimientoIdInserted);
                    await cmdUpdCab.ExecuteNonQueryAsync();
                }

                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { movimientoIdInserted }, dbConn, transaccion)
                    : new HashSet<int>();

                // =========================================================================
                // 🚀 VALIDACIÓN INDUSTRIAL EN RAM CONTRA LA BASE DE DATOS
                // =========================================================================
                using (var cmdTable = dbConn.CreateCommand())
                {
                    cmdTable.Transaction = transaccion;
                    cmdTable.CommandText = "CREATE TABLE #temp_salida_valida (id INT NOT NULL PRIMARY KEY);";
                    await cmdTable.ExecuteNonQueryAsync();
                }

                const int batchSize = 1000;
                var todosLosCodigosIds = listaCodigos.Select(c => c.MovCodigo.CodigoCreadoId).Distinct().ToList();

                for (int i = 0; i < todosLosCodigosIds.Count; i += batchSize)
                {
                    var chunk = todosLosCodigosIds.Skip(i).Take(batchSize).ToList();
                    var sbIns = new System.Text.StringBuilder("INSERT INTO #temp_salida_valida (id) VALUES ");
                    using var cmdIns = dbConn.CreateCommand();
                    cmdIns.Transaction = transaccion;

                    for (int j = 0; j < chunk.Count; j++)
                    {
                        sbIns.Append($"(@id{j})");
                        if (j < chunk.Count - 1) sbIns.Append(",");
                        AgregarParametro(cmdIns, $"@id{j}", chunk[j]);
                    }
                    cmdIns.CommandText = sbIns.ToString();
                    await cmdIns.ExecuteNonQueryAsync();
                }

                // Cruzamos de un solo golpe contra tu índice compuesto para atrapar códigos sin stock
                string sqlCheckStock = @"
                    SELECT cc.id, cc.codigo, cc.estado_id 
                    FROM #temp_salida_valida tmp
                    INNER JOIN codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf)) ON cc.id = tmp.id
                    WHERE cc.estado_id != 3"; // 3 = Debe estar estrictamente En Almacén

                var codigosInvalidos = new List<string>();
                using (var cmdCheckStock = dbConn.CreateCommand())
                {
                    cmdCheckStock.Transaction = transaccion;
                    cmdCheckStock.CommandText = QueryAdapter.FormatearConsulta(sqlCheckStock);
                    using var rdrStock = await cmdCheckStock.ExecuteReaderAsync();
                    while (await rdrStock.ReadAsync())
                    {
                        int idCod = rdrStock.GetInt32(0);
                        if (!codigosPreviosEnBD.Contains(idCod))
                        {
                            codigosInvalidos.Add($"- {rdrStock.GetString(1)} (Estado: {rdrStock.GetInt32(2)})");
                        }
                    }
                }

                if (codigosInvalidos.Any())
                {
                    throw new Exception("Operación Cancelada por Seguridad del Inventario.\n\n" +
                                        "Los siguientes códigos ya no están disponibles en Almacén (fueron despachados en otra venta):\n" +
                                        string.Join("\n", codigosInvalidos.Take(5)));
                }

                // 3. REGISTRO Y BATCHING GRÁFICO SUAVE
                var idsDetallesActivos = new List<int>();
                var nuevosCodigosIds = new HashSet<int>();
                int totalCodigosGlobal = listaCodigos.Count == 0 ? 1 : listaCodigos.Count;
                int codigosProcesadosGlobal = 0;
                int ultimoPorcentajeReportado = -1;

                foreach (var item in listaProductos)
                {
                    int idDetalle = 0;
                    using (var cmdCheck = dbConn.CreateCommand())
                    {
                        cmdCheck.Transaction = transaccion;
                        cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND producto_id = @prodId");
                        AgregarParametro(cmdCheck, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdCheck, "@prodId", item.ProductoId);
                        object? resDet = await cmdCheck.ExecuteScalarAsync();
                        if (resDet != null && resDet != DBNull.Value) idDetalle = Convert.ToInt32(resDet);
                    }

                    if (idDetalle > 0)
                    {
                        using var cmdUpd = dbConn.CreateCommand();
                        cmdUpd.Transaction = transaccion;
                        cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_salida = @cant, costo_unitario = @costo WHERE id = @detId");
                        AgregarParametro(cmdUpd, "@cant", item.Cantidad);
                        AgregarParametro(cmdUpd, "@costo", item.Detalle?.CostoUnitario ?? 0);
                        AgregarParametro(cmdUpd, "@detId", idDetalle);
                        await cmdUpd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        string queryDetalle = $@"INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                                                 VALUES (@movId, @prodId, 0, @cant, @costo, GETDATE()); {selectId}";
                        using var cmdDet = dbConn.CreateCommand();
                        cmdDet.Transaction = transaccion;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                        AgregarParametro(cmdDet, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                        AgregarParametro(cmdDet, "@cant", item.Cantidad);
                        AgregarParametro(cmdDet, "@costo", item.Detalle?.CostoUnitario ?? 0);
                        idDetalle = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    idsDetallesActivos.Add(idDetalle);

                    var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId).ToList();
                    if (codigosProd.Any())
                    {
                        var codigosNuevosParaInsertar = new List<int>();

                        foreach (var cod in codigosProd)
                        {
                            if (await TieneMovimientosPosterioresAsync(cod.MovCodigo.CodigoCreadoId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion))
                            {
                                throw new Exception($"El código ID {cod.MovCodigo.CodigoCreadoId} cuenta con transacciones posteriores en kárdex. Operación abortada.");
                            }

                            nuevosCodigosIds.Add(cod.MovCodigo.CodigoCreadoId);
                            if (!codigosPreviosEnBD.Contains(cod.MovCodigo.CodigoCreadoId))
                            {
                                codigosNuevosParaInsertar.Add(cod.MovCodigo.CodigoCreadoId);
                            }
                        }

                        const int insertBatchSize = 1000;
                        for (int i = 0; i < codigosNuevosParaInsertar.Count; i += insertBatchSize)
                        {
                            var batch = codigosNuevosParaInsertar.Skip(i).Take(insertBatchSize).ToList();
                            await InsertarMovimientoCodigosSalidaMasivoAsync(movimientoIdInserted, idDetalle, batch, dbConn, transaccion);
                            await ActualizarEstadoCodigosMasivoAsync(batch, 4, dbConn, transaccion); // 4 = Despachado / Salida
                        }

                        codigosProcesadosGlobal += codigosProd.Count;
                        int nuevoPorcentaje = (codigosProcesadosGlobal * 100) / totalCodigosGlobal;
                        if (nuevoPorcentaje > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = nuevoPorcentaje;
                            progress?.Report(nuevoPorcentaje);
                        }
                    }
                }

                // 4. REMOCIONES CRONOLÓGICAS
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();
                foreach (var codId in codigosAEliminar)
                {
                    bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);
                    if (tieneFuturo) throw new Exception($"El código ID {codId} no puede eliminarse porque tiene movimientos posteriores.");

                    int estadoAnterior = await ObtenerEstadoAnteriorAsync(codId, movimientoIdInserted, dbConn, transaccion);
                    await ActualizarEstadoCodigo(codId, estadoAnterior, dbConn, transaccion);
                    await EliminarMovimientoCodigoAsync(movimientoIdInserted, codId, dbConn, transaccion);
                }

                if (existingMovimientoId.HasValue && idsDetallesActivos.Any())
                {
                    var paramNamesAct = new List<string>();
                    for (int i = 0; i < idsDetallesActivos.Count; i++) paramNamesAct.Add("@act" + i);

                    string qLimpiar = $"DELETE FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNamesAct)});";
                    using var cmdLimp = dbConn.CreateCommand();
                    cmdLimp.Transaction = transaccion;
                    cmdLimp.CommandText = QueryAdapter.FormatearConsulta(qLimpiar);
                    AgregarParametro(cmdLimp, "@movId", movimientoIdInserted);
                    for (int i = 0; i < idsDetallesActivos.Count; i++) AgregarParametro(cmdLimp, "@act" + i, idsDetallesActivos[i]);
                    await cmdLimp.ExecuteNonQueryAsync();
                }

                transaccion.Commit();
                return true;
            }
            catch (Exception)
            {
                transaccion.Rollback();
                throw;
            }
            finally
            {
                try
                {
                    using var cmdDrop = dbConn.CreateCommand();
                    cmdDrop.Transaction = transaccion;
                    cmdDrop.CommandText = "DROP TABLE IF EXISTS #temp_salida_valida;";
                    await cmdDrop.ExecuteNonQueryAsync();
                }
                catch { }
            }
        }

        // =========================================================================
        // 5. OBTENER COMPLETO PARA VISTA/EDICIÓN (CORREGIDO)
        // =========================================================================
        public async Task<MovimientoCompletoDTO?> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var resultado = new MovimientoCompletoDTO();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) return null;
            if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

            // Cabecera estricta
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    SELECT id, fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, 
                           persona_comercial_id, ubicacion_id, serie_guia, numero_guia, observacion, estado_id
                    FROM movimientos
                    WHERE serie_documento = @serie AND numero_documento = @numero");

                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);

                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;

                // Si por error intentan jalar una Entrada anulada o similar
                if (rd.GetInt32(rd.GetOrdinal("estado_id")) == 5) return null;

                resultado.Movimiento = new Movimiento
                {
                    Id = rd.GetInt32(rd.GetOrdinal("id")),
                    FechaMovimiento = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = rd["serie_documento"].ToString(),
                    NumeroDocumento = rd["numero_documento"].ToString(),
                    MotivoProductoId = rd.IsDBNull(rd.GetOrdinal("motivo_producto_id")) ? 0 : rd.GetInt32(rd.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = rd.IsDBNull(rd.GetOrdinal("persona_comercial_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("persona_comercial_id")),
                    UbicacionId = rd.IsDBNull(rd.GetOrdinal("ubicacion_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("ubicacion_id")),
                    SerieGuia = rd["serie_guia"]?.ToString(),
                    NumeroGuia = rd["numero_guia"]?.ToString(),
                    Observacion = rd["observacion"]?.ToString()
                };
            }

            // Detalles
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    SELECT id, movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario 
                    FROM movimiento_detalles 
                    WHERE movimiento_id = @id");

                AgregarParametro(cmd, "@id", resultado.Movimiento.Id);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    resultado.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rd.GetInt32(rd.GetOrdinal("id")),
                        MovimientoId = rd.GetInt32(rd.GetOrdinal("movimiento_id")),
                        ProductoId = rd.GetInt32(rd.GetOrdinal("producto_id")),
                        CantidadIngreso = rd.IsDBNull(rd.GetOrdinal("cantidad_ingreso")) ? 0 : (int)rd.GetDecimal(rd.GetOrdinal("cantidad_ingreso")),
                        CantidadSalida = rd.IsDBNull(rd.GetOrdinal("cantidad_salida")) ? 0 : (int)rd.GetDecimal(rd.GetOrdinal("cantidad_salida")),
                        CostoUnitario = rd.IsDBNull(rd.GetOrdinal("costo_unitario")) ? 0 : rd.GetDecimal(rd.GetOrdinal("costo_unitario"))
                    });
                }
            }
            return resultado;
        }

        // =========================================================================
        // 🚀 6. SISTEMA DE ANULACIÓN MASIVA BLINDADO POR TABLA TEMPORAL
        // =========================================================================
        public async Task<bool> AnularMovimientoSalidaCompletoAsync(int movimientoId, IProgress<int>? progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                DateTime fechaMovimiento;
                using (var cmdMov = dbConn.CreateCommand())
                {
                    cmdMov.Transaction = transaccion;
                    cmdMov.CommandText = QueryAdapter.FormatearConsulta("SELECT fecha_movimiento, estado_id FROM movimientos WHERE id = @movId");
                    AgregarParametro(cmdMov, "@movId", movimientoId);
                    using var rdrMov = await cmdMov.ExecuteReaderAsync();
                    if (!await rdrMov.ReadAsync()) throw new Exception("El movimiento no existe.");
                    if (rdrMov.GetInt32(1) == 5) throw new Exception("Este movimiento de salida ya está anulado.");
                    fechaMovimiento = rdrMov.IsDBNull(0) ? DateTime.Today : rdrMov.GetDateTime(0);
                }

                using (var cmdCreate = dbConn.CreateCommand())
                {
                    cmdCreate.Transaction = transaccion;
                    cmdCreate.CommandText = QueryAdapter.FormatearConsulta("CREATE TABLE #temp_codigos_anular_salida (codigo_creado_id INT NOT NULL PRIMARY KEY);");
                    await cmdCreate.ExecuteNonQueryAsync();
                }

                using (var cmdPopulate = dbConn.CreateCommand())
                {
                    cmdPopulate.Transaction = transaccion;
                    cmdPopulate.CommandText = QueryAdapter.FormatearConsulta("INSERT INTO #temp_codigos_anular_salida (codigo_creado_id) SELECT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId;");
                    AgregarParametro(cmdPopulate, "@movId", movimientoId);
                    await cmdPopulate.ExecuteNonQueryAsync();
                }

                // 🛑 CANDADO REGLA DE ORO: Si vas a reincorporar stock de salida, no debe haber movimientos en el futuro
                using (var cmdCheck = dbConn.CreateCommand())
                {
                    cmdCheck.Transaction = transaccion;
                    cmdCheck.CommandText = QueryAdapter.FormatearConsulta(@"
                        SELECT COUNT(*) 
                        FROM movimiento_codigos mc
                        INNER JOIN #temp_codigos_anular_salida tmp ON tmp.codigo_creado_id = mc.codigo_creado_id
                        INNER JOIN movimientos m ON m.id = mc.movimiento_id
                        WHERE m.fecha_movimiento > @fechaMov OR (m.fecha_movimiento = @fechaMov AND m.id > @movId)");

                    AgregarParametro(cmdCheck, "@fechaMov", fechaMovimiento);
                    AgregarParametro(cmdCheck, "@movId", movimientoId);

                    int posteriores = Convert.ToInt32(await cmdCheck.ExecuteScalarAsync());
                    if (posteriores > 0) throw new Exception($"Rechazado: {posteriores} códigos registran movimientos logísticos posteriores.");
                }

                progress?.Report(40);

                // Los códigos retornan triunfantes a estar físicamente Disponibles en Almacén (estado_id = 3)
                string sqlRevertirEstados = @"
                    UPDATE cc SET cc.estado_id = 3
                    FROM codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf))
                    INNER JOIN #temp_codigos_anular_salida tmp ON tmp.codigo_creado_id = cc.id";

                using (var cmdRevert = dbConn.CreateCommand())
                {
                    cmdRevert.Transaction = transaccion;
                    cmdRevert.CommandText = QueryAdapter.FormatearConsulta(sqlRevertirEstados);
                    await cmdRevert.ExecuteNonQueryAsync();
                }

                // Cambiar estado a Anulado (5)
                using (var cmdStatus = dbConn.CreateCommand())
                {
                    cmdStatus.Transaction = transaccion;
                    cmdStatus.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimientos SET estado_id = 5 WHERE id = @movId");
                    AgregarParametro(cmdStatus, "@movId", movimientoId);
                    await cmdStatus.ExecuteNonQueryAsync();
                }

                progress?.Report(100);
                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception(ex.Message);
            }
            finally
            {
                try
                {
                    using var cmdDrop = dbConn.CreateCommand();
                    cmdDrop.Transaction = transaccion;
                    cmdDrop.CommandText = "DROP TABLE IF EXISTS #temp_codigos_anular_salida;";
                    await cmdDrop.ExecuteNonQueryAsync();
                }
                catch { }
            }
        }
    }
}