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
    public class ReporteVentasClienteService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public ReporteVentasClienteService()
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



        public async Task<List<ReporteVentaProductoResumenDTO>> ObtenerResumenProductosVendidosAsync(
            int personaId, DateTime desde, DateTime hasta, int almacenId)
        {
            var lista = new List<ReporteVentaProductoResumenDTO>();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string castInt = QueryAdapter.EsMySQL ? "LPAD(p.id, 5, '0')" : "RIGHT('00000' + CAST(p.id AS VARCHAR), 5)";

            // 🌟 Sin dependencias de tablas externas para la unidad de medida
            string sql = $@"
                SELECT 
                    p.id AS producto_id,
                    COALESCE(NULLIF(p.abreviatura, ''), {castInt}) AS codigo,
                    p.descripcion,
                    'PACKS' AS unidad_medida,
                    SUM(fd.cantidad) AS cantidad_total,
                    SUM(fd.importe_total) AS importe_total
                FROM facturacion_detalle fd {nolock}
                INNER JOIN facturacion_cabecera fc {nolock} ON fd.facturacion_cabecera_id = fc.id
                INNER JOIN productos p {nolock} ON fd.producto_id = p.id
                WHERE fc.estado_registro = 1
                  AND (fc.almacen_id = @AlmId OR fc.punto_venta_id = @AlmId)
                  AND (fc.comprador_id = @PersonaId OR fc.institucion_id = @PersonaId)
                  AND fc.fecha_emision >= @Desde 
                  AND fc.fecha_emision <= @Hasta
                GROUP BY p.id, p.abreviatura, p.descripcion
                ORDER BY p.descripcion ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

            AgregarParametro(cmd, "@AlmId", almacenId);
            AgregarParametro(cmd, "@PersonaId", personaId);
            AgregarParametro(cmd, "@Desde", desde.Date);
            AgregarParametro(cmd, "@Hasta", hasta.Date.AddDays(1).AddTicks(-1));

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new ReporteVentaProductoResumenDTO
                {
                    ProductoId = Convert.ToInt32(rdr["producto_id"]),
                    Codigo = rdr["codigo"]?.ToString() ?? "",
                    Descripcion = rdr["descripcion"]?.ToString() ?? "",
                    UnidadMedida = rdr["unidad_medida"]?.ToString() ?? "PACKS",
                    Cantidad = Convert.ToDecimal(rdr["cantidad_total"]),
                    Importe = Convert.ToDecimal(rdr["importe_total"])
                });
            }

            return lista;
        }

        public async Task<List<ReporteVentaCodigoDetalleDTO>> ObtenerDetalleCodigosProductoAsync(
            int personaId, int productoId, DateTime desde, DateTime hasta, int almacenId)
        {
            var lista = new List<ReporteVentaCodigoDetalleDTO>();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            // 🌟 Dinámico: Cruza con la tabla 'documentos' para obtener la abreviatura oficial (FAC, BOL, REC, etc.)
            string sql = $@"
                SELECT 
                    COALESCE(cc.codigo, 'VENTA MANUAL / SIN CÓDIGO') AS codigo_libro,
                    CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), 
                           CASE WHEN rc.categoria_producto_id = 1 THEN 'LIBROS GUÍA' ELSE 'LIBROS VENTA' END) AS coleccion_tipo,
                    fc.fecha_emision,
                    CONCAT(
                        COALESCE(NULLIF(TRIM(d.abreviatura), ''), d.des_docu, 'DOC'),
                        '-',
                        fc.serie_documento, '-', fc.numero_documento
                    ) AS documento_completo,
                    fd.precio_unitario AS importe_unitario
                FROM facturacion_detalle fd {nolock}
                INNER JOIN facturacion_cabecera fc {nolock} ON fd.facturacion_cabecera_id = fc.id
                LEFT JOIN documentos d {nolock} ON fc.tipo_documento = d.cod_docu
                LEFT JOIN facturacion_detalle_codigos fdc {nolock} ON fdc.facturacion_detalle_id = fd.id
                LEFT JOIN codigos_creados cc {nolock} ON fdc.codigo_creado_id = cc.id
                LEFT JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
                LEFT JOIN colecciones c {nolock} ON rc.coleccion_id = c.id
                WHERE fc.estado_registro = 1
                  AND (fc.almacen_id = @AlmId OR fc.punto_venta_id = @AlmId)
                  AND fd.producto_id = @ProductoId
                  AND (fc.comprador_id = @PersonaId OR fc.institucion_id = @PersonaId)
                  AND fc.fecha_emision >= @Desde 
                  AND fc.fecha_emision <= @Hasta
                ORDER BY fc.fecha_emision ASC;";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

            AgregarParametro(cmd, "@AlmId", almacenId);
            AgregarParametro(cmd, "@PersonaId", personaId);
            AgregarParametro(cmd, "@ProductoId", productoId);
            AgregarParametro(cmd, "@Desde", desde.Date);
            AgregarParametro(cmd, "@Hasta", hasta.Date.AddDays(1).AddTicks(-1));

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new ReporteVentaCodigoDetalleDTO
                {
                    ProductoId = productoId,
                    Cantidad = 1,
                    Codigo = rdr["codigo_libro"]?.ToString() ?? "VENTA MANUAL",
                    ColeccionTipo = rdr.IsDBNull(rdr.GetOrdinal("coleccion_tipo")) ? "LIBROS VENTA" : rdr["coleccion_tipo"].ToString()!,
                    Fecha = Convert.ToDateTime(rdr["fecha_emision"]),
                    Documento = rdr["documento_completo"]?.ToString() ?? "",
                    Importe = Convert.ToDecimal(rdr["importe_unitario"])
                });
            }

            return lista;
        }
    }
}