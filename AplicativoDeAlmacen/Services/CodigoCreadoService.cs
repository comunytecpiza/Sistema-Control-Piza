using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class CodigoAuditoriaDTO
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string CondicionNombre { get; set; } = string.Empty;
        public string AlmacenNombre { get; set; } = string.Empty;
        public string UsuarioCreador { get; set; } = string.Empty;
        public string OrigenCreacion { get; set; } = string.Empty;
        public DateTime FechaCreacion { get; set; }
        public bool PermitirSalida { get; set; }
    }

    public class CodigoCreadoService
    {
        private readonly DatabaseConnection _database;

        public CodigoCreadoService()
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

        // 🌟 CONSULTA DE CÓDIGOS CON SOPORTE MULTI-MOTOR (MYSQL / SQL SERVER)
        public async Task<List<CodigoCreado>> ObtenerPorRegistroIdAsync(int registroCodigoId, int? almacenIdFiltro = null)
        {
            var lista = new List<CodigoCreado>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";

                string query = $@"
                    SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id, cc.condicion_id, cc.almacen_id, cc.usuario_id, cc.origen_creacion, cc.created_at,
                           {coalesceFunc}(a.nombre, 'SIN ALMACÉN') AS almacen_nombre,
                           cond.nombre AS condicion_nombre
                    FROM codigos_creados cc {nolock}
                    INNER JOIN condiciones_codigo cond {nolock} ON cc.condicion_id = cond.id
                    LEFT JOIN almacenes a {nolock} ON cc.almacen_id = a.id
                    WHERE cc.registro_codigo_id = @id";

                if (almacenIdFiltro.HasValue)
                {
                    query += " AND cc.almacen_id = @almacenId";
                }

                query += " ORDER BY cc.codigo ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@id", registroCodigoId);
                    if (almacenIdFiltro.HasValue)
                    {
                        AgregarParametro(cmd, "@almacenId", almacenIdFiltro.Value);
                    }

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new CodigoCreado
                            {
                                Id = reader.GetInt32(0),
                                RegistroCodigoId = reader.GetInt32(1),
                                Codigo = reader.IsDBNull(2) ? "SIN CÓDIGO" : reader.GetString(2),
                                EsManual = !reader.IsDBNull(3) && reader.GetBoolean(3),
                                EstadoId = reader.GetInt32(4),
                                CondicionId = reader.GetInt32(5),
                                AlmacenId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                                UsuarioId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                                OrigenCreacion = reader.IsDBNull(8) ? "SECUENCIA" : reader.GetString(8),
                                CreatedAt = reader.GetDateTime(9),
                                AlmacenNombre = reader.IsDBNull(10) ? "SIN ALMACÉN" : reader.GetString(10)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<int> CambiarCondicionPorRangoAsync(int registroCodigoId, int desdeNum, int hastaNum, int nuevaCondicionId, int usuarioModificadorId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🛡️ Filtro agnóstico para números correlativos al final del código
                string condicionNumero = QueryAdapter.EsMySQL
                    ? "RIGHT(codigo, 7) REGEXP '^[0-9]+$' AND CAST(RIGHT(codigo, 7) AS SIGNED)"
                    : "ISNUMERIC(RIGHT(codigo, 7)) = 1 AND CAST(RIGHT(codigo, 7) AS INT)";

                string query = $@"
            UPDATE codigos_creados 
            SET condicion_id = @condicionId, 
                usuario_id = @usuarioId 
            WHERE registro_codigo_id = @rid 
              AND {condicionNumero} BETWEEN @desde AND @hasta";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@condicionId", nuevaCondicionId);
                    AgregarParametro(cmd, "@usuarioId", usuarioModificadorId);
                    AgregarParametro(cmd, "@rid", registroCodigoId);
                    AgregarParametro(cmd, "@desde", desdeNum);
                    AgregarParametro(cmd, "@hasta", hastaNum);

                    return await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // 🌟 2. CAMBIAR CONDICIÓN POR LISTA DE IDS (SELECCIÓN CON CTRL / SHIFT + CLIC)
        public async Task<int> CambiarCondicionPorListaIdsAsync(List<int> idsCodigos, int nuevaCondicionId, int usuarioModificadorId)
        {
            if (idsCodigos == null || !idsCodigos.Any()) return 0;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                var paramNames = new List<string>();
                using var cmd = dbConn.CreateCommand();

                for (int i = 0; i < idsCodigos.Count; i++)
                {
                    string paramName = "@id" + i;
                    paramNames.Add(paramName);
                    AgregarParametro(cmd, paramName, idsCodigos[i]);
                }

                string query = $@"
            UPDATE codigos_creados 
            SET condicion_id = @condicionId, 
                usuario_id = @usuarioId 
            WHERE id IN ({string.Join(",", paramNames)})";

                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@condicionId", nuevaCondicionId);
                AgregarParametro(cmd, "@usuarioId", usuarioModificadorId);

                return await cmd.ExecuteNonQueryAsync();
            }
        }

        // 🌟 AUDITORÍA CON NOMBRE REAL DE USUARIO CREADOR/MODIFICADOR
        public async Task<CodigoAuditoriaDTO?> ObtenerAuditoriaCompletaAsync(int codigoId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                string query = $@"
            SELECT cc.id, cc.codigo, cond.nombre AS condicion, 
                   {coalesceFunc}(a.nombre, 'Sin Almacén') AS almacen,
                   {coalesceFunc}(u.nombres, 'SISTEMA') AS usuario, 
                   cc.origen_creacion, cc.created_at, cond.permitir_salida
            FROM codigos_creados cc {nolock}
            INNER JOIN condiciones_codigo cond {nolock} ON cc.condicion_id = cond.id
            LEFT JOIN almacenes a {nolock} ON cc.almacen_id = a.id
            LEFT JOIN usuarios u {nolock} ON cc.usuario_id = u.id
            WHERE cc.id = @id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@id", codigoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new CodigoAuditoriaDTO
                            {
                                Id = reader.GetInt32(0),
                                Codigo = reader.GetString(1),
                                CondicionNombre = reader.GetString(2),
                                AlmacenNombre = reader.GetString(3),
                                UsuarioCreador = reader.GetString(4),
                                OrigenCreacion = reader.IsDBNull(5) ? "SECUENCIA" : reader.GetString(5),
                                FechaCreacion = reader.GetDateTime(6),
                                PermitirSalida = Convert.ToBoolean(reader[7])
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> ExisteCodigoAsync(int registroId, string codigo)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    SELECT COUNT(1) 
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    WHERE cc.codigo = @cod 
                      AND rc.producto_id = (SELECT producto_id FROM registro_codigos WHERE id = @rid)";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@cod", codigo);
                    AgregarParametro(cmd, "@rid", registroId);

                    int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    return count > 0;
                }
            }
        }

        public async Task RegistrarManualAsync(int registroId, string codigo, int usuarioId, int almacenId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                string query = $@"
                    INSERT INTO codigos_creados 
                    (registro_codigo_id, codigo, es_manual, estado_id, condicion_id, almacen_id, usuario_id, origen_creacion, created_at)
                    VALUES (@rid, @cod, 1, 1, 1, @almId, @usrId, 'MANUAL', {nowFunc})";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@rid", registroId);
                    AgregarParametro(cmd, "@cod", codigo);
                    AgregarParametro(cmd, "@almId", almacenId);
                    AgregarParametro(cmd, "@usrId", usuarioId);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}