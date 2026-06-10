using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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

        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<UnidadMedida>> ObtenerTodosAsync()
        {
            var lista = new List<UnidadMedida>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                SELECT
                    u.id,
                    u.descripcion,
                    u.abreviatura,
                    u.estado_id,
                    e.nombre AS estado_nombre
                FROM unidad_medida u
                LEFT JOIN estados e ON u.estado_id = e.id";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new UnidadMedida
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Descripcion = reader["descripcion"]?.ToString() ?? string.Empty,
                    Abreviatura = reader["abreviatura"]?.ToString() ?? string.Empty,

                    EstadoId = reader["estado_id"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(reader["estado_id"]),

                    Estado = new Estado
                    {
                        Nombre = reader["estado_nombre"]?.ToString() ?? "DESCONOCIDO"
                    }
                });
            }

            return lista;
        }

        public async Task InsertarAsync(UnidadMedida u)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                INSERT INTO unidad_medida
                (
                    descripcion,
                    abreviatura,
                    estado_id
                )
                VALUES
                (
                    @Descripcion,
                    @Abreviatura,
                    @EstadoId
                )";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Descripcion",
                string.IsNullOrWhiteSpace(u.Descripcion)
                    ? DBNull.Value
                    : u.Descripcion);

            AgregarParametro(cmd, "@Abreviatura",
                string.IsNullOrWhiteSpace(u.Abreviatura)
                    ? DBNull.Value
                    : u.Abreviatura);

            AgregarParametro(cmd, "@EstadoId", u.EstadoId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task ActualizarAsync(UnidadMedida u)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                UPDATE unidad_medida
                SET
                    descripcion = @Descripcion,
                    abreviatura = @Abreviatura,
                    estado_id = @EstadoId
                WHERE id = @Id";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Id", u.Id);

            AgregarParametro(cmd, "@Descripcion",
                string.IsNullOrWhiteSpace(u.Descripcion)
                    ? DBNull.Value
                    : u.Descripcion);

            AgregarParametro(cmd, "@Abreviatura",
                string.IsNullOrWhiteSpace(u.Abreviatura)
                    ? DBNull.Value
                    : u.Abreviatura);

            AgregarParametro(cmd, "@EstadoId", u.EstadoId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, nombre FROM estados ORDER BY nombre";

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Estado
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Nombre = reader["nombre"]?.ToString() ?? string.Empty
                });
            }

            return lista;
        }
    }
}