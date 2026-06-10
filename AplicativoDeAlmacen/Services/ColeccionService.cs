using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
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

        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<Coleccion>> ObtenerTodosAsync()
        {
            var lista = new List<Coleccion>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                SELECT c.id,
                       c.ano,
                       c.estado_id,
                       e.nombre AS estado_nombre
                FROM colecciones c
                LEFT JOIN estados e ON c.estado_id = e.id
                ORDER BY c.ano DESC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new Coleccion
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),

                    Ano = reader.IsDBNull(reader.GetOrdinal("ano"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("ano")),

                    EstadoId = reader.GetInt32(reader.GetOrdinal("estado_id")),

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

        public async Task InsertarAsync(Coleccion c)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                INSERT INTO colecciones
                (
                    ano,
                    estado_id
                )
                VALUES
                (
                    @Ano,
                    @EstadoId
                )";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Ano", c.Ano);
            AgregarParametro(cmd, "@EstadoId", c.EstadoId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                SELECT id, nombre
                FROM estados
                ORDER BY
                CASE WHEN nombre = 'Activo' THEN 0 ELSE 1 END,
                nombre";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await ((DbDataReader)reader).ReadAsync())
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