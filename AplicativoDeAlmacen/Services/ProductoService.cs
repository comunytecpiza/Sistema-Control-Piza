using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models;
using static AplicativoDeAlmacen.Data.DataConnection; // Tu conexión central

namespace AplicativoDeAlmacen.Services
{
    public class ProductoService
    {
        private readonly DatabaseConnection _database;

        public ProductoService()
        {
            _database = new DatabaseConnection();
        }

        // 1. LEER TODOS LOS PRODUCTOS
        public async Task<List<Producto>> ObtenerTodosAsync()
        {
            var lista = new List<Producto>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"
                SELECT p.id, p.descripcion, p.abreviatura, p.unidad_medida_id, um.descripcion AS unidad_medida, 
                p.tipo_producto_id, p.precio_unitario, p.porcentaje, p.nivel_id, p.grado_id, p.curso_id,
                p.titulo_curso_id, p.afectacion_igv_id, ai.nombre AS afectacion_igv, p.estado_id, e.nombre AS estado
                FROM productos p
                LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                LEFT JOIN afectacion_igv ai ON p.afectacion_igv_id = ai.id
                LEFT JOIN estados e ON p.estado_id = e.id";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Producto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                    Abreviatura = reader.IsDBNull(reader.GetOrdinal("abreviatura")) ? null : reader.GetString(reader.GetOrdinal("abreviatura")),
                    UnidadMedidaId = reader.IsDBNull(reader.GetOrdinal("unidad_medida_id")) ? null : reader.GetInt32(reader.GetOrdinal("unidad_medida_id")),
                    UnidadMedida = reader.IsDBNull(reader.GetOrdinal("unidad_medida")) ? string.Empty : reader.GetString(reader.GetOrdinal("unidad_medida")),
                    TipoProductoId = reader.IsDBNull(reader.GetOrdinal("tipo_producto_id")) ? null : reader.GetInt32(reader.GetOrdinal("tipo_producto_id")),
                    PrecioUnitario = reader.IsDBNull(reader.GetOrdinal("precio_unitario")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("precio_unitario")),
                    Porcentaje = reader.IsDBNull(reader.GetOrdinal("porcentaje")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("porcentaje")),
                    NivelId = reader.IsDBNull(reader.GetOrdinal("nivel_id")) ? null : reader.GetInt32(reader.GetOrdinal("nivel_id")),
                    GradoId = reader.IsDBNull(reader.GetOrdinal("grado_id")) ? null : reader.GetInt32(reader.GetOrdinal("grado_id")),
                    CursoId = reader.IsDBNull(reader.GetOrdinal("curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("curso_id")),
                    TituloCursoId = reader.IsDBNull(reader.GetOrdinal("titulo_curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("titulo_curso_id")),
                    AfectacionIgvId = reader.IsDBNull(reader.GetOrdinal("afectacion_igv_id")) ? null : reader.GetInt32(reader.GetOrdinal("afectacion_igv_id")),
                    AfectacionIgv = reader.IsDBNull(reader.GetOrdinal("afectacion_igv")) ? string.Empty : reader.GetString(reader.GetOrdinal("afectacion_igv")),
                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : reader.GetInt32(reader.GetOrdinal("estado_id")),
                    Estado = reader.IsDBNull(reader.GetOrdinal("estado")) ? string.Empty : reader.GetString(reader.GetOrdinal("estado"))
                });
            }
            return lista;
        }

        // 2. INSERTAR PRODUCTO
        public async Task InsertarAsync(Producto p)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"INSERT INTO productos (descripcion, abreviatura, unidad_medida_id, tipo_producto_id, 
                             precio_unitario, porcentaje, nivel_id, grado_id, curso_id, titulo_curso_id, 
                             afectacion_igv_id, estado_id, created_at, updated_at) 
                             VALUES (@Descripcion, @Abreviatura, @UnidadMedidaId, @TipoProductoId, @PrecioUnitario, 
                             @Porcentaje, @NivelId, @GradoId, @CursoId, @TituloCursoId, @AfectacionIgvId, @EstadoId, 
                             GETDATE(), GETDATE())";

            using var cmd = new SqlCommand(query, conn);
            MapearParametros(cmd, p);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. ACTUALIZAR PRODUCTO
        public async Task ActualizarAsync(Producto p)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"UPDATE productos SET 
                             descripcion = @Descripcion, abreviatura = @Abreviatura, unidad_medida_id = @UnidadMedidaId, 
                             tipo_producto_id = @TipoProductoId, precio_unitario = @PrecioUnitario, porcentaje = @Porcentaje, 
                             nivel_id = @NivelId, grado_id = @GradoId, curso_id = @CursoId, titulo_curso_id = @TituloCursoId, 
                             afectacion_igv_id = @AfectacionIgvId, estado_id = @EstadoId, updated_at = GETDATE() 
                             WHERE id = @Id";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", p.Id);
            MapearParametros(cmd, p);
            await cmd.ExecuteNonQueryAsync();
        }

        // 4. ELIMINAR PRODUCTO
        public async Task EliminarAsync(int id)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "DELETE FROM productos WHERE id = @Id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // Método auxiliar para no repetir código en Insertar y Actualizar
        private void MapearParametros(SqlCommand cmd, Producto p)
        {
            cmd.Parameters.AddWithValue("@Descripcion", p.Descripcion);
            cmd.Parameters.AddWithValue("@Abreviatura", string.IsNullOrEmpty(p.Abreviatura) ? DBNull.Value : p.Abreviatura);
            cmd.Parameters.AddWithValue("@UnidadMedidaId", p.UnidadMedidaId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TipoProductoId", p.TipoProductoId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PrecioUnitario", p.PrecioUnitario);
            cmd.Parameters.AddWithValue("@Porcentaje", p.Porcentaje);
            cmd.Parameters.AddWithValue("@NivelId", p.NivelId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@GradoId", p.GradoId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CursoId", p.CursoId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TituloCursoId", p.TituloCursoId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@AfectacionIgvId", p.AfectacionIgvId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@EstadoId", p.EstadoId ?? (object)DBNull.Value);
        }
    }
}