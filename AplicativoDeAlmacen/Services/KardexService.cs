#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
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

        private static void AgregarParametro(IDbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta, int almacenId)
        {
            var reporte = new KardexFisicoReporte { AlmacenId = almacenId };

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                // 1. CÁLCULO DE STOCK INICIAL (Excluye explícitamente m.estado_id != 2)
                using (IDbCommand cmdInit = conn.CreateCommand())
                {
                    string qInit = $@"
        SELECT 
            COALESCE(SUM(CASE WHEN mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END), 0) -
            COALESCE(SUM(CASE WHEN mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END), 0)
        FROM movimiento_detalles md {nolock}
        INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
        WHERE md.producto_id = @ProductoId
          AND m.fecha_movimiento < @FechaDesde
          AND m.estado_id != 2"; // 👈 NUNCA CONSIDERA ANULADOS

                    cmdInit.CommandText = QueryAdapter.FormatearConsulta(qInit);
                    AgregarParametro(cmdInit, "@ProductoId", productoId);
                    AgregarParametro(cmdInit, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmdInit, "@AlmacenId", almacenId);

                    object resInit = await ((DbCommand)cmdInit).ExecuteScalarAsync();
                    reporte.StockInicial = (resInit != null && resInit != DBNull.Value) ? Convert.ToDecimal(resInit) : 0m;
                }

                // 2. CONSULTA DE MOVIMIENTOS EN RANGO (Filtro m.estado_id != 2 en SQL)
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
        SELECT
            m.fecha_movimiento,
            mp.descripcion AS motivo,
            CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_movimiento,
            CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
            CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
            COALESCE(pc.razon_social, u.descripcion, CASE WHEN mp.tipo_movimiento_id = 1 THEN alm_orig.nombre ELSE alm_dest.nombre END, 'ALMACÉN') AS entidad,
            CASE WHEN mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END AS Ingreso,
            CASE WHEN mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END AS Salida,
            m.estado_id,
            m.motivo_producto_id
        FROM movimiento_detalles md {nolock}
        INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
        LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
        LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
        LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
        LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
        WHERE md.producto_id = @ProductoId
          AND m.fecha_movimiento >= @FechaDesde
          AND m.fecha_movimiento <= @FechaHasta
          AND m.estado_id != 2 -- 👈 FILTRO ESTRICTO ANTI-ANULADOS
          AND (
             (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
             (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
          )
        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoAcumulado = reporte.StockInicial;
                        const int ALMACEN_CENTRAL_ID = 1;
                        bool esAlmacenCentral = (almacenId == ALMACEN_CENTRAL_ID);

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int motivoId = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);
                            string motivoTexto = Convert.ToString(reader.GetValue(1)) ?? "";

                            decimal ing = reader.GetDecimal(6);
                            decimal sal = reader.GetDecimal(7);

                            var item = new KardexFisicoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = motivoTexto,
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(4)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(5)) ?? "",
                                Ingreso = ing,
                                Salida = sal,
                                IngresoNormal = ing,
                                SalidaNormal = sal,
                                IsAnulado = false
                            };

                            reporte.TotalSalidas += sal;

                            // 🌟 LÓGICA DE TARJETAS DE INGRESOS
                            if (ing > 0)
                            {
                                string motivoUpper = motivoTexto.ToUpperInvariant();
                                bool esCompra = motivoUpper.Contains("COMPRA") || motivoId == 1;
                                bool esTransferencia = motivoUpper.Contains("TRANSFERENCIA") || motivoId == 4;

                                if (esAlmacenCentral)
                                {
                                    // Almacén Central: Solo COMPRA es Ingreso Principal. El resto son Devoluciones.
                                    if (esCompra)
                                    {
                                        reporte.TotalIngresos += ing;
                                    }
                                    else
                                    {
                                        reporte.TotalDevoluciones += ing;
                                    }
                                }
                                else
                                {
                                    // Sucursales: TRANSFERENCIAS (y Compras si aplica) son Ingreso Principal. El resto son Devoluciones.
                                    if (esTransferencia || esCompra)
                                    {
                                        reporte.TotalIngresos += ing;
                                    }
                                    else
                                    {
                                        reporte.TotalDevoluciones += ing;
                                    }
                                }
                            }

                            saldoAcumulado += (ing - sal);
                            item.SaldoAcumulado = saldoAcumulado;
                            item.SaldoFinal = saldoAcumulado;

                            reporte.Detalles.Add(item);
                        }

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
                    string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                    string queryRaw = $@"
            WITH MovimientosRango AS (
                SELECT
                    md.producto_id,
                    SUM(CASE WHEN m.fecha_movimiento < @FechaDesde THEN 
                             (CASE WHEN mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END) -
                             (CASE WHEN mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END)
                        ELSE 0 END) AS StockInicial,
                    SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta AND mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END) AS TotalIngresos,
                    SUM(CASE WHEN m.fecha_movimiento >= @FechaDesde AND m.fecha_movimiento <= @FechaHasta AND mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END) AS TotalSalidas
                FROM movimiento_detalles md {nolock}
                INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                WHERE m.estado_id != 2
                  AND (
                     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
                     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
                  )
                GROUP BY md.producto_id
            )
            SELECT
                COALESCE(p.abreviatura, CAST(p.id AS CHAR)) AS codigo,
                p.descripcion,
                COALESCE(mr.StockInicial,   0) AS StockInicial,
                COALESCE(mr.TotalIngresos,  0) AS TotalIngresos,
                COALESCE(mr.TotalSalidas,   0) AS TotalSalidas
            FROM productos p {nolock}
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
                                Codigo = Convert.ToString(reader["codigo"]) ?? "",
                                Descripcion = Convert.ToString(reader["descripcion"]) ?? "",
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
        // 3. CONSULTA DE MOVIMIENTOS DETALLADOS (Aislamiento por Almacén + Fix Double Cast)
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

                // 1. TABLA IZQUIERDA — Movimientos
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                    // 🌟 CASCADA INTELIGENTE:
                    // Prioriza Razón Social -> Ubicación -> Almacén Relacionado (Origen u Destino según la operación)
                    string queryRaw = $@"
    SELECT DISTINCT
        m.id,
        m.fecha_movimiento,
        CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
        COALESCE(
            pc.razon_social, 
            u.descripcion, 
            CASE 
                WHEN mp.tipo_movimiento_id = 1 THEN alm_orig.nombre 
                WHEN mp.tipo_movimiento_id = 2 THEN alm_dest.nombre 
                ELSE 'SIN REGISTRO' 
            END,
            'SIN REGISTRO'
        ) AS razon_ubicacion,
        CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
        CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE 0 END AS cantidad_ingreso,
        CASE WHEN mp.tipo_movimiento_id = 2 THEN md.cantidad_salida ELSE 0 END AS cantidad_salida,
        m.estado_id,
        m.motivo_producto_id,
        mp.tipo_movimiento_id,
        rc.categoria_producto_id
    FROM movimiento_detalles md {nolock}
    INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
    INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
    LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
    LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
    LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
    LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
    LEFT JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
    LEFT JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
    LEFT JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
    WHERE md.producto_id = @ProductoId
      AND m.fecha_movimiento >= @FechaDesde
      AND m.fecha_movimiento <= @FechaHasta
      AND (
         (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
         (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
      )";

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        queryRaw += " AND (pc.razon_social LIKE @RazonSocial OR alm_orig.nombre LIKE @RazonSocial OR alm_dest.nombre LIKE @RazonSocial)";

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
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + Convert.ToString(reader.GetValue(2)),
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(3)) ?? "",
                                NumeroGuia = Convert.ToString(reader.GetValue(4)) ?? "",
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
                    string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                    string queryRaw = $@"
    SELECT DISTINCT
        COALESCE(cc.codigo, 'N/A') AS codigo,
        COALESCE(cat.nombre, 'SIN TIPO') AS coleccion_tipo,
        CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
        CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov
    FROM movimiento_detalles md {nolock}
    INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
    INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
    INNER JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
    INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
    INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
    LEFT JOIN categoria_producto cat {nolock} ON rc.categoria_producto_id = cat.id
    LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
    LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
    LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
    LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
    WHERE md.producto_id = @ProductoId
      AND m.fecha_movimiento >= @FechaDesde
      AND m.fecha_movimiento <= @FechaHasta
      AND m.estado_id != 2
      AND (
         (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
         (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
      )";

                    if (!string.IsNullOrWhiteSpace(razonSocial))
                        queryRaw += " AND (pc.razon_social LIKE @RazonSocial OR alm_orig.nombre LIKE @RazonSocial OR alm_dest.nombre LIKE @RazonSocial)";

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
                                Codigo = Convert.ToString(reader.GetValue(0)) ?? "N/A",
                                ColeccionTipo = Convert.ToString(reader.GetValue(1)) ?? "SIN TIPO",
                                NumeroRegistro = Convert.ToString(reader.GetValue(2)) ?? "",
                                TipoMovimiento = Convert.ToString(reader.GetValue(3)) ?? ""
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
            var reporte = new KardexValorizadoReporte
            {
                AlmacenId = almacenId
            };

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmdNombre = conn.CreateCommand())
                {
                    cmdNombre.CommandText = QueryAdapter.FormatearConsulta("SELECT nombre FROM almacenes WHERE id = @almId");
                    AgregarParametro(cmdNombre, "@almId", almacenId);
                    var nomObj = await ((DbCommand)cmdNombre).ExecuteScalarAsync();
                    reporte.AlmacenNombre = nomObj?.ToString() ?? "Almacén " + almacenId;
                }

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                // 🌟 1. CÁLCULO DEL STOCK INICIAL REAL (Excluyendo anulados)
                decimal stockInicialFisico = 0m;
                using (IDbCommand cmdInit = conn.CreateCommand())
                {
                    string qInit = $@"
                        SELECT 
                            COALESCE(SUM(CASE WHEN mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END), 0) -
                            COALESCE(SUM(CASE WHEN mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END), 0)
                        FROM movimiento_detalles md {nolock}
                        INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
                        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                        WHERE md.producto_id = @ProductoId
                          AND m.fecha_movimiento < @FechaDesde
                          AND m.estado_id != 2";

                    cmdInit.CommandText = QueryAdapter.FormatearConsulta(qInit);
                    AgregarParametro(cmdInit, "@ProductoId", productoId);
                    AgregarParametro(cmdInit, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmdInit, "@AlmacenId", almacenId);

                    object resInit = await ((DbCommand)cmdInit).ExecuteScalarAsync();
                    stockInicialFisico = (resInit != null && resInit != DBNull.Value) ? Convert.ToDecimal(resInit) : 0m;
                }

                // 🌟 2. CONSULTA DE MOVIMIENTOS EN RANGO (FORZANDO EL COSTO UNITARIO DEL DETALLE)
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
                        SELECT
                            m.fecha_movimiento,
                            mp.descripcion AS motivo,
                            CASE WHEN mp.tipo_movimiento_id = 1 THEN 'entrada' WHEN mp.tipo_movimiento_id = 2 THEN 'salida' ELSE 'otro' END AS tipo_movimiento,
                            CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
                            COALESCE(pc.razon_social, COALESCE(u.descripcion, 'SIN UBICACIÓN')) AS razon_ubicacion,
                            CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
                            CASE WHEN mp.tipo_movimiento_id = 1 THEN COALESCE(md.cantidad_ingreso, 0) ELSE 0 END AS cantidad_ingreso,
                            CASE WHEN mp.tipo_movimiento_id = 2 THEN COALESCE(md.cantidad_salida, 0) ELSE 0 END AS cantidad_salida,
                            COALESCE(md.costo_unitario, 0) AS costo_unitario, -- 👈 LECTURA DIRECTA Y EXCLUSIVA DEL COSTO DEL MOVIMIENTO
                            m.estado_id,
                            alm.id AS alm_id,
                            alm.nombre AS alm_nombre
                        FROM movimiento_detalles md {nolock}
                        INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
                        INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                        LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
                        LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
                        LEFT JOIN almacenes alm {nolock} ON alm.id = @AlmacenId
                        WHERE md.producto_id = @ProductoId
                          AND m.fecha_movimiento >= @FechaDesde
                          AND m.fecha_movimiento <= @FechaHasta
                          AND m.estado_id != 2 
                          AND (
                             (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
                             (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
                          )
                        ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHasta.Date);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoFisicoAcumulado = stockInicialFisico;
                        decimal saldoValorizadoAcumulado = stockInicialFisico * 95.00m;
                        decimal costoPromedioActual = 95.00m;

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            var item = new KardexValorizadoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = Convert.ToString(reader.GetValue(1)) ?? "",
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(4)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(5)) ?? "",
                                IngresoFisico = reader.GetDecimal(6),
                                SalidaFisico = reader.GetDecimal(7),
                                CostoUnitario = reader.GetDecimal(8), // 👈 Toma el 95.00 o el costo que tenga el detalle
                                IsAnulado = false,
                                AlmacenId = reader.IsDBNull(reader.GetOrdinal("alm_id")) ? almacenId : reader.GetInt32(reader.GetOrdinal("alm_id")),
                                AlmacenNombre = reader.IsDBNull(reader.GetOrdinal("alm_nombre")) ? reporte.AlmacenNombre : Convert.ToString(reader.GetValue(reader.GetOrdinal("alm_nombre")))!
                            };

                            if (costoPromedioActual == 95.00m && item.CostoUnitario > 0)
                            {
                                costoPromedioActual = item.CostoUnitario;
                            }

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
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT razon_social FROM personas_comerciales WHERE razon_social IS NOT NULL");
                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(Convert.ToString(reader.GetValue(0)) ?? "");
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
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT descripcion FROM ubicaciones WHERE descripcion IS NOT NULL");
                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(Convert.ToString(reader.GetValue(0)) ?? "");
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
                    string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                    // 🌟 CONSULTA COMPATIBLE SQL SERVER & MYSQL
                    string sql = $@"
                SELECT DISTINCT
                    m.fecha_movimiento,
                    m.created_at,
                    CASE WHEN m.estado_id = 2 THEN CONCAT('❌ ANULADO - ', mp.descripcion) ELSE mp.descripcion END AS tipo_mov,
                    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS doc,
                    COALESCE(pc.razon_social, u.descripcion, CASE WHEN mp.tipo_movimiento_id = 1 THEN alm_orig.nombre ELSE alm_dest.nombre END, 'ALMACÉN') AS raz_ub,
                    CONCAT(COALESCE(m.serie_guia, ''), '-', COALESCE(m.numero_guia, '')) AS gu,
                    CASE WHEN m.estado_id = 2 THEN 0 ELSE COALESCE(md.cantidad_ingreso, 0) END AS cant_in,
                    CASE WHEN m.estado_id = 2 THEN 0 ELSE COALESCE(md.cantidad_salida, 0) END AS cant_sa,
                    m.estado_id
                FROM movimiento_codigos mc {nolock}
                INNER JOIN movimiento_detalles md {nolock} ON mc.movimiento_detalle_id = md.id
                INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
                LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
                LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
                LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
                INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
                INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
                WHERE md.producto_id = @ProductoId
                  AND rc.categoria_producto_id = @CategoriaProductoId
                  AND (cc.codigo LIKE @CodigoExacto OR cc.codigo = @CodigoPuro)
                ORDER BY m.created_at ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@CategoriaProductoId", categoriaProductoId);
                    AgregarParametro(cmd, "@CodigoExacto", "%" + codigoEscaneado.Trim());
                    AgregarParametro(cmd, "@CodigoPuro", codigoEscaneado.Trim());

                    using (var reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int estId = reader.IsDBNull(8) ? 1 : reader.GetInt32(8);
                            bool anulado = (estId == 2);

                            var item = new KardexFisicoItem
                            {
                                Fecha = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                                Tipo = Convert.ToString(reader.GetValue(2)) ?? "",
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(4)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(5)) ?? "",
                                IngresoNormal = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                SalidaNormal = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                                Ingreso = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                Salida = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                                IsAnulado = anulado
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