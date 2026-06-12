using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
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

        public async Task<List<MotivoProducto>> ObtenerMotivosProductosAsync()
        {
            var lista = new List<MotivoProducto>();

            using var conn = _database.GetConnection();
            await conn.OpenAsync();

           // string query = "SELECT id, descripcion, tipo_movimiento FROM motivo_productos ORDER BY descripcion ASC";
            string query = @"SELECT id, descripcion, tipo_movimiento 
                     FROM motivo_productos 
                     WHERE tipo_movimiento = 'entrada' 
                     ORDER BY descripcion ASC";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (reader.Read())
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

            return lista;
        }

        public async Task<Movimiento> GenerarSiguienteCorrelativoAsync(string serie)
        {
            var resultado = new Movimiento
            {
                SerieDocumento = serie,
                NumeroDocumento = "0000001"
            };

            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"
                SELECT ISNULL(MAX(CAST(numero_documento AS INT)), 0) + 1 
                FROM movimientos 
                WHERE serie_documento = @serie";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@serie", serie);

            object resultObj = await cmd.ExecuteScalarAsync();

            if (resultObj != null && resultObj != DBNull.Value)
            {
                int siguienteNumero = Convert.ToInt32(resultObj);
                resultado.NumeroDocumento = siguienteNumero.ToString("D7");
            }

            return resultado;
        }

        /// <summary>
        /// Registra de manera transaccional todo el movimiento, sus detalles y códigos asociados.
        /// </summary>
        public async Task<bool> RegistrarMovimientoCompletoAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> productos,
            List<RangoCodigoItem> rangos,
            int ubicacionId)
        {
            using (SqlConnection conexion = _database.GetConnection())
            {
                await conexion.OpenAsync();

                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        // =======================================================
                        // PASO 1: Insertar Cabecera del Movimiento
                        // =======================================================
                        string queryCabecera = @"
                            INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                                     motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id)
                            OUTPUT INSERTED.id
                            VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId);";

                        int movimientoIdInserted = 0;
                        using (SqlCommand cmdCab = new SqlCommand(queryCabecera, conexion, transaccion))
                        {
                            DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue
                                ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                                : DateTime.Today;

                            cmdCab.Parameters.AddWithValue("@estadoId", 1);
                            cmdCab.Parameters.AddWithValue("@fecha", fechaConvertida);
                            cmdCab.Parameters.AddWithValue("@serie", cabecera.SerieDocumento);
                            cmdCab.Parameters.AddWithValue("@numero", cabecera.NumeroDocumento);
                            cmdCab.Parameters.AddWithValue("@motivoId", cabecera.MotivoProductoId);
                            cmdCab.Parameters.AddWithValue("@ubicacionId", cabecera.UbicacionId);
                            cmdCab.Parameters.AddWithValue("@usuarioId", cabecera.UsuarioId);
                            cmdCab.Parameters.AddWithValue("@personaId", (object)cabecera.PersonaComercialId ?? DBNull.Value);
                            cmdCab.Parameters.AddWithValue("@observacion", (object)cabecera.Observacion ?? DBNull.Value);

                            movimientoIdInserted = Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
                        }

                        // =======================================================
                        // PASO 2: Insertar el Detalle de Productos y sus Rangos
                        // =======================================================
                        string queryDetalle = @"
                            INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, costo_unitario)
                            OUTPUT INSERTED.id
                            VALUES (@movimientoId, @productoId, @cantidad, @costo);";

                        string queryRangos = @"
                            INSERT INTO registro_rangos (
                                producto_id, 
                                categoria_producto_id, 
                                abreviatura_base, 
                                desde_num, 
                                hasta_num, 
                                movimiento_detalle_id, 
                                usuario_id
                            )
                            VALUES (
                                @productoId, 
                                @categoriaProductoId, 
                                @abreviaturaBase, 
                                @desdeNum, 
                                @hastaNum, 
                                @movimientoDetalleId, 
                                @usuarioId
                            );";

                        foreach (var item in productos)
                        {
                            int detalleIdInserted = 0;

                            // 1. Insertamos el producto en movimiento_detalles
                            using (SqlCommand cmdDet = new SqlCommand(queryDetalle, conexion, transaccion))
                            {
                                cmdDet.Parameters.AddWithValue("@movimientoId", movimientoIdInserted);
                                cmdDet.Parameters.AddWithValue("@productoId", item.Detalle.ProductoId);
                                cmdDet.Parameters.AddWithValue("@cantidad", item.Detalle.CantidadIngreso);
                                cmdDet.Parameters.AddWithValue("@costo", item.Detalle.CostoUnitario);

                                detalleIdInserted = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                            }

                            // 2. CORRECCIÓN: Filtrado usando "ProductoId" con mayúscula para evitar errores de nombres
                            var rangosDelProducto = rangos.Where(r => r.productoId== item.Detalle.ProductoId);

                            foreach (var rango in rangosDelProducto)
                            {
                                // Guardamos en la tabla de históricos de rangos
                                using (SqlCommand cmdRan = new SqlCommand(queryRangos, conexion, transaccion))
                                {
                                    cmdRan.Parameters.AddWithValue("@productoId", rango.productoId);
                                    cmdRan.Parameters.AddWithValue("@categoriaProductoId", rango.CategoriaProductoId);
                                    cmdRan.Parameters.AddWithValue("@abreviaturaBase", rango.AbreviaturaBase);
                                    cmdRan.Parameters.AddWithValue("@desdeNum", rango.DesdeNum);
                                    cmdRan.Parameters.AddWithValue("@hastaNum", rango.HastaNum);
                                    cmdRan.Parameters.AddWithValue("@movimientoDetalleId", detalleIdInserted);
                                    cmdRan.Parameters.AddWithValue("@usuarioId", cabecera.UsuarioId);

                                    await cmdRan.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Confirmamos la transacción de forma limpia y segura
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

        private async Task<List<int>> ObtenerIdsCodigosPorRangoAsync(string baseLimpia, int categoriaId, int desde, int hasta, SqlConnection conn, SqlTransaction trans)
        {
            List<int> ids = new List<int>();
            string query = @"
                SELECT cc.id 
                FROM codigos_creados cc
                INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                WHERE cc.codigo LIKE @patron
                  AND rc.categoria_producto_id = @categoriaId
                  AND TRY_CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

            using (SqlCommand cmd = new SqlCommand(query, conn, trans))
            {
                cmd.Parameters.Add("@patron", SqlDbType.VarChar).Value = baseLimpia + "%";
                cmd.Parameters.Add("@categoriaId", SqlDbType.Int).Value = categoriaId;
                cmd.Parameters.Add("@desde", SqlDbType.Int).Value = desde;
                cmd.Parameters.Add("@hasta", SqlDbType.Int).Value = hasta;

                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
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