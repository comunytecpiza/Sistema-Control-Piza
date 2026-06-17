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

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<CodigoCreado>> ObtenerPorRegistroIdAsync(int registroCodigoId)
        {
            var lista = new List<CodigoCreado>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    SELECT id,
                           registro_codigo_id,
                           codigo,
                           es_manual,
                           estado_id
                    FROM codigos_creados
                    WHERE registro_codigo_id = @id
                    ORDER BY es_manual ASC, codigo ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@id", registroCodigoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
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
                    }
                }
            }
            return lista;
        }

        public async Task RegistrarManualAsync(int registroId, string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

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

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@rid", registroId);
                    AgregarParametro(cmd, "@cod", codigo);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // ==============================================================
        // ESTE ES EL MÉTODO QUE TE FALTABA PARA QUE DEJE DE SALIR EL ERROR
        // ==============================================================
        public async Task EliminarAsync(int id)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = "DELETE FROM codigos_creados WHERE id = @id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@id", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}