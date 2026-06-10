using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services
{
    public class UsuarioService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public UsuarioService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        public async Task<List<Usuario>> ObtenerTodosAsync(string filtro = "")
        {
            var lista = new List<Usuario>();
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();

            string queryRaw = @"
                SELECT 
                    u.id, u.username, u.nombres, u.password, u.rol_usuario_id, u.estado,
                    r.nombre AS rol_nombre, r.descripcion AS rol_desc
                FROM usuarios u
                INNER JOIN roles_usuario r ON u.rol_usuario_id = r.id
                WHERE (u.nombres LIKE @Filtro OR u.username LIKE @Filtro)
                ORDER BY u.nombres ASC";

            string queryFinal = QueryAdapter.FormatearConsulta(queryRaw);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = queryFinal;

                var pFiltro = cmd.CreateParameter();
                pFiltro.ParameterName = "@Filtro";
                pFiltro.Value = "%" + filtro + "%";
                cmd.Parameters.Add(pFiltro);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
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
            return lista;
        }

        public async Task InsertarAsync(Usuario u)
        {
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();

            string queryRaw = @"
                INSERT INTO usuarios (username, nombres, password, rol_usuario_id, estado, created_at, updated_at)
                VALUES (@Username, @Nombres, @Password, @Rol, @Estado, GETDATE(), GETDATE())";

            string queryFinal = QueryAdapter.FormatearConsulta(queryRaw);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = queryFinal;
                string codigoAleatorio = "USR" + new Random().Next(10, 99);

                AgregarParametro(cmd, "@Username", codigoAleatorio);
                AgregarParametro(cmd, "@Nombres", u.Nombres);
                AgregarParametro(cmd, "@Password", u.Password);
                AgregarParametro(cmd, "@Rol", u.RolUsuarioId);
                AgregarParametro(cmd, "@Estado", u.Estado ? 1 : 0);

                cmd.ExecuteNonQuery();
            }
        }

        public async Task ActualizarAsync(Usuario u)
        {
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();

            string queryRaw = @"
                UPDATE usuarios SET 
                    nombres = @Nombres, 
                    password = @Password, 
                    rol_usuario_id = @Rol, 
                    estado = @Estado,
                    updated_at = GETDATE()
                WHERE id = @Id";

            string queryFinal = QueryAdapter.FormatearConsulta(queryRaw);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = queryFinal;

                AgregarParametro(cmd, "@Id", u.Id);
                AgregarParametro(cmd, "@Nombres", u.Nombres);
                AgregarParametro(cmd, "@Password", u.Password);
                AgregarParametro(cmd, "@Rol", u.RolUsuarioId);
                AgregarParametro(cmd, "@Estado", u.Estado ? 1 : 0);

                cmd.ExecuteNonQuery();
            }
        }

        public async Task<List<RolesUsuario>> ObtenerRolesActivosAsync()
        {
            var lista = new List<RolesUsuario>();
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();

            string query = "SELECT id, nombre, descripcion FROM roles_usuario WHERE estado = 1 ORDER BY nombre ASC";

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = query;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
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
            return lista;
        }

        public async Task<List<RolPermiso>> ObtenerPermisosPorRolAsync(int rolUsuarioId)
        {
            var lista = new List<RolPermiso>();
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();

            string queryRaw = @"
                SELECT 
                    m.id AS modulo_id, m.codigo_modulo, m.nombre_modulo,
                    ISNULL(p.id, 0) AS permiso_id,
                    ISNULL(p.puede_ver, 0) AS puede_ver,
                    ISNULL(p.puede_crear, 0) AS puede_crear,
                    ISNULL(p.puede_editar, 0) AS puede_editar,
                    ISNULL(p.puede_eliminar, 0) AS puede_eliminar,
                    ISNULL(p.puede_imprimir, 0) AS puede_imprimir
                FROM modulos_sistema m
                LEFT JOIN rol_permisos p ON m.id = p.modulo_id AND p.rol_usuario_id = @RolId";

            string queryFinal = QueryAdapter.FormatearConsulta(queryRaw);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = queryFinal;
                AgregarParametro(cmd, "@RolId", rolUsuarioId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new RolPermiso
                        {
                            ModuloId = reader.GetInt32(0),
                            CodigoModulo = reader.GetString(1),
                            NombreModulo = reader.GetString(2),
                            Id = reader.GetInt32(3),
                            RolUsuarioId = rolUsuarioId,
                            PuedeVer = reader.GetBoolean(4),
                            PuedeCrear = reader.GetBoolean(5),
                            PuedeEditar = reader.GetBoolean(6),
                            PuedeEliminar = reader.GetBoolean(7),
                            PuedeImprimir = reader.GetBoolean(8)
                        });
                    }
                }
            }
            return lista;
        }

        public async Task GuardarPermisosMasivosAsync(int rolUsuarioId, List<RolPermiso> matrizPermisos)
        {
            using var conn = _database.GetConnection();
            await ((System.Data.Common.DbConnection)conn).OpenAsync();
            using var trans = conn.BeginTransaction();

            try
            {
                foreach (var permiso in matrizPermisos)
                {
                    bool existe = false;

                    using (var cmdCheck = conn.CreateCommand())
                    {
                        cmdCheck.Transaction = trans;
                        cmdCheck.CommandText = "SELECT COUNT(*) FROM rol_permisos WHERE rol_usuario_id = @RolId AND modulo_id = @ModId";
                        AgregarParametro(cmdCheck, "@RolId", rolUsuarioId);
                        AgregarParametro(cmdCheck, "@ModId", permiso.ModuloId);
                        existe = Convert.ToInt32(cmdCheck.ExecuteScalar()) > 0;
                    }

                    using (var cmdAction = conn.CreateCommand())
                    {
                        cmdAction.Transaction = trans;
                        if (existe)
                        {
                            cmdAction.CommandText = @"
                                UPDATE rol_permisos SET 
                                    puede_ver = @Ver, puede_crear = @Crear, puede_editar = @Editar, 
                                    puede_eliminar = @Eliminar, puede_imprimir = @Imprimir
                                WHERE rol_usuario_id = @RolId AND modulo_id = @ModId";
                        }
                        else
                        {
                            cmdAction.CommandText = @"
                                INSERT INTO rol_permisos (rol_usuario_id, modulo_id, puede_ver, puede_crear, puede_editar, puede_eliminar, puede_imprimir)
                                VALUES (@RolId, @ModId, @Ver, @Crear, @Editar, @Eliminar, @Imprimir)";
                        }

                        AgregarParametro(cmdAction, "@RolId", rolUsuarioId);
                        AgregarParametro(cmdAction, "@ModId", permiso.ModuloId);
                        AgregarParametro(cmdAction, "@Ver", permiso.Permanent ? 1 : 0); // Corrección para compatibilidad de tipos
                        AgregarParametro(cmdAction, "@Ver", permiso.PuedeVer ? 1 : 0);
                        AgregarParametro(cmdAction, "@Crear", permiso.PuedeCrear ? 1 : 0);
                        AgregarParametro(cmdAction, "@Editar", permiso.PuedeEditar ? 1 : 0);
                        AgregarParametro(cmdAction, "@Eliminar", permiso.PuedeEliminar ? 1 : 0);
                        AgregarParametro(cmdAction, "@Imprimir", permiso.PuedeImprimir ? 1 : 0);

                        cmdAction.ExecuteNonQuery();
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

        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor;
            cmd.Parameters.Add(p);
        }
    }
}