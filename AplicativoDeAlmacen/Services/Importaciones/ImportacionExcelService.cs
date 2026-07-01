using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services.Importaciones
{
    public class ImportacionExcelService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public ImportacionExcelService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        public async Task<List<string>> LeerCodigosDesdeExcelAsync(string rutaArchivo)
        {
            var codigos = new List<string>();

            await Task.Run(() =>
            {
                using var stream = new System.IO.FileStream(
                    rutaArchivo,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite);

                using var wb = new XLWorkbook(stream);

                var ws = wb.Worksheet(1);

                foreach (var row in ws.RowsUsed())
                {
                    foreach (var cell in row.CellsUsed())
                    {
                        string codigo = cell.GetString().Trim();

                        if (!string.IsNullOrWhiteSpace(codigo))
                            codigos.Add(codigo);
                    }
                }
            });

            return codigos.Distinct().ToList();
        }

        public async Task<List<string>> ObtenerCodigosDuplicadosAsync(List<string> codigosExcel)
        {
            var duplicados = new List<string>();

            if (!codigosExcel.Any())
                return duplicados;

            using var conn = _database.GetConnection();

            var dbConn = (DbConnection)conn;

            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            cmd.CommandText = @"
SELECT COUNT(*)
FROM codigos_creados
WHERE codigo=@codigo";

            var p = cmd.CreateParameter();
            p.ParameterName = "@codigo";
            cmd.Parameters.Add(p);

            foreach (var codigo in codigosExcel)
            {
                p.Value = codigo;

                int existe = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (existe > 0)
                    duplicados.Add(codigo);
            }

            return duplicados;
        }

        public async Task GuardarCodigosImportadosTransactionAsync(
            int coleccionId,
            int productoId,
            int categoriaId,
            List<string> codigosValidos)
        {
            if (!codigosValidos.Any())
                throw new Exception("No hay códigos para guardar.");

            using var conn = _database.GetConnection();

            var dbConn = (DbConnection)conn;

            await dbConn.OpenAsync();

            using var trans = dbConn.BeginTransaction();

            try
            {
                int loteId;

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.Transaction = trans;

                    cmd.CommandText = @"

                        INSERT INTO registro_codigos
                        (
                        coleccion_id,
                        producto_id,
                        categoria_producto_id,
                        cantidad,
                        desde,
                        hasta
                        )

                        OUTPUT INSERTED.id

                        VALUES
                        (
                        @coleccion,
                        @producto,
                        @categoria,
                        @cantidad,
                        @desde,
                        @hasta
                        )";

                    AgregarParametro(cmd, "@coleccion", coleccionId);
                    AgregarParametro(cmd, "@producto", productoId);
                    AgregarParametro(cmd, "@categoria", categoriaId);
                    AgregarParametro(cmd, "@cantidad", codigosValidos.Count);
                    AgregarParametro(cmd, "@desde", codigosValidos.First());
                    AgregarParametro(cmd, "@hasta", codigosValidos.Last());

                    loteId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.Transaction = trans;

                    cmd.CommandText = @"

INSERT INTO codigos_creados
(
registro_codigo_id,
codigo,
estado_id,
es_manual
)

VALUES
(
@registro,
@codigo,
1,
0
)";

                    var pRegistro = cmd.CreateParameter();
                    pRegistro.ParameterName = "@registro";
                    cmd.Parameters.Add(pRegistro);

                    var pCodigo = cmd.CreateParameter();
                    pCodigo.ParameterName = "@codigo";
                    cmd.Parameters.Add(pCodigo);

                    foreach (var codigo in codigosValidos)
                    {
                        pRegistro.Value = loteId;
                        pCodigo.Value = codigo;

                        await cmd.ExecuteNonQueryAsync();
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

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();

            p.ParameterName = nombre;

            p.Value = valor ?? DBNull.Value;

            cmd.Parameters.Add(p);
        }
    }
}