using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Facturación
{
    public class MedioPagoService
    {
        private readonly DatabaseConnection _database;

        public MedioPagoService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =========================================================================
        // LISTAR MEDIOS DE PAGO
        // =========================================================================
        public async Task<List<MedioPago>> ObtenerMediosPagoAsync(bool soloActivos = true)
        {
            var lista = new List<MedioPago>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
                SELECT id, nombre, codigo_sunat, tipo, es_activo, created_at
                FROM medios_pago
                " + (soloActivos ? "WHERE es_activo = 1 " : "") + @"
                ORDER BY nombre ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new MedioPago
                {
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    CodigoSunat = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Tipo = reader.IsDBNull(3) ? "CONTADO" : reader.GetString(3),
                    EsActivo = Convert.ToBoolean(reader.GetValue(4)),
                    CreatedAt = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5)
                });
            }

            return lista;
        }

        // =========================================================================
        // GUARDAR (CREAR / EDITAR)
        // =========================================================================
        public async Task<int> GuardarMedioPagoAsync(MedioPago mp)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            if (mp.Id > 0)
            {
                string sqlUpd = @"
                    UPDATE medios_pago 
                    SET nombre = @nom,
                        codigo_sunat = @cs,
                        tipo = @tipo,
                        es_activo = @act
                    WHERE id = @id;";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(sqlUpd);
                AgregarParametro(cmd, "@nom", mp.Nombre.Trim().ToUpper());
                AgregarParametro(cmd, "@cs", string.IsNullOrWhiteSpace(mp.CodigoSunat) ? null : mp.CodigoSunat.Trim());
                AgregarParametro(cmd, "@tipo", mp.Tipo?.Trim().ToUpper() ?? "CONTADO");
                AgregarParametro(cmd, "@act", mp.EsActivo ? 1 : 0);
                AgregarParametro(cmd, "@id", mp.Id);

                await cmd.ExecuteNonQueryAsync();
                return mp.Id;
            }
            else
            {
                string sqlIns = $@"
                    INSERT INTO medios_pago (nombre, codigo_sunat, tipo, es_activo, created_at)
                    VALUES (@nom, @cs, @tipo, @act, NOW());
                    {selectId}";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(sqlIns);
                AgregarParametro(cmd, "@nom", mp.Nombre.Trim().ToUpper());
                AgregarParametro(cmd, "@cs", string.IsNullOrWhiteSpace(mp.CodigoSunat) ? null : mp.CodigoSunat.Trim());
                AgregarParametro(cmd, "@tipo", mp.Tipo?.Trim().ToUpper() ?? "CONTADO");
                AgregarParametro(cmd, "@act", mp.EsActivo ? 1 : 0);

                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res);
            }
        }

        // =========================================================================
        // ACTIVAR / DESACTIVAR
        // =========================================================================
        public async Task<bool> CambiarEstadoMedioPagoAsync(int medioPagoId, bool activo)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE medios_pago SET es_activo = @act WHERE id = @id;");
            AgregarParametro(cmd, "@act", activo ? 1 : 0);
            AgregarParametro(cmd, "@id", medioPagoId);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}