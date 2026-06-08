using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class KardexService
    {
        private readonly DatabaseConnection _database;

        public KardexService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta)
        {
            var reporte = new KardexFisicoReporte();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            // Ya NO hacemos la consulta de Stock Inicial. Vamos directo a los movimientos.
            string queryDetalle = @"
        SELECT 
            m.fecha_movimiento,
            mp.descripcion AS motivo,
            mp.tipo_movimiento,
            m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia,
            pc.razon_social, u.descripcion AS ubicacion_desc,
            md.costo_unitario,
            CASE WHEN mp.tipo_movimiento = 'entrada' AND mp.descripcion LIKE '%DEVOLUCION%' THEN md.cantidad_ingreso ELSE 0 END AS IngresoDev,
            CASE WHEN mp.tipo_movimiento = 'entrada' AND mp.descripcion NOT LIKE '%DEVOLUCION%' THEN md.cantidad_ingreso ELSE 0 END AS IngresoNorm,
            CASE WHEN mp.tipo_movimiento = 'salida' AND mp.descripcion LIKE '%DEVOLUCION%' THEN md.cantidad_salida ELSE 0 END AS SalidaDev,
            CASE WHEN mp.tipo_movimiento = 'salida' AND mp.descripcion NOT LIKE '%DEVOLUCION%' THEN md.cantidad_salida ELSE 0 END AS SalidaNorm
        FROM movimiento_detalles md
        INNER JOIN movimientos m ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
        LEFT JOIN personas_comerciales pc ON m.persona_comercial_id = pc.id
        LEFT JOIN ubicaciones u ON m.ubicacion_id = u.id
        WHERE md.producto_id = @ProductoId 
          AND m.fecha_movimiento >= @FechaDesde 
          AND m.fecha_movimiento <= @FechaHasta
        ORDER BY m.fecha_movimiento ASC, m.id ASC";

            using (var cmdDetalle = new SqlCommand(queryDetalle, conn))
            {
                cmdDetalle.Parameters.AddWithValue("@ProductoId", productoId);
                cmdDetalle.Parameters.AddWithValue("@FechaDesde", fechaDesde.Date);
                cmdDetalle.Parameters.AddWithValue("@FechaHasta", fechaHasta.Date);

                using (var reader = await cmdDetalle.ExecuteReaderAsync())
                {
                    // Iniciamos en 0 porque "el inicial es el que se ingresa"
                    decimal saldoAcumulado = 0;
                    decimal totalIngresos = 0;
                    decimal totalDevIngresos = 0;
                    decimal totalSalidas = 0;
                    decimal totalDevSalidas = 0;

                    while (await reader.ReadAsync())
                    {
                        var item = new KardexFisicoItem();
                        item.Fecha = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
                        item.Tipo = (reader["tipo_movimiento"].ToString() == "entrada" ? "I. " : "S. ") + reader["motivo"].ToString();
                        item.Registro = $"{reader["serie_documento"]}-{reader["numero_documento"]}";
                        item.Guia = $"{reader["serie_guia"]}-{reader["numero_guia"]}";
                        item.RazonSocialUbicacion = reader["razon_social"]?.ToString() ?? reader["ubicacion_desc"]?.ToString() ?? "";
                        item.CostoUnitario = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9);

                        item.IngresoDevolucion = reader.GetDecimal(10);
                        item.IngresoNormal = reader.GetDecimal(11);
                        item.SalidaDevolucion = reader.GetDecimal(12);
                        item.SalidaNormal = reader.GetDecimal(13);

                        // Sumamos individualmente para el reporte final de la vista
                        totalIngresos += item.IngresoNormal;
                        totalDevIngresos += item.IngresoDevolucion;
                        totalSalidas += item.SalidaNormal;
                        totalDevSalidas += item.SalidaDevolucion;

                        // Saldo Final de la fila = (Saldo Acumulado + Todos los Ingresos) - (Todas las Salidas)
                        saldoAcumulado += (item.IngresoNormal + item.IngresoDevolucion) - (item.SalidaNormal + item.SalidaDevolucion);
                        item.SaldoFinal = saldoAcumulado;

                        reporte.Detalles.Add(item);
                    }

                    // Asignamos los cálculos separados al reporte
                    reporte.TotalIngresos = totalIngresos;
                    reporte.TotalDevIngresos = totalDevIngresos;
                    reporte.TotalSalidas = totalSalidas;
                    reporte.TotalDevSalidas = totalDevSalidas;
                    reporte.StockFinal = saldoAcumulado;
                }
            }

            return reporte;
        }
    }
}