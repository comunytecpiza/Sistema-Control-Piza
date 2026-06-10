using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class CodigoCreadoService
    {
        private readonly DatabaseConnection _database;

        public CodigoCreadoService()
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

        public async Task<List<CodigoCreado>> ObtenerPorRegistroIdAsync(int registroCodigoId)
        {
            var lista = new List<CodigoCreado>();

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                SELECT id,
                       registro_codigo_id,
                       codigo,
                       es_manual,
                       estado_id
                FROM codigos_creados
                WHERE registro_codigo_id = @id
                ORDER BY es_manual ASC, codigo ASC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@id", registroCodigoId);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new CodigoCreado
                {
                    Id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    Codigo = reader.IsDBNull(2) ? "SIN CÓDIGO" : reader.GetString(2),
                    EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                    EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                });
            }

            return lista;
        }

        public async Task RegistrarManualAsync(int registroId, string codigo)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"
                INSERT INTO codigos_creados
                (
                    registro_codigo_id,
                    codigo,
                    es_manual,
                    estado_id
                )
                VALUES
                (
                    @rid,
                    @cod,
                    1,
                    1
                )";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@rid", registroId);
            AgregarParametro(cmd, "@cod", codigo);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }
    }
}