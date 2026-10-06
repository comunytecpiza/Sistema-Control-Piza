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
    DateTime? desde = null,
    DateTime? hasta = null,
    string? tipoDoc = null,
    string? serie = null)
        {
            var lista = new List<RegistroVentaItemDTO>();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            string filtroFecha = (desde.HasValue && hasta.HasValue)
                ? " AND fc.fecha_emision >= @Desde AND fc.fecha_emision <= @Hasta "
                : "";
            string filtroTipo = !string.IsNullOrWhiteSpace(tipoDoc) ? " AND fc.tipo_documento = @TipoDoc " : "";
            string filtroSerie = !string.IsNullOrWhiteSpace(serie) ? " AND fc.serie_documento = @Serie " : "";

            string sql = $@"
        SELECT 
            fc.fecha_emision,
            fc.fecha_registro,
            fc.updated_at,
            fc.tipo_documento,
            fc.serie_documento,
            fc.numero_documento,
            CONCAT(
                COALESCE(NULLIF(TRIM(d.abreviatura), ''), d.des_docu, 'DOC'),
                '-', 
                fc.serie_documento, '-', fc.numero_documento
            ) AS documento_completo,
            CASE 
                WHEN p_inst.id IS NOT NULL AND p_comp.id IS NOT NULL AND p_inst.id <> p_comp.id THEN
                    CONCAT(
                        COALESCE(NULLIF(TRIM(p_comp.razon_social), ''), NULLIF(TRIM(CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno)), ''), 'CLIENTES VARIOS'),
                        ' - [',
                        COALESCE(NULLIF(TRIM(p_inst.razon_social), ''), NULLIF(TRIM(CONCAT(p_inst.nombres, ' ', p_inst.apellido_paterno)), ''), 'COLEGIO'),
                        ']'
                    )
                WHEN p_inst.id IS NOT NULL AND (p_comp.id IS NULL OR p_comp.id = p_inst.id) THEN
                    CONCAT(
                        'CLIENTES VARIOS - [',
                        COALESCE(NULLIF(TRIM(p_inst.razon_social), ''), NULLIF(TRIM(CONCAT(p_inst.nombres, ' ', p_inst.apellido_paterno)), ''), 'COLEGIO'),
                        ']'
                    )
                WHEN p_comp.id IS NOT NULL THEN
                    COALESCE(NULLIF(TRIM(p_comp.razon_social), ''), NULLIF(TRIM(CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno)), ''), 'CLIENTES VARIOS')
                ELSE 'CLIENTES VARIOS'
            END AS cliente_razon_social,
            COALESCE(p_comp.ruc, p_comp.dni, p_inst.ruc, p_inst.dni, '') AS cliente_documento,
            fc.total_gravado,
            fc.total_exonerado,
            fc.total_inafecto,
            fc.total_igv,
            COALESCE(fc.monto_delivery, 0.00) AS monto_delivery,
            fc.importe_total,
            COALESCE(u_crea.nombres, 'SISTEMA') AS usuario_creador,
            COALESCE(u_edit.nombres, '-') AS usuario_editor
        FROM facturacion_cabecera fc {nolock}
        LEFT JOIN documentos d {nolock} ON fc.tipo_documento = d.cod_docu
        LEFT JOIN personas_comerciales p_comp {nolock} ON fc.comprador_id = p_comp.id
        LEFT JOIN personas_comerciales p_inst {nolock} ON fc.institucion_id = p_inst.id
        LEFT JOIN usuarios u_crea {nolock} ON fc.usuario_id = u_crea.id
        LEFT JOIN usuarios u_edit {nolock} ON fc.usuario_update_id = u_edit.id
        WHERE fc.estado_registro = 1
          AND (fc.almacen_id = @AlmId OR fc.punto_venta_id = @AlmId)
          {filtroFecha}
          {filtroTipo}
          {filtroSerie}
        ORDER BY fc.fecha_emision DESC, fc.id DESC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

            AgregarParametro(cmd, "@AlmId", almacenId);
            if (desde.HasValue && hasta.HasValue)
            {
                AgregarParametro(cmd, "@Desde", desde.Value.Date);
                AgregarParametro(cmd, "@Hasta", hasta.Value.Date.AddDays(1).AddTicks(-1));
            }
            if (!string.IsNullOrWhiteSpace(tipoDoc)) AgregarParametro(cmd, "@TipoDoc", tipoDoc);
            if (!string.IsNullOrWhiteSpace(serie)) AgregarParametro(cmd, "@Serie", serie);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new RegistroVentaItemDTO
                {
                    FechaEmision = Convert.ToDateTime(rdr["fecha_emision"]),
                    FechaRegistro = Convert.ToDateTime(rdr["fecha_registro"]),
                    FechaModificacion = rdr.IsDBNull(rdr.GetOrdinal("updated_at")) ? null : Convert.ToDateTime(rdr["updated_at"]),
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
                    MontoDelivery = Convert.ToDecimal(rdr["monto_delivery"]),
                    ImporteTotal = Convert.ToDecimal(rdr["importe_total"]),
                    UsuarioCreador = rdr["usuario_creador"]?.ToString() ?? "SISTEMA",
                    UsuarioEditor = rdr["usuario_editor"]?.ToString() ?? "-"
                });
            }

            return lista;
        }
    }
}