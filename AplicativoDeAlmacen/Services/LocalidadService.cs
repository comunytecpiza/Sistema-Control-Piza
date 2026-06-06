using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;



namespace AplicativoDeAlmacen.Services
{
    public class LocalidadService
    {
        private readonly DatabaseConnection _database;

        public LocalidadService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<Localidad>> ObtenerTodosAsync()
        {
            var lista = new List<Localidad>();

            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"
                SELECT
                    l.id,
                    l.nombre,
                    e.id AS estado_id,
                    e.nombre AS estado_nombre
                FROM localidades l
                LEFT JOIN estados e ON l.estado_id = e.id";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                Estado estado = null;

                if (!reader.IsDBNull(reader.GetOrdinal("estado_id")))
                {
                    estado = new Estado
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("estado_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("estado_nombre"))
                    };
                }

                lista.Add(new Localidad
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Nombre = reader.GetString(reader.GetOrdinal("nombre")),
                    Estado = estado
                });
            }

            return lista;
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();

            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = "SELECT id,nombre FROM estados ORDER BY nombre";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Estado
                {
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1)
                });
            }

            return lista;
        }

        public async Task GuardarAsync(Localidad localidad)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            bool esEdicion = localidad.Id > 0;

            string query = esEdicion
                ? @"UPDATE localidades
                    SET nombre = @nombre,
                        estado_id = @estadoId
                    WHERE id = @id"
                : @"INSERT INTO localidades
                    (
                        nombre,
                        estado_id
                    )
                    VALUES
                    (
                        @nombre,
                        @estadoId
                    )";

            using var cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@nombre", localidad.Nombre);

            cmd.Parameters.AddWithValue(
                "@estadoId",
                localidad.Estado?.Id ?? (object)DBNull.Value
            );

            if (esEdicion)
            {
                cmd.Parameters.AddWithValue("@id", localidad.Id);
            }

            await cmd.ExecuteNonQueryAsync();
        }
    }
}