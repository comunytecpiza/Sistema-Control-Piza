using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Services
{
    public class ConfiguracionService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public ConfiguracionService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        #region MÉTODOS AUXILIARES (HELPERS)

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        #endregion

        #region GESTIÓN DE CATEGORÍAS

        public async Task<List<CategoriaModulo>> ObtenerCategoriasActivasAsync()
        {
            var lista = new List<CategoriaModulo>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, nombre FROM categorias_modulos WHERE estado = 1 ORDER BY orden ASC";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new CategoriaModulo
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<CategoriaModulo>> ObtenerCategoriasAsync()
        {
            var lista = new List<CategoriaModulo>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, nombre, icono, color, orden, estado FROM categorias_modulos ORDER BY orden ASC";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new CategoriaModulo
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                                Icono = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Color = reader.IsDBNull(3) ? "#FFFFFF" : reader.GetString(3),
                                Orden = reader.GetInt32(4),
                                Estado = Convert.ToBoolean(reader["estado"]),
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task GuardarCategoriaAsync(CategoriaModulo cat, bool esNuevo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    if (esNuevo)
                        cmd.CommandText = "INSERT INTO categorias_modulos (nombre, icono, color, orden, estado) VALUES (@N, @I, @C, @O, @E)";
                    else
                        cmd.CommandText = "UPDATE categorias_modulos SET nombre=@N, icono=@I, color=@C, orden=@O, estado=@E WHERE id=@Id";

                    AgregarParametro(cmd, "@N", cat.Nombre);
                    AgregarParametro(cmd, "@I", cat.Icono);
                    AgregarParametro(cmd, "@C", cat.Color);
                    AgregarParametro(cmd, "@O", cat.Orden);
                    AgregarParametro(cmd, "@E", cat.Estado ? 1 : 0);
                    if (!esNuevo) AgregarParametro(cmd, "@Id", cat.Id);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task CambiarEstadoCategoriaAsync(int id, bool estado)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE categorias_modulos SET estado=@Estado WHERE id=@Id";
                    AgregarParametro(cmd, "@Estado", estado ? 1 : 0);
                    AgregarParametro(cmd, "@Id", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        #endregion

        #region GESTIÓN DE MÓDULOS Y VISTAS

        public async Task<List<ModuloSistema>> ObtenerModulosCompletosAsync()
        {
            var lista = new List<ModuloSistema>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT m.id, m.nombre_modulo, m.codigo_modulo, m.orden, m.control_wpf, m.estado, c.nombre 
                        FROM modulos_sistema m
                        LEFT JOIN categorias_modulos c ON m.categoria_id = c.id
                        ORDER BY m.orden ASC";

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new ModuloSistema
                            {
                                Id = reader.GetInt32(0),
                                NombreModulo = reader.GetString(1),
                                CodigoModulo = reader.GetString(2),
                                Orden = reader.GetInt32(3),
                                ControlWpf = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Estado = reader.IsDBNull(5) ? false : Convert.ToBoolean(reader["estado"]),
                                NombreCategoria = reader.IsDBNull(6) ? "Sin Categoría" : reader.GetString(6)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<ModuloSistema>> ObtenerModulosSinVistaAsync()
        {
            var lista = new List<ModuloSistema>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, nombre_modulo, codigo_modulo FROM modulos_sistema WHERE control_wpf IS NULL OR control_wpf = '' ORDER BY nombre_modulo ASC";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new ModuloSistema
                            {
                                Id = reader.GetInt32(0),
                                NombreModulo = reader.GetString(1),
                                CodigoModulo = reader.GetString(2)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task RegistrarNuevoModuloAsync(ModuloSistema mod)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var trans = dbConn.BeginTransaction())
                {
                    try
                    {
                        // 1. Insertamos el Módulo
                        using (var cmdInsert = dbConn.CreateCommand())
                        {
                            cmdInsert.Transaction = trans;
                            cmdInsert.CommandText = @"
                                INSERT INTO modulos_sistema (codigo_modulo, nombre_modulo, categoria_id, orden, control_wpf, estado) 
                                VALUES (@Codigo, @Nombre, @CatId, @Orden, @ControlWpf, 1)";

                            AgregarParametro(cmdInsert, "@Codigo", mod.CodigoModulo);
                            AgregarParametro(cmdInsert, "@Nombre", mod.NombreModulo);
                            AgregarParametro(cmdInsert, "@CatId", mod.CategoriaId);
                            AgregarParametro(cmdInsert, "@Orden", mod.Orden);
                            AgregarParametro(cmdInsert, "@ControlWpf", mod.ControlWpf);
                            await cmdInsert.ExecuteNonQueryAsync();
                        }

                        // 2. Obtenemos el ID que se acaba de crear
                        int nuevoModuloId = 0;
                        using (var cmdId = dbConn.CreateCommand())
                        {
                            cmdId.Transaction = trans;
                            cmdId.CommandText = "SELECT id FROM modulos_sistema WHERE codigo_modulo = @Codigo";
                            AgregarParametro(cmdId, "@Codigo", mod.CodigoModulo);
                            nuevoModuloId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());
                        }

                        // 3. Asignación automática de permisos a todos los roles activos
                        using (var cmdPermisos = dbConn.CreateCommand())
                        {
                            cmdPermisos.Transaction = trans;
                            cmdPermisos.CommandText = @"
                                INSERT INTO rol_permisos (rol_usuario_id, modulo_id, puede_ver, puede_crear, puede_editar, puede_eliminar, puede_imprimir)
                                SELECT id, @NuevoModId, 1, 1, 1, 1, 1 FROM roles_usuario WHERE estado = 1";

                            AgregarParametro(cmdPermisos, "@NuevoModId", nuevoModuloId);
                            await cmdPermisos.ExecuteNonQueryAsync();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task ActualizarModuloAsync(ModuloSistema mod)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE modulos_sistema SET nombre_modulo=@N, codigo_modulo=@C, categoria_id=@Cat, orden=@O WHERE id=@Id";
                    AgregarParametro(cmd, "@N", mod.NombreModulo);
                    AgregarParametro(cmd, "@C", mod.CodigoModulo);
                    AgregarParametro(cmd, "@Cat", mod.CategoriaId);
                    AgregarParametro(cmd, "@O", mod.Orden);
                    AgregarParametro(cmd, "@Id", mod.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task CambiarEstadoModuloAsync(int id, bool estado)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE modulos_sistema SET estado=@Estado WHERE id=@Id";
                    AgregarParametro(cmd, "@Estado", estado ? 1 : 0);
                    AgregarParametro(cmd, "@Id", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task VincularVistaAModuloAsync(int moduloId, string nombreVista)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE modulos_sistema SET control_wpf = @Vista WHERE id = @Id";
                    AgregarParametro(cmd, "@Vista", nombreVista);
                    AgregarParametro(cmd, "@Id", moduloId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<int> ObtenerSiguienteOrdenPorCategoriaAsync(int categoriaId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(orden), 0) + 1 FROM modulos_sistema WHERE categoria_id = @CatId";
                    AgregarParametro(cmd, "@CatId", categoriaId);

                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }

        #endregion
    }
}