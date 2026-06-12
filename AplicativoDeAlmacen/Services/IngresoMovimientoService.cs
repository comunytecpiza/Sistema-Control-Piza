using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class IngresoMovimientoService
    {
        private readonly DatabaseConnection _database;

        public IngresoMovimientoService()
        {
            _database = new DatabaseConnection();
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
            int ubicacionId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaccion = dbConn.BeginTransaction())
                {
                    try
                    {
                        // CAMBIO MULTI-MOTOR: Solución dinámica para obtener el ID sin usar OUTPUT
                        string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

                        // =======================================================
                        // PASO 1: Insertar Cabecera del Movimiento
                        // =======================================================
                        string queryCabecera = $@"
                            INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                                     motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id)
                            VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId);
                            {selectId}";

                        int movimientoIdInserted = 0;
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
                            AgregarParametro(cmdCab, "@usuarioId", cabecera.UsuarioId);
                            AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
                            AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);

                            object resultCab = await cmdCab.ExecuteScalarAsync();
                            if (resultCab == null || resultCab == DBNull.Value) throw new Exception("No se pudo obtener el ID de la cabecera.");
                            movimientoIdInserted = Convert.ToInt32(resultCab);
                        }

                        // =======================================================
                        // PASO 2: Insertar el Detalle de Productos
                        // =======================================================
                        string queryDetalle = $@"
                            INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, costo_unitario)
                            VALUES (@movimientoId, @productoId, @cantidad, @costo);
                            {selectId}";

                        string queryRangos = @"
                            INSERT INTO registro_rangos (
                                producto_id, categoria_producto_id, abreviatura_base, 
                                desde_num, hasta_num, movimiento_detalle_id, usuario_id
                            )
                            VALUES (
                                @productoId, @categoriaProductoId, @abreviaturaBase, 
                                @desdeNum, @hastaNum, @movimientoDetalleId, @usuarioId
                            );";

                        // =======================================================
                        // PASO 3: Vincular códigos físicos
                        // =======================================================
                        string queryMovCodigos = @"
                            INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id) 
                            VALUES (@movId, @detId, @codId);";

                        foreach (var item in productos)
                        {
                            int detalleIdInserted = 0;

                            using (var cmdDet = dbConn.CreateCommand())
                            {
                                cmdDet.Transaction = transaccion;
                                cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                                AgregarParametro(cmdDet, "@movimientoId", movimientoIdInserted);
                                AgregarParametro(cmdDet, "@productoId", item.Detalle.ProductoId);
                                AgregarParametro(cmdDet, "@cantidad", item.Detalle.CantidadIngreso);
                                AgregarParametro(cmdDet, "@costo", item.Detalle.CostoUnitario);

                                object resultDet = await cmdDet.ExecuteScalarAsync();
                                if (resultDet == null || resultDet == DBNull.Value) throw new Exception("No se pudo obtener el ID del detalle.");
                                detalleIdInserted = Convert.ToInt32(resultDet);
                            }

                            var rangosDelProducto = rangos.Where(r => r.productoId == item.Detalle.ProductoId);

                            foreach (var rango in rangosDelProducto)
                            {
                                using (var cmdRan = dbConn.CreateCommand())
                                {
                                    cmdRan.Transaction = transaccion;
                                    cmdRan.CommandText = QueryAdapter.FormatearConsulta(queryRangos);

                                    AgregarParametro(cmdRan, "@productoId", rango.productoId);
                                    AgregarParametro(cmdRan, "@categoriaProductoId", rango.CategoriaProductoId);
                                    AgregarParametro(cmdRan, "@abreviaturaBase", rango.AbreviaturaBase);
                                    AgregarParametro(cmdRan, "@desdeNum", rango.DesdeNum);
                                    AgregarParametro(cmdRan, "@hastaNum", rango.HastaNum);
                                    AgregarParametro(cmdRan, "@movimientoDetalleId", detalleIdInserted);
                                    AgregarParametro(cmdRan, "@usuarioId", cabecera.UsuarioId);

                                    await cmdRan.ExecuteNonQueryAsync();
                                }

                                var idsCodigosFisicos = await ObtenerIdsCodigosPorRangoAsync(
                                    rango.AbreviaturaBase,
                                    rango.CategoriaProductoId,
                                    rango.DesdeNum,
                                    rango.HastaNum,
                                    dbConn,
                                    transaccion);

                                using (var cmdMovCod = dbConn.CreateCommand())
                                {
                                    cmdMovCod.Transaction = transaccion;
                                    cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(queryMovCodigos);

                                    var pMovId = cmdMovCod.CreateParameter();
                                    pMovId.ParameterName = "@movId";
                                    cmdMovCod.Parameters.Add(pMovId);

                                    var pDetId = cmdMovCod.CreateParameter();
                                    pDetId.ParameterName = "@detId";
                                    cmdMovCod.Parameters.Add(pDetId);

                                    var pCodId = cmdMovCod.CreateParameter();
                                    pCodId.ParameterName = "@codId";
                                    cmdMovCod.Parameters.Add(pCodId);

                                    foreach (var idFisico in idsCodigosFisicos)
                                    {
                                        pMovId.Value = movimientoIdInserted;
                                        pDetId.Value = detalleIdInserted;
                                        pCodId.Value = idFisico;
                                        await cmdMovCod.ExecuteNonQueryAsync();
                                    }
                                }
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

        private async Task<List<int>> ObtenerIdsCodigosPorRangoAsync(string baseLimpia, int categoriaId, int desde, int hasta, DbConnection conn, DbTransaction trans)
        {
            List<int> ids = new List<int>();
            string query = @"
                SELECT cc.id 
                FROM codigos_creados cc
                INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                WHERE cc.codigo LIKE @patron
                  AND rc.categoria_producto_id = @categoriaId
                  AND TRY_CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@patron", baseLimpia + "%");
                AgregarParametro(cmd, "@categoriaId", categoriaId);
                AgregarParametro(cmd, "@desde", desde);
                AgregarParametro(cmd, "@hasta", hasta);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        ids.Add(reader.GetInt32(0));
                    }
                }
            }
            return ids;
        }
    }
}