using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services
{
    public class RegistroCodigoService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public RegistroCodigoService() => _database = new DataConnection.DatabaseConnection();

        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<Coleccion>> ObtenerColeccionesAsync()
        {
            var lista = new List<Coleccion>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, ano FROM colecciones ORDER BY ano DESC");

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
                lista.Add(new Coleccion { Id = reader.GetInt32(0), Ano = reader.GetInt32(1) });

            return lista;
        }

        public async Task<List<Producto>> ObtenerProductosComboAsync()
        {
            var lista = new List<Producto>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"SELECT p.id, p.descripcion, p.abreviatura, um.descripcion AS unidad_medida 
                             FROM productos p 
                             INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id 
                             WHERE p.descripcion IS NOT NULL AND p.abreviatura IS NOT NULL";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new Producto
                {
                    Id = reader.GetInt32(0),
                    Descripcion = reader["descripcion"] as string,
                    Abreviatura = reader["abreviatura"] as string,
                    UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida"] as string }
                });
            }
            return lista;
        }

        public async Task<List<CategoriaProducto>> ObtenerCategoriasAsync()
        {
            var lista = new List<CategoriaProducto>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM categoria_producto");

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
                lista.Add(new CategoriaProducto { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });

            return lista;
        }

        public async Task<List<RegistroCodigo>> ObtenerRegistrosAsync(int coleccionId, int categoriaId)
        {
            var lista = new List<RegistroCodigo>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string query = @"SELECT rc.id, rc.cantidad, rc.desde, rc.hasta, p.descripcion AS producto_desc, 
                                   p.abreviatura, um.descripcion AS unidad_medida_desc, cp.nombre AS categoria_nombre
                             FROM registro_codigos rc
                             INNER JOIN productos p ON rc.producto_id = p.id
                             INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id
                             INNER JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
                             WHERE rc.coleccion_id = @coleccionId AND rc.categoria_producto_id = @categoriaId
                             ORDER BY rc.desde DESC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@coleccionId", coleccionId);
            AgregarParametro(cmd, "@categoriaId", categoriaId);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new RegistroCodigo
                {
                    Id = reader.GetInt32(0),
                    Cantidad = reader.GetInt32(1),
                    Desde = reader["desde"] as string,
                    Hasta = reader["hasta"] as string,
                    Producto = new Producto { Descripcion = reader["producto_desc"] as string, Abreviatura = reader["abreviatura"] as string, UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida_desc"] as string } },
                    CategoriaProducto = new CategoriaProducto { Nombre = reader["categoria_nombre"] as string }
                });
            }
            return lista;
        }

        public async Task<int> ObtenerUltimoCodigoAsync(int productoId, string abreviatura, int categoriaId)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            string lenFunc = QueryAdapter.EsMySQL ? "LENGTH" : "LEN";

            // Agregamos el filtro rc.categoria_producto_id = @categoriaId
            string query = $@"
            SELECT MAX(CAST(SUBSTRING(codigo, {lenFunc}(@abreviatura) + 2, {lenFunc}(codigo)) AS SIGNED))
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
            WHERE rc.producto_id = @productoId 
              AND rc.categoria_producto_id = @categoriaId";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@abreviatura", abreviatura ?? "");
            AgregarParametro(cmd, "@productoId", productoId);
            AgregarParametro(cmd, "@categoriaId", categoriaId);

            object result = await ((DbCommand)cmd).ExecuteScalarAsync();
            return (result != DBNull.Value && result != null) ? Convert.ToInt32(result) : 0;
        }

        public async Task GuardarCodigosTransactionAsync(int coleccionId, int productoId, int cantidad, string desde, string hasta, int categoriaId)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                // 1. Obtener registroId (usando el código que ya corregimos antes)
                string queryRegistro = "INSERT INTO registro_codigos (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id) VALUES (@cId, @pId, @cant, @des, @has, @catId);";
                string selectId = QueryAdapter.EsMySQL ? " SELECT LAST_INSERT_ID();" : " SELECT SCOPE_IDENTITY();";

                int registroId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRegistro + selectId);
                    AgregarParametro(cmd, "@cId", coleccionId);
                    AgregarParametro(cmd, "@pId", productoId);
                    AgregarParametro(cmd, "@cant", cantidad);
                    AgregarParametro(cmd, "@des", desde);
                    AgregarParametro(cmd, "@has", hasta);
                    AgregarParametro(cmd, "@catId", categoriaId);
                    registroId = Convert.ToInt32(await ((DbCommand)cmd).ExecuteScalarAsync());
                }

                // 2. INSERCIÓN MASIVA (Optimizado)
                int desdeInt = int.Parse(desde.Substring(desde.LastIndexOf('-') + 1));
                string prefijo = desde.Substring(0, desde.LastIndexOf('-') + 1);

                // Construimos un solo comando masivo: INSERT INTO ... VALUES (...), (...), (...)
                var sb = new System.Text.StringBuilder("INSERT INTO codigos_creados (registro_codigo_id, codigo) VALUES ");
                var inserts = new List<string>();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    for (int i = 0; i < cantidad; i++)
                    {
                        string paramName = $"@cod{i}";
                        inserts.Add($"({registroId}, {paramName})");
                        AgregarParametro(cmd, paramName, $"{prefijo}{(desdeInt + i):D7}");
                    }
                    cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString() + string.Join(",", inserts));
                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }

        public async Task EliminarRegistroTransactionAsync(int registroCodigoId)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM codigos_creados WHERE registro_codigo_id = @id");
                    AgregarParametro(cmd, "@id", registroCodigoId);
                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM registro_codigos WHERE id = @id");
                    AgregarParametro(cmd, "@id", registroCodigoId);
                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }
    }
}