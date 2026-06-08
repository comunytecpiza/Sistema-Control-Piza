using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Models;
using System.Data.SqlClient;
using AplicativoDeAlmacen.Views;

{
    
}

namespace AplicativoDeAlmacen.Services
{
    public class MovimientoService
    {
        private readonly DatabaseConnection _database;

        public MovimientoService()
        {
            _database = new DatabaseConnection();
        }

        public List<MotivoProducto> ObtenerMotivosEntrada()
        {
            var lista = new List<MotivoProducto>();
            string query = @"SELECT id, descripcion 
                             FROM motivo_productos 
                             WHERE tipo_movimiento = 'entrada' 
                             ORDER BY id";
            try
            {

                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new MotivoProducto
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                            });
                        }
                    }
                }
                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar motivos de entrada: " + ex.Message);
            }
        }

        public int GenerarSiguienteIdMovimiento()
        {
            string query = "SELECT ISNULL(MAX(id), 0) + 1 FROM movimientos_productos";
            try
            {
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error al calcular el siguiente ID de movimiento: " + ex.Message);
            }
        }

        public int ObtenerUltimoSecuencialCodigo(int productoId)
        {
            string query = "SELECT ISNULL(MAX(numero_secuencia), 0) FROM codigos_qr_productos WHERE producto_id = @ProductoId";
            try
            {
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ProductoId", productoId);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener la secuencia de códigos: " + ex.Message);
            }
        }

        public void RegistrarIngresoProductos(Movimiento movimiento, List<Producto> productos, List<IngresoProductosUserControl.CodigoDetalle> codigos)
        {
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Iteramos los productos agregados a la grilla para procesar los movimientos
                        foreach (var producto in productos)
                        {
                            // En tu base de datos la tabla se llama 'movimientos_productos' 
                            string queryMovimiento = @"
                                INSERT INTO movimientos_productos 
                                (producto_id, cantidad, fecha_movimiento, tipo_movimiento,
                                 motivo_producto_id, personas_comerciales_id, guia, observacion, usuario_id)
                                OUTPUT INSERTED.ID
                                VALUES 
                                (@ProductoId, @Cantidad, @FechaMovimiento, 'entrada',
                                 @MotivoId, @PersonaId, @Guia, @Observacion, @UsuarioId)";

                            int movimientoId;
                            using (SqlCommand cmd = new SqlCommand(queryMovimiento, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@ProductoId", producto.Id);
                                cmd.Parameters.AddWithValue("@Cantidad", producto.Cantidad);
                                
                                // Conversión segura de DateOnly a DateTime para SQL Server
                                DateTime fechaSql = movimiento.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
                                cmd.Parameters.AddWithValue("@FechaMovimiento", fechaSql);
                                
                                cmd.Parameters.AddWithValue("@MotivoId", movimiento.MotivoProducto.Id);
                                cmd.Parameters.AddWithValue("@PersonaId", movimiento.PersonaComercial.Id);
                                cmd.Parameters.AddWithValue("@Guia", movimiento.NumeroGuia ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Observacion", movimiento.Observacion ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@UsuarioId", movimiento.Usuario?.Id ?? 1);

                                movimientoId = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // 2. Modificación de Stock en cascada dentro de la misma transacción
                            string queryUpdateStock = @"UPDATE productos 
                                                       SET cantidad = cantidad + @Cantidad 
                                                       WHERE id = @ProductoId";

                            using (SqlCommand cmdStock = new SqlCommand(queryUpdateStock, conn, transaction))
                            {
                                cmdStock.Parameters.AddWithValue("@Cantidad", producto.Cantidad);
                                cmdStock.Parameters.AddWithValue("@ProductoId", producto.Id);
                                cmdStock.ExecuteNonQuery();
                            }

                            // 3. Persistencia de códigos correlativos generados para el producto actual
                            string prefix = $"PROD-{producto.Id}-";
                            var codigosDelProducto = codigos.Where(c => c.Codigo.StartsWith(prefix));

                            foreach (var codigo in codigosDelProducto)
                            {
                                string queryCodigo = @"
                                    INSERT INTO codigos_qr_productos 
                                    (producto_id, numero_secuencia, codigo_unico, estado_id, created_at)
                                    VALUES 
                                    (@ProductoId, @NumeroSecuencia, @CodigoUnico, @EstadoId, GETDATE())";

                                using (SqlCommand cmdCodigo = new SqlCommand(queryCodigo, conn, transaction))
                                {
                                    cmdCodigo.Parameters.AddWithValue("@ProductoId", producto.Id);
                                    cmdCodigo.Parameters.AddWithValue("@NumeroSecuencia", codigo.NumeroFila);
                                    cmdCodigo.Parameters.AddWithValue("@CodigoUnico", codigo.Codigo);
                                    cmdCodigo.Parameters.AddWithValue("@EstadoId", movimiento.Estado?.Id ?? 1);
                                    cmdCodigo.ExecuteNonQuery();
                                }
                            }
                        }

                        // Si todas las inserciones y updates fueron exitosos, consolidamos la transacción
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
