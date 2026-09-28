#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Reportes;

namespace AplicativoDeAlmacen.Services.Reportes
{
    public class RegistroVentasService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public RegistroVentasService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<string>> ObtenerSeriesPorSedeAsync(int almacenId, string? tipoDoc = null)
        {
            var series = new List<string>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string sqlTipo = !string.IsNullOrWhiteSpace(tipoDoc) ? " AND tipo_documento = @TipoDoc " : "";
            string sql = $@"
                SELECT DISTINCT serie_documento 
                FROM facturacion_cabecera 
                WHERE estado_registro = 1 
                  AND (almacen_id = @AlmId OR punto_venta_id = @AlmId)
                  {sqlTipo}
                ORDER BY serie_documento ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);
            AgregarParametro(cmd, "@AlmId", almacenId);
            if (!string.IsNullOrWhiteSpace(tipoDoc))
                AgregarParametro(cmd, "@TipoDoc", tipoDoc);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                if (!rdr.IsDBNull(0))
                    series.Add(rdr.GetString(0));
            }
            return series;
        }

        public async Task<List<RegistroVentaItemDTO>> ConsultarRegistroVentasAsync(
    int almacenId,
    DateTime desde,
    DateTime hasta,
    string? tipoDoc = null,
    string? serie = null)
        {
            var lista = new List<RegistroVentaItemDTO>();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            string filtroTipo = !string.IsNullOrWhiteSpace(tipoDoc) ? " AND fc.tipo_documento = @TipoDoc " : "";
            string filtroSerie = !string.IsNullOrWhiteSpace(serie) ? " AND fc.serie_documento = @Serie " : "";

            // 🌟 SEPARACIÓN DE JOINS: Un join para comprador y otro para institucion
            // Así NO duplica filas y concatena limpiamente: "CLIENTES VARIOS - [ABEJITAS]"
            string sql = $@"
        SELECT 
            fc.fecha_emision,
            fc.tipo_documento,
            fc.serie_documento,
            fc.numero_documento,
            CONCAT(
                CASE 
                    WHEN fc.tipo_documento = '01' THEN 'FAC-'
                    WHEN fc.tipo_documento = '03' THEN 'REC-'
                    ELSE 'BOL-'
                END, 
                fc.serie_documento, '-', fc.numero_documento
            ) AS documento_completo,
            CASE 
                WHEN p_inst.id IS NOT NULL AND p_inst.id <> p_comp.id THEN
                    CONCAT(COALESCE(p_comp.razon_social, CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno), 'VARIOS'),
                           ' - [', 
                           COALESCE(p_inst.razon_social, CONCAT(p_inst.nombres, ' ', p_inst.apellido_paterno)), 
                           ']')
                ELSE
                    COALESCE(p_comp.razon_social, CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno),
                             p_inst.razon_social, CONCAT(p_inst.nombres, ' ', p_inst.apellido_paterno), 
                             'CLIENTES VARIOS')
            END AS cliente_razon_social,
            COALESCE(p_comp.ruc, p_comp.dni, p_inst.ruc, p_inst.dni, '') AS cliente_documento,
            fc.total_gravado,
            fc.total_exonerado,
            fc.total_inafecto,
            fc.total_igv,
            fc.importe_total
        FROM facturacion_cabecera fc {nolock}
        LEFT JOIN personas_comerciales p_comp {nolock} ON fc.comprador_id = p_comp.id
        LEFT JOIN personas_comerciales p_inst {nolock} ON fc.institucion_id = p_inst.id
        WHERE fc.estado_registro = 1
          AND (fc.almacen_id = @AlmId OR fc.punto_venta_id = @AlmId)
          AND fc.fecha_emision >= @Desde 
          AND fc.fecha_emision <= @Hasta
          {filtroTipo}
          {filtroSerie}
        ORDER BY fc.fecha_emision ASC, fc.id ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

            AgregarParametro(cmd, "@AlmId", almacenId);
            AgregarParametro(cmd, "@Desde", desde.Date);
            AgregarParametro(cmd, "@Hasta", hasta.Date.AddDays(1).AddTicks(-1));
            if (!string.IsNullOrWhiteSpace(tipoDoc)) AgregarParametro(cmd, "@TipoDoc", tipoDoc);
            if (!string.IsNullOrWhiteSpace(serie)) AgregarParametro(cmd, "@Serie", serie);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new RegistroVentaItemDTO
                {
                    FechaEmision = Convert.ToDateTime(rdr["fecha_emision"]),
                    TipoDoc = rdr["tipo_documento"]?.ToString() ?? "",
                    Serie = rdr["serie_documento"]?.ToString() ?? "",
                    Numero = rdr["numero_documento"]?.ToString() ?? "",
                    Documento = rdr["documento_completo"]?.ToString() ?? "",
                    Cliente = rdr["cliente_razon_social"]?.ToString() ?? "CLIENTES VARIOS",
                    RucDni = rdr["cliente_documento"]?.ToString() ?? "",
                    TotalGravado = Convert.ToDecimal(rdr["total_gravado"]),
                    TotalExonerado = Convert.ToDecimal(rdr["total_exonerado"]),
                    TotalInafecto = Convert.ToDecimal(rdr["total_inafecto"]),
                    TotalIgv = Convert.ToDecimal(rdr["total_igv"]),
                    ImporteTotal = Convert.ToDecimal(rdr["importe_total"])
                });
            }

            return lista;
        }
    }
}