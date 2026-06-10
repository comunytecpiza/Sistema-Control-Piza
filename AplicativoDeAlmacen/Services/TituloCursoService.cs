using System;
using System.Data;
using System.Data.Common;
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
        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        public async Task<List<TituloCurso>> ObtenerTodosAsync()
        {
            var lista = new List<TituloCurso>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
            SELECT
                t.id,
                t.nombre,
                t.estado_id,
                e.nombre AS estado_nombre
            FROM titulo_curso t
            LEFT JOIN estados e ON t.estado_id = e.id
            ORDER BY t.nombre ASC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TituloCurso
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Nombre = reader.IsDBNull(reader.GetOrdinal("nombre"))
                        ? string.Empty
                        : reader.GetString(reader.GetOrdinal("nombre")),

                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("estado_id")),

                    Estado = new Estado
                    {
                        Nombre = reader.IsDBNull(reader.GetOrdinal("estado_nombre"))
                            ? "DESCONOCIDO"
                            : reader.GetString(reader.GetOrdinal("estado_nombre"))
                    }
                });
            }

            return lista;
        }

        public async Task InsertarAsync(TituloCurso t)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
            INSERT INTO titulo_curso
            (
                nombre,
                estado_id
            )
            VALUES
            (
                @Nombre,
                @EstadoId
            )";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(
                cmd,
                "@Nombre",
                string.IsNullOrEmpty(t.Nombre)
                    ? DBNull.Value
                    : t.Nombre);

            AgregarParametro(
                cmd,
                "@EstadoId",
                t.EstadoId ?? (object)DBNull.Value);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task ActualizarAsync(TituloCurso t)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
            UPDATE titulo_curso
            SET
                nombre = @Nombre,
                estado_id = @EstadoId
            WHERE id = @Id";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Id", t.Id);

            AgregarParametro(
                cmd,
                "@Nombre",
                string.IsNullOrEmpty(t.Nombre)
                    ? DBNull.Value
                    : t.Nombre);

            AgregarParametro(
                cmd,
                "@EstadoId",
                t.EstadoId ?? (object)DBNull.Value);

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
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1)
                });
            }

            return lista;
        }
    }
}