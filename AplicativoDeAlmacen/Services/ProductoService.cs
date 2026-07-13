using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common; // Usamos Common para soporte Multi-Motor, adiós a SqlClient
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class ProductoService
    {
        private readonly DatabaseConnection _database;

        public ProductoService()
        {
            _database = new DatabaseConnection();
        }

        // =======================================================
        // FUNCIÓN AYUDANTE MULTI-MOTOR
        // =======================================================
        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<Producto>> ObtenerTodosAsync()
        {
            var lista = new List<Producto>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    SELECT p.id, p.descripcion, p.abreviatura, p.unidad_medida_id,
                           um.descripcion AS unidad_medida,
                           p.tipo_producto_id, p.precio_unitario, p.porcentaje,
                           p.nivel_id, p.grado_id, p.curso_id,
                           p.titulo_curso_id, p.afectacion_igv_id,
                           ai.nombre AS afectacion_igv,
                           p.estado_id, e.nombre AS estado,
                           p.stock_minimo
                    FROM productos p
                    LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                    LEFT JOIN afectacion_igv ai ON p.afectacion_igv_id = ai.id
                    LEFT JOIN estados e ON p.estado_id = e.id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Producto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Abreviatura = reader.IsDBNull(reader.GetOrdinal("abreviatura")) ? null : reader.GetString(reader.GetOrdinal("abreviatura")),
                                UnidadMedidaId = reader.IsDBNull(reader.GetOrdinal("unidad_medida_id")) ? null : reader.GetInt32(reader.GetOrdinal("unidad_medida_id")),
                                UnidadMedida = new UnidadMedida { Descripcion = reader.IsDBNull(reader.GetOrdinal("unidad_medida")) ? string.Empty : reader.GetString(reader.GetOrdinal("unidad_medida")) },
                                TipoProductoId = reader.IsDBNull(reader.GetOrdinal("tipo_producto_id")) ? null : reader.GetInt32(reader.GetOrdinal("tipo_producto_id")),
                                PrecioUnitario = reader.IsDBNull(reader.GetOrdinal("precio_unitario")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("precio_unitario")),
                                Porcentaje = reader.IsDBNull(reader.GetOrdinal("porcentaje")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("porcentaje")),
                                NivelId = reader.IsDBNull(reader.GetOrdinal("nivel_id")) ? null : reader.GetInt32(reader.GetOrdinal("nivel_id")),
                                GradoId = reader.IsDBNull(reader.GetOrdinal("grado_id")) ? null : reader.GetInt32(reader.GetOrdinal("grado_id")),
                                CursoId = reader.IsDBNull(reader.GetOrdinal("curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("curso_id")),
                                TituloCursoId = reader.IsDBNull(reader.GetOrdinal("titulo_curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("titulo_curso_id")),
                                AfectacionIgvId = reader.IsDBNull(reader.GetOrdinal("afectacion_igv_id")) ? null : reader.GetInt32(reader.GetOrdinal("afectacion_igv_id")),
                                afectacion = new AfectacionIgv { Nombre = reader.IsDBNull(reader.GetOrdinal("afectacion_igv")) ? string.Empty : reader.GetString(reader.GetOrdinal("afectacion_igv")) },
                                EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : reader.GetInt32(reader.GetOrdinal("estado_id")),
                                Estado = new Estado { Nombre = reader.IsDBNull(reader.GetOrdinal("estado")) ? string.Empty : reader.GetString(reader.GetOrdinal("estado")) },
                                StockMinimo = reader.IsDBNull(reader.GetOrdinal("stock_minimo")) ? 0 : reader.GetInt32(reader.GetOrdinal("stock_minimo")) 
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task InsertarAsync(Producto p)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"INSERT INTO productos
                        (
                            descripcion, abreviatura, unidad_medida_id, tipo_producto_id,
                            precio_unitario, porcentaje, nivel_id, grado_id, curso_id,
                            titulo_curso_id, afectacion_igv_id, estado_id, stock_minimo,
                            created_at, updated_at
                        )
                        VALUES
                        (
                            @Descripcion, @Abreviatura, @UnidadMedidaId, @TipoProductoId,
                            @PrecioUnitario, @Porcentaje, @NivelId, @GradoId, @CursoId,
                            @TituloCursoId, @AfectacionIgvId, @EstadoId, @StockMinimo,
                            GETDATE(), GETDATE()
                        )";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    MapearParametros(cmd, p);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task ActualizarAsync(Producto p)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"UPDATE productos SET
                         descripcion = @Descripcion, abreviatura = @Abreviatura, unidad_medida_id = @UnidadMedidaId,
                         tipo_producto_id = @TipoProductoId, precio_unitario = @PrecioUnitario, porcentaje = @Porcentaje,
                         nivel_id = @NivelId, grado_id = @GradoId, curso_id = @CursoId,
                         titulo_curso_id = @TituloCursoId, afectacion_igv_id = @AfectacionIgvId, estado_id = @EstadoId,
                         stock_minimo = @StockMinimo,
                         updated_at = GETDATE()
                         WHERE id = @Id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@Id", p.Id);
                    MapearParametros(cmd, p);
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

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM productos WHERE id = @Id");
                    AgregarParametro(cmd, "@Id", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private void MapearParametros(DbCommand cmd, Producto p)
        {
            AgregarParametro(cmd, "@Descripcion", p.Descripcion);
            AgregarParametro(cmd, "@Abreviatura", string.IsNullOrEmpty(p.Abreviatura) ? DBNull.Value : p.Abreviatura);
            AgregarParametro(cmd, "@UnidadMedidaId", p.UnidadMedidaId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@TipoProductoId", p.TipoProductoId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@PrecioUnitario", p.PrecioUnitario);
            AgregarParametro(cmd, "@Porcentaje", p.Porcentaje);
            AgregarParametro(cmd, "@NivelId", p.NivelId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@GradoId", p.GradoId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@CursoId", p.CursoId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@TituloCursoId", p.TituloCursoId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@AfectacionIgvId", p.AfectacionIgvId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@EstadoId", p.EstadoId ?? (object)DBNull.Value);
            AgregarParametro(cmd, "@StockMinimo", p.StockMinimo); 
        }

        // =======================================================
        // CATÁLOGOS AUXILIARES
        // =======================================================
        public async Task<List<UnidadMedida>> ObtenerUnidadesMedidaAsync()
        {
            var lista = new List<UnidadMedida>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion FROM unidad_medida ORDER BY descripcion");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new UnidadMedida { Id = reader.GetInt32(0), Descripcion = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<TipoProducto>> ObtenerTiposProductoAsync()
        {
            var lista = new List<TipoProducto>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM tipo_producto ORDER BY nombre");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new TipoProducto { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Nivel>> ObtenerNivelesAsync()
        {
            var lista = new List<Nivel>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM niveles ORDER BY nombre");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Nivel { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Grado>> ObtenerGradosAsync(int nivelId)
        {
            var lista = new List<Grado>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM grados WHERE nivel_id = @NivelId ORDER BY nombre");
                    AgregarParametro(cmd, "@NivelId", nivelId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Grado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Curso>> ObtenerCursosAsync(int nivelId)
        {
            var lista = new List<Curso>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM curso WHERE nivel_id = @NivelId ORDER BY nombre");
                    AgregarParametro(cmd, "@NivelId", nivelId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Curso { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<TituloCurso>> ObtenerTitulosAsync()
        {
            var lista = new List<TituloCurso>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM titulo_curso ORDER BY nombre");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new TituloCurso { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<AfectacionIgv>> ObtenerAfectacionesIgvAsync()
        {
            var lista = new List<AfectacionIgv>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM afectacion_igv ORDER BY nombre");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new AfectacionIgv { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM estados ORDER BY nombre");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Estado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

                public async Task<List<ProductoStock>> ObtenerStockCriticoAsync()
        {
            var lista = new List<ProductoStock>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 MAGIA: Filtramos donde el StockActual sea <= al stock_minimo del producto
                string query = @"
                SELECT Id, Descripcion, StockActual, StockMinimo, TipoProductoId, GradoNombre
                FROM (
                    SELECT p.id AS Id,
                           p.descripcion AS Descripcion, 
                           (ISNULL(ing.total, 0) - ISNULL(sal.total, 0)) AS StockActual,
                           p.stock_minimo AS StockMinimo,
                           p.tipo_producto_id AS TipoProductoId,
                           ISNULL(g.nombre, 'Sin Grado') AS GradoNombre
                    FROM productos p
                    LEFT JOIN (SELECT producto_id, SUM(cantidad_ingreso) as total FROM movimiento_detalles GROUP BY producto_id) ing ON p.id = ing.producto_id
                    LEFT JOIN (SELECT producto_id, SUM(cantidad_salida) as total FROM movimiento_detalles GROUP BY producto_id) sal ON p.id = sal.producto_id
                    LEFT JOIN grados g ON p.grado_id = g.id
                    WHERE p.estado_id = 1
                ) AS Resultado
                WHERE StockActual <= StockMinimo AND StockMinimo > 0
                ORDER BY (StockActual - StockMinimo) ASC, Descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new ProductoStock
                            {
                                Id = reader.IsDBNull(reader.GetOrdinal("Id")) ? 0 : reader.GetInt32(reader.GetOrdinal("Id")),
                                Descripcion = reader.IsDBNull(reader.GetOrdinal("Descripcion")) ? "Sin Descripción" : reader.GetString(reader.GetOrdinal("Descripcion")),
                                StockActual = reader.IsDBNull(reader.GetOrdinal("StockActual")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("StockActual"))),
                                StockMinimo = reader.IsDBNull(reader.GetOrdinal("StockMinimo")) ? 0 : reader.GetInt32(reader.GetOrdinal("StockMinimo")),
                                TipoProductoId = reader.IsDBNull(reader.GetOrdinal("TipoProductoId")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("TipoProductoId"))),
                                GradoNombre = reader.IsDBNull(reader.GetOrdinal("GradoNombre")) ? "Sin Grado" : reader.GetString(reader.GetOrdinal("GradoNombre"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // =======================================================
        // METODO CONFLICTIVO ARREGLADO (BuscarProductosPorTexto)
        // =======================================================
        // Le agregamos 'Async' al nombre por convención de buenas prácticas
        public async Task<List<Producto>> BuscarProductosPorTextoAsync(string texto)
        {
            List<Producto> resultados = new List<Producto>();

            // 🌟 CORRECCIÓN: Agregados LEFT JOINs para unidad_medida y afectacion_igv e incluimos campos faltantes
            string query = @"
                SELECT 
                    p.id, 
                    p.descripcion, 
                    p.abreviatura, 
                    p.unidad_medida_id, 
                    um.descripcion AS unidad_medida,
                    p.precio_unitario,
                    p.porcentaje,
                    p.afectacion_igv_id,
                    ai.nombre AS afectacion_igv,
                    p.cantidad, 
                    ISNULL((SELECT SUM(rc.cantidad) FROM registro_codigos rc WHERE rc.producto_id = p.id), 0) AS cantidad_codigos
                FROM productos p
                LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                LEFT JOIN afectacion_igv ai ON p.afectacion_igv_id = ai.id
                WHERE (p.descripcion LIKE @Texto OR p.abreviatura LIKE @Texto)
                  AND p.estado_id = 1";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;

                try
                {
                    if (dbConn.State == System.Data.ConnectionState.Closed)
                        await dbConn.OpenAsync();

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        AgregarParametro(cmd, "@Texto", "%" + texto + "%");

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                Producto prod = new Producto();

                                prod.Id = Convert.ToInt32(reader["id"]);
                                prod.Descripcion = reader["descripcion"].ToString();
                                prod.Abreviatura = reader["abreviatura"] != DBNull.Value ? reader["abreviatura"].ToString() : "";
                                prod.UnidadMedidaId = reader["unidad_medida_id"] != DBNull.Value ? Convert.ToInt32(reader["unidad_medida_id"]) : (int?)null;
                                prod.UnidadMedida = new UnidadMedida { Descripcion = reader["unidad_medida"] != DBNull.Value ? reader["unidad_medida"].ToString() : string.Empty };
                                prod.PrecioUnitario = reader["precio_unitario"] != DBNull.Value ? Convert.ToDecimal(reader["precio_unitario"]) : (decimal?)null;
                                prod.Porcentaje = reader["porcentaje"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje"]) : 0.00m;
                                prod.AfectacionIgvId = reader["afectacion_igv_id"] != DBNull.Value ? Convert.ToInt32(reader["afectacion_igv_id"]) : (int?)null;
                                prod.afectacion = new AfectacionIgv { Nombre = reader["afectacion_igv"] != DBNull.Value ? reader["afectacion_igv"].ToString() : string.Empty };
                                prod.Cantidad = reader["cantidad"] != DBNull.Value ? Convert.ToInt32(reader["cantidad"]) : 0;
                                prod.CantidadCodigos = reader["cantidad_codigos"] != DBNull.Value ? Convert.ToInt32(reader["cantidad_codigos"]) : 0;

                                resultados.Add(prod);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al consultar productos: " + ex.Message);
                }
            }

            return resultados;
        }

        public async Task<Producto> ObtenerPorIdAsync(int id)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                SELECT p.id, p.descripcion, p.abreviatura, p.unidad_medida_id,
                       um.descripcion AS unidad_medida,
                       p.tipo_producto_id, p.precio_unitario, p.porcentaje,
                       p.nivel_id, p.grado_id, p.curso_id,
                       p.titulo_curso_id, p.afectacion_igv_id,
                       ai.nombre AS afectacion_igv,
                       p.estado_id, e.nombre AS estado,
                       p.stock_minimo
                FROM productos p
                LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                LEFT JOIN afectacion_igv ai ON p.afectacion_igv_id = ai.id
                LEFT JOIN estados e ON p.estado_id = e.id
                WHERE p.id = @Id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@Id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Producto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Abreviatura = reader.IsDBNull(reader.GetOrdinal("abreviatura")) ? null : reader.GetString(reader.GetOrdinal("abreviatura")),
                                UnidadMedidaId = reader.IsDBNull(reader.GetOrdinal("unidad_medida_id")) ? null : reader.GetInt32(reader.GetOrdinal("unidad_medida_id")),
                                UnidadMedida = new UnidadMedida { Descripcion = reader.IsDBNull(reader.GetOrdinal("unidad_medida")) ? string.Empty : reader.GetString(reader.GetOrdinal("unidad_medida")) },
                                TipoProductoId = reader.IsDBNull(reader.GetOrdinal("tipo_producto_id")) ? null : reader.GetInt32(reader.GetOrdinal("tipo_producto_id")),
                                PrecioUnitario = reader.IsDBNull(reader.GetOrdinal("precio_unitario")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("precio_unitario")),
                                Porcentaje = reader.IsDBNull(reader.GetOrdinal("porcentaje")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("porcentaje")),
                                NivelId = reader.IsDBNull(reader.GetOrdinal("nivel_id")) ? null : reader.GetInt32(reader.GetOrdinal("nivel_id")),
                                GradoId = reader.IsDBNull(reader.GetOrdinal("grado_id")) ? null : reader.GetInt32(reader.GetOrdinal("grado_id")),
                                CursoId = reader.IsDBNull(reader.GetOrdinal("curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("curso_id")),
                                TituloCursoId = reader.IsDBNull(reader.GetOrdinal("titulo_curso_id")) ? null : reader.GetInt32(reader.GetOrdinal("titulo_curso_id")),
                                AfectacionIgvId = reader.IsDBNull(reader.GetOrdinal("afectacion_igv_id")) ? null : reader.GetInt32(reader.GetOrdinal("afectacion_igv_id")),
                                afectacion = new AfectacionIgv { Nombre = reader.IsDBNull(reader.GetOrdinal("afectacion_igv")) ? string.Empty : reader.GetString(reader.GetOrdinal("afectacion_igv")) },
                                EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : reader.GetInt32(reader.GetOrdinal("estado_id")),
                                Estado = new Estado { Nombre = reader.IsDBNull(reader.GetOrdinal("estado")) ? string.Empty : reader.GetString(reader.GetOrdinal("estado")) },
                                StockMinimo = reader.IsDBNull(reader.GetOrdinal("stock_minimo")) ? 0 : reader.GetInt32(reader.GetOrdinal("stock_minimo"))
                            };
                        }
                    }
                }
            }

            return null; 
        }

    }
}