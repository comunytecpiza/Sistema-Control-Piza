using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    /// <summary>
    /// KardexService Agnóstico y Multi-Motor.
    /// Traduce dinámicamente queries complejas (WITH, ISNULL, COALESCE) 
    /// para funcionar de forma transparente tanto en SQL Server local como en MySQL/MariaDB (cPanel).
    /// </summary>
    public class KardexService
    {
        private readonly DatabaseConnection _database;

        public KardexService()
        {
            _database = new DatabaseConnection();
        }

        // =========================================================
        // HELPER INTERNO — crea y agrega un parámetro genérico
        // =========================================================
        private static void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =========================================================
        // KARDEX FÍSICO (Refacturado Asíncrono y Multi-Motor)
        // =========================================================
        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(
            int productoId, DateTime fechaDesde, DateTime fechaHasta)
        {
            var reporte = new KardexFisicoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
                        SELECT
                            m.fecha_movimiento,
                            mp.descripcion          AS motivo,
                            mp.tipo_movimiento,
                            m.serie_documento,
                            m.numero_documento,
                            m.serie_guia,
                            m.numero_guia,
                            pc.razon_social,
                            u.descripcion           AS ubicacion_desc,
                            CASE WHEN mp.tipo_movimiento = 'entrada' AND mp.descripcion LIKE '%DEVOLUCION%' THEN md.cantidad_ingreso ELSE 0 END AS IngresoDev,
                            CASE WHEN mp.tipo_movimiento = 'entrada' AND mp.descripcion NOT LIKE '%DEVOLUCION%' THEN md.cantidad_ingreso ELSE 0 END AS IngresoNorm,
                            CASE WHEN mp.tipo_movimiento = 'salida' AND mp.descripcion LIKE '%DEVOLUCION%' THEN md.cantidad_salida ELSE 0 END  AS SalidaDev,
                            CASE WHEN mp.tipo_movimiento = 'salida' AND mp.descripcion NOT LIKE '%DEVOLUCION%' THEN md.cantidad_salida ELSE 0 END  AS SalidaNorm
                        FROM movimiento_detalles md
                        INNER JOIN movimientos       m  ON md.movimiento_id      = m.id
                        INNER JOIN motivo_productos  mp ON m.motivo_producto_id  = mp.id
                        LEFT  JOIN personas_comerciales pc ON m.persona_comercial_id = pc.id
                        LEFT  JOIN ubicaciones       u  ON m.ubicacion_id        = u.id
                        WHERE md.producto_id       = @ProductoId
                          AND m.fecha_movimiento  >= @FechaDesde
                          AND m.fecha_movimiento  <= @FechaHasta
                        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    // CORRECCIÓN PROTECCIÓN: Formateamos la consulta dinámicamente
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);

                    // CORRECCIÓN ASINCRONÍA: Cambiado a ExecuteReaderAsync para optimizar WPF
                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoAcumulado = 0;
                        decimal totalIngresos = 0;
                        decimal totalDevIngresos = 0;
                        decimal totalSalidas = 0;
                        decimal totalDevSalidas = 0;

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            var item = new KardexFisicoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = (reader.IsDBNull(2) ? "" : (reader.GetString(2) == "entrada" ? "I. " : "S. ")) + (reader.IsDBNull(1) ? "" : reader.GetString(1)),
                                Registro = $"{(reader.IsDBNull(3) ? "" : reader.GetString(3))}-{(reader.IsDBNull(4) ? "" : reader.GetString(4))}",
                                Guia = $"{(reader.IsDBNull(5) ? "" : reader.GetString(5))}-{(reader.IsDBNull(6) ? "" : reader.GetString(6))}",
                                RazonSocialUbicacion = !reader.IsDBNull(7) ? reader.GetString(7) : (!reader.IsDBNull(8) ? reader.GetString(8) : ""),
                                IngresoDevolucion = reader.GetDecimal(9),
                                IngresoNormal = reader.GetDecimal(10),
                                SalidaDevolucion = reader.GetDecimal(11),
                                SalidaNormal = reader.GetDecimal(12)
                            };

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
            }

            return reporte;
        }

        // =========================================================
        // SALDOS DE PRODUCTOS  (Soportado en SQL Server y MySQL 8.0+)
        // =========================================================
        public async Task<List<SaldoProductoItem>> ObtenerSaldosYMovimientosAsync(
            DateTime fechaDesde, DateTime fechaHasta)
        {
            var lista = new List<SaldoProductoItem>();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
                        WITH MovimientosRango AS (
                            SELECT
                                md.producto_id,
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
                            ISNULL(mr.StockInicial,   0) AS StockInicial,
                            ISNULL(mr.TotalIngresos,  0) AS TotalIngresos,
                            ISNULL(mr.TotalSalidas,   0) AS TotalSalidas
                        FROM productos p
                        LEFT JOIN MovimientosRango mr ON p.id = mr.producto_id
                        WHERE p.estado_id = 1
                        ORDER BY p.descripcion";

                    // CORRECCIÓN PROTECCIÓN: Traduce dinámicamente ISNULL a IFNULL y VARCHAR a CHAR para MySQL
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
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
            }

            return lista;
        }

        // =========================================================
        // CONSULTA DE MOVIMIENTOS DETALLADOS (Esquema Corregido)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarMovimientosDetalladosAsync(
            int productoId, DateTime fechaDesde, DateTime fechaHasta)
        {
            var reporte = new ConsultaMovimientoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                // 1. TABLA IZQUIERDA — Movimientos del producto en el rango
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
                        SELECT
                            m.fecha_movimiento,
                            COALESCE(m.serie_documento,  '') + '-' + COALESCE(m.numero_documento, '')             AS registro,
                            COALESCE(pc.razon_social, COALESCE(u.descripcion, 'SIN UBICACIÓN')) AS razon_ubicacion,
                            COALESCE(m.serie_guia,    '000') + '-' + COALESCE(m.numero_guia,   '0000000')         AS guia,
                            COALESCE(md.cantidad_ingreso, 0)                                             AS cantidad_ingreso,
                            COALESCE(md.cantidad_salida,  0)                                             AS cantidad_salida
                        FROM movimiento_detalles md
                        INNER JOIN movimientos          m  ON md.movimiento_id       = m.id
                        LEFT  JOIN personas_comerciales pc ON m.persona_comercial_id = pc.id
                        LEFT  JOIN ubicaciones          u  ON m.ubicacion_id         = u.id
                        WHERE md.producto_id      = @ProductoId
                          AND m.fecha_movimiento >= @FechaDesde
                          AND m.fecha_movimiento <= @FechaHasta
                        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
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

                // 2. TABLA DERECHA — Códigos físicos movilizados
                // Se eliminó el "catch" silencioso. El ORDER BY se corrigió al alias 'codigo'
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
                        SELECT DISTINCT
                            COALESCE(cc.codigo, 'N/A') AS codigo,
                            COALESCE(cat.nombre, 'SIN TIPO') AS coleccion_tipo, -- AQUÍ ESTÁ EL CAMBIO
                            COALESCE(m.serie_documento, '') + '-' + COALESCE(m.numero_documento, '') AS numero_registro
                        FROM movimiento_detalles md
                        INNER JOIN movimientos          m   ON md.movimiento_id         = m.id
                        INNER JOIN movimiento_codigos   mc  ON mc.movimiento_detalle_id = md.id
                        INNER JOIN codigos_creados      cc  ON mc.codigo_creado_id      = cc.id
                        INNER JOIN registro_codigos     rc  ON cc.registro_codigo_id    = rc.id
                        LEFT JOIN categoria_producto    cat ON rc.categoria_producto_id = cat.id -- CAMBIO: Unimos con categoría
                        WHERE md.producto_id      = @ProductoId
                          AND m.fecha_movimiento >= @FechaDesde
                          AND m.fecha_movimiento <= @FechaHasta
                        ORDER BY codigo ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                Codigo = reader.GetString(0),
                                ColeccionTipo = reader.GetString(1),
                                NumeroRegistro = reader.GetString(2) // NUEVO: Extraemos el puente
                            });
                        }
                    }
                }
            }

            return reporte;
        }
    }
}