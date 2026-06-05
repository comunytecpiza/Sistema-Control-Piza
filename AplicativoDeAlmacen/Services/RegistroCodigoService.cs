using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services
{
    public class RegistroCodigoService
    {
        public async Task<List<Coleccion>> ObtenerColeccionesAsync()
        {
            var lista = new List<Coleccion>();
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, ano FROM colecciones ORDER BY ano DESC", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Coleccion { Id = reader.GetInt32(0), Ano = reader.GetInt32(1) });
            }
            return lista;
        }

        // Devolvemos el modelo Producto puro
        public async Task<List<Producto>> ObtenerProductosComboAsync()
        {
            var lista = new List<Producto>();
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            string query = @"SELECT p.id, p.descripcion, p.abreviatura, um.descripcion AS unidad_medida 
                             FROM productos p
                             JOIN unidad_medida um ON p.unidad_medida_id = um.id
                             WHERE p.descripcion IS NOT NULL AND p.abreviatura IS NOT NULL";
            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var p = new Producto
                {
                    Id = reader.GetInt32(0),
                    Descripcion = reader["descripcion"] as string,
                    Abreviatura = reader["abreviatura"] as string
                };

                // Anidamos la unidad de medida (como se hizo en el CRUD de productos)
                p.UnidadMedida = new UnidadMedida // o UnidadMedia según tu clase
                {
                    Descripcion = reader["unidad_medida"] as string
                };

                lista.Add(p);
            }
            return lista;
        }

        public async Task<List<CategoriaProducto>> ObtenerCategoriasAsync()
        {
            var lista = new List<CategoriaProducto>();
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT id, nombre FROM categoria_producto", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new CategoriaProducto { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        // Aquí aplicamos la lógica de tu compañero
        public async Task<List<RegistroCodigo>> ObtenerRegistrosAsync(int coleccionId, int categoriaId)
        {
            var lista = new List<RegistroCodigo>();
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            string query = @"SELECT rc.id, rc.cantidad, rc.desde, rc.hasta, 
                                    p.descripcion AS producto_desc, p.abreviatura, 
                                    um.descripcion AS unidad_medida_desc,
                                    cp.nombre AS categoria_nombre
                             FROM registro_codigos rc
                             JOIN productos p ON rc.producto_id = p.id
                             JOIN unidad_medida um ON p.unidad_medida_id = um.id
                             JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
                             WHERE rc.coleccion_id = @coleccionId AND rc.categoria_producto_id = @categoriaId
                             ORDER BY rc.desde DESC";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@coleccionId", coleccionId);
            cmd.Parameters.AddWithValue("@categoriaId", categoriaId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var registro = new RegistroCodigo
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Cantidad = reader.GetInt32(reader.GetOrdinal("cantidad")),
                    Desde = reader["desde"] as string,
                    Hasta = reader["hasta"] as string
                };

                // Anidamos el Producto
                registro.Producto = new Producto
                {
                    Descripcion = reader["producto_desc"] as string,
                    Abreviatura = reader["abreviatura"] as string,
                    UnidadMedida = new UnidadMedida
                    {
                        Descripcion = reader["unidad_medida_desc"] as string
                    }
                };

                // Anidamos la Categoria
                registro.CategoriaProducto = new CategoriaProducto
                {
                    Nombre = reader["categoria_nombre"] as string
                };

                lista.Add(registro);
            }
            return lista;
        }

        public async Task<int> ObtenerUltimoCodigoAsync(int productoId, string abreviatura)
        {
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            string query = @"SELECT MAX(CAST(SUBSTRING(codigo, LEN(@abreviatura) + 2, LEN(codigo)) AS INT))
                             FROM codigos_creados cc
                             JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                             WHERE rc.producto_id = @productoId";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@abreviatura", abreviatura ?? "");
            cmd.Parameters.AddWithValue("@productoId", productoId);
            object result = await cmd.ExecuteScalarAsync();

            return result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        public async Task GuardarCodigosTransactionAsync(int coleccionId, int productoId, int cantidad, string desde, string hasta, int categoriaId)
        {
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                string queryRegistro = @"INSERT INTO registro_codigos (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id) 
                                         OUTPUT INSERTED.ID VALUES (@coleccionId, @productoId, @cantidad, @desde, @hasta, @categoriaId)";

                int registroId;
                using (var cmd = new SqlCommand(queryRegistro, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@coleccionId", coleccionId);
                    cmd.Parameters.AddWithValue("@productoId", productoId);
                    cmd.Parameters.AddWithValue("@cantidad", cantidad);
                    cmd.Parameters.AddWithValue("@desde", desde);
                    cmd.Parameters.AddWithValue("@hasta", hasta);
                    cmd.Parameters.AddWithValue("@categoriaId", categoriaId);
                    registroId = (int)await cmd.ExecuteScalarAsync();
                }

                string queryCodigos = "INSERT INTO codigos_creados (registro_codigo_id, codigo) VALUES (@registroId, @codigo)";
                using (var cmd = new SqlCommand(queryCodigos, conn, transaction))
                {
                    string desdeNumerico = desde.Substring(desde.LastIndexOf('-') + 1);
                    string hastaNumerico = hasta.Substring(hasta.LastIndexOf('-') + 1);
                    int desdeInt = int.Parse(desdeNumerico);
                    int hastaInt = int.Parse(hastaNumerico);
                    string prefijo = desde.Substring(0, desde.LastIndexOf('-') + 1);

                    for (int i = desdeInt; i <= hastaInt; i++)
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@registroId", registroId);
                        cmd.Parameters.AddWithValue("@codigo", $"{prefijo}{i:D7}");
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

        public async Task EliminarRegistroTransactionAsync(int registroCodigoId)
        {
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                using (var cmd = new SqlCommand("DELETE FROM codigos_creados WHERE registro_codigo_id = @id", conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", registroCodigoId);
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = new SqlCommand("DELETE FROM registro_codigos WHERE id = @id", conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", registroCodigoId);
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