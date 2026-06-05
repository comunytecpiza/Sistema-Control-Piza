using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
// ⚠️ Eliminamos 'using AplicativoDeAlmacen.Views;' para quitar la ambigüedad
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class ColeccionService
    {
        private readonly DatabaseConnection _database;

        public ColeccionService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<Coleccion>> ObtenerTodosAsync()
        {
            var lista = new List<Coleccion>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"SELECT c.id, c.ano, c.estado_id, e.nombre AS estado_nombre 
                             FROM colecciones c 
                             LEFT JOIN estados e ON c.estado_id = e.id 
                             ORDER BY c.ano DESC";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Coleccion
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),

                    // Ano en tu modelo es int? (acepta nulos)
                    Ano = reader.IsDBNull(reader.GetOrdinal("ano")) ? null : reader.GetInt32(reader.GetOrdinal("ano")),

                    // EstadoId en tu modelo es int (no acepta nulos, va directo)
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

        public async Task InsertarAsync(Coleccion c)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            string query = "INSERT INTO colecciones (ano, estado_id) VALUES (@Ano, @EstadoId)";

            using var cmd = new SqlCommand(query, conn);

            // Ano ahora es int?, mandamos DBNull si no tiene valor
            cmd.Parameters.AddWithValue("@Ano", c.Ano ?? (object)DBNull.Value);

            // EstadoId es int, se manda directo
            cmd.Parameters.AddWithValue("@EstadoId", c.EstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        // Reutilizamos el método para obtener estados
        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            using var cmd = new SqlCommand("SELECT id, nombre FROM estados ORDER BY CASE WHEN nombre = 'Activo' THEN 0 ELSE 1 END, nombre", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Estado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }
    }
}