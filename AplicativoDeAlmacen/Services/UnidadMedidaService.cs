using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class UnidadMedidaService
    {
        private readonly DatabaseConnection _database;

        public UnidadMedidaService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<UnidadMedida>> ObtenerTodosAsync()
        {
            var lista = new List<UnidadMedida>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"SELECT u.id, u.descripcion, u.abreviatura, u.estado_id, e.nombre AS estado_nombre 
                             FROM unidad_medida u 
                             LEFT JOIN estados e ON u.estado_id = e.id";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new UnidadMedida
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? string.Empty : reader.GetString(reader.GetOrdinal("descripcion")),
                    Abreviatura = reader.IsDBNull(reader.GetOrdinal("abreviatura")) ? string.Empty : reader.GetString(reader.GetOrdinal("abreviatura")),

                    // Como en tu modelo EstadoId ya no tiene el signo de interrogación (?), asumimos que siempre tiene valor
                    EstadoId = reader.GetInt32(reader.GetOrdinal("estado_id")),

                    // Llenamos el objeto Estado directamente
                    Estado = new Estado
                    {
                        Nombre = reader.IsDBNull(reader.GetOrdinal("estado_nombre")) ? "DESCONOCIDO" : reader.GetString(reader.GetOrdinal("estado_nombre"))
                    }
                });
            }
            return lista;
        }

        public async Task InsertarAsync(UnidadMedida u)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "INSERT INTO unidad_medida (descripcion, abreviatura, estado_id) VALUES (@Descripcion, @Abreviatura, @EstadoId)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrEmpty(u.Descripcion) ? (object)DBNull.Value : u.Descripcion);
            cmd.Parameters.AddWithValue("@Abreviatura", string.IsNullOrEmpty(u.Abreviatura) ? (object)DBNull.Value : u.Abreviatura);
            cmd.Parameters.AddWithValue("@EstadoId", u.EstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ActualizarAsync(UnidadMedida u)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "UPDATE unidad_medida SET descripcion = @Descripcion, abreviatura = @Abreviatura, estado_id = @EstadoId WHERE id = @Id";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", u.Id);
            cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrEmpty(u.Descripcion) ? (object)DBNull.Value : u.Descripcion);
            cmd.Parameters.AddWithValue("@Abreviatura", string.IsNullOrEmpty(u.Abreviatura) ? (object)DBNull.Value : u.Abreviatura);
            cmd.Parameters.AddWithValue("@EstadoId", u.EstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        // Método para cargar el ComboBox de Estados en la vista
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