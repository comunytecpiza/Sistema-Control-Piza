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
            ORDER BY u.id ASC"; // 👈 ORDENA POR ID PARA MANTENER LA SECUENCIA USR01, USR02, USR03...

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

        public async Task<int> InsertarAsync(Usuario u)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                string queryRaw = $@"
            INSERT INTO usuarios (username, nombres, password, rol_usuario_id, estado, created_at, updated_at)
            VALUES (@Username, @Nombres, @Password, @Rol, @Estado, {nowFunc}, {nowFunc}); {selectId}";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    string queryMax = "SELECT COALESCE(MAX(id), 0) + 1 FROM usuarios";
                    using var cmdMax = dbConn.CreateCommand();
                    cmdMax.CommandText = queryMax;
                    int siguienteId = Convert.ToInt32(await cmdMax.ExecuteScalarAsync());
                    string codigoAleatorio = "USR" + siguienteId.ToString("D2");
                    AgregarParametro(cmd, "@Username", codigoAleatorio);
                    AgregarParametro(cmd, "@Nombres", u.Nombres);
                    AgregarParametro(cmd, "@Password", u.Password);
                    AgregarParametro(cmd, "@Rol", u.RolUsuarioId);
                    AgregarParametro(cmd, "@Estado", u.Estado ? 1 : 0);

                    object res = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(res);
                }
            }
        }
        public async Task<List<dynamic>> ObtenerAlmacenesActivosAsync()
        {
            var lista = new List<dynamic>();
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = "SELECT id, nombre FROM almacenes WHERE estado_id = 1 ORDER BY nombre ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                        }
                    }
                }
            }
            return lista;
        }

        // 🟢 2. Obtener el AlmacenId asignado a un Usuario
        public async Task<int?> ObtenerAlmacenPorUsuarioAsync(int usuarioId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = "SELECT almacen_id FROM usuario_almacenes WHERE usuario_id = @UsuarioId";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@UsuarioId", usuarioId);

                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value)
                    {
                        return Convert.ToInt32(res);
                    }
                }
            }
            return null;
        }

        // 🟢 3. Asignar/Actualizar Almacén de Usuario
        public async Task GuardarUsuarioAlmacenAsync(int usuarioId, int almacenId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string queryUpsert;
                if (QueryAdapter.EsMySQL)
                {
                    queryUpsert = @"
                INSERT INTO usuario_almacenes (usuario_id, almacen_id, created_at)
                VALUES (@UsuarioId, @AlmacenId, NOW())
                ON DUPLICATE KEY UPDATE almacen_id = @AlmacenId;";
                }
                else
                {
                    queryUpsert = @"
                IF EXISTS (SELECT 1 FROM usuario_almacenes WHERE usuario_id = @UsuarioId)
                BEGIN
                    UPDATE usuario_almacenes SET almacen_id = @AlmacenId WHERE usuario_id = @UsuarioId;
                END
                ELSE
                BEGIN
                    INSERT INTO usuario_almacenes (usuario_id, almacen_id, created_at) VALUES (@UsuarioId, @AlmacenId, GETDATE());
                END";
                }

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryUpsert);
                    AgregarParametro(cmd, "@UsuarioId", usuarioId);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

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

                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                string queryRaw = $@"
            UPDATE usuarios SET 
                nombres = @Nombres, 
                password = @Password, 
                rol_usuario_id = @Rol, 
                estado = @Estado,
                updated_at = {nowFunc}
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
            if (matrizPermisos == null || matrizPermisos.Count == 0) return;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var trans = dbConn.BeginTransaction())
                {
                    try
                    {
                        // 🚀 OPTIMIZACIÓN EXTREMA: Borrar e Insertar todo en 1 solo BATCH
                        using (var cmdDelete = dbConn.CreateCommand())
                        {
                            cmdDelete.Transaction = trans;
                            cmdDelete.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM rol_permisos WHERE rol_usuario_id = @RolId");
                            AgregarParametro(cmdDelete, "@RolId", rolUsuarioId);
                            await cmdDelete.ExecuteNonQueryAsync();
                        }

                        // Inserción en bloques de 50 para máxima velocidad en SQL Server y MySQL
                        const int batchSize = 50;
                        for (int i = 0; i < matrizPermisos.Count; i += batchSize)
                        {
                            var lote = matrizPermisos.Skip(i).Take(batchSize).ToList();
                            var sb = new System.Text.StringBuilder();
                            using var cmdInsert = dbConn.CreateCommand();
                            cmdInsert.Transaction = trans;

                            sb.Append("INSERT INTO rol_permisos (rol_usuario_id, modulo_id, puede_ver, puede_crear, puede_editar, puede_eliminar, puede_imprimir) VALUES ");

                            for (int j = 0; j < lote.Count; j++)
                            {
                                var p = lote[j];
                                sb.Append($"(@Rol, @m{j}, @v{j}, @c{j}, @e{j}, @d{j}, @i{j})");
                                if (j < lote.Count - 1) sb.Append(",");

                                AgregarParametro(cmdInsert, $"@m{j}", p.ModuloId);
                                AgregarParametro(cmdInsert, $"@v{j}", p.PuedeVer ? 1 : 0);
                                AgregarParametro(cmdInsert, $"@c{j}", p.PuedeCrear ? 1 : 0);
                                AgregarParametro(cmdInsert, $"@e{j}", p.PuedeEditar ? 1 : 0);
                                AgregarParametro(cmdInsert, $"@d{j}", p.PuedeEliminar ? 1 : 0);
                                AgregarParametro(cmdInsert, $"@i{j}", p.PuedeImprimir ? 1 : 0);
                            }

                            AgregarParametro(cmdInsert, "@Rol", rolUsuarioId);
                            cmdInsert.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
                            await cmdInsert.ExecuteNonQueryAsync();
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

        public async Task<int> CrearRolAsync(string nombre, string descripcion)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            string query = $@"
        INSERT INTO roles_usuario (nombre, descripcion, estado, created_at, updated_at)
        VALUES (@Nombre, @Desc, 1, {nowFunc}, {nowFunc}); {selectId}";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@Nombre", nombre);
            AgregarParametro(cmd, "@Desc", descripcion);

            object res = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(res);
        }
    }
}