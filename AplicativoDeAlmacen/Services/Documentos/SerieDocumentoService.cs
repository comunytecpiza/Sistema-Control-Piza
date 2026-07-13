using System;
using System.Collections.Generic;
using System.Data;
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

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =======================================================
        // CATÁLOGO DE DOCUMENTOS (Factura, Boleta, etc.)
        // =======================================================
        public async Task<List<Documento>> ObtenerDocumentosActivosAsync()
        {
            var lista = new List<Documento>();
            string query = "SELECT cod_docu, des_docu FROM Documentos ORDER BY cod_docu";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Documento
                            {
                                Codigo = reader["cod_docu"].ToString(),
                                Descripcion = reader["des_docu"].ToString()
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // =======================================================
        // OPERACIONES DE SERIES POR UBICACIÓN
        // =======================================================

        public async Task<List<SerieDocumento>> ObtenerSeriesPorUbicacionAsync(int ubicacionId)
        {
            var lista = new List<SerieDocumento>();
            string query = @"
                SELECT id, ubicacion_id, num_seri, tip_seri, 
                       num_fact, num_bole, num_reci, fec_regi, cod_usua, est_regi
                FROM series_documentos
                WHERE ubicacion_id = @UbicacionId AND est_regi = 1
                ORDER BY num_seri";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@UbicacionId", ubicacionId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new SerieDocumento
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                UbicacionId = Convert.ToInt32(reader["ubicacion_id"]),
                                NumeroSerie = reader["num_seri"].ToString(),
                                TipoSerie = reader["tip_seri"].ToString(),
                                CorrelativoFactura = Convert.ToInt32(reader["num_fact"]),
                                CorrelativoBoleta = Convert.ToInt32(reader["num_bole"]),
                                CorrelativoRecibo = Convert.ToInt32(reader["num_reci"]),
                                FechaRegistro = Convert.ToDateTime(reader["fec_regi"]),
                                CodigoUsuario = reader["cod_usua"].ToString(),
                                EstadoId = Convert.ToInt32(reader["est_regi"])
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task InsertarSerieAsync(SerieDocumento serie)
        {
            string query = @"
                INSERT INTO series_documentos 
                (ubicacion_id, num_seri, tip_seri, num_fact, num_bole, num_reci, fec_regi, cod_usua, est_regi)
                VALUES 
                (@UbicacionId, @NumSeri, @TipSeri, @NumFact, @NumBole, @NumReci, GETDATE(), @CodUsua, @EstRegi)";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@UbicacionId", serie.UbicacionId);
                    AgregarParametro(cmd, "@NumSeri", serie.NumeroSerie);
                    AgregarParametro(cmd, "@TipSeri", serie.TipoSerie);
                    AgregarParametro(cmd, "@NumFact", serie.CorrelativoFactura);
                    AgregarParametro(cmd, "@NumBole", serie.CorrelativoBoleta);
                    AgregarParametro(cmd, "@NumReci", serie.CorrelativoRecibo);
                    AgregarParametro(cmd, "@CodUsua", serie.CodigoUsuario ?? "SYS");
                    AgregarParametro(cmd, "@EstRegi", serie.EstadoId == 0 ? 1 : serie.EstadoId);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // 🌟 MÉTODO FALTANTE AÑADIDO 🌟
        public async Task ActualizarSerieAsync(SerieDocumento serie)
        {
            string query = @"
                UPDATE series_documentos 
                SET num_seri = @NumSeri, tip_seri = @TipSeri, 
                    num_fact = @NumFact, num_bole = @NumBole, num_reci = @NumReci
                WHERE id = @Id";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@Id", serie.Id);
                    AgregarParametro(cmd, "@NumSeri", serie.NumeroSerie);
                    AgregarParametro(cmd, "@TipSeri", serie.TipoSerie);
                    AgregarParametro(cmd, "@NumFact", serie.CorrelativoFactura);
                    AgregarParametro(cmd, "@NumBole", serie.CorrelativoBoleta);
                    AgregarParametro(cmd, "@NumReci", serie.CorrelativoRecibo);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // 🌟 MÉTODO FALTANTE AÑADIDO 🌟
        public async Task EliminarSerieAsync(int serieId)
        {
            string query = "DELETE FROM series_documentos WHERE id = @Id";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@Id", serieId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task ActualizarCorrelativoAsync(int serieId, string tipoDocumento)
        {
            string campoUpdate = tipoDocumento == "01" ? "num_fact = num_fact + 1" :
                                 tipoDocumento == "02" ? "num_bole = num_bole + 1" :
                                 tipoDocumento == "03" ? "num_reci = num_reci + 1" : "";

            if (string.IsNullOrEmpty(campoUpdate)) return;

            string query = $"UPDATE series_documentos SET {campoUpdate} WHERE id = @Id";

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@Id", serieId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}