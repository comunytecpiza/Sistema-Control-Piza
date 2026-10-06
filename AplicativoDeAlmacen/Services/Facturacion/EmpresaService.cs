#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Data;
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

        // =======================================================
        // OBTENER EMPRESAS (Cambiado a la tabla "empresas")
        // =======================================================
        public async Task<List<Empresa>> ObtenerEmpresasAsync(bool soloActivas = true)
        {
            var lista = new List<Empresa>();
            // SE CORRIGIÓ EL NOMBRE DE LA TABLA A "empresas"
            string query = "SELECT id, ruc, razon_social, nombre_comercial, direccion, es_activo FROM empresas";
            if (soloActivas) query += " WHERE es_activo = 1";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Empresa
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Ruc = reader["ruc"].ToString()!,
                    RazonSocial = reader["razon_social"].ToString()!,
                    NombreComercial = reader["nombre_comercial"] != DBNull.Value ? reader["nombre_comercial"].ToString() : null,
                    Direccion = reader["direccion"] != DBNull.Value ? reader["direccion"].ToString() : null,
                    EsActivo = Convert.ToInt32(reader["es_activo"]) == 1
                });
            }
            return lista;
        }

        // =======================================================
        // OBTENER SERIES (Ya incluye la empresa vinculada)
        // =======================================================
        public async Task<List<SerieDocumento>> ObtenerSeriesConEmpresaAsync()
        {
            var lista = new List<SerieDocumento>();
            // SE CORRIGIÓ EL JOIN A LA TABLA "empresas"
            string query = @"
                SELECT s.id, s.num_seri, s.tip_seri, s.num_fact, s.num_bole, s.num_reci, s.empresa_id,
                       e.ruc, e.razon_social 
                FROM series_documentos s
                LEFT JOIN empresas e ON s.empresa_id = e.id
                WHERE s.est_regi = 1 
                ORDER BY s.num_seri";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var serie = new SerieDocumento
                {
                    Id = Convert.ToInt32(reader["id"]),
                    NumeroSerie = reader["num_seri"].ToString()!,
                    TipoSerie = reader["tip_seri"].ToString(),
                    CorrelativoFactura = Convert.ToInt32(reader["num_fact"]),
                    CorrelativoBoleta = Convert.ToInt32(reader["num_bole"]),
                    CorrelativoRecibo = Convert.ToInt32(reader["num_reci"]),
                    EmpresaId = reader["empresa_id"] != DBNull.Value ? Convert.ToInt32(reader["empresa_id"]) : (int?)null
                };

                if (serie.EmpresaId.HasValue)
                {
                    serie.Empresa = new Empresa
                    {
                        Id = serie.EmpresaId.Value,
                        Ruc = reader["ruc"].ToString()!,
                        RazonSocial = reader["razon_social"].ToString()!
                    };
                }

                lista.Add(serie);
            }
            return lista;
        }

        // =======================================================
        // VINCULAR EMPRESA A SERIE
        // =======================================================
        public async Task AsignarEmpresaASerieAsync(int serieId, int? empresaId)
        {
            string query = "UPDATE series_documentos SET empresa_id = @EmpresaId WHERE id = @Id";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@Id", serieId);
            AgregarParametro(cmd, "@EmpresaId", empresaId);

            await cmd.ExecuteNonQueryAsync();
        }

        // =======================================================
        // GUARDAR EMPRESA (Cambiado a la tabla "empresas")
        // =======================================================
        public async Task GuardarEmpresaAsync(Empresa emp)
        {
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
            string query;

            if (emp.Id == 0)
            {
                // SE CORRIGIÓ EL INSERT A LA TABLA "empresas"
                query = $@"INSERT INTO empresas (ruc, razon_social, nombre_comercial, direccion, es_activo, created_at, updated_at) 
                           VALUES (@Ruc, @Razon, @Comercial, @Dir, @Activo, {nowFunc}, {nowFunc})";
            }
            else
            {
                // SE CORRIGIÓ EL UPDATE A LA TABLA "empresas"
                query = $@"UPDATE empresas SET ruc = @Ruc, razon_social = @Razon, nombre_comercial = @Comercial, 
                           direccion = @Dir, es_activo = @Activo, updated_at = {nowFunc} WHERE id = @Id";
            }

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            if (emp.Id != 0) AgregarParametro(cmd, "@Id", emp.Id);
            AgregarParametro(cmd, "@Ruc", emp.Ruc);
            AgregarParametro(cmd, "@Razon", emp.RazonSocial);
            AgregarParametro(cmd, "@Comercial", emp.NombreComercial);
            AgregarParametro(cmd, "@Dir", emp.Direccion);
            AgregarParametro(cmd, "@Activo", emp.EsActivo ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();
        }

        // =======================================================
        // MÉTODOS DE MONEDA 
        // =======================================================
        // =======================================================
        // MÉTODOS DE MONEDA (Adaptados a tu tabla física real)
        // =======================================================
        public async Task<List<Moneda>> ObtenerMonedasAsync(bool soloActivas = true)
        {
            var lista = new List<Moneda>();
            // Se corrigió "codigo_sunat" y se omitieron fechas porque no existen en tu tabla
            string query = "SELECT id, codigo_sunat, descripcion, simbolo, es_activo FROM monedas";
            if (soloActivas) query += " WHERE es_activo = 1";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Moneda
                {
                    Id = Convert.ToInt32(reader["id"]),
                    CodigoSunat = reader["codigo_sunat"].ToString()!, // Corregido aquí
                    Descripcion = reader["descripcion"].ToString()!,
                    Simbolo = reader["simbolo"] != DBNull.Value ? reader["simbolo"].ToString() : null,
                    EsActivo = Convert.ToInt32(reader["es_activo"]) == 1
                });
            }
            return lista;
        }

        public async Task GuardarMonedaAsync(Moneda mon)
        {
            string query;

            if (mon.Id == 0)
            {
                // Se corrigió "codigo_sunat" y se quitaron created_at/updated_at
                query = @"INSERT INTO monedas (codigo_sunat, descripcion, simbolo, es_activo) 
                          VALUES (@CodSunat, @Desc, @Simbolo, @Activo)";
            }
            else
            {
                // Se corrigió "codigo_sunat" y se quitaron created_at/updated_at
                query = @"UPDATE monedas SET codigo_sunat = @CodSunat, descripcion = @Desc, 
                          simbolo = @Simbolo, es_activo = @Activo WHERE id = @Id";
            }

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            if (mon.Id != 0) AgregarParametro(cmd, "@Id", mon.Id);

            AgregarParametro(cmd, "@CodSunat", mon.CodigoSunat);
            AgregarParametro(cmd, "@Desc", mon.Descripcion);
            AgregarParametro(cmd, "@Simbolo", mon.Simbolo);
            AgregarParametro(cmd, "@Activo", mon.EsActivo ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}