using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Models; // Importante para que detecte Nivele, UnidadMedidum, etc.

namespace AplicativoDeAlmacen.Services
{
    public class ProductoService
    {
        private readonly DatabaseConnection _database;

        public ProductoService()
        {
            _database = new DatabaseConnection();
        }

        // =========================================================================
        // 1. MÉTODOS CRUD PRINCIPALES (PRODUCTO)
        // =========================================================================

        public async Task<List<Producto>> ObtenerTodosAsync()
        {
            var lista = new List<Producto>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            // La consulta hace los JOIN por ID y trae los nombres exactos
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

                    // 1. Instanciamos y llenamos el objeto UnidadMedida
                    UnidadMedida = new UnidadMedida
                    {
                        Descripcion = reader.IsDBNull(reader.GetOrdinal("unidad_medida")) ? string.Empty : reader.GetString(reader.GetOrdinal("unidad_medida"))
                    },

                    TipoProductoId = reader.IsDBNull(reader.GetOrdinal("tipo_producto_id")) ? null : reader.GetInt32(reader.GetOrdinal("tipo_producto_id")),
                    PrecioUnitario = reader.IsDBNull(reader.GetOrdinal("precio_unitario")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("precio_unitario")),
                    Porcentaje = reader.IsDBNull(reader.GetOrdinal("porcentaje")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("porcentaje")),

                    NivelId = reader.IsDBNull(reader.GetOrdinal("nivel_id")) ? null : reader.GetInt32(reader.GetOrdinal("nivel_id")),
                    GradoId = reader.IsDBNull(reader.GetOrdinal("grado_id")) ? null : reader.GetInt32(reader.GetOrdinal("grado_id")),
                    CursoId = reader.IsDBNull(reader.GetOrdinal("curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("curso_id")),
                    TituloCursoId = reader.IsDBNull(reader.GetOrdinal("titulo_curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("titulo_curso_id")),

                    AfectacionIgvId = reader.IsDBNull(reader.GetOrdinal("afectacion_igv_id")) ? null : reader.GetInt32(reader.GetOrdinal("afectacion_igv_id")),

                    // 2. Instanciamos y llenamos el objeto afectacion (respetando tu nombre en minúscula)
                    afectacion = new AfectacionIgv
                    {
                        Nombre = reader.IsDBNull(reader.GetOrdinal("afectacion_igv")) ? string.Empty : reader.GetString(reader.GetOrdinal("afectacion_igv"))
                    },

                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : reader.GetInt32(reader.GetOrdinal("estado_id")),

                    // 3. Instanciamos y llenamos el objeto Estado
                    Estado = new Estado
                    {
                        Nombre = reader.IsDBNull(reader.GetOrdinal("estado")) ? string.Empty : reader.GetString(reader.GetOrdinal("estado"))
                    }
                });
            }
            return lista;
        }

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

        public async Task EliminarAsync(int id)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "DELETE FROM productos WHERE id = @Id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

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

        // =========================================================================
        // 2. MÉTODOS PARA LLENAR LOS COMBOBOX (CATÁLOGOS)
        // =========================================================================

        public async Task<List<UnidadMedida>> ObtenerUnidadesMedidaAsync()
        {
            var lista = new List<UnidadMedida>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, descripcion FROM unidad_medida WHERE estado_id = 1 ORDER BY descripcion", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new UnidadMedida { Id = reader.GetInt32(0), Descripcion = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<TipoProducto>> ObtenerTiposProductoAsync()
        {
            var lista = new List<TipoProducto>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM tipo_producto ORDER BY nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new TipoProducto { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Nivele>> ObtenerNivelesAsync()
        {
            var lista = new List<Nivele>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM niveles ORDER BY nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Nivele { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Grado>> ObtenerGradosAsync(int nivelId)
        {
            var lista = new List<Grado>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM grados WHERE nivel_id = @NivelId ORDER BY nombre", conn);
            cmd.Parameters.AddWithValue("@NivelId", nivelId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Grado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Curso>> ObtenerCursosAsync(int nivelId)
        {
            var lista = new List<Curso>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM curso WHERE nivel_id = @NivelId ORDER BY nombre", conn);
            cmd.Parameters.AddWithValue("@NivelId", nivelId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Curso { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        // Le quitamos el "(int cursoId)" de los paréntesis
        public async Task<List<TituloCurso>> ObtenerTitulosAsync()
        {
            var lista = new List<TituloCurso>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            // Ya no filtramos por curso_id en el WHERE
            using var cmd = new SqlCommand("SELECT id, nombre FROM titulo_curso WHERE estado_id = 1 ORDER BY nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TituloCurso { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }

            return lista;
        }

        public async Task<List<AfectacionIgv>> ObtenerAfectacionesIgvAsync()
        {
            var lista = new List<AfectacionIgv>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM afectacion_igv ORDER BY nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new AfectacionIgv { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM estados ORDER BY nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Estado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }
    }
}