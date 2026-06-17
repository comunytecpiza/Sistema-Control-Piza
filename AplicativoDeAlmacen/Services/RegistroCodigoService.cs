using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class RegistroCodigoService
    {
        private readonly DatabaseConnection _database;

        public RegistroCodigoService()
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

        public async Task<List<Coleccion>> ObtenerColeccionesAsync()
        {
            var lista = new List<Coleccion>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, ano FROM colecciones ORDER BY ano DESC");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Coleccion { Id = reader.GetInt32(0), Ano = reader.GetInt32(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Producto>> ObtenerProductosComboAsync()
        {
            var lista = new List<Producto>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT p.id, p.descripcion, p.abreviatura, um.descripcion AS unidad_medida 
                                 FROM productos p 
                                 INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id 
                                 WHERE p.descripcion IS NOT NULL AND p.abreviatura IS NOT NULL";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Producto
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader["descripcion"] as string,
                                Abreviatura = reader["abreviatura"] as string,
                                UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida"] as string }
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<CategoriaProducto>> ObtenerCategoriasAsync()
        {
            var lista = new List<CategoriaProducto>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM categoria_producto");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new CategoriaProducto { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<RegistroCodigo>> ObtenerRegistrosAsync(int coleccionId, int categoriaId)
        {
            var lista = new List<RegistroCodigo>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 LA MAGIA: Hacemos un SELECT COUNT() dinámico para que la cantidad SIEMPRE sea exacta
                string query = @"SELECT rc.id, 
                                        (SELECT COUNT(cc.id) FROM codigos_creados cc WHERE cc.registro_codigo_id = rc.id) AS cantidad_real, 
                                        rc.desde, rc.hasta, p.descripcion AS producto_desc, 
                                        p.abreviatura, um.descripcion AS unidad_medida_desc, cp.nombre AS categoria_nombre
                                 FROM registro_codigos rc
                                 INNER JOIN productos p ON rc.producto_id = p.id
                                 INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id
                                 INNER JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
                                 WHERE rc.coleccion_id = @coleccionId AND rc.categoria_producto_id = @categoriaId
                                 ORDER BY rc.desde DESC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@coleccionId", coleccionId);
                    AgregarParametro(cmd, "@categoriaId", categoriaId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new RegistroCodigo
                            {
                                Id = reader.GetInt32(0),
                                // Usamos Convert.ToInt32 porque COUNT en MySQL devuelve un BigInt y en SQLServer un Int
                                Cantidad = Convert.ToInt32(reader.GetValue(1)),
                                Desde = reader["desde"] as string,
                                Hasta = reader["hasta"] as string,
                                Producto = new Producto
                                {
                                    Descripcion = reader["producto_desc"] as string,
                                    Abreviatura = reader["abreviatura"] as string,
                                    UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida_desc"] as string }
                                },
                                CategoriaProducto = new CategoriaProducto { Nombre = reader["categoria_nombre"] as string }
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<int> ObtenerUltimoCodigoAsync(int productoId, string abreviatura, int categoriaId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string lenFunc = QueryAdapter.EsMySQL ? "LENGTH" : "LEN";
                string castType = QueryAdapter.EsMySQL ? "SIGNED" : "INT";

                string query = $@"
                    SELECT MAX(CAST(SUBSTRING(codigo, {lenFunc}(@abreviatura) + 2, {lenFunc}(codigo)) AS {castType}))
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    WHERE rc.producto_id = @productoId 
                      AND rc.categoria_producto_id = @categoriaId";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@abreviatura", abreviatura ?? "");
                    AgregarParametro(cmd, "@productoId", productoId);
                    AgregarParametro(cmd, "@categoriaId", categoriaId);

                    object result = await cmd.ExecuteScalarAsync();
                    return (result != DBNull.Value && result != null) ? Convert.ToInt32(result) : 0;
                }
            }
        }

        public async Task GuardarCodigosTransactionAsync(int coleccionId, int productoId, int cantidad, string desde, string hasta, int categoriaId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaction = dbConn.BeginTransaction())
                {
                    try
                    {
                        string queryRegistro = "INSERT INTO registro_codigos (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id) VALUES (@cId, @pId, @cant, @des, @has, @catId);";
                        string selectId = QueryAdapter.EsMySQL ? " SELECT LAST_INSERT_ID();" : " SELECT SCOPE_IDENTITY();";

                        int registroId;
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryRegistro + selectId);
                            AgregarParametro(cmd, "@cId", coleccionId);
                            AgregarParametro(cmd, "@pId", productoId);
                            AgregarParametro(cmd, "@cant", cantidad);
                            AgregarParametro(cmd, "@des", desde);
                            AgregarParametro(cmd, "@has", hasta);
                            AgregarParametro(cmd, "@catId", categoriaId);

                            registroId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        int lastDashIndex = desde.LastIndexOf('-');
                        int desdeInt = int.Parse(desde.Substring(lastDashIndex + 1));
                        string prefijo = desde.Substring(0, lastDashIndex + 1);

                        string queryCodigos = "INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id) VALUES (@regId, @cod, 1)";

                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryCodigos);

                            var pRegId = cmd.CreateParameter();
                            pRegId.ParameterName = "@regId";
                            cmd.Parameters.Add(pRegId);

                            var pCod = cmd.CreateParameter();
                            pCod.ParameterName = "@cod";
                            cmd.Parameters.Add(pCod);

                            for (int i = 0; i < cantidad; i++)
                            {
                                pRegId.Value = registroId;
                                pCod.Value = $"{prefijo}{(desdeInt + i):D7}";
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task EliminarRegistroTransactionAsync(int registroCodigoId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaction = dbConn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM codigos_creados WHERE registro_codigo_id = @id");
                            AgregarParametro(cmd, "@id", registroCodigoId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM registro_codigos WHERE id = @id");
                            AgregarParametro(cmd, "@id", registroCodigoId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}