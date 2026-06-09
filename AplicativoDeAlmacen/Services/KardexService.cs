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

        // =========================================================
        // KARDEX FÍSICO
        // =========================================================
        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta)
        {
            var reporte = new KardexFisicoReporte();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string queryDetalle = @"
        SELECT 
            m.fecha_movimiento,
            mp.descripcion AS motivo,
            mp.tipo_movimiento,
            m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia,
            pc.razon_social, u.descripcion AS ubicacion_desc,
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
                    decimal saldoAcumulado = 0;
                    decimal totalIngresos = 0;
                    decimal totalDevIngresos = 0;
                    decimal totalSalidas = 0;
                    decimal totalDevSalidas = 0;

                    while (await reader.ReadAsync())
                    {
                        var item = new KardexFisicoItem();
                        item.Fecha = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
                        item.Tipo = (reader.IsDBNull(2) ? "" : (reader.GetString(2) == "entrada" ? "I. " : "S. ")) + (reader.IsDBNull(1) ? "" : reader.GetString(1));
                        item.Registro = $"{(reader.IsDBNull(3) ? "" : reader.GetString(3))}-{(reader.IsDBNull(4) ? "" : reader.GetString(4))}";
                        item.Guia = $"{(reader.IsDBNull(5) ? "" : reader.GetString(5))}-{(reader.IsDBNull(6) ? "" : reader.GetString(6))}";
                        item.RazonSocialUbicacion = !reader.IsDBNull(7) ? reader.GetString(7) : (!reader.IsDBNull(8) ? reader.GetString(8) : "");

                        item.IngresoDevolucion = reader.GetDecimal(9);
                        item.IngresoNormal = reader.GetDecimal(10);
                        item.SalidaDevolucion = reader.GetDecimal(11);
                        item.SalidaNormal = reader.GetDecimal(12);

                        totalIngresos += item.IngresoNormal;
                        totalDevIngresos += item.IngresoDevolucion;
                        totalSalidas += item.SalidaNormal;
                        totalDevSalidas += item.SalidaDevolucion;

                        saldoAcumulado += (item.IngresoNormal + item.IngresoDevolucion) - (item.SalidaNormal + item.SalidaDevolucion);
                        item.SaldoFinal = saldoAcumulado;

                        reporte.Detalles.Add(item);
                    }

                    reporte.TotalIngresos = totalIngresos;
                    reporte.TotalDevIngresos = totalDevIngresos;
                    reporte.TotalSalidas = totalSalidas;
                    reporte.TotalDevSalidas = totalDevSalidas;
                    reporte.StockFinal = saldoAcumulado;
                }
            }
            return reporte;
        }

        // =========================================================
        // SALDOS DE PRODUCTOS (CTE)
        // =========================================================
        public async Task<List<SaldoProductoItem>> ObtenerSaldosYMovimientosAsync(DateTime fechaDesde, DateTime fechaHasta)
        {
            var lista = new List<SaldoProductoItem>();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            string query = @"
        WITH MovimientosRango AS (
            SELECT md.producto_id,
                   SUM(CASE WHEN m.fecha_movimiento < @FechaDesde THEN md.cantidad_ingreso - md.cantidad_salida ELSE 0 END) AS StockInicial,
                   SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta THEN md.cantidad_ingreso ELSE 0 END) AS TotalIngresos,
                   SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta THEN md.cantidad_salida ELSE 0 END) AS TotalSalidas
            FROM movimiento_detalles md
            INNER JOIN movimientos m ON md.movimiento_id = m.id
            GROUP BY md.producto_id
        )
        SELECT 
            ISNULL(p.abreviatura, CAST(p.id AS VARCHAR)) AS codigo,
            p.descripcion,
            ISNULL(mr.StockInicial, 0) AS StockInicial,
            ISNULL(mr.TotalIngresos, 0) AS TotalIngresos,
            ISNULL(mr.TotalSalidas, 0) AS TotalSalidas
        FROM productos p
        LEFT JOIN MovimientosRango mr ON p.id = mr.producto_id
        WHERE p.estado_id = 1
        ORDER BY p.descripcion";

            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@FechaDesde", fechaDesde.Date);
                cmd.Parameters.AddWithValue("@FechaHasta", fechaHasta.Date);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        lista.Add(new SaldoProductoItem
                        {
                            Codigo = reader["codigo"].ToString(),
                            Descripcion = reader["descripcion"].ToString(),
                            StockInicial = reader.GetDecimal(2),
                            TotalIngresos = reader.GetDecimal(3),
                            TotalSalidas = reader.GetDecimal(4)
                        });
                    }
                }
            }
            return lista;
        }

        // =========================================================
        // CONSULTA DE MOVIMIENTOS DETALLADOS (FLEXIBLE CON NULOS)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarMovimientosDetalladosAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta)
        {
            var reporte = new ConsultaMovimientoReporte();
            using var conn = _database.GetConnection();
            await conn.OpenAsync();

            // 1. Obtener la tabla izquierda (Movimientos)
            string queryMov = @"
        SELECT 
            m.fecha_movimiento,
            ISNULL(m.serie_documento, '') + '-' + ISNULL(m.numero_documento, '') AS registro,
            ISNULL(pc.razon_social, ISNULL(u.descripcion, 'SIN UBICACIÓN')) AS razon_ubicacion,
            ISNULL(m.serie_guia, '000') + '-' + ISNULL(m.numero_guia, '0000000') AS guia,
            ISNULL(md.cantidad_ingreso, 0) as cantidad_ingreso,
            ISNULL(md.cantidad_salida, 0) as cantidad_salida
        FROM movimiento_detalles md
        INNER JOIN movimientos m ON md.movimiento_id = m.id
        LEFT JOIN personas_comerciales pc ON m.persona_comercial_id = pc.id
        LEFT JOIN ubicaciones u ON m.ubicacion_id = u.id
        WHERE md.producto_id = @ProductoId 
          AND m.fecha_movimiento >= @FechaDesde 
          AND m.fecha_movimiento <= @FechaHasta
        ORDER BY m.fecha_movimiento ASC";

            using (var cmdMov = new SqlCommand(queryMov, conn))
            {
                cmdMov.Parameters.AddWithValue("@ProductoId", productoId);
                cmdMov.Parameters.AddWithValue("@FechaDesde", fechaDesde.Date);
                cmdMov.Parameters.AddWithValue("@FechaHasta", fechaHasta.Date);

                using (var reader = await cmdMov.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        reporte.Movimientos.Add(new ConsultaMovimientoItem
                        {
                            Fecha = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                            NumeroRegistro = reader.GetString(1),
                            RazonSocialUbicacion = reader.GetString(2),
                            NumeroGuia = reader.GetString(3),
                            Ingreso = reader.GetDecimal(4),
                            Salida = reader.GetDecimal(5)
                        });
                    }
                }
            }

            // 2. Obtener la tabla derecha (Códigos) - Evita crashear si no hay tabla de códigos
            try
            {
                string queryCod = @"
                SELECT 
                    ISNULL(c.codigo, 'N/A'),
                    ISNULL(tp.nombre, 'SIN TIPO') AS coleccion_tipo
                FROM codigos_producto c
                INNER JOIN productos p ON c.producto_id = p.id
                LEFT JOIN tipo_producto tp ON p.tipo_producto_id = tp.id
                WHERE c.producto_id = @ProductoId";

                using (var cmdCod = new SqlCommand(queryCod, conn))
                {
                    cmdCod.Parameters.AddWithValue("@ProductoId", productoId);
                    using (var reader = await cmdCod.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                Codigo = reader.GetString(0),
                                ColeccionTipo = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch
            {
                // Si la tabla codigos_producto no existe aún en BD o falla, ignora el error y devuelve la lista vacía.
            }

            return reporte;
        }
    }
}