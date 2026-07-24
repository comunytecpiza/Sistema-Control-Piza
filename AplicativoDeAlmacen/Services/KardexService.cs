#nullable enable

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
    /// KardexService Agnóstico, Multi-Motor y Anti-Trampa Multi-Almacén.
    /// Aisla de forma estricta los reportes por sede/almacén activo.
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
        private static void AgregarParametro(IDbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =========================================================
        // 1. KARDEX FÍSICO INDEPENDIENTE POR ALMACÉN
        // =========================================================
        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta, int almacenId)
        {
            var reporte = new KardexFisicoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    // 🌟 FILTRO BLINDADO ANTI-TRAMPA MULTI-ALMACÉN:
                    // Entradas: Tienen destino en mi almacén.
                    // Salidas: Tienen origen en mi almacén.
                    string queryRaw = @"
        SELECT
            m.fecha_movimiento,
            mp.descripcion AS motivo,
            CASE WHEN mp.tipo_movimiento_id = 1 THEN 'entrada' WHEN mp.tipo_movimiento_id = 2 THEN 'salida' ELSE 'otro' END AS tipo_movimiento,
            m.serie_documento,
            m.numero_documento,
            m.serie_guia,
            m.numero_guia,
            pc.razon_social,
            u.descripcion AS ubicacion_desc,
            CASE WHEN m.motivo_producto_id = 2 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END AS IngresoDev,
            CASE WHEN m.motivo_producto_id != 2 AND mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END AS IngresoDoc,
            CASE WHEN m.motivo_producto_id = 6 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END AS SalidaDev,
            CASE WHEN m.motivo_producto_id != 6 AND mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END AS SalidaDoc,
            m.estado_id
        FROM movimiento_detalles md WITH (NOLOCK)
        INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
        LEFT JOIN personas_comerciales pc WITH (NOLOCK) ON m.persona_comercial_id = pc.id
        LEFT JOIN ubicaciones u WITH (NOLOCK) ON m.ubicacion_id = u.id
        WHERE md.producto_id = @ProductoId
          AND m.fecha_movimiento >= @FechaDesde
          AND m.fecha_movimiento <= @FechaHasta
          AND m.estado_id != 2 -- 2 = Anulado
          -- 🌟 FILTRO ESTRICTO DE PERTENENCIA LOGÍSTICA
          AND (
             (mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId) OR
             (mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId)
          )
        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoAcumulado = 0;
                        decimal totalIngresos = 0;
                        decimal totalDevIngresos = 0;
                        decimal totalSalidas = 0;
                        decimal totalDevSalidas = 0;

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int estId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id"));
                            bool isAnulado = (estId == 2);

                            var item = new KardexFisicoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = (reader.IsDBNull(2) ? "" : (reader.GetString(2) == "entrada" ? "I. " : "S. ")) + (reader.IsDBNull(1) ? "" : reader.GetString(1)),
                                Registro = $"{(reader.IsDBNull(3) ? "" : reader.GetString(3))}-{(reader.IsDBNull(4) ? "" : reader.GetString(4))}",
                                Guia = $"{(reader.IsDBNull(5) ? "" : reader.GetString(5))}-{(reader.IsDBNull(6) ? "" : reader.GetString(6))}",
                                RazonSocialUbicacion = !reader.IsDBNull(7) ? reader.GetString(7) : (!reader.IsDBNull(8) ? reader.GetString(8) : ""),
                                IngresoDevolucion = isAnulado ? 0 : reader.GetDecimal(reader.GetOrdinal("IngresoDev")),
                                IngresoNormal = isAnulado ? 0 : reader.GetDecimal(reader.GetOrdinal("IngresoDoc")),
                                SalidaDevolucion = isAnulado ? 0 : reader.GetDecimal(reader.GetOrdinal("SalidaDev")),
                                SalidaNormal = isAnulado ? 0 : reader.GetDecimal(reader.GetOrdinal("SalidaDoc")),
                                IsAnulado = isAnulado
                            };

                            if (!item.IsAnulado)
                            {
                                totalIngresos += item.IngresoNormal;
                                totalDevIngresos += item.IngresoDevolucion;
                                totalSalidas += item.SalidaNormal;
                                totalDevSalidas += item.SalidaDevolucion;

                                saldoAcumulado += (item.IngresoNormal + item.IngresoDevolucion) - (item.SalidaNormal + item.SalidaDevolucion);
                            }
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
        // 2. SALDOS Y MOVIMIENTOS INDEPENDIENTES POR ALMACÉN
        // =========================================================
        public async Task<List<SaldoProductoItem>> ObtenerSaldosYMovimientosAsync(DateTime fechaDesde, DateTime fechaHasta, int almacenId)
        {
            var lista = new List<SaldoProductoItem>();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    // 🌟 CÁLCULO DE STOCK INICIAL Y MOVIMIENTOS EXCLUSIVAMENTE PARA EL ALMACÉN SELECCIONADO
                    string queryRaw = @"
            WITH MovimientosRango AS (
                SELECT
                    md.producto_id,
                    SUM(CASE WHEN m.fecha_movimiento < @FechaDesde THEN 
                             (CASE WHEN mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END) -
                             (CASE WHEN mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END)
                        ELSE 0 END) AS StockInicial,
                    SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta AND mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END) AS TotalIngresos,
                    SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta AND mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END) AS TotalSalidas
                FROM movimiento_detalles md WITH (NOLOCK)
                INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                WHERE m.estado_id != 2
                  AND (
                     (mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId) OR
                     (mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId)
                  )
                GROUP BY md.producto_id
            )
            SELECT
                ISNULL(p.abreviatura, CAST(p.id AS VARCHAR)) AS codigo,
                p.descripcion,
                ISNULL(mr.StockInicial,   0) AS StockInicial,
                ISNULL(mr.TotalIngresos,  0) AS TotalIngresos,
                ISNULL(mr.TotalSalidas,   0) AS TotalSalidas
            FROM productos p WITH (NOLOCK)
            LEFT JOIN MovimientosRango mr ON p.id = mr.producto_id
            WHERE p.estado_id = 1
            ORDER BY p.descripcion";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

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
        // 3. CONSULTA DE MOVIMIENTOS DETALLADOS (Aislamiento por Almacén)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarMovimientosDetalladosAsync(
            int productoId,
            DateTime fechaDesde,
            DateTime fechaHasta,
            string? razonSocial = null,
            string? ubicacion = null,
            int? categoriaProductoId = null,
            int almacenId = 1)
        {
            var reporte = new ConsultaMovimientoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                // 1. TABLA IZQUIERDA — Movimientos filtrados por mi Almacén
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
            SELECT DISTINCT
                m.id,
                m.fecha_movimiento,
                COALESCE(m.serie_documento, '') + '-' + COALESCE(m.numero_documento, '') AS registro,
                COALESCE(pc.razon_social, COALESCE(u.descripcion, 'SIN UBICACIÓN')) AS razon_ubicacion,
                COALESCE(m.serie_guia, '000') + '-' + COALESCE(m.numero_guia, '0000000') AS guia,
                CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE 0 END AS cantidad_ingreso,
                CASE WHEN mp.tipo_movimiento_id = 2 THEN md.cantidad_salida ELSE 0 END AS cantidad_salida,
                m.estado_id,
                m.motivo_producto_id,
                mp.tipo_movimiento_id,
                rc.categoria_producto_id
            FROM movimiento_detalles md WITH (NOLOCK)
            INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
            INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            LEFT JOIN personas_comerciales pc WITH (NOLOCK) ON m.persona_comercial_id = pc.id
            LEFT JOIN ubicaciones u WITH (NOLOCK) ON m.ubicacion_id = u.id
            LEFT JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
            LEFT JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            LEFT JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
            WHERE md.producto_id = @ProductoId
              AND m.fecha_movimiento >= @FechaDesde
              AND m.fecha_movimiento <= @FechaHasta
              AND (
                 (mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId) OR
                 (mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId)
              )";

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        queryRaw += " AND pc.razon_social LIKE @RazonSocial";

                    if (!string.IsNullOrWhiteSpace(ubicacion))
                        queryRaw += " AND u.descripcion LIKE @Ubicacion";

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        queryRaw += " AND (rc.categoria_producto_id = @CategoriaId OR rc.categoria_producto_id IS NULL)";

                    queryRaw += " ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        AgregarParametro(cmd, "@RazonSocial", "%" + razonSocial.Trim() + "%");

                    if (!string.IsNullOrWhiteSpace(ubicacion))
                        AgregarParametro(cmd, "@Ubicacion", "%" + ubicacion.Trim() + "%");

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        AgregarParametro(cmd, "@CategoriaId", categoriaProductoId.Value);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int estadoId = reader.IsDBNull(7) ? 1 : reader.GetInt32(7);
                            bool anulado = (estadoId == 2);

                            reporte.Movimientos.Add(new ConsultaMovimientoItem
                            {
                                Fecha = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1),
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + reader.GetString(2),
                                RazonSocialUbicacion = reader.GetString(3),
                                NumeroGuia = reader.GetString(4),
                                Ingreso = reader.GetDecimal(5),
                                Salida = reader.GetDecimal(6),
                                IsAnulado = anulado
                            });
                        }
                    }
                }

                // 2. TABLA DERECHA — Códigos físicos
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
            SELECT DISTINCT
                COALESCE(cc.codigo, 'N/A') AS codigo,
                COALESCE(cat.nombre, 'SIN TIPO') AS coleccion_tipo,
                COALESCE(m.serie_documento, '') + '-' + COALESCE(m.numero_documento, '') AS numero_registro,
                CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov
            FROM movimiento_detalles md WITH (NOLOCK)
            INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
            INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
            INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
            LEFT JOIN categoria_producto cat WITH (NOLOCK) ON rc.categoria_producto_id = cat.id
            LEFT JOIN personas_comerciales pc WITH (NOLOCK) ON m.persona_comercial_id = pc.id
            LEFT JOIN ubicaciones u WITH (NOLOCK) ON m.ubicacion_id = u.id
            WHERE md.producto_id = @ProductoId
              AND m.fecha_movimiento >= @FechaDesde
              AND m.fecha_movimiento <= @FechaHasta
              AND m.estado_id != 2
              AND (
                 (mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId) OR
                 (mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId)
              )";

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        queryRaw += " AND pc.razon_social LIKE @RazonSocial";

                    if (!string.IsNullOrWhiteSpace(ubicacion))
                        queryRaw += " AND u.descripcion LIKE @Ubicacion";

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        queryRaw += " AND rc.categoria_producto_id = @CategoriaId";

                    queryRaw += " ORDER BY codigo ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        AgregarParametro(cmd, "@RazonSocial", "%" + razonSocial.Trim() + "%");

                    if (!string.IsNullOrWhiteSpace(ubicacion))
                        AgregarParametro(cmd, "@Ubicacion", "%" + ubicacion.Trim() + "%");

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        AgregarParametro(cmd, "@CategoriaId", categoriaProductoId.Value);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                Codigo = reader.GetString(0),
                                ColeccionTipo = reader.GetString(1),
                                NumeroRegistro = reader.GetString(2),
                                TipoMovimiento = reader.GetString(3)
                            });
                        }
                    }
                }
            }

            return reporte;
        }

        // =========================================================
        // 4. KARDEX VALORIZADO INDEPENDIENTE POR ALMACÉN
        // =========================================================
        public async Task<KardexValorizadoReporte> GenerarKardexValorizadoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta, int almacenId)
        {
            var reporte = new KardexValorizadoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = @"
        SELECT
            m.fecha_movimiento,
            mp.descripcion AS motivo,
            CASE WHEN mp.tipo_movimiento_id = 1 THEN 'entrada' WHEN mp.tipo_movimiento_id = 2 THEN 'salida' ELSE 'otro' END AS tipo_movimiento,
            COALESCE(m.serie_documento, '') + '-' + COALESCE(m.numero_documento, '') AS registro,
            COALESCE(pc.razon_social, COALESCE(u.descripcion, 'SIN UBICACIÓN')) AS razon_ubicacion,
            COALESCE(m.serie_guia, '000') + '-' + COALESCE(m.numero_guia, '0000000') AS guia,
            CASE WHEN mp.tipo_movimiento_id = 1 THEN COALESCE(md.cantidad_ingreso, 0) ELSE 0 END AS cantidad_ingreso,
            CASE WHEN mp.tipo_movimiento_id = 2 THEN COALESCE(md.cantidad_salida, 0) ELSE 0 END AS cantidad_salida,
            COALESCE(p.precio_unitario, 0) AS costo_base,
            m.estado_id
        FROM movimiento_detalles md WITH (NOLOCK)
        INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
        INNER JOIN productos p WITH (NOLOCK) ON md.producto_id = p.id
        LEFT JOIN personas_comerciales pc WITH (NOLOCK) ON m.persona_comercial_id = pc.id
        LEFT JOIN ubicaciones u WITH (NOLOCK) ON m.ubicacion_id = u.id
        WHERE md.producto_id = @ProductoId
          AND m.fecha_movimiento >= @FechaDesde
          AND m.fecha_movimiento <= @FechaHasta
          AND (
             (mp.tipo_movimiento_id = 1 AND ISNULL(m.almacen_destino_id, 1) = @AlmacenId) OR
             (mp.tipo_movimiento_id = 2 AND ISNULL(m.almacen_origen_id, 1) = @AlmacenId)
          )
        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoFisicoAcumulado = 0;
                        decimal saldoValorizadoAcumulado = 0;
                        decimal costoPromedioActual = 0;

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            bool isAnulado = !reader.IsDBNull(9) && reader.GetInt32(9) == 2;

                            var item = new KardexValorizadoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = reader.GetString(1),
                                Registro = reader.GetString(3),
                                RazonSocialUbicacion = reader.GetString(4),
                                Guia = reader.GetString(5),
                                IngresoFisico = reader.GetDecimal(6),
                                SalidaFisico = reader.GetDecimal(7),
                                CostoUnitario = reader.GetDecimal(8),
                                IsAnulado = isAnulado
                            };

                            if (costoPromedioActual == 0 && item.CostoUnitario > 0)
                            {
                                costoPromedioActual = item.CostoUnitario;
                            }

                            if (!item.IsAnulado)
                            {
                                if (item.IngresoFisico > 0)
                                {
                                    item.IngresoValorado = item.IngresoFisico * item.CostoUnitario;
                                    saldoFisicoAcumulado += item.IngresoFisico;
                                    saldoValorizadoAcumulado += item.IngresoValorado;

                                    if (saldoFisicoAcumulado > 0)
                                        costoPromedioActual = saldoValorizadoAcumulado / saldoFisicoAcumulado;
                                }
                                else if (item.SalidaFisico > 0)
                                {
                                    item.CostoUnitario = costoPromedioActual;
                                    item.SalidaValorado = item.SalidaFisico * costoPromedioActual;

                                    saldoFisicoAcumulado -= item.SalidaFisico;
                                    saldoValorizadoAcumulado -= item.SalidaValorado;
                                }

                                reporte.TotalIngresoFisico += item.IngresoFisico;
                                reporte.TotalSalidaFisico += item.SalidaFisico;
                                reporte.TotalIngresoValorado += item.IngresoValorado;
                                reporte.TotalSalidaValorado += item.SalidaValorado;
                            }
                            else
                            {
                                item.IngresoFisico = 0;
                                item.SalidaFisico = 0;
                                item.IngresoValorado = 0;
                                item.SalidaValorado = 0;
                            }

                            item.CostoPromedio = costoPromedioActual;
                            item.SaldoFisico = saldoFisicoAcumulado;
                            item.SaldoValorado = saldoValorizadoAcumulado;

                            reporte.Detalles.Add(item);
                        }

                        reporte.StockFinalFisico = saldoFisicoAcumulado;
                        reporte.SaldoFinalValorado = saldoValorizadoAcumulado;
                    }
                }
            }
            return reporte;
        }

        // =========================================================
        // LISTAS Y BÚSQUEDAS AUXILIARES
        // =========================================================
        public async Task<List<string>> ObtenerRazonesSocialesAsync()
        {
            var lista = new List<string>();
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT razon_social FROM personas_comerciales WHERE razon_social IS NOT NULL";
                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<string>> ObtenerUbicacionesAsync()
        {
            var lista = new List<string>();
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT descripcion FROM ubicaciones WHERE descripcion IS NOT NULL";
                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<KardexFisicoItem>> ObtenerHistorialCompletoPorCodigoAsync(int productoId, string codigoEscaneado, int categoriaProductoId)
        {
            var lista = new List<KardexFisicoItem>();

            using (var conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (var cmd = conn.CreateCommand())
                {
                    string sql = @"
                SELECT DISTINCT
                    m.fecha_movimiento,
                    m.created_at,
                    CASE WHEN m.estado_id = 2 THEN '❌ ANULADO - ' + mp.descripcion ELSE mp.descripcion END AS tipo_mov,
                    ISNULL(m.serie_documento, '') + '-' + ISNULL(m.numero_documento, '') AS doc,
                    COALESCE(pc.razon_social, u.descripcion, 'ALMACEN') AS raz_ub,
                    ISNULL(m.serie_guia, '') + '-' + ISNULL(m.numero_guia, '') AS gu,
                    CASE WHEN m.estado_id = 2 THEN 0 ELSE COALESCE(md.cantidad_ingreso, 0) END AS cant_in,
                    CASE WHEN m.estado_id = 2 THEN 0 ELSE COALESCE(md.cantidad_salida, 0) END AS cant_sa
                FROM movimiento_codigos mc WITH (NOLOCK)
                INNER JOIN movimiento_detalles md WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
                INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                LEFT JOIN personas_comerciales pc WITH (NOLOCK) ON m.persona_comercial_id = pc.id
                LEFT JOIN ubicaciones u WITH (NOLOCK) ON m.ubicacion_id = u.id
                INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
                WHERE md.producto_id = @ProductoId
                  AND rc.categoria_producto_id = @CategoriaProductoId
                  AND CAST(cc.codigo AS VARCHAR) LIKE @CodigoExacto
                ORDER BY m.created_at ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@CategoriaProductoId", categoriaProductoId);
                    AgregarParametro(cmd, "@CodigoExacto", "%" + codigoEscaneado.TrimStart('0'));

                    using (var reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var item = new KardexFisicoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Registro = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                RazonSocialUbicacion = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Guia = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                IngresoNormal = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                SalidaNormal = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7)
                            };

                            lista.Add(item);
                        }
                    }
                }
            }
            return lista;
        }
    }
}