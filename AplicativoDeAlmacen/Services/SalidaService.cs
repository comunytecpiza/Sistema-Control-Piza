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
        private string? _ultimoSql;

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

                string queryUltimaSerie = @"
                SELECT TOP 1 serie_documento 
                FROM movimientos 
                WHERE motivo_producto_id IN (SELECT id FROM motivo_productos WHERE tipo_movimiento_id = 2)
                ORDER BY id DESC";

                string serieActual = seriePorDefecto;
                using (var cmdSerie = dbConn.CreateCommand())
                {
                    cmdSerie.CommandText = QueryAdapter.FormatearConsulta(queryUltimaSerie);
                    var resSerie = await cmdSerie.ExecuteScalarAsync();
                    if (resSerie != null && resSerie != DBNull.Value)
                    {
                        serieActual = resSerie.ToString()!;
                    }
                }

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

                if (ultimoNumero >= 9999999)
                {
                    if (int.TryParse(serieActual, out int serieVal))
                    {
                        int siguienteSerieInt = serieVal + 1;
                        resultado.Movimiento.SerieDocumento = siguienteSerieInt.ToString("D4");
                        resultado.Movimiento.NumeroDocumento = "0000001";
                    }
                    else
                    {
                        resultado.Movimiento.SerieDocumento = serieActual;
                        resultado.Movimiento.NumeroDocumento = "0000001";
                    }
                }
                else
                {
                    resultado.Movimiento.SerieDocumento = serieActual;
                    resultado.Movimiento.NumeroDocumento = (ultimoNumero + 1).ToString("D7");
                }
            }
            return resultado;
        }

        private async Task ActualizarStockProductoPorKardexAsync(int productoId, DbConnection conn, DbTransaction trans)
        {
            bool esComputed = false;
            try
            {
                if (!QueryAdapter.EsMySQL)
                {
                    using var cmdCheck = conn.CreateCommand();
                    cmdCheck.Transaction = trans;
                    cmdCheck.CommandText = @"SELECT CAST(ISNULL(MAX(CASE WHEN c.is_computed = 1 THEN 1 ELSE 0 END),0) AS INT)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'productos' AND c.name = 'cantidad'";
                    var res = await cmdCheck.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value)
                        esComputed = Convert.ToInt32(res) == 1;
                }
            }
            catch
            {
                esComputed = false;
            }

            if (esComputed) return;

            string queryUpdate = @"
            UPDATE productos 
            SET cantidad = (
                SELECT COALESCE(
                    (SELECT COALESCE(SUM(md.cantidad_ingreso), 0)
                     FROM movimiento_detalles md
                     INNER JOIN movimientos m ON md.movimiento_id = m.id
                     WHERE md.producto_id = productos.id AND m.estado_id = 1
                     AND m.motivo_producto_id IN (SELECT id FROM motivo_productos WHERE tipo_movimiento_id = 1)
                    ) -
                    (SELECT COALESCE(SUM(md.cantidad_salida), 0)
                     FROM movimiento_detalles md
                     INNER JOIN movimientos m ON md.movimiento_id = m.id
                     WHERE md.producto_id = productos.id AND m.estado_id = 1
                     AND m.motivo_producto_id IN (SELECT id FROM motivo_productos WHERE tipo_movimiento_id = 2)
                    ), 0)
            )
            WHERE id = @ProdId";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryUpdate);

            AgregarParametro(cmd, "@ProdId", productoId);
            _ultimoSql = cmd.CommandText;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MotivoProducto>> ObtenerMotivosSalidaAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion FROM motivo_productos 
                                 WHERE tipo_movimiento_id = 2 
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
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion"))
                            });
                        }
                    }
                }
            }
            return lista;
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
            return 3; // Estado 3 = EN ALMACÉN para revertir salidas
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
            _ultimoSql = cmd.CommandText;
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
            _ultimoSql = cmd.CommandText;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task EliminarMovimientoCodigoAsync(int movId, int codId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_id = @movId AND codigo_creado_id = @codId");
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@codId", codId);
            _ultimoSql = cmd.CommandText;
            await cmd.ExecuteNonQueryAsync();
        }

        // 🌟 BÚSQUEDA ROBUSTA DE CÓDIGOS PARA SALIDAS (Extrae directamente por correlativo final)
        private async Task<List<(CodigoCreado CodigoObj, int Seq)>> ObtenerIdsCodigosPorRangoSalidaAsync(int productoId, int desde, int hasta, DbConnection conn, DbTransaction trans)
        {
            var resultados = new List<(CodigoCreado CodigoObj, int Seq)>();

            string queryMaster = @"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id,
                   CAST(RIGHT(cc.codigo, 7) AS INT) as seq
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND ISNUMERIC(RIGHT(cc.codigo, 7)) = 1
              AND CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

            using var cmdQuery = conn.CreateCommand();
            cmdQuery.Transaction = trans;
            cmdQuery.CommandText = QueryAdapter.FormatearConsulta(queryMaster);
            AgregarParametro(cmdQuery, "@productoId", productoId);
            AgregarParametro(cmdQuery, "@desde", desde);
            AgregarParametro(cmdQuery, "@hasta", hasta);

            using var reader = await cmdQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultados.Add((
                    new CodigoCreado
                    {
                        Id = reader.GetInt32(0),
                        RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        Codigo = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        EsManual = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                    },
                    reader.GetInt32(5)
                ));
            }

            return resultados;
        }

        public async Task<bool> RegistrarSalidaCompletaAsync(Movimiento cabecera, List<VistaProductoGrid> listaProductos, List<VistaCodigoGrid> listaCodigos, int usuarioId,
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

                var detectorDuplicadosInternos = new HashSet<int>();
                foreach (var cod in listaCodigos)
                {
                    if (cod.MovCodigo?.CodigoCreadoId > 0 && !detectorDuplicadosInternos.Add(cod.MovCodigo.CodigoCreadoId))
                    {
                        throw new Exception($"Error de Operación: El código con ID {cod.MovCodigo.CodigoCreadoId} se encuentra duplicado en la grilla de despacho.");
                    }
                }

                if (!existingMovimientoId.HasValue)
                {
                    string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "0001" : cabecera.SerieDocumento;
                    string bloqueoConcurrencia = QueryAdapter.EsMySQL ? "FOR UPDATE" : "WITH (TABLOCKX, HOLDLOCK)";

                    using (var cmdGen = dbConn.CreateCommand())
                    {
                        cmdGen.Transaction = transaccion;
                        cmdGen.CommandText = QueryAdapter.FormatearConsulta($@"
                            SELECT COALESCE(MAX(CAST(m.numero_documento AS INT)), 0) + 1 
                            FROM movimientos m {bloqueoConcurrencia}
                            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                            WHERE m.serie_documento = @serie 
                            AND mp.tipo_movimiento_id = 2");

                        AgregarParametro(cmdGen, "@serie", serieParaGenerar);
                        _ultimoSql = cmdGen.CommandText;
                        object? genRes = await cmdGen.ExecuteScalarAsync();
                        int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;

                        cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                        cabecera.SerieDocumento = serieParaGenerar;
                    }

                    string queryCabecera = $@"INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, serie_guia, numero_guia, observacion, estado_id, created_at)
                                               VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @serieGuia, @numeroGuia, @observacion, 1, GETDATE()); {selectId}";

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

                    try
                    {
                        _ultimoSql = cmdCab.CommandText;
                        movimientoIdInserted = Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
                    }
                    catch (DbException ex) when (ex.Message.Contains("PRIMARY KEY") || ex.Message.Contains("UNIQUE") || ex.ErrorCode == 2627)
                    {
                        throw new Exception("Colisión de Red: Otro terminal generó la misma numeración al mismo tiempo. Intente guardar nuevamente.");
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
                    _ultimoSql = cmdUpdCab.CommandText;
                    await cmdUpdCab.ExecuteNonQueryAsync();
                }

                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { movimientoIdInserted }, dbConn, transaccion)
                    : new HashSet<int>();

                // 🌟 PROCESADO DE DETALLES Y RANGOS
                var idsDetallesActivos = new List<int>();
                var nuevosCodigosIds = new HashSet<int>();

                foreach (var item in listaProductos)
                {
                    int idDetalle = 0;
                    using (var cmdCheck = dbConn.CreateCommand())
                    {
                        cmdCheck.Transaction = transaccion;
                        cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND producto_id = @prodId");
                        AgregarParametro(cmdCheck, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdCheck, "@prodId", item.ProductoId);
                        _ultimoSql = cmdCheck.CommandText;
                        object? resDet = await cmdCheck.ExecuteScalarAsync();
                        if (resDet != null && resDet != DBNull.Value) idDetalle = Convert.ToInt32(resDet);
                    }

                    int cantidadDespachoPura = listaCodigos.Count(c => c.ProductoId == item.ProductoId);
                    if (cantidadDespachoPura == 0 && item.Detalle != null) cantidadDespachoPura = Convert.ToInt32(item.Detalle.CantidadSalida);
                    if (cantidadDespachoPura == 0) cantidadDespachoPura = Convert.ToInt32(item.Cantidad);

                    decimal costoUnitarioPuro = item.Detalle?.CostoUnitario ?? 0;

                    if (idDetalle > 0)
                    {
                        using var cmdUpd = dbConn.CreateCommand();
                        cmdUpd.Transaction = transaccion;
                        cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_salida = @cant, costo_unitario = @costo WHERE id = @detId");
                        AgregarParametro(cmdUpd, "@cant", cantidadDespachoPura);
                        AgregarParametro(cmdUpd, "@costo", costoUnitarioPuro);
                        AgregarParametro(cmdUpd, "@detId", idDetalle);
                        _ultimoSql = cmdUpd.CommandText;
                        await cmdUpd.ExecuteNonQueryAsync();

                        string sqlLimpiar = "DELETE FROM registro_rangos WHERE movimiento_detalle_id = @detId";
                        using var cmdLimp = dbConn.CreateCommand();
                        cmdLimp.Transaction = transaccion;
                        cmdLimp.CommandText = QueryAdapter.FormatearConsulta(sqlLimpiar);
                        AgregarParametro(cmdLimp, "@detId", idDetalle);
                        await cmdLimp.ExecuteNonQueryAsync();
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
                        AgregarParametro(cmdDet, "@cant", cantidadDespachoPura);
                        AgregarParametro(cmdDet, "@costo", costoUnitarioPuro);
                        _ultimoSql = cmdDet.CommandText;
                        idDetalle = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    idsDetallesActivos.Add(idDetalle);

                    var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId).ToList();
                    if (codigosProd.Any())
                    {
                        var codigosNuevosParaInsertar = new List<int>();
                        foreach (var cod in codigosProd)
                        {
                            nuevosCodigosIds.Add(cod.MovCodigo.CodigoCreadoId);
                            if (!codigosPreviosEnBD.Contains(cod.MovCodigo.CodigoCreadoId))
                            {
                                codigosNuevosParaInsertar.Add(cod.MovCodigo.CodigoCreadoId);
                            }
                        }

                        // Reconstrucción atómica de rangos para la tabla `registro_rangos`
                        var serviceIng = new IngresoMovimientoService();
                        var rangosReconstruidos = serviceIng.GenerarRangosDesdeCodigos(codigosProd);
                        foreach (var r in rangosReconstruidos)
                        {
                            string sqlInsRango = @"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at) 
                                                   VALUES (@pId, @cat, @abrev, @dNum, @hNum, @detId, GETDATE())";
                            using var cmdR = dbConn.CreateCommand();
                            cmdR.Transaction = transaccion;
                            cmdR.CommandText = QueryAdapter.FormatearConsulta(sqlInsRango);
                            AgregarParametro(cmdR, "@pId", item.ProductoId);
                            AgregarParametro(cmdR, "@cat", r.CategoriaProductoId);
                            AgregarParametro(cmdR, "@abrev", r.AbreviaturaBase);
                            AgregarParametro(cmdR, "@dNum", r.DesdeNum);
                            AgregarParametro(cmdR, "@hNum", r.HastaNum);
                            AgregarParametro(cmdR, "@detId", idDetalle);
                            _ultimoSql = cmdR.CommandText;
                            await cmdR.ExecuteNonQueryAsync();
                        }

                        const int insertBatchSize = 1000;
                        for (int i = 0; i < codigosNuevosParaInsertar.Count; i += insertBatchSize)
                        {
                            var batch = codigosNuevosParaInsertar.Skip(i).Take(insertBatchSize).ToList();
                            await InsertarMovimientoCodigosSalidaMasivoAsync(movimientoIdInserted, idDetalle, batch, dbConn, transaccion);

                            // Si es devolución a proveedor (Motivo 10) pasa a 1, de lo contrario a 4 (Vendido)
                            int estadoDestinoCodigo = (cabecera.MotivoProductoId == 10) ? 1 : 4;
                            await ActualizarEstadoCodigosMasivoAsync(batch, estadoDestinoCodigo, dbConn, transaccion);
                        }
                    }
                }

                // Limpieza de códigos descartados al modificar (pasan nuevamente a Estado 3)
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();
                foreach (var codId in codigosAEliminar)
                {
                    bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);
                    if (tieneFuturo) throw new Exception($"El código ID {codId} no puede eliminarse porque tiene movimientos posteriores.");

                    int estadoAnterior = await ObtenerEstadoAnteriorAsync(codId, movimientoIdInserted, dbConn, transaccion);
                    await ActualizarEstadoCodigo(codId, estadoAnterior, dbConn, transaccion);
                    await EliminarMovimientoCodigoAsync(movimientoIdInserted, codId, dbConn, transaccion);
                }

                var productosUnicos = listaProductos.Select(p => p.ProductoId).Distinct();
                foreach (var pid in productosUnicos)
                {
                    await ActualizarStockProductoPorKardexAsync(pid, dbConn, transaccion);
                }

                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                var msg = ex.Message;
                if (!string.IsNullOrEmpty(_ultimoSql)) msg += "\nÚltima SQL ejecutada:\n" + _ultimoSql;
                throw new Exception(msg, ex);
            }
        }

        public async Task<MovimientoCompletoDTO?> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var resultado = new MovimientoCompletoDTO();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) return null;
            if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, 
                       m.persona_comercial_id, m.ubicacion_id, m.serie_guia, m.numero_guia, m.observacion, m.estado_id
                FROM movimientos m
                INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                WHERE m.serie_documento = @serie 
                AND m.numero_documento = @numero
                AND mp.tipo_movimiento_id = 2");

                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);

                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;

                if (rd.GetInt32(rd.GetOrdinal("estado_id")) == 2) return null; // 2 = ANULADO

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
                        CantidadIngreso = rd.IsDBNull(rd.GetOrdinal("cantidad_ingreso")) ? 0 : Convert.ToInt32(rd.GetValue(rd.GetOrdinal("cantidad_ingreso"))),
                        CantidadSalida = rd.IsDBNull(rd.GetOrdinal("cantidad_salida")) ? 0 : Convert.ToInt32(rd.GetValue(rd.GetOrdinal("cantidad_salida"))),
                        CostoUnitario = rd.IsDBNull(rd.GetOrdinal("costo_unitario")) ? 0 : rd.GetDecimal(rd.GetOrdinal("costo_unitario"))
                    });
                }
            }
            return resultado;
        }

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
                    if (rdrMov.GetInt32(1) == 2) throw new Exception("Este movimiento de salida ya está anulado.");
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

                string sqlEliminarRangosAnulados = @"
                DELETE FROM registro_rangos 
                WHERE movimiento_detalle_id IN (
                    SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId
                )";

                using (var cmdDelRangos = dbConn.CreateCommand())
                {
                    cmdDelRangos.Transaction = transaccion;
                    cmdDelRangos.CommandText = QueryAdapter.FormatearConsulta(sqlEliminarRangosAnulados);
                    AgregarParametro(cmdDelRangos, "@movId", movimientoId);
                    await cmdDelRangos.ExecuteNonQueryAsync();
                }

                // Reversión de códigos al Almacén (Estado 3)
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

                using (var cmdStatus = dbConn.CreateCommand())
                {
                    cmdStatus.Transaction = transaccion;
                    cmdStatus.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimientos SET estado_id = 2 WHERE id = @movId");
                    AgregarParametro(cmdStatus, "@movId", movimientoId);
                    await cmdStatus.ExecuteNonQueryAsync();
                }

                using (var cmdProds = dbConn.CreateCommand())
                {
                    cmdProds.Transaction = transaccion;
                    cmdProds.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT producto_id FROM movimiento_detalles WHERE movimiento_id = @movId");
                    AgregarParametro(cmdProds, "@movId", movimientoId);

                    using var rdrP = await cmdProds.ExecuteReaderAsync();
                    var prodIds = new List<int>();
                    while (await rdrP.ReadAsync()) prodIds.Add(rdrP.GetInt32(0));
                    rdrP.Close();

                    foreach (var pid in prodIds)
                    {
                        await ActualizarStockProductoPorKardexAsync(pid, dbConn, transaccion);
                    }
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