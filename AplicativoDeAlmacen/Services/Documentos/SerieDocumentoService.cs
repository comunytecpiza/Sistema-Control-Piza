#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Documentos;

namespace AplicativoDeAlmacen.Services.Documentos
{
    public class SerieDocumentoService
    {
        private readonly DatabaseConnection _database;

        public SerieDocumentoService()
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
        // CATÁLOGO DE DOCUMENTOS (Para combos y referencias)
        // =======================================================
        public async Task<List<Documento>> ObtenerDocumentosActivosAsync()
        {
            var lista = new List<Documento>();
            string query = "SELECT cod_docu, des_docu, abreviatura, est_regi FROM documentos WHERE est_regi = 1 ORDER BY cod_docu";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Documento
                {
                    Codigo = reader["cod_docu"].ToString()!,
                    Descripcion = reader["des_docu"].ToString()!,
                    Abreviatura = reader["abreviatura"] != DBNull.Value ? reader["abreviatura"].ToString() : null,
                    Estado = true
                });
            }
            return lista;
        }

        // =======================================================
        // OPERACIONES DE SERIES POR UBICACIÓN O EMPRESA
        // =======================================================
        public async Task<List<SerieDocumento>> ObtenerSeriesPorUbicacionAsync(int ubicacionId)
        {
            var lista = new List<SerieDocumento>();
            string query = @"
                SELECT id, ubicacion_id, empresa_id, num_seri, tip_seri, 
                       num_fact, num_bole, num_reci, fec_regi, cod_usua, est_regi
                FROM series_documentos
                WHERE ubicacion_id = @UbicacionId AND est_regi = 1
                ORDER BY num_seri";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@UbicacionId", ubicacionId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SerieDocumento
                {
                    Id = Convert.ToInt32(reader["id"]),
                    UbicacionId = reader["ubicacion_id"] != DBNull.Value ? Convert.ToInt32(reader["ubicacion_id"]) : 0,
                    EmpresaId = reader["empresa_id"] != DBNull.Value ? Convert.ToInt32(reader["empresa_id"]) : null,
                    NumeroSerie = reader["num_seri"].ToString() ?? "",
                    TipoSerie = reader["tip_seri"] != DBNull.Value ? reader["tip_seri"].ToString()! : "",
                    CorrelativoFactura = Convert.ToInt32(reader["num_fact"]),
                    CorrelativoBoleta = Convert.ToInt32(reader["num_bole"]),
                    CorrelativoRecibo = Convert.ToInt32(reader["num_reci"]),
                    FechaRegistro = reader["fec_regi"] != DBNull.Value ? Convert.ToDateTime(reader["fec_regi"]) : DateTime.Now,
                    CodigoUsuario = reader["cod_usua"] != DBNull.Value ? reader["cod_usua"].ToString() : "SYS",
                    EstadoId = Convert.ToInt32(reader["est_regi"])
                });
            }
            return lista;
        }

        public async Task InsertarSerieAsync(SerieDocumento serie)
        {
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
            string query = $@"
                INSERT INTO series_documentos 
                (ubicacion_id, empresa_id, num_seri, tip_seri, num_fact, num_bole, num_reci, fec_regi, cod_usua, est_regi)
                VALUES 
                (@UbicacionId, @EmpresaId, @NumSeri, @TipSeri, @NumFact, @NumBole, @NumReci, {nowFunc}, @CodUsua, @EstRegi)";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@UbicacionId", serie.UbicacionId > 0 ? serie.UbicacionId : (object)DBNull.Value);
            AgregarParametro(cmd, "@EmpresaId", serie.EmpresaId);
            AgregarParametro(cmd, "@NumSeri", serie.NumeroSerie.Trim().ToUpper());
            AgregarParametro(cmd, "@TipSeri", string.IsNullOrWhiteSpace(serie.TipoSerie) ? "01" : serie.TipoSerie.Trim());
            AgregarParametro(cmd, "@NumFact", serie.CorrelativoFactura);
            AgregarParametro(cmd, "@NumBole", serie.CorrelativoBoleta);
            AgregarParametro(cmd, "@NumReci", serie.CorrelativoRecibo);
            AgregarParametro(cmd, "@CodUsua", serie.CodigoUsuario ?? "SYS");
            AgregarParametro(cmd, "@EstRegi", serie.EstadoId == 0 ? 1 : serie.EstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ActualizarSerieAsync(SerieDocumento serie)
        {
            string query = @"
                UPDATE series_documentos 
                SET empresa_id = @EmpresaId,
                    num_seri = @NumSeri, 
                    tip_seri = @TipSeri, 
                    num_fact = @NumFact, 
                    num_bole = @NumBole, 
                    num_reci = @NumReci
                WHERE id = @Id";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@Id", serie.Id);
            AgregarParametro(cmd, "@EmpresaId", serie.EmpresaId);
            AgregarParametro(cmd, "@NumSeri", serie.NumeroSerie.Trim().ToUpper());
            AgregarParametro(cmd, "@TipSeri", string.IsNullOrWhiteSpace(serie.TipoSerie) ? "01" : serie.TipoSerie.Trim());
            AgregarParametro(cmd, "@NumFact", serie.CorrelativoFactura);
            AgregarParametro(cmd, "@NumBole", serie.CorrelativoBoleta);
            AgregarParametro(cmd, "@NumReci", serie.CorrelativoRecibo);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EliminarSerieAsync(int serieId)
        {
            string query = "DELETE FROM series_documentos WHERE id = @Id";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@Id", serieId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ActualizarCorrelativoAsync(int serieId, string tipoDocumento)
        {
            string campoUpdate = tipoDocumento switch
            {
                "01" => "num_fact = num_fact + 1",
                "02" or "03" => "num_bole = num_bole + 1",
                _ => "num_reci = num_reci + 1"
            };

            string query = $"UPDATE series_documentos SET {campoUpdate} WHERE id = @Id";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@Id", serieId);
            await cmd.ExecuteNonQueryAsync();
        }


        public async Task<List<SerieDocumento>> ObtenerTodasLasSeriesAsync()
        {
            var lista = new List<SerieDocumento>();
            string query = @"
        SELECT s.id, s.ubicacion_id, s.empresa_id, s.num_seri, s.tip_seri, 
               s.num_fact, s.num_bole, s.num_reci, s.fec_regi, s.cod_usua, s.est_regi,
               e.razon_social AS empresa_razon_social
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
                    UbicacionId = reader["ubicacion_id"] != DBNull.Value ? Convert.ToInt32(reader["ubicacion_id"]) : 0,
                    EmpresaId = reader["empresa_id"] != DBNull.Value ? Convert.ToInt32(reader["empresa_id"]) : null,
                    NumeroSerie = reader["num_seri"].ToString() ?? "",
                    TipoSerie = reader["tip_seri"] != DBNull.Value ? reader["tip_seri"].ToString()! : "",
                    CorrelativoFactura = Convert.ToInt32(reader["num_fact"]),
                    CorrelativoBoleta = Convert.ToInt32(reader["num_bole"]),
                    CorrelativoRecibo = Convert.ToInt32(reader["num_reci"]),
                    FechaRegistro = reader["fec_regi"] != DBNull.Value ? Convert.ToDateTime(reader["fec_regi"]) : DateTime.Now,
                    CodigoUsuario = reader["cod_usua"] != DBNull.Value ? reader["cod_usua"].ToString() : "SYS",
                    EstadoId = Convert.ToInt32(reader["est_regi"])
                };

                if (serie.EmpresaId.HasValue && reader["empresa_razon_social"] != DBNull.Value)
                {
                    serie.Empresa = new Empresa
                    {
                        Id = serie.EmpresaId.Value,
                        RazonSocial = reader["empresa_razon_social"].ToString()!
                    };
                }

                lista.Add(serie);
            }
            return lista;
        }
    }
}