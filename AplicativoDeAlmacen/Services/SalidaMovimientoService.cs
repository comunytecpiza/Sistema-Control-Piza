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

                // 🌟 Adaptación Agnostic: TOP 1 / LIMIT 1 según el motor
                string queryUltimaSerie = QueryAdapter.EsMySQL
                    ? @"SELECT serie_documento 
                        FROM movimientos 
                        WHERE motivo_producto_id IN (SELECT id FROM motivo_productos WHERE tipo_movimiento_id = 2)
                        ORDER BY id DESC LIMIT 1"
                    : @"SELECT TOP 1 serie_documento 
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

                // 🌟 Conversión de enteros compatible con ambos motores
                string castInt = QueryAdapter.EsMySQL ? "CAST(numero_documento AS SIGNED)" : "CAST(numero_documento AS INT)";
                string queryMaxNum = $@"
                SELECT COALESCE(MAX({castInt}), 0)
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

        // 🌟 RECALCULA EL STOCK FÍSICO REAL EN LA TABLA stock_almacen
        private async Task ActualizarStockProductoPorKardexAsync(int productoId, int almacenId, DbConnection conn, DbTransaction trans)
        {
            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            string queryCalculo = $@"
        SELECT COALESCE(
            SUM(CASE 
                WHEN m.almacen_destino_id = @AlmId AND mp.tipo_movimiento_id = 1 
                THEN md.cantidad_ingreso 
                ELSE 0 
            END) -
            SUM(CASE 
                WHEN m.almacen_origen_id = @AlmId AND mp.tipo_movimiento_id = 2 
                THEN md.cantidad_salida 
                ELSE 0 
            END), 0) AS stock_calculado
        FROM movimiento_detalles md {nolock}
        INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
        WHERE md.producto_id = @ProdId
          AND m.estado_id = 1
          AND (
              (m.almacen_destino_id = @AlmId AND mp.tipo_movimiento_id = 1)
              OR
              (m.almacen_origen_id = @AlmId AND mp.tipo_movimiento_id = 2)
          )";

            int stockCalculado = 0;
            using (var cmdCalc = conn.CreateCommand())
            {
                cmdCalc.Transaction = trans;
                cmdCalc.CommandText = QueryAdapter.FormatearConsulta(queryCalculo);
                AgregarParametro(cmdCalc, "@ProdId", productoId);
                AgregarParametro(cmdCalc, "@AlmId", almacenId);

                object? result = await cmdCalc.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    stockCalculado = Convert.ToInt32(result);
                }
            }

            // 🌟 Upsert adaptable a MySQL (ON DUPLICATE KEY UPDATE) y SQL Server (IF EXISTS)
            string queryUpsert;
            if (QueryAdapter.EsMySQL)
            {
                queryUpsert = @"
                INSERT INTO stock_almacen (producto_id, almacen_id, stock_actual, updated_at)
                VALUES (@ProdId, @AlmId, @Stock, NOW())
                ON DUPLICATE KEY UPDATE stock_actual = @Stock, updated_at = NOW();";
            }
            else
            {
                queryUpsert = @"
                IF EXISTS (SELECT 1 FROM stock_almacen WHERE producto_id = @ProdId AND almacen_id = @AlmId)
                BEGIN
                    UPDATE stock_almacen 
                    SET stock_actual = @Stock, updated_at = GETDATE()
                    WHERE producto_id = @ProdId AND almacen_id = @AlmId;
                END
                ELSE
                BEGIN
                    INSERT INTO stock_almacen (producto_id, almacen_id, stock_actual, updated_at)
                    VALUES (@ProdId, @AlmId, @Stock, GETDATE());
                END";
            }

            using (var cmdUpsert = conn.CreateCommand())
            {
                cmdUpsert.Transaction = trans;
                cmdUpsert.CommandText = QueryAdapter.FormatearConsulta(queryUpsert);
                AgregarParametro(cmdUpsert, "@ProdId", productoId);
                AgregarParametro(cmdUpsert, "@AlmId", almacenId);
                AgregarParametro(cmdUpsert, "@Stock", stockCalculado);

                await cmdUpsert.ExecuteNonQueryAsync();
            }
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

        public async Task<bool> TieneMovimientosPosterioresAsync(int codigoId, int movimientoActualId, DateTime fechaEdicion, DbConnection conn, DbTransaction trans)
        {
            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string query = $@"
        SELECT COUNT(*) 
        FROM movimiento_codigos mc {nolock}
        INNER JOIN movimientos m {nolock} ON mc.movimiento_id = m.id
        WHERE mc.codigo_creado_id = @codId 
          AND m.id != @movId -- 🌟 EXCLUSIÓN CLAVE: Ignora el propio documento en edición
          AND m.estado_id = 1
          AND (
              m.fecha_movimiento > @fechaEdicion 
              OR (m.fecha_movimiento = @fechaEdicion AND m.id > @movId)
          )";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            AgregarParametro(cmd, "@codId", codigoId);
            AgregarParametro(cmd, "@movId", movimientoActualId);
            AgregarParametro(cmd, "@fechaEdicion", fechaEdicion);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && Convert.ToInt32(result) > 0;
        }

        public async Task<(int EstadoId, int AlmacenId)> ObtenerEstadoYAlmacenAnteriorAsync(int codigoId, int movimientoActualId, DbConnection conn, DbTransaction trans)
        {
            string query;

            if (QueryAdapter.EsMySQL)
            {
                query = @"
            SELECT m.motivo_producto_id, 
                   mp.tipo_movimiento_id,
                   COALESCE(m.almacen_destino_id, m.almacen_id, 1) AS alm_destino,
                   COALESCE(m.almacen_origen_id, m.almacen_id, 1) AS alm_origen
            FROM movimiento_codigos mc
            INNER JOIN movimientos m ON mc.movimiento_id = m.id
            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
            WHERE mc.codigo_creado_id = @codId 
              AND m.id < @movId
              AND m.estado_id = 1
            ORDER BY m.fecha_movimiento DESC, m.id DESC
            LIMIT 1;";
            }
            else
            {
                query = @"
            SELECT TOP 1 m.motivo_producto_id, 
                         mp.tipo_movimiento_id,
                         ISNULL(m.almacen_destino_id, ISNULL(m.almacen_id, 1)) AS alm_destino,
                         ISNULL(m.almacen_origen_id, ISNULL(m.almacen_id, 1)) AS alm_origen
            FROM movimiento_codigos mc WITH (NOLOCK)
            INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
            INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            WHERE mc.codigo_creado_id = @codId 
              AND m.id < @movId
              AND m.estado_id = 1
            ORDER BY m.fecha_movimiento DESC, m.id DESC;";
            }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@codId", codigoId);
                AgregarParametro(cmd, "@movId", movimientoActualId);

                using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    int motivoId = rdr.GetInt32(0);
                    int tipoMovimiento = rdr.GetInt32(1); // 1 = Ingreso, 2 = Salida
                    int almDestino = rdr.GetInt32(2);
                    int almOrigen = rdr.GetInt32(3);

                    // 🌟 1. BLINDAJE PARA TRANSFERENCIAS ENTRE ALMACENES (Motivos 4 o 10):
                    // Si el movimiento anterior registrado fue una Transferencia de Salida (Salida en Tránsito),
                    // el código debe retornar a ESTADO 5 (En Tránsito) asignado al Almacén Destino.
                    if ((motivoId == 4 || motivoId == 10) && tipoMovimiento == 2)
                    {
                        return (5, almDestino);
                    }

                    // 🌟 2. TRAZABILIDAD ESTÁNDAR DE KÁRDEX:
                    // - Si el movimiento anterior fue un Ingreso / Compra / Reingreso (tipo_movimiento_id = 1):
                    //   El código regresa a ESTADO 3 (Disponible en Almacén) en el Almacén Destino del ingreso.
                    // - Si fue una Salida / Venta / Despacho a cliente (tipo_movimiento_id = 2):
                    //   El código regresa a ESTADO 4 (Vendido / Fuera de Almacén) en el Almacén Origen del despacho.
                    if (tipoMovimiento == 1)
                    {
                        return (3, almDestino);
                    }
                    else
                    {
                        return (4, almOrigen);
                    }
                }

                // 🌟 3. SIN HISTORIAL PREVIO EN BD:
                // Si el código no tiene ningún movimiento anterior registrado en el Kárdex,
                // regresa a su estado inicial de Creación (Estado 1 - Creado) en el Almacén Principal (1).
                return (1, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar historial del código {codigoId} en Salidas: {ex.Message}");
                return (3, 1); // Contingencia de seguridad para Salidas
            }
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

            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
            var sb = new System.Text.StringBuilder();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            sb.Append($"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES ");

            for (int i = 0; i < codigosIds.Count; i++)
            {
                sb.Append($"(@movId, @detId, @c{i}, 0, 1, {nowFunc})");
                if (i < codigosIds.Count - 1) sb.Append(",");
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@detId", detId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ActualizarEstadoYAlmacenCodigosMasivoAsync(List<int> codigosIds, int nuevoEstadoId, int? nuevoAlmacenId, DbConnection conn, DbTransaction trans)
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

            string sqlUpdate = nuevoAlmacenId.HasValue
                ? $"UPDATE codigos_creados SET estado_id = @estado, almacen_id = @almId WHERE id IN ({string.Join(",", paramNames)})"
                : $"UPDATE codigos_creados SET estado_id = @estado WHERE id IN ({string.Join(",", paramNames)})";

            cmd.CommandText = QueryAdapter.FormatearConsulta(sqlUpdate);
            AgregarParametro(cmd, "@estado", nuevoEstadoId);
            if (nuevoAlmacenId.HasValue)
            {
                AgregarParametro(cmd, "@almId", nuevoAlmacenId.Value);
            }

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

        public async Task<bool> RegistrarSalidaCompletaAsync(  Movimiento cabecera,
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
                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
                string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";
                string castInt = QueryAdapter.EsMySQL ? "CAST(m.numero_documento AS SIGNED)" : "CAST(m.numero_documento AS INT)";
                int movimientoIdInserted = 0;

                // 🌟 SANITIZACIÓN DE PARÁMETROS OPCIONALES
                object valUbicacionSalida = (cabecera.UbicacionId.HasValue && cabecera.UbicacionId.Value > 0)
                    ? (object)cabecera.UbicacionId.Value
                    : DBNull.Value;

                object valPersonaSalida = (cabecera.PersonaComercialId.HasValue && cabecera.PersonaComercialId.Value > 0)
                    ? (object)cabecera.PersonaComercialId.Value
                    : DBNull.Value;

                var detectorDuplicadosInternos = new HashSet<int>();
                foreach (var cod in listaCodigos)
                {
                    if (cod.MovCodigo?.CodigoCreadoId > 0 && !detectorDuplicadosInternos.Add(cod.MovCodigo.CodigoCreadoId))
                    {
                        throw new Exception($"Error de Operación: El código con ID {cod.MovCodigo.CodigoCreadoId} se encuentra duplicado en la grilla.");
                    }
                }

                if (!existingMovimientoId.HasValue)
                {
                    string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "0001" : cabecera.SerieDocumento;

                    string hintSqlTable = QueryAdapter.EsMySQL ? "" : "WITH (TABLOCKX, HOLDLOCK)";
                    string hintSqlForUpdate = QueryAdapter.EsMySQL ? "FOR UPDATE" : "";

                    using (var cmdGen = dbConn.CreateCommand())
                    {
                        cmdGen.Transaction = transaccion;
                        cmdGen.CommandText = QueryAdapter.FormatearConsulta($@"
                SELECT COALESCE(MAX({castInt}), 0) + 1 
                FROM movimientos m {hintSqlTable}
                INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                WHERE m.serie_documento = @serie 
                  AND mp.tipo_movimiento_id = 2
                  AND {coalesceFunc}(m.almacen_id, {coalesceFunc}(m.almacen_origen_id, 1)) = @almId 
                {hintSqlForUpdate}");

                        AgregarParametro(cmdGen, "@serie", serieParaGenerar);
                        AgregarParametro(cmdGen, "@almId", cabecera.AlmacenId ?? cabecera.AlmacenOrigenId ?? 1);
                        object? genRes = await cmdGen.ExecuteScalarAsync();
                        int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;

                        cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                        cabecera.SerieDocumento = serieParaGenerar;
                    }

                    string queryCabecera = $@"
                INSERT INTO movimientos 
                (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, 
                 almacen_id, almacen_origen_id, almacen_destino_id, usuario_id, persona_comercial_id, serie_guia, numero_guia, observacion, estado_id, created_at)
                VALUES 
                (@fecha, @serie, @numero, @motivoId, @ubicacionId, @almId, @almOrigen, @almDestino, @usuarioId, @personaId, @serieGuia, @numeroGuia, @observacion, 1, {nowFunc}); {selectId}";

                    using var cmdCab = dbConn.CreateCommand();
                    cmdCab.Transaction = transaccion;
                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                    AgregarParametro(cmdCab, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now);
                    AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                    AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                    AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdCab, "@ubicacionId", valUbicacionSalida);
                    AgregarParametro(cmdCab, "@almId", cabecera.AlmacenId ?? cabecera.AlmacenOrigenId ?? 1);
                    AgregarParametro(cmdCab, "@almOrigen", cabecera.AlmacenOrigenId);
                    AgregarParametro(cmdCab, "@almDestino", cabecera.AlmacenDestinoId);
                    AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                    AgregarParametro(cmdCab, "@personaId", valPersonaSalida);
                    AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                    AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);
                    AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);

                    movimientoIdInserted = Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
                }
                else
                {
                    movimientoIdInserted = existingMovimientoId.Value;
                    string updateCab = @"
                UPDATE movimientos 
                SET fecha_movimiento = @fecha, 
                    motivo_producto_id = @motivoId, 
                    ubicacion_id = @ubicacionId, 
                    almacen_id = @almId,
                    almacen_origen_id = @almOrigen,
                    almacen_destino_id = @almDestino,
                    usuario_id = @usuarioId, 
                    persona_comercial_id = @personaId, 
                    observacion = @observacion, 
                    serie_guia = @serieGuia, 
                    numero_guia = @numeroGuia 
                WHERE id = @id";

                    using var cmdUpdCab = dbConn.CreateCommand();
                    cmdUpdCab.Transaction = transaccion;
                    cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                    AgregarParametro(cmdUpdCab, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now);
                    AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmdUpdCab, "@ubicacionId", valUbicacionSalida);
                    AgregarParametro(cmdUpdCab, "@almId", cabecera.AlmacenId ?? cabecera.AlmacenOrigenId ?? 1);
                    AgregarParametro(cmdUpdCab, "@almOrigen", cabecera.AlmacenOrigenId);
                    AgregarParametro(cmdUpdCab, "@almDestino", cabecera.AlmacenDestinoId);
                    AgregarParametro(cmdUpdCab, "@usuarioId", usuarioId);
                    AgregarParametro(cmdUpdCab, "@personaId", valPersonaSalida);
                    AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                    AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                    AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                    AgregarParametro(cmdUpdCab, "@id", movimientoIdInserted);
                    await cmdUpdCab.ExecuteNonQueryAsync();
                }

                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { movimientoIdInserted }, dbConn, transaccion)
                    : new HashSet<int>();

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
                                         VALUES (@movId, @prodId, 0, @cant, @costo, {nowFunc}); {selectId}";
                        using var cmdDet = dbConn.CreateCommand();
                        cmdDet.Transaction = transaccion;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                        AgregarParametro(cmdDet, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                        AgregarParametro(cmdDet, "@cant", cantidadDespachoPura);
                        AgregarParametro(cmdDet, "@costo", costoUnitarioPuro);
                        idDetalle = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    idsDetallesActivos.Add(idDetalle);

                    var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId).ToList();
                    if (codigosProd.Any())
                    {
                        var todosLosCodigosDelDetalle = new List<int>();
                        var codigosNuevosParaInsertar = new List<int>();

                        foreach (var cod in codigosProd)
                        {
                            // ✅ VALIDACIÓN: Solo procesar si tiene ID válido
                            if (cod.MovCodigo == null || cod.MovCodigo.CodigoCreadoId <= 0)
                            {
                                continue;  // Saltar códigos sin ID resuelto
                            }

                            int cId = cod.MovCodigo.CodigoCreadoId;
                            nuevosCodigosIds.Add(cId);
                            todosLosCodigosDelDetalle.Add(cId);

                            if (!codigosPreviosEnBD.Contains(cId))
                            {
                                codigosNuevosParaInsertar.Add(cId);
                            }
                        }

                        var serviceIng = new IngresoMovimientoService();
                        var rangosReconstruidos = serviceIng.GenerarRangosDesdeCodigos(codigosProd);
                        foreach (var r in rangosReconstruidos)
                        {
                            string sqlInsRango = $@"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at) 
                       VALUES (@pId, @cat, @abrev, @dNum, @hNum, @detId, {nowFunc})";
                            using var cmdR = dbConn.CreateCommand();
                            cmdR.Transaction = transaccion;
                            cmdR.CommandText = QueryAdapter.FormatearConsulta(sqlInsRango);
                            AgregarParametro(cmdR, "@pId", item.ProductoId);
                            AgregarParametro(cmdR, "@cat", r.CategoriaProductoId);
                            AgregarParametro(cmdR, "@abrev", r.AbreviaturaBase);
                            AgregarParametro(cmdR, "@dNum", r.DesdeNum);
                            AgregarParametro(cmdR, "@hNum", r.HastaNum);
                            AgregarParametro(cmdR, "@detId", idDetalle);
                            await cmdR.ExecuteNonQueryAsync();
                        }

                        if (codigosNuevosParaInsertar.Any())
                        {
                            await InsertarMovimientoCodigosSalidaMasivoAsync(movimientoIdInserted, idDetalle, codigosNuevosParaInsertar, dbConn, transaccion);
                        }

                        bool esTransferencia = (cabecera.MotivoProductoId == 4 || cabecera.MotivoProductoId == 10) && cabecera.AlmacenDestinoId.HasValue;
                        int estadoDestinoCodigo = esTransferencia ? 5 : 4;
                        int? almacenAsignar = esTransferencia ? cabecera.AlmacenDestinoId : null;

                        await ActualizarEstadoYAlmacenCodigosMasivoAsync(todosLosCodigosDelDetalle, estadoDestinoCodigo, almacenAsignar, dbConn, transaccion);
                    }
                }

                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();
                foreach (var codId in codigosAEliminar)
                {
                    if (codId <= 0) continue;
                    bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, movimientoIdInserted, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);

                    if (tieneFuturo)
                    {
                        string codigoTexto = "";
                        using (var cmdName = dbConn.CreateCommand())
                        {
                            cmdName.Transaction = transaccion;
                            cmdName.CommandText = QueryAdapter.FormatearConsulta("SELECT codigo FROM codigos_creados WHERE id = @cId");
                            AgregarParametro(cmdName, "@cId", codId);
                            var resName = await cmdName.ExecuteScalarAsync();
                            if (resName != null) codigoTexto = resName.ToString()!;
                        }

                        throw new Exception($"⚠️ Operación Rechazada por Seguridad de Kárdex:\n\nEl código '{codigoTexto}' no puede ser retirado de esta salida porque ya cuenta con un reingreso o devolución posterior registrada.");
                    }

                    var (estadoAnterior, almacenAnterior) = await ObtenerEstadoYAlmacenAnteriorAsync(codId, movimientoIdInserted, dbConn, transaccion);
                    await ActualizarEstadoYAlmacenCodigosMasivoAsync(new List<int> { codId }, estadoAnterior, almacenAnterior, dbConn, transaccion);
                    await EliminarMovimientoCodigoAsync(movimientoIdInserted, codId, dbConn, transaccion);
                }

                string sqlPurgarDetallesVacios = @"
    DELETE FROM registro_rangos 
    WHERE movimiento_detalle_id IN (
        SELECT id FROM movimiento_detalles 
        WHERE movimiento_id = @movId AND (cantidad_salida <= 0 OR id NOT IN (
            SELECT DISTINCT movimiento_detalle_id FROM movimiento_codigos WHERE movimiento_id = @movId
        ))
    );

    DELETE FROM movimiento_detalles 
    WHERE movimiento_id = @movId 
      AND (cantidad_salida <= 0 OR id NOT IN (
          SELECT DISTINCT movimiento_detalle_id FROM movimiento_codigos WHERE movimiento_id = @movId
      ));";

                using (var cmdPurga = dbConn.CreateCommand())
                {
                    cmdPurga.Transaction = transaccion;
                    cmdPurga.CommandText = QueryAdapter.FormatearConsulta(sqlPurgarDetallesVacios);
                    AgregarParametro(cmdPurga, "@movId", movimientoIdInserted);
                    await cmdPurga.ExecuteNonQueryAsync();
                }

                int almacenEmisor = cabecera.AlmacenOrigenId ?? 1;
                var productosUnicos = listaProductos.Select(p => p.ProductoId).Distinct();
                foreach (var pid in productosUnicos)
                {
                    await ActualizarStockProductoPorKardexAsync(pid, almacenEmisor, dbConn, transaccion);
                }

                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception(ex.Message, ex);
            }
        }

        public async Task<MovimientoCompletoDTO?> GetMovimientoCompletoAsync(string serie, string numero, int miAlmacenId)
        {
            var resultado = new MovimientoCompletoDTO();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) return null;
            if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
        SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, 
               m.persona_comercial_id, m.ubicacion_id, m.almacen_id, m.almacen_origen_id, m.almacen_destino_id,
               m.serie_guia, m.numero_guia, m.observacion, m.estado_id
        FROM movimientos m {nolock}
        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
        WHERE m.serie_documento = @serie 
        AND m.numero_documento = @numero
        AND mp.tipo_movimiento_id = 2
        AND {coalesceFunc}(m.almacen_id, {coalesceFunc}(m.almacen_origen_id, 1)) = @miAlmacen");

                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);
                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);

                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;

                resultado.Movimiento = new Movimiento
                {
                    Id = rd.GetInt32(rd.GetOrdinal("id")),
                    FechaMovimiento = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = rd["serie_documento"].ToString(),
                    NumeroDocumento = rd["numero_documento"].ToString(),
                    MotivoProductoId = rd.IsDBNull(rd.GetOrdinal("motivo_producto_id")) ? 0 : rd.GetInt32(rd.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = rd.IsDBNull(rd.GetOrdinal("persona_comercial_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("persona_comercial_id")),
                    UbicacionId = rd.IsDBNull(rd.GetOrdinal("ubicacion_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("ubicacion_id")),
                    AlmacenId = rd.IsDBNull(rd.GetOrdinal("almacen_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("almacen_id")),
                    AlmacenOrigenId = rd.IsDBNull(rd.GetOrdinal("almacen_origen_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("almacen_origen_id")),
                    AlmacenDestinoId = rd.IsDBNull(rd.GetOrdinal("almacen_destino_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("almacen_destino_id")),
                    SerieGuia = rd["serie_guia"]?.ToString(),
                    NumeroGuia = rd["numero_guia"]?.ToString(),
                    Observacion = rd["observacion"]?.ToString(),
                    EstadoId = rd.GetInt32(rd.GetOrdinal("estado_id"))
                };
            }

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
            SELECT id, movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario 
            FROM movimiento_detalles {nolock}
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

            string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";

            try
            {
                DateTime fechaMovimiento;
                int almacenEmisor = 1;

                using (var cmdMov = dbConn.CreateCommand())
                {
                    cmdMov.Transaction = transaccion;
                    cmdMov.CommandText = QueryAdapter.FormatearConsulta(
                        $"SELECT fecha_movimiento, estado_id, {coalesceFunc}(almacen_id, {coalesceFunc}(almacen_origen_id, 1)) FROM movimientos WHERE id = @movId");
                    AgregarParametro(cmdMov, "@movId", movimientoId);

                    using var rdrMov = await cmdMov.ExecuteReaderAsync();
                    if (!await rdrMov.ReadAsync()) throw new Exception("El movimiento no existe.");
                    if (rdrMov.GetInt32(1) == 2) throw new Exception("Este movimiento de salida ya está anulado.");

                    fechaMovimiento = rdrMov.IsDBNull(0) ? DateTime.Today : rdrMov.GetDateTime(0);
                    almacenEmisor = rdrMov.GetInt32(2);
                }

                // 1. Obtener lista de códigos afectados en esta salida
                var codigosAnular = new List<int>();
                using (var cmdCod = dbConn.CreateCommand())
                {
                    cmdCod.Transaction = transaccion;
                    cmdCod.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId");
                    AgregarParametro(cmdCod, "@movId", movimientoId);
                    using var rdrC = await cmdCod.ExecuteReaderAsync();
                    while (await rdrC.ReadAsync()) codigosAnular.Add(rdrC.GetInt32(0));
                }

                // 2. Validar si tienen movimientos POSTERIORES al de la anulación
                foreach (var codId in codigosAnular)
                {
                    bool tienePost = await TieneMovimientosPosterioresAsync(codId, movimientoId, fechaMovimiento, dbConn, transaccion);
                    if (tienePost) throw new Exception($"Rechazado: El código ID {codId} registra movimientos logísticos posteriores.");
                }

                progress?.Report(30);

                // 3. Eliminar rangos registrados en esta salida
                string sqlEliminarRangosAnulados = "DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
                using (var cmdDelRangos = dbConn.CreateCommand())
                {
                    cmdDelRangos.Transaction = transaccion;
                    cmdDelRangos.CommandText = QueryAdapter.FormatearConsulta(sqlEliminarRangosAnulados);
                    AgregarParametro(cmdDelRangos, "@movId", movimientoId);
                    await cmdDelRangos.ExecuteNonQueryAsync();
                }

                progress?.Report(60);

                // 4. 🌟 REVERSIÓN EMPRESARIAL REAL: Consultar historial individual de cada código
                foreach (var codId in codigosAnular)
                {
                    var (estadoAnterior, almacenAnterior) = await ObtenerEstadoYAlmacenAnteriorAsync(codId, movimientoId, dbConn, transaccion);

                    using var cmdRevert = dbConn.CreateCommand();
                    cmdRevert.Transaction = transaccion;
                    cmdRevert.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = @est, almacen_id = @alm WHERE id = @codId");
                    AgregarParametro(cmdRevert, "@est", estadoAnterior);
                    AgregarParametro(cmdRevert, "@alm", almacenAnterior);
                    AgregarParametro(cmdRevert, "@codId", codId);
                    await cmdRevert.ExecuteNonQueryAsync();
                }

                // 5. 🌟 DESVINCULACIÓN (NUEVO): Eliminar registros de movimiento_codigos por consistencia con Ingresos
                using (var cmdDelMC = dbConn.CreateCommand())
                {
                    cmdDelMC.Transaction = transaccion;
                    cmdDelMC.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_id = @movId");
                    AgregarParametro(cmdDelMC, "@movId", movimientoId);
                    await cmdDelMC.ExecuteNonQueryAsync();
                }

                // 6. Marcar movimiento como Anulado (2)
                using (var cmdStatus = dbConn.CreateCommand())
                {
                    cmdStatus.Transaction = transaccion;
                    cmdStatus.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimientos SET estado_id = 2 WHERE id = @movId");
                    AgregarParametro(cmdStatus, "@movId", movimientoId);
                    await cmdStatus.ExecuteNonQueryAsync();
                }

                // 7. Recalcular Kárdex/Stock de productos involucrados
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
                        await ActualizarStockProductoPorKardexAsync(pid, almacenEmisor, dbConn, transaccion);
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
        }
    }
}