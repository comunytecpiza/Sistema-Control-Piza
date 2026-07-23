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
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Presentation;

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

                // 🌟 CORRECCIÓN MAESTRA: Quitamos la restricción 'p.abreviatura IS NOT NULL'.
                // Ahora el combo listará de forma inteligente todos los libros secuenciales Y alfanuméricos puros.
                string query = @"SELECT p.id, p.descripcion, p.abreviatura, um.descripcion AS unidad_medida, p.tipo_producto_id
                                 FROM productos p 
                                 INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id 
                                 INNER JOIN tipo_producto tp ON p.tipo_producto_id = tp.id
                                 WHERE p.descripcion IS NOT NULL 
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
                                // Si es NULL en la BD, lo capturamos de manera segura como null
                                Abreviatura = reader.IsDBNull(2) ? null : reader.GetString(2),
                                UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida"] as string },
                                TipoProductoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
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

        public async Task<List<RegistroCodigo>> ObtenerRegistrosAsync(int coleccionId, int categoriaId, int? almacenIdFiltro = null)
        {
            var lista = new List<RegistroCodigo>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 SI HAY FILTRO DE ALMACÉN (Trabajador): Cuenta solo los códigos de su sede y oculta lotes con 0
                string sqlCantidad = almacenIdFiltro.HasValue
                    ? "(SELECT COUNT(cc.id) FROM codigos_creados cc WHERE cc.registro_codigo_id = rc.id AND cc.almacen_id = @almId)"
                    : "(SELECT COUNT(cc.id) FROM codigos_creados cc WHERE cc.registro_codigo_id = rc.id)";

                string query = $@"
            SELECT rc.id, 
                   {sqlCantidad} AS cantidad_real, 
                   rc.desde, rc.hasta, p.descripcion AS producto_desc, 
                   p.abreviatura, um.descripcion AS unidad_medida_desc, cp.nombre AS categoria_nombre,
                   rc.created_at
            FROM registro_codigos rc
            INNER JOIN productos p ON rc.producto_id = p.id
            INNER JOIN unidad_medida um ON p.unidad_medida_id = um.id
            INNER JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
            WHERE rc.coleccion_id = @coleccionId AND rc.categoria_producto_id = @categoriaId";

                if (almacenIdFiltro.HasValue)
                {
                    query += $" AND {sqlCantidad} > 0"; // 🛡️ Si Lima tiene 0 unidades, el lote no le aparece
                }

                query += " ORDER BY rc.created_at DESC, rc.id DESC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@coleccionId", coleccionId);
                    AgregarParametro(cmd, "@categoriaId", categoriaId);
                    if (almacenIdFiltro.HasValue)
                    {
                        AgregarParametro(cmd, "@almId", almacenIdFiltro.Value);
                    }

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new RegistroCodigo
                            {
                                Id = reader.GetInt32(0),
                                Cantidad = Convert.ToInt32(reader.GetValue(1)),
                                Desde = reader.IsDBNull(2) ? "" : reader["desde"] as string,
                                Hasta = reader.IsDBNull(3) ? "" : reader["hasta"] as string,
                                Producto = new Producto
                                {
                                    Descripcion = reader["producto_desc"] as string,
                                    Abreviatura = reader.IsDBNull(5) ? null : reader["abreviatura"] as string,
                                    UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida_desc"] as string }
                                },
                                CategoriaProducto = new CategoriaProducto { Nombre = reader["categoria_nombre"] as string },
                                CreatedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<int> ObtenerUltimoCodigoAsync(int productoId, string abreviatura, int categoriaId)
        {
            // 🌟 Si el libro no tiene abreviatura configurada, es un PackLibro alfanumérico puro. 
            // Retornamos 0 de inmediato de forma inteligente y profesional para evitar romper funciones de cadena en SQL.
            if (string.IsNullOrWhiteSpace(abreviatura)) return 0;

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
                    AgregarParametro(cmd, "@abreviatura", abreviatura);
                    AgregarParametro(cmd, "@productoId", productoId);
                    AgregarParametro(cmd, "@categoriaId", categoriaId);

                    object result = await cmd.ExecuteScalarAsync();
                    return (result != DBNull.Value && result != null) ? Convert.ToInt32(result) : 0;
                }
            }
        }

        public async Task<bool> CambiarCondicionCodigoAsync(int codigoCreadoId, int nuevaCondicionId, int usuarioId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
        UPDATE codigos_creados 
        SET condicion_id = @condicionId, 
            usuario_id = @usuarioId 
        WHERE id = @id";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@condicionId", nuevaCondicionId);
            AgregarParametro(cmd, "@usuarioId", usuarioId);
            AgregarParametro(cmd, "@id", codigoCreadoId);

            int filasAfectadas = await cmd.ExecuteNonQueryAsync();
            return filasAfectadas > 0;
        }
        public async Task GuardarCodigosTransactionAsync(
    int coleccionId,
    int productoId,
    int cantidad,
    string desde,
    string hasta,
    int categoriaId,
    int usuarioId,
    int almacenId, // 🌟 Pasamos el almacén activo de la sesión
    string origenRegistro,
    IProgress<int> progress = null)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaction = dbConn.BeginTransaction())
                {
                    try
                    {
                        int lastDashIndex = desde.LastIndexOf('-');
                        int desdeInt = lastDashIndex >= 0 ? int.Parse(desde.Substring(lastDashIndex + 1)) : 0;
                        string prefijo = lastDashIndex >= 0 ? desde.Substring(0, lastDashIndex + 1) : desde;

                        // Abreviatura base limpia para la tabla registro_rangos (ej: 'LMA4-C26-V')
                        string abreviaturaBase = lastDashIndex >= 0 ? desde.Substring(0, lastDashIndex) : desde;
                        int hastaInt = desdeInt + cantidad - 1;

                        // 1. Inserción del lote principal (registro_codigos)
                        string queryRegistro = @"
                    INSERT INTO registro_codigos 
                    (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id, usuario_id, origen_registro, created_at) 
                    VALUES 
                    (@cId, @pId, @cant, @des, @has, @catId, @uId, @origen, GETDATE());";

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
                            AgregarParametro(cmd, "@uId", usuarioId > 0 ? usuarioId : 1);
                            AgregarParametro(cmd, "@origen", string.IsNullOrWhiteSpace(origenRegistro) ? "SECUENCIAL" : origenRegistro);

                            registroId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        // 🌟 2. INSERCIÓN OBLIGATORIA EN 'registro_rangos' PARA EL MÓDULO DE MOVIMIENTOS
                        // 🌟 2. INSERCIÓN EN 'registro_rangos' (Sin enviar 'cantidad' porque es columna calculada)
                        string queryRango = @"
                        INSERT INTO registro_rangos 
                        (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at, usuario_id) 
                        VALUES 
                        (@pId, @catId, @abrev, @dNum, @hNum, NULL, GETDATE(), @uId);";

                        using (var cmdRango = dbConn.CreateCommand())
                        {
                            cmdRango.Transaction = transaction;
                            cmdRango.CommandText = QueryAdapter.FormatearConsulta(queryRango);

                            AgregarParametro(cmdRango, "@pId", productoId);
                            AgregarParametro(cmdRango, "@catId", categoriaId);
                            AgregarParametro(cmdRango, "@abrev", abreviaturaBase);
                            AgregarParametro(cmdRango, "@dNum", desdeInt);
                            AgregarParametro(cmdRango, "@hNum", hastaInt);
                            AgregarParametro(cmdRango, "@uId", usuarioId > 0 ? usuarioId : 1);

                            await cmdRango.ExecuteNonQueryAsync();
                        }

                        // 3. Inserción en bloque con AUDITORÍA COMPLETA (codigos_creados)
                        int batchSize = 1000;
                        for (int i = 0; i < cantidad; i += batchSize)
                        {
                            int currentBatch = Math.Min(batchSize, cantidad - i);
                            var queryBuilder = new StringBuilder(@"
                        INSERT INTO codigos_creados 
                        (registro_codigo_id, codigo, estado_id, condicion_id, almacen_id, usuario_id, origen_creacion, created_at) 
                        VALUES ");

                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = transaction;

                                for (int j = 0; j < currentBatch; j++)
                                {
                                    int idx = i + j;
                                    string paramCod = $"@cod{idx}";

                                    string codigoGenerado = lastDashIndex >= 0 ? $"{prefijo}{(desdeInt + idx):D7}" : $"{prefijo}-{idx}";
                                    codigoGenerado = codigoGenerado.Replace("'", "-");

                                    // estado_id = 1 (En stock/creado), condicion_id = 1 (OK / Operativo)
                                    queryBuilder.Append($"({registroId}, {paramCod}, 1, 1, @almId, @usrId, @origenC, GETDATE())");
                                    if (j < currentBatch - 1) queryBuilder.Append(", ");

                                    AgregarParametro(cmd, paramCod, codigoGenerado);
                                }

                                AgregarParametro(cmd, "@almId", almacenId);
                                AgregarParametro(cmd, "@usrId", usuarioId > 0 ? usuarioId : 1);
                                AgregarParametro(cmd, "@origenC", string.IsNullOrWhiteSpace(origenRegistro) ? "SECUENCIAL" : origenRegistro);

                                cmd.CommandText = QueryAdapter.FormatearConsulta(queryBuilder.ToString());
                                await cmd.ExecuteNonQueryAsync();
                            }

                            int pct = ((i + currentBatch) * 100) / cantidad;
                            progress?.Report(pct);
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
        public static class CodigoUtils
        {
            public static string NormalizarParaBusqueda(string codigo)
            {
                if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;
                // Reemplazamos TODO lo que no sea número o letra por NADA
                // Esto convierte 'LMA4 C26'V0019' en "LMA4C26V0019"
                return Regex.Replace(codigo.ToUpperInvariant(), @"[^A-Z0-9]", "");
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

        public async Task<int> ObtenerUltimoCodigoSecuencialAsync(int productoId, string abreviatura, int categoriaId)
        {
            if (string.IsNullOrWhiteSpace(abreviatura)) return 0;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    SELECT ISNULL(MAX(CAST(RIGHT(cc.codigo,7) AS INT)),0)
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id=rc.id
                    WHERE rc.producto_id=@producto
                      AND rc.categoria_producto_id=@categoria
                      AND cc.codigo LIKE @prefijo
                      AND ISNUMERIC(RIGHT(cc.codigo,7))=1";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@producto", productoId);
                    AgregarParametro(cmd, "@categoria", categoriaId);
                    AgregarParametro(cmd, "@prefijo", abreviatura + "-%");

                    object result = await cmd.ExecuteScalarAsync();
                    return result == DBNull.Value ? 0 : Convert.ToInt32(result);
                }
            }
        }
    }
}