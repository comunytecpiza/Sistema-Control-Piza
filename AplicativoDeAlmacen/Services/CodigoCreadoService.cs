using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    // DTO para la Auditoría Completa del Modal de Administrador
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

        // 🌟 CONSULTA DE CÓDIGOS CON FILTRO POR ALMACÉN DE SESIÓN
        public async Task<List<CodigoCreado>> ObtenerPorRegistroIdAsync(int registroCodigoId, int? almacenIdFiltro = null)
        {
            var lista = new List<CodigoCreado>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
            SELECT cc.id,                -- [0]
                   cc.registro_codigo_id,-- [1]
                   cc.codigo,            -- [2]
                   cc.es_manual,         -- [3]
                   cc.estado_id,         -- [4]
                   cc.condicion_id,      -- [5]
                   cc.almacen_id,        -- [6]
                   cc.usuario_id,        -- [7]
                   cc.origen_creacion,   -- [8]
                   cc.created_at,        -- [9]
                   ISNULL(a.nombre, 'SIN ALMACÉN') AS almacen_nombre, -- [10] 👈 ALMACÉN
                   cond.nombre AS condicion_nombre                     -- [11] 👈 CONDICIÓN
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN condiciones_codigo cond WITH (NOLOCK) ON cc.condicion_id = cond.id
            LEFT JOIN almacenes a WITH (NOLOCK) ON cc.almacen_id = a.id
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

                                // 🌟 ASIGNACIÓN EXACTA DEL NOMBRE DEL ALMACÉN
                                AlmacenNombre = reader.IsDBNull(10) ? "SIN ALMACÉN" : reader.GetString(10)
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // 🌟 CAMBIAR CONDICIÓN POR RANGO (Ej: De correlativo 10 a 20)
        public async Task<int> CambiarCondicionPorRangoAsync(int registroCodigoId, int desdeNum, int hastaNum, int nuevaCondicionId, int usuarioModificadorId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    UPDATE codigos_creados 
                    SET condicion_id = @condicionId, 
                        usuario_id = @usuarioId 
                    WHERE registro_codigo_id = @rid 
                      AND ISNUMERIC(RIGHT(codigo, 7)) = 1 
                      AND CAST(RIGHT(codigo, 7) AS INT) BETWEEN @desde AND @hasta";

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

        // 🌟 OBTENER AUDITORÍA COMPLETA PARA EL MODAL DE ADMINISTRADOR
        public async Task<CodigoAuditoriaDTO?> ObtenerAuditoriaCompletaAsync(int codigoId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                    SELECT cc.id, cc.codigo, cond.nombre AS condicion, ISNULL(a.nombre, 'Sin Almacén') AS almacen,
                           ISNULL(u.nombres, 'SISTEMA') AS usuario, cc.origen_creacion, cc.created_at, cond.permitir_salida
                    FROM codigos_creados cc
                    INNER JOIN condiciones_codigo cond ON cc.condicion_id = cond.id
                    LEFT JOIN almacenes a ON cc.almacen_id = a.id
                    LEFT JOIN usuarios u ON cc.usuario_id = u.id
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

                string query = @"
                    INSERT INTO codigos_creados 
                    (registro_codigo_id, codigo, es_manual, estado_id, condicion_id, almacen_id, usuario_id, origen_creacion, created_at)
                    VALUES (@rid, @cod, 1, 1, 1, @almId, @usrId, 'MANUAL', GETDATE())";

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