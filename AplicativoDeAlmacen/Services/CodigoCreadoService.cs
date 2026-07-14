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
                    SELECT id, registro_codigo_id, codigo, es_manual, estado_id
                    FROM codigos_creados
                    WHERE registro_codigo_id = @id
                    ORDER BY codigo ASC"; // Ordenamos por código para mantener la secuencia visual

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

        // 🌟 NUEVO MÉTODO: Evita duplicados a nivel de base de datos
        public async Task<bool> ExisteCodigoAsync(int registroId, string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // Verificamos si este código ya existe para el mismo producto (incluso en otros lotes)
                string query = @"
                    SELECT COUNT(1) 
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    WHERE cc.codigo = @cod 
                      AND rc.producto_id = (SELECT producto_id FROM registro_codigos WHERE id = @rid)";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@cod", codigo);
                    AgregarParametro(cmd, "@rid", registroId);

                    int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    return count > 0;
                }
            }
        }

        public async Task RegistrarManualAsync(int registroId, string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    INSERT INTO codigos_creados (registro_codigo_id, codigo, es_manual, estado_id)
                    VALUES (@rid, @cod, 1, 1)"; // es_manual = 1

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@rid", registroId);
                    AgregarParametro(cmd, "@cod", codigo);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

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


        public async Task<int> ObtenerIdCodigoPorTextoAsync(string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open)
                    await dbConn.OpenAsync();

                string query = "SELECT id FROM codigos_creados WHERE codigo = @codigo";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    // Asegúrate de pasar el valor correctamente
                    AgregarParametro(cmd, "@codigo", codigo);

                    // Usamos ExecuteScalarAsync para no bloquear el hilo de la UI
                    object result = await cmd.ExecuteScalarAsync();

                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
        }

        public async Task<CodigoCreado?> ObtenerPorCodigoExactoAsync(string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // Esta búsqueda es simple y directa, sin filtros de estado
                string query = "SELECT id, registro_codigo_id, codigo, es_manual, estado_id FROM codigos_creados WHERE codigo = @cod";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@cod", codigo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new CodigoCreado
                            {
                                Id = reader.GetInt32(0),
                                Codigo = reader.GetString(2),
                                EstadoId = reader.GetInt32(4)
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<int> ObtenerIdCodigoPorProductoAsync(string codigo, int productoId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open)
                    await dbConn.OpenAsync();

                // 🌟 Esta consulta es la clave: busca por código Y por el ID de producto
                string query = @"SELECT cc.id 
                         FROM codigos_creados cc
                         INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                         WHERE cc.codigo = @codigo 
                         AND rc.producto_id = @prodId";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@codigo", codigo);
                    AgregarParametro(cmd, "@prodId", productoId);

                    object result = await cmd.ExecuteScalarAsync();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
        }
    }
}