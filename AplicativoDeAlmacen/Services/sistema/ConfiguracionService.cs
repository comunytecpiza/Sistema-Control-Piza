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

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

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

                        // 2. Obtenemos el ID que se acaba de crear (Compatible Multi-Motor)
                        int nuevoModuloId = 0;
                        using (var cmdId = dbConn.CreateCommand())
                        {
                            cmdId.Transaction = trans;
                            cmdId.CommandText = "SELECT id FROM modulos_sistema WHERE codigo_modulo = @Codigo";
                            AgregarParametro(cmdId, "@Codigo", mod.CodigoModulo);
                            nuevoModuloId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());
                        }

                        // 3. ¡LA MAGIA AUTOMÁTICA! Le damos acceso a todos los roles activos por defecto
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


        public async Task<int> ObtenerSiguienteOrdenPorCategoriaAsync(int categoriaId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    // COALESCE convierte el NULL en 0 si la categoría está vacía, y le suma 1.
                    cmd.CommandText = "SELECT COALESCE(MAX(orden), 0) + 1 FROM modulos_sistema WHERE categoria_id = @CatId";
                    AgregarParametro(cmd, "@CatId", categoriaId);

                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }

        // Trae los módulos que existen pero tienen control_wpf en NULL o vacío
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

        // Hace el UPDATE al módulo existente
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
    }
}