using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
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
                                 INNER JOIN tipo_producto tp ON p.tipo_producto_id = tp.id
                                 WHERE p.descripcion IS NOT NULL 
                                   AND p.abreviatura IS NOT NULL
                                   AND (tp.nombre LIKE '%Texto Escolar%' OR tp.nombre LIKE '%Plan Lector%')";

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
                        // =========================================================================
                        // 🛡️ ESCUDO ANTI-COLISIONES (Para múltiples sedes simultáneas)
                        // =========================================================================
                        int lastDashIndex = desde.LastIndexOf('-');
                        int desdeInt = int.Parse(desde.Substring(lastDashIndex + 1));
                        string prefijo = desde.Substring(0, lastDashIndex + 1);

                        string lenFunc = QueryAdapter.EsMySQL ? "LENGTH" : "LEN";
                        string castType = QueryAdapter.EsMySQL ? "SIGNED" : "INT";

                        // Si es SQL Server, usa WITH (UPDLOCK, HOLDLOCK). Si es MySQL, usa FOR UPDATE.
                        string bloqueoSQLServer = QueryAdapter.EsMySQL ? "" : "WITH (UPDLOCK, HOLDLOCK)";
                        string bloqueoMySQL = QueryAdapter.EsMySQL ? "FOR UPDATE" : "";

                        string queryVerif = $@"
                            SELECT MAX(CAST(SUBSTRING(codigo, {lenFunc}(@pref) + 1, {lenFunc}(codigo)) AS {castType}))
                            FROM codigos_creados cc {bloqueoSQLServer}
                            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                            WHERE rc.producto_id = @pId AND rc.categoria_producto_id = @catId
                            {bloqueoMySQL}";

                        using (var cmdVerif = dbConn.CreateCommand())
                        {
                            cmdVerif.Transaction = transaction;
                            cmdVerif.CommandText = QueryAdapter.FormatearConsulta(queryVerif);
                            AgregarParametro(cmdVerif, "@pref", prefijo);
                            AgregarParametro(cmdVerif, "@pId", productoId);
                            AgregarParametro(cmdVerif, "@catId", categoriaId);

                            object resultVerif = await cmdVerif.ExecuteScalarAsync();
                            int maxActual = (resultVerif != DBNull.Value && resultVerif != null) ? Convert.ToInt32(resultVerif) : 0;

                            if (maxActual >= desdeInt)
                            {
                                throw new Exception($"¡Colisión detectada!\n\nOtro operador en otra sede acaba de registrar códigos para este producto.\n\nEl sistema intentaba registrar desde el código {desdeInt}, pero la base de datos ya va en el {maxActual}.\n\nPor favor, cancele esta ventana y vuelva a seleccionar el producto para obtener el rango actualizado.");
                            }
                        }

                        // =========================================================================
                        // 1. GUARDAR REGISTRO MAESTRO
                        // =========================================================================
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

                        // =========================================================================
                        // 2. INSERCIÓN EN BLOQUE (BULK INSERT) PARA LA NUBE
                        // =========================================================================
                        int batchSize = 500;
                        for (int i = 0; i < cantidad; i += batchSize)
                        {
                            int currentBatch = Math.Min(batchSize, cantidad - i);
                            System.Text.StringBuilder queryBuilder = new System.Text.StringBuilder("INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id) VALUES ");

                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = transaction;

                                for (int j = 0; j < currentBatch; j++)
                                {
                                    int idx = i + j;
                                    string paramCod = $"@cod{idx}";
                                    string codigoGenerado = $"{prefijo}{(desdeInt + idx):D7}";

                                    queryBuilder.Append($"({registroId}, {paramCod}, 1)");
                                    if (j < currentBatch - 1) queryBuilder.Append(", ");

                                    AgregarParametro(cmd, paramCod, codigoGenerado);
                                }

                                cmd.CommandText = QueryAdapter.FormatearConsulta(queryBuilder.ToString());
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