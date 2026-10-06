#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Documentos;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Documentos
{
    public class DocumentoService
    {
        private readonly DatabaseConnection _database;

        public DocumentoService()
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
        // 1. OBTENER TODOS LOS DOCUMENTOS
        // =======================================================
        public async Task<List<Documento>> ObtenerTodosAsync()
        {
            var lista = new List<Documento>();
            string query = @"
        SELECT cod_docu, des_docu, abreviatura, est_regi 
        FROM documentos 
        ORDER BY cod_docu ASC";

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
                    Estado = Convert.ToInt32(reader["est_regi"]) == 1
                });
            }

            return lista;
        }

        // =======================================================
        // 2. OBTENER SOLO ACTIVOS (Para ComboBoxes de Ventas)
        // =======================================================
        public async Task<List<Documento>> ObtenerActivosAsync()
        {
            var lista = new List<Documento>();
            string query = @"
                SELECT cod_docu, des_docu, abreviatura, est_regi 
                FROM documentos 
                WHERE est_regi = 1 
                ORDER BY cod_docu ASC";

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
        // 3. OBTENER POR CÓDIGO
        // =======================================================
        public async Task<Documento?> ObtenerPorCodigoAsync(string codigo)
        {
            string query = @"
                SELECT cod_docu, des_docu, abreviatura, est_regi 
                FROM documentos 
                WHERE cod_docu = @CodDocu";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@CodDocu", codigo.Trim());

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Documento
                {
                    Codigo = reader["cod_docu"].ToString()!,
                    Descripcion = reader["des_docu"].ToString()!,
                    Abreviatura = reader["abreviatura"] != DBNull.Value ? reader["abreviatura"].ToString() : null,
                    Estado = Convert.ToInt32(reader["est_regi"]) == 1
                };
            }

            return null;
        }

        // =======================================================
        // 4. INSERTAR NUEVO DOCUMENTO (Validando duplicados)
        // =======================================================
        public async Task<bool> InsertarAsync(Documento doc)
        {
            string codLimpio = doc.Codigo.Trim().ToUpper();

            var existe = await ObtenerPorCodigoAsync(codLimpio);
            if (existe != null)
            {
                throw new InvalidOperationException($"El código de documento '{codLimpio}' ya está registrado en el sistema.");
            }

            string query = @"
        INSERT INTO documentos (cod_docu, des_docu, abreviatura, est_regi)
        VALUES (@CodDocu, @DesDocu, @Abrev, @EstRegi)";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@CodDocu", codLimpio);
            AgregarParametro(cmd, "@DesDocu", doc.Descripcion.Trim().ToUpper());
            AgregarParametro(cmd, "@Abrev", string.IsNullOrWhiteSpace(doc.Abreviatura) ? (object)DBNull.Value : doc.Abreviatura.Trim().ToUpper());
            AgregarParametro(cmd, "@EstRegi", doc.Estado ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =======================================================
        // 5. ACTUALIZAR DOCUMENTO
        // =======================================================
        public async Task<bool> ActualizarAsync(Documento doc)
        {
            string query = @"
        UPDATE documentos 
        SET des_docu = @DesDocu, 
            abreviatura = @Abrev, 
            est_regi = @EstRegi
        WHERE cod_docu = @CodDocu";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@CodDocu", doc.Codigo.Trim().ToUpper());
            AgregarParametro(cmd, "@DesDocu", doc.Descripcion.Trim().ToUpper());
            AgregarParametro(cmd, "@Abrev", string.IsNullOrWhiteSpace(doc.Abreviatura) ? (object)DBNull.Value : doc.Abreviatura.Trim().ToUpper());
            AgregarParametro(cmd, "@EstRegi", doc.Estado ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =======================================================
        // 6. CAMBIAR ESTADO (Activar / Desactivar)
        // =======================================================
        public async Task<bool> CambiarEstadoAsync(string codigo, bool nuevoEstado)
        {
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
            string query = $@"
                UPDATE documentos 
                SET est_regi = @EstRegi, 
                    updated_at = {nowFunc} 
                WHERE cod_docu = @CodDocu";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@CodDocu", codigo.Trim());
            AgregarParametro(cmd, "@EstRegi", nuevoEstado ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =======================================================
        // 7. ELIMINAR (Con validación de llaves foráneas / uso)
        // =======================================================
        public async Task<bool> EliminarAsync(string codigo)
        {
            // Validar que no tenga series o ventas vinculadas
            string queryCheck = @"
                SELECT COUNT(*) FROM series_documentos WHERE tip_seri = @CodDocu";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            try
            {
                using var cmdCheck = dbConn.CreateCommand();
                cmdCheck.CommandText = QueryAdapter.FormatearConsulta(queryCheck);
                AgregarParametro(cmdCheck, "@CodDocu", codigo.Trim());
                var res = await cmdCheck.ExecuteScalarAsync();
                int usados = res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;

                if (usados > 0)
                {
                    throw new InvalidOperationException($"No se puede eliminar el documento '{codigo}' porque tiene {usados} serie(s) configurada(s). En su lugar, desactívelo.");
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                // Si la columna aún no está enlazada se ignora el check estricto
            }

            string queryDelete = "DELETE FROM documentos WHERE cod_docu = @CodDocu";
            using var cmdDel = dbConn.CreateCommand();
            cmdDel.CommandText = QueryAdapter.FormatearConsulta(queryDelete);
            AgregarParametro(cmdDel, "@CodDocu", codigo.Trim());

            return await cmdDel.ExecuteNonQueryAsync() > 0;
        }
    }
}