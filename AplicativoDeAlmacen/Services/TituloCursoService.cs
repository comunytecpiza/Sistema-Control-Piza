using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class TituloCursoService
    {
        private readonly DatabaseConnection _database;

        public TituloCursoService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<TituloCurso>> ObtenerTodosAsync()
        {
            var lista = new List<TituloCurso>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"SELECT t.id, t.nombre, t.estado_id, e.nombre AS estado_nombre 
                             FROM titulo_curso t 
                             LEFT JOIN estados e ON t.estado_id = e.id 
                             ORDER BY t.nombre ASC";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TituloCurso
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Nombre = reader.IsDBNull(reader.GetOrdinal("nombre")) ? string.Empty : reader.GetString(reader.GetOrdinal("nombre")),
                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : reader.GetInt32(reader.GetOrdinal("estado_id")),

                    Estado = new Estado
                    {
                        Nombre = reader.IsDBNull(reader.GetOrdinal("estado_nombre")) ? "DESCONOCIDO" : reader.GetString(reader.GetOrdinal("estado_nombre"))
                    }
                });
            }
            return lista;
        }

        public async Task InsertarAsync(TituloCurso t)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "INSERT INTO titulo_curso (nombre, estado_id) VALUES (@Nombre, @EstadoId)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Nombre", string.IsNullOrEmpty(t.Nombre) ? (object)DBNull.Value : t.Nombre);
            cmd.Parameters.AddWithValue("@EstadoId", t.EstadoId ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ActualizarAsync(TituloCurso t)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "UPDATE titulo_curso SET nombre = @Nombre, estado_id = @EstadoId WHERE id = @Id";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", t.Id);
            cmd.Parameters.AddWithValue("@Nombre", string.IsNullOrEmpty(t.Nombre) ? (object)DBNull.Value : t.Nombre);
            cmd.Parameters.AddWithValue("@EstadoId", t.EstadoId ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
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