using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common; // Aseguramos el uso de DbCommon para compatibilidad
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;


namespace AplicativoDeAlmacen.Services
{
    public class UsuarioService
    {
        private readonly DatabaseConnection _database;

        public UsuarioService()
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

        public async Task<List<Usuario>> ObtenerTodosAsync(string filtro = "")
        {
            var lista = new List<Usuario>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string queryRaw = @"
                    SELECT 
                        u.id, u.username, u.nombres, u.password, u.rol_usuario_id, u.estado,
                        r.nombre AS rol_nombre, r.descripcion AS rol_desc
                    FROM usuarios u
                    INNER JOIN roles_usuario r ON u.rol_usuario_id = r.id
                    WHERE (u.nombres LIKE @Filtro OR u.username LIKE @Filtro)
                    ORDER BY u.nombres ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@Filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var usuario = new Usuario
                            {
                                Id = reader.GetInt32(0),
                                Username = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Nombres = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Password = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                RolUsuarioId = reader.GetInt32(4),
                                Estado = reader.IsDBNull(5) ? false : reader.GetBoolean(5),
                                Rol = new RolesUsuario
                                {
                                    Id = reader.GetInt32(4),
                                    Nombre = reader.GetString(6),
                                    Descripcion = reader.IsDBNull(7) ? "" : reader.GetString(7)
                                }
                            };
                            lista.Add(usuario);
                        }
                    }
                }
            }
            return lista;
        }

        public async Task InsertarAsync(Usuario u)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string queryRaw = @"
                    INSERT INTO usuarios (username, nombres, password, rol_usuario_id, estado, created_at, updated_at)
                    VALUES (@Username, @Nombres, @Password, @Rol, @Estado, GETDATE(), GETDATE())";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    string codigoAleatorio = "USR" + new Random().Next(10, 99);

                    AgregarParametro(cmd, "@Username", codigoAleatorio);
                    AgregarParametro(cmd, "@Nombres", u.Nombres);
                    AgregarParametro(cmd, "@Password", u.Password);
                    AgregarParametro(cmd, "@Rol", u.RolUsuarioId);
                    AgregarParametro(cmd, "@Estado", u.Estado ? 1 : 0);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task ActualizarAsync(Usuario u)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string queryRaw = @"
                    UPDATE usuarios SET 
                        nombres = @Nombres, 
                        password = @Password, 
                        rol_usuario_id = @Rol, 
                        estado = @Estado,
                        updated_at = GETDATE()
                    WHERE id = @Id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@Id", u.Id);
                    AgregarParametro(cmd, "@Nombres", u.Nombres);
                    AgregarParametro(cmd, "@Password", u.Password);
                    AgregarParametro(cmd, "@Rol", u.RolUsuarioId);
                    AgregarParametro(cmd, "@Estado", u.Estado ? 1 : 0);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<List<RolesUsuario>> ObtenerRolesActivosAsync()
        {
            var lista = new List<RolesUsuario>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = "SELECT id, nombre, descripcion FROM roles_usuario WHERE estado = 1 ORDER BY nombre ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new RolesUsuario
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                                Descripcion = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<RolPermiso>> ObtenerPermisosPorRolAsync(int rolUsuarioId)
        {
            var lista = new List<RolPermiso>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;

                await dbConn.OpenAsync();

                string queryRaw = @"

                    SELECT

                    m.id,
                    m.codigo_modulo,
                    m.nombre_modulo,

                    c.id,
                    c.nombre,
                    c.icono,
                    c.orden,
                    c.color,
                    c.estado,

                    m.orden,
                    m.control_wpf,
                    m.estado,

                    COALESCE(p.id,0),

                    COALESCE(p.puede_ver,1),
                    COALESCE(p.puede_crear,1),
                    COALESCE(p.puede_editar,1),
                    COALESCE(p.puede_eliminar,1),
                    COALESCE(p.puede_imprimir,1)

                    FROM modulos_sistema m

                    INNER JOIN categorias_modulos c
                    ON c.id=m.categoria_id

                    LEFT JOIN rol_permisos p
                    ON p.modulo_id=m.id
                    AND p.rol_usuario_id=@RolId

                    ORDER BY c.orden,m.orden";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText =
                        QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(
                        cmd,
                        "@RolId",
                        rolUsuarioId);

                    using (var reader =
                           await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new RolPermiso
                            {
                                ModuloId =
                                    reader.GetInt32(0),

                                CodigoModulo =
                                    reader.GetString(1),

                                NombreModulo =
                                    reader.GetString(2),

                                CategoriaId =
                                    reader.GetInt32(3),

                                CategoriaNombre =
                                    reader.GetString(4),

                                IconoCategoria =
                                    reader.IsDBNull(5)
                                    ? ""
                                    : reader.GetString(5),

                                OrdenCategoria =
                                    reader.IsDBNull(6)
                                    ? 99
                                    : reader.GetInt32(6),

                                ColorCategoria =
                                    reader.IsDBNull(7)
                                    ? "#2563EB"
                                    : reader.GetString(7),

                                EstadoCategoria =
                                    Convert.ToBoolean(reader[8]),

                                Orden =
                                    reader.IsDBNull(9)
                                    ? 99
                                    : reader.GetInt32(9),

                                ControlWpf =
                                    reader.IsDBNull(10)
                                    ? ""
                                    : reader.GetString(10),

                                EstadoModulo =
                                    Convert.ToBoolean(reader[11]),

                                Id =
                                    Convert.ToInt32(reader[12]),

                                RolUsuarioId =
                                    rolUsuarioId,

                                PuedeVer =
                                    Convert.ToBoolean(reader[13]),

                                PuedeCrear =
                                    Convert.ToBoolean(reader[14]),

                                PuedeEditar =
                                    Convert.ToBoolean(reader[15]),

                                PuedeEliminar =
                                    Convert.ToBoolean(reader[16]),

                                PuedeImprimir =
                                    Convert.ToBoolean(reader[17])

                            });
                        }
                    }
                }
            }

            return lista;
        }

        public async Task GuardarPermisosMasivosAsync(int rolUsuarioId, List<RolPermiso> matrizPermisos)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var trans = dbConn.BeginTransaction())
                {
                    try
                    {
                        foreach (var permiso in matrizPermisos)
                        {
                            bool existe = false;

                            using (var cmdCheck = dbConn.CreateCommand())
                            {
                                cmdCheck.Transaction = trans;
                                cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT COUNT(*) FROM rol_permisos WHERE rol_usuario_id = @RolId AND modulo_id = @ModId");
                                AgregarParametro(cmdCheck, "@RolId", rolUsuarioId);
                                AgregarParametro(cmdCheck, "@ModId", permiso.ModuloId);
                                existe = Convert.ToInt32(await cmdCheck.ExecuteScalarAsync()) > 0;
                            }

                            using (var cmdAction = dbConn.CreateCommand())
                            {
                                cmdAction.Transaction = trans;
                                if (existe)
                                {
                                    cmdAction.CommandText = QueryAdapter.FormatearConsulta(@"
                                        UPDATE rol_permisos SET 
                                            puede_ver = @Ver, puede_crear = @Crear, puede_editar = @Editar, 
                                            puede_eliminar = @Eliminar, puede_imprimir = @Imprimir
                                        WHERE rol_usuario_id = @RolId AND modulo_id = @ModId");
                                }
                                else
                                {
                                    cmdAction.CommandText = QueryAdapter.FormatearConsulta(@"
                                        INSERT INTO rol_permisos (rol_usuario_id, modulo_id, puede_ver, puede_crear, puede_editar, puede_eliminar, puede_imprimir)
                                        VALUES (@RolId, @ModId, @Ver, @Crear, @Editar, @Eliminar, @Imprimir)");
                                }

                                AgregarParametro(cmdAction, "@RolId", rolUsuarioId);
                                AgregarParametro(cmdAction, "@ModId", permiso.ModuloId);

                                // BUG CORREGIDO: Aquí estabas agregando "@Ver" dos veces. Lo borré.
                                AgregarParametro(cmdAction, "@Ver", permiso.PuedeVer ? 1 : 0);
                                AgregarParametro(cmdAction, "@Crear", permiso.PuedeCrear ? 1 : 0);
                                AgregarParametro(cmdAction, "@Editar", permiso.PuedeEditar ? 1 : 0);
                                AgregarParametro(cmdAction, "@Eliminar", permiso.PuedeEliminar ? 1 : 0);
                                AgregarParametro(cmdAction, "@Imprimir", permiso.PuedeImprimir ? 1 : 0);

                                await cmdAction.ExecuteNonQueryAsync();
                            }
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
    }
}