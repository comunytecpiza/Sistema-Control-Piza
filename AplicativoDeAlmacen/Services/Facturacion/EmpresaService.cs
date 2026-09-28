using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Models.Facturación;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Facturación
{
    public class EmpresaService
    {
        private readonly DatabaseConnection _database;

        public EmpresaService()
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
        // 1. EMPRESAS (CRUD)
        // =========================================================================

        public async Task<List<Empresa>> ObtenerEmpresasAsync(bool soloActivas = true)
        {
            var lista = new List<Empresa>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
                SELECT id, ruc, razon_social, nombre_comercial, direccion, es_activo, created_at
                FROM empresas
                " + (soloActivas ? "WHERE es_activo = 1 " : "") + @"
                ORDER BY razon_social ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Empresa
                {
                    Id = reader.GetInt32(0),
                    Ruc = reader.GetString(1),
                    RazonSocial = reader.GetString(2),
                    NombreComercial = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Direccion = reader.IsDBNull(4) ? null : reader.GetString(4),
                    EsActivo = Convert.ToBoolean(reader.GetValue(5)),
                    FechaRegistro = reader.IsDBNull(6) ? DateTime.Now : reader.GetDateTime(6)
                });
            }

            return lista;
        }

        public async Task<int> GuardarEmpresaAsync(Empresa emp)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            if (emp.Id > 0)
            {
                string sqlUpd = @"
                    UPDATE empresas 
                    SET ruc = @ruc,
                        razon_social = @rs,
                        nombre_comercial = @nc,
                        direccion = @dir,
                        es_activo = @act,
                        updated_at = NOW()
                    WHERE id = @id;";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(sqlUpd);
                AgregarParametro(cmd, "@ruc", emp.Ruc.Trim());
                AgregarParametro(cmd, "@rs", emp.RazonSocial.Trim());
                AgregarParametro(cmd, "@nc", emp.NombreComercial?.Trim());
                AgregarParametro(cmd, "@dir", emp.Direccion?.Trim());
                AgregarParametro(cmd, "@act", emp.EsActivo ? 1 : 0);
                AgregarParametro(cmd, "@id", emp.Id);

                await cmd.ExecuteNonQueryAsync();
                return emp.Id;
            }
            else
            {
                string sqlIns = $@"
                    INSERT INTO empresas (ruc, razon_social, nombre_comercial, direccion, es_activo, created_at)
                    VALUES (@ruc, @rs, @nc, @dir, @act, NOW());
                    {selectId}";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(sqlIns);
                AgregarParametro(cmd, "@ruc", emp.Ruc.Trim());
                AgregarParametro(cmd, "@rs", emp.RazonSocial.Trim());
                AgregarParametro(cmd, "@nc", emp.NombreComercial?.Trim());
                AgregarParametro(cmd, "@dir", emp.Direccion?.Trim());
                AgregarParametro(cmd, "@act", emp.EsActivo ? 1 : 0);

                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res);
            }
        }

        public async Task<bool> CambiarEstadoEmpresaAsync(int empresaId, bool activo)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE empresas SET es_activo = @act WHERE id = @id;");
            AgregarParametro(cmd, "@act", activo ? 1 : 0);
            AgregarParametro(cmd, "@id", empresaId);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =========================================================================
        // 2. SERIES VINCULADAS A EMPRESAS (series_documentos)
        // =========================================================================

        public async Task<List<SerieDocumento>> ObtenerSeriesConEmpresaAsync()
        {
            var lista = new List<SerieDocumento>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
                SELECT 
                    s.id, 
                    s.ubicacion_id, 
                    s.empresa_id, 
                    s.num_seri, 
                    s.tip_seri, 
                    s.num_fact, 
                    s.num_bole, 
                    s.num_reci, 
                    s.fec_regi, 
                    s.cod_usua, 
                    s.est_regi,
                    e.razon_social AS empresa_nombre
                FROM series_documentos s
                LEFT JOIN empresas e ON s.empresa_id = e.id
                ORDER BY s.num_seri ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var s = new SerieDocumento
                {
                    Id = reader.GetInt32(0),
                    UbicacionId = reader.GetInt32(1),
                    EmpresaId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    NumeroSerie = reader.GetString(3),
                    TipoSerie = reader.IsDBNull(4) ? "E" : reader.GetString(4),
                    CorrelativoFactura = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    CorrelativoBoleta = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    CorrelativoRecibo = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    FechaRegistro = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8),
                    CodigoUsuario = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    EstadoId = reader.IsDBNull(10) ? 1 : reader.GetInt32(10)
                };

                if (!reader.IsDBNull(2) && !reader.IsDBNull(11))
                {
                    s.Empresa = new Empresa
                    {
                        Id = reader.GetInt32(2),
                        RazonSocial = reader.GetString(11)
                    };
                }

                lista.Add(s);
            }

            return lista;
        }

        public async Task<bool> AsignarEmpresaASerieAsync(int serieDocumentoId, int? empresaId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE series_documentos SET empresa_id = @empId WHERE id = @id;");
            AgregarParametro(cmd, "@empId", empresaId);
            AgregarParametro(cmd, "@id", serieDocumentoId);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =========================================================================
        // 3. MONEDAS (CONSULTA Y GESTIÓN RÁPIDA)
        // =========================================================================

        public async Task<List<Moneda>> ObtenerMonedasAsync(bool soloActivas = true)
        {
            var lista = new List<Moneda>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
                SELECT id, codigo_sunat, descripcion, simbolo, es_activo
                FROM monedas
                " + (soloActivas ? "WHERE es_activo = 1 " : "") + @"
                ORDER BY id ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Moneda
                {
                    Id = reader.GetInt32(0),
                    CodigoSunat = reader.GetString(1),
                    Descripcion = reader.GetString(2),
                    Simbolo = reader.GetString(3),
                    EsActivo = Convert.ToBoolean(reader.GetValue(4))
                });
            }

            return lista;
        }

        public async Task<int> GuardarMonedaAsync(Moneda m)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            if (m.Id > 0)
            {
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    UPDATE monedas 
                    SET codigo_sunat = @cod, descripcion = @desc, simbolo = @sim, es_activo = @act 
                    WHERE id = @id;");
                AgregarParametro(cmd, "@cod", m.CodigoSunat.Trim().ToUpper());
                AgregarParametro(cmd, "@desc", m.Descripcion.Trim().ToUpper());
                AgregarParametro(cmd, "@sim", m.Simbolo.Trim());
                AgregarParametro(cmd, "@act", m.EsActivo ? 1 : 0);
                AgregarParametro(cmd, "@id", m.Id);

                await cmd.ExecuteNonQueryAsync();
                return m.Id;
            }
            else
            {
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                    INSERT INTO monedas (codigo_sunat, descripcion, simbolo, es_activo) 
                    VALUES (@cod, @desc, @sim, @act); 
                    {selectId}");
                AgregarParametro(cmd, "@cod", m.CodigoSunat.Trim().ToUpper());
                AgregarParametro(cmd, "@desc", m.Descripcion.Trim().ToUpper());
                AgregarParametro(cmd, "@sim", m.Simbolo.Trim());
                AgregarParametro(cmd, "@act", m.EsActivo ? 1 : 0);

                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res);
            }
        }
    }
}