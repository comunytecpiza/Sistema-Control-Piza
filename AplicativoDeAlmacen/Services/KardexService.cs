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
using AplicativoDeAlmacen.Models.Almacen;

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

        // =========================================================
        // 1. GENERAR KÁRDEX FÍSICO
        // =========================================================
        public async Task<KardexFisicoReporte> GenerarKardexFisicoAsync(int productoId, DateTime fechaDesde, DateTime fechaHasta, int almacenId)
        {
            var reporte = new KardexFisicoReporte { AlmacenId = almacenId };

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                // 1. CÁLCULO DE STOCK INICIAL
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
                    reporte.StockInicial = (resInit != null && resInit != DBNull.Value) ? Convert.ToDecimal(resInit) : 0m;
                }

                // 2. CONSULTA DE MOVIMIENTOS EN RANGO
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string subqueryTrazabilidad = $@"
            CASE 
                WHEN m.motivo_producto_id = 2 THEN 1
                WHEN m.motivo_producto_id = 1 THEN 0
                WHEN mp.tipo_movimiento_id = 1 THEN 
                    CASE WHEN EXISTS (
                        SELECT 1 
                        FROM movimiento_codigos mc_curr {nolock}
                        INNER JOIN movimiento_codigos mc_prev {nolock} ON mc_curr.codigo_creado_id = mc_prev.codigo_creado_id
                        INNER JOIN movimientos m_prev {nolock} ON mc_prev.movimiento_id = m_prev.id
                        INNER JOIN motivo_productos mp_prev {nolock} ON m_prev.motivo_producto_id = mp_prev.id
                        WHERE mc_curr.movimiento_id = m.id
                          AND mp_prev.tipo_movimiento_id = 2
                          AND m_prev.id < m.id
                          AND m_prev.estado_id = 1
                    ) THEN 1 ELSE 0 END
                ELSE 0 
            END AS es_devolucion_real";

                    string exprFechaOrden = QueryAdapter.EsMySQL
                        ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                        : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

                    string queryRaw = $@"
SELECT
    m.fecha_movimiento,
    mp.descripcion AS motivo,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_movimiento,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
    COALESCE(
        pc.razon_social,
        u.descripcion,
        CASE 
            WHEN mp.tipo_movimiento_id = 1 AND m.motivo_producto_id = 4 THEN alm_orig.nombre
            WHEN mp.tipo_movimiento_id = 2 AND m.motivo_producto_id = 10 THEN alm_dest.nombre
            ELSE NULL 
        END,
        'ALMACÉN'
    ) AS entidad,
    CASE WHEN mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId THEN md.cantidad_ingreso ELSE 0 END AS Ingreso,
    CASE WHEN mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId THEN md.cantidad_salida ELSE 0 END AS Salida,
    m.estado_id,
    m.motivo_producto_id,
    m.created_at,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador,
    {subqueryTrazabilidad}
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
WHERE md.producto_id = @ProductoId
  AND m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )
ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@ProductoId", productoId);
                    DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
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

                            bool esDevolucionReal = !reader.IsDBNull(14) && Convert.ToInt32(reader.GetValue(14)) == 1;

                            decimal ing = reader.GetDecimal(6);
                            decimal sal = reader.GetDecimal(7);

                            DateTime? fechaMovRaw = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
                            DateTime? createdAtRaw = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10);

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            var item = new KardexFisicoItem
                            {
                                Fecha = fechaMovFinal,
                                Tipo = motivoTexto,
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(4)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(5)) ?? "",
                                Ingreso = ing,
                                Salida = sal,
                                IngresoNormal = ing,
                                SalidaNormal = sal,
                                IsAnulado = false,

                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(11) ? "" : Convert.ToString(reader.GetValue(11))!,
                                UpdatedAt = reader.IsDBNull(12) ? (DateTime?)null : reader.GetDateTime(12),
                                UsuarioModificador = reader.IsDBNull(13) ? null : Convert.ToString(reader.GetValue(13))
                            };

                            reporte.TotalSalidas += sal;

                            if (ing > 0)
                            {
                                string motivoUpper = motivoTexto.ToUpperInvariant();
                                bool esCompra = motivoUpper.Contains("COMPRA") || motivoId == 1;
                                bool esTransferencia = motivoUpper.Contains("TRANSFERENCIA") || motivoId == 4;

                                if (esAlmacenCentral)
                                {
                                    if (esCompra || (!esDevolucionReal && motivoId != 2 && !esTransferencia))
                                        reporte.TotalIngresos += ing;
                                    else
                                        reporte.TotalDevoluciones += ing;
                                }
                                else
                                {
                                    if (esTransferencia || esCompra || (!esDevolucionReal && motivoId != 2))
                                        reporte.TotalIngresos += ing;
                                    else
                                        reporte.TotalDevoluciones += ing;
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

                    DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
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
        // 3. CONSULTA DE MOVIMIENTOS DETALLADOS
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
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                string exprFechaOrden = QueryAdapter.EsMySQL
                    ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                    : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

                // 1. TABLA IZQUIERDA — Movimientos
                using (IDbCommand cmd = conn.CreateCommand())
                {
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
    rc.categoria_producto_id,
    COALESCE(m.almacen_destino_id, COALESCE(m.almacen_origen_id, m.almacen_id)) AS alm_relacionado_id,
    m.created_at,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador,
    md.id AS movimiento_detalle_id,
    md.producto_id
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
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
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

                    queryRaw += $" ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@ProductoId", productoId);
                    DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
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
                            int estadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id"));
                            bool anulado = (estadoId == 2);
                            int almRelId = reader.IsDBNull(reader.GetOrdinal("alm_relacionado_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("alm_relacionado_id"));

                            DateTime? fechaMovRaw = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("fecha_movimiento"));
                            DateTime? createdAtRaw = reader.IsDBNull(reader.GetOrdinal("created_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("created_at"));

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            reporte.Movimientos.Add(new ConsultaMovimientoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Fecha = fechaMovFinal ?? DateTime.MinValue,
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + reader["registro"].ToString(),
                                RazonSocialUbicacion = reader["razon_ubicacion"].ToString() ?? "",
                                NumeroGuia = reader["guia"].ToString() ?? "",
                                Ingreso = reader.GetDecimal(reader.GetOrdinal("cantidad_ingreso")),
                                Salida = reader.GetDecimal(reader.GetOrdinal("cantidad_salida")),
                                IsAnulado = anulado,
                                AlmacenId = almRelId,
                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(reader.GetOrdinal("usuario_creador")) ? "" : reader["usuario_creador"].ToString()!,
                                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                                UsuarioModificador = reader.IsDBNull(reader.GetOrdinal("usuario_modificador")) ? null : reader["usuario_modificador"].ToString()
                            });
                        }
                    }
                }

                // 2. TABLA DERECHA — Códigos físicos
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    cc.codigo AS codigo,
    COALESCE(cat.nombre, 'SIN TIPO') AS coleccion_tipo,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov,
    md.id AS movimiento_detalle_id,
    md.producto_id
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
                    DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
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
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Codigo = reader["codigo"].ToString() ?? "N/A",
                                ColeccionTipo = reader["coleccion_tipo"].ToString() ?? "SIN TIPO",
                                NumeroRegistro = reader["numero_registro"].ToString() ?? "",
                                TipoMovimiento = reader["tipo_mov"].ToString() ?? ""
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

                // 1. CÁLCULO DEL STOCK INICIAL REAL
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

                // 2. CONSULTA DE MOVIMIENTOS EN RANGO CON AUDITORÍA
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string exprFechaOrden = QueryAdapter.EsMySQL
                        ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                        : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

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
                    COALESCE(md.costo_unitario, 0) AS costo_unitario,
                    m.estado_id,
                    alm.id AS alm_id,
                    alm.nombre AS alm_nombre,
                    m.created_at,
                    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
                    m.updated_at,
                    usr_u.nombres AS usuario_modificador
                FROM movimiento_detalles md {nolock}
                INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
                LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
                LEFT JOIN almacenes alm {nolock} ON alm.id = @AlmacenId
                LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
                LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
                WHERE md.producto_id = @ProductoId
                  AND m.fecha_movimiento >= @FechaDesde
                  AND m.fecha_movimiento <= @FechaHasta
                  AND m.estado_id != 2 
                  AND (
                     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
                     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
                  )
                ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        decimal saldoFisicoAcumulado = stockInicialFisico;
                        decimal saldoValorizadoAcumulado = stockInicialFisico * 95.00m;
                        decimal costoPromedioActual = 95.00m;

                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            DateTime? fechaMovRaw = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
                            DateTime? createdAtRaw = reader.IsDBNull(12) ? (DateTime?)null : reader.GetDateTime(12);

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            var item = new KardexValorizadoItem
                            {
                                Fecha = fechaMovFinal,
                                Tipo = Convert.ToString(reader.GetValue(1)) ?? "",
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(4)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(5)) ?? "",
                                IngresoFisico = reader.GetDecimal(6),
                                SalidaFisico = reader.GetDecimal(7),
                                CostoUnitario = reader.GetDecimal(8),
                                IsAnulado = false,
                                AlmacenId = reader.IsDBNull(10) ? almacenId : reader.GetInt32(10),
                                AlmacenNombre = reader.IsDBNull(11) ? reporte.AlmacenNombre : Convert.ToString(reader.GetValue(11))!,

                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(13) ? "" : Convert.ToString(reader.GetValue(13))!,
                                UpdatedAt = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14),
                                UsuarioModificador = reader.IsDBNull(15) ? null : Convert.ToString(reader.GetValue(15))
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

        // =========================================================
        // 5. HISTORIAL TRAZABLE POR CÓDIGO FÍSICO
        // =========================================================
        public async Task<List<KardexFisicoItem>> ObtenerHistorialCompletoPorCodigoAsync(
            int productoId,
            string codigoEscaneado,
            int categoriaProductoId,
            int almacenId = 1,
            bool incluirAnulados = false)
        {
            var lista = new List<KardexFisicoItem>();

            using (var conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                string abreviaturaBase = "";
                using (var cmdAbrev = conn.CreateCommand())
                {
                    string nolockAbrev = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                    cmdAbrev.CommandText = QueryAdapter.FormatearConsulta($"SELECT abreviatura FROM productos {nolockAbrev} WHERE id = @prodId");
                    AgregarParametro(cmdAbrev, "@prodId", productoId);
                    var resAbrev = await ((DbCommand)cmdAbrev).ExecuteScalarAsync();
                    if (resAbrev != null && resAbrev != DBNull.Value)
                    {
                        abreviaturaBase = resAbrev.ToString()!.Trim();
                    }
                }

                string codigoLimpio = codigoEscaneado.Trim().Replace("'", "-");

                if (int.TryParse(codigoLimpio, out int numParsed))
                {
                    codigoLimpio = $"{abreviaturaBase}-{numParsed:D7}";
                }
                else if (!codigoLimpio.Contains("-") && !string.IsNullOrEmpty(abreviaturaBase))
                {
                    codigoLimpio = $"{abreviaturaBase}-{codigoLimpio}";
                }

                string codigoVariacionEspacio = codigoLimpio;
                int primerGuion = codigoLimpio.IndexOf('-');
                if (primerGuion > 0)
                {
                    codigoVariacionEspacio = string.Concat(codigoLimpio.AsSpan(0, primerGuion), " ", codigoLimpio.AsSpan(primerGuion + 1));
                }

                using (var cmd = conn.CreateCommand())
                {
                    string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                    string sqlAnuladosFiltro = incluirAnulados ? "" : " AND m.estado_id != 2 ";

                    string exprFechaOrden = QueryAdapter.EsMySQL
                        ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                        : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

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
    m.estado_id,
    CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), CASE WHEN rc.categoria_producto_id = 1 THEN 'LIBRO GUÍA' ELSE 'LIBRO VENTA' END) AS coleccion_nombre,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador
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
LEFT JOIN colecciones c {nolock} ON rc.coleccion_id = c.id
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
WHERE md.producto_id = @ProductoId
  AND rc.categoria_producto_id = @CategoriaProductoId
  AND (
        cc.codigo = @CodigoBusqueda 
        OR cc.codigo = @CodigoVariacion
        OR REPLACE(cc.codigo, '''', '-') = @CodigoBusqueda
        OR REPLACE(cc.codigo, ' ', '-') = @CodigoBusqueda
      )
  AND (m.almacen_id = @AlmacenId OR m.almacen_origen_id = @AlmacenId OR m.almacen_destino_id = @AlmacenId)
  {sqlAnuladosFiltro}
ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@CategoriaProductoId", categoriaProductoId);
                    AgregarParametro(cmd, "@CodigoBusqueda", codigoLimpio);
                    AgregarParametro(cmd, "@CodigoVariacion", codigoVariacionEspacio);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    using (var reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int estId = reader.IsDBNull(8) ? 1 : reader.GetInt32(8);
                            bool anulado = (estId == 2);

                            DateTime? fechaMovRaw = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
                            DateTime? createdAtRaw = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            var item = new KardexFisicoItem
                            {
                                Fecha = fechaMovFinal,
                                Tipo = Convert.ToString(reader.GetValue(2)) ?? "",
                                Registro = Convert.ToString(reader.GetValue(3)) ?? "",
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(4)) ?? "",
                                Guia = Convert.ToString(reader.GetValue(5)) ?? "",
                                IngresoNormal = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                SalidaNormal = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                                Ingreso = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                Salida = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                                IsAnulado = anulado,

                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(10) ? "" : Convert.ToString(reader.GetValue(10))!,
                                UpdatedAt = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11),
                                UsuarioModificador = reader.IsDBNull(12) ? null : Convert.ToString(reader.GetValue(12))
                            };

                            lista.Add(item);
                        }
                    }
                }
            }
            return lista;
        }

        // =========================================================
        // 6. OBTENER NOMBRE COLECCIÓN POR CÓDIGO
        // =========================================================
        public async Task<string> ObtenerNombreColeccionCodigoAsync(int productoId, string codigo, int categoriaProductoId)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                string sql = $@"
            SELECT CONCAT(COALESCE(CONCAT('C', c.ano, ' / '), ''), CASE WHEN rc.categoria_producto_id = 1 THEN 'LIBRO GUÍA' ELSE 'LIBRO VENTA' END)
            FROM codigos_creados cc {nolock}
            INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
            LEFT JOIN colecciones c {nolock} ON rc.coleccion_id = c.id
            WHERE rc.producto_id = @prodId
              AND rc.categoria_producto_id = @catId
              AND (cc.codigo LIKE @codExacto OR cc.codigo = @codPuro)
            LIMIT 1";

                cmd.CommandText = QueryAdapter.FormatearConsulta(sql);
                AgregarParametro(cmd, "@prodId", productoId);
                AgregarParametro(cmd, "@catId", categoriaProductoId);
                AgregarParametro(cmd, "@codExacto", "%" + codigo.Trim());
                AgregarParametro(cmd, "@codPuro", codigo.Trim());

                var res = await cmd.ExecuteScalarAsync();
                return res != null && res != DBNull.Value ? res.ToString()! : "";
            }
            catch
            {
                return "";
            }
        }

        // =========================================================
        // 7. KÁRDEX POR UBICACIÓN (VINCULACIÓN EXACTA POR DETALLE)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarKardexPorUbicacionAsync(
            int productoId,
            DateTime fechaDesde,
            DateTime fechaHasta,
            string? filtroUbicacionTexto = null,
            int almacenId = 1)
        {
            var reporte = new ConsultaMovimientoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                string exprFechaOrden = QueryAdapter.EsMySQL
                    ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                    : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

                // 1. TABLA IZQUIERDA: MOVIMIENTOS
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    m.id,
    m.fecha_movimiento,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
    u.descripcion AS ubicacion_nombre,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE 0 END AS cantidad_ingreso,
    CASE WHEN mp.tipo_movimiento_id = 2 THEN md.cantidad_salida ELSE 0 END AS cantidad_salida,
    m.estado_id,
    m.motivo_producto_id,
    mp.tipo_movimiento_id,
    m.created_at,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
WHERE md.producto_id = @ProductoId
  AND m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        queryRaw += " AND u.descripcion LIKE @FiltroUbicacion";
                    }

                    queryRaw += $" ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        AgregarParametro(cmd, "@FiltroUbicacion", "%" + filtroUbicacionTexto.Trim() + "%");
                    }

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int estadoId = reader.IsDBNull(7) ? 1 : reader.GetInt32(7);
                            bool anulado = (estadoId == 2);

                            DateTime? fechaMovRaw = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                            DateTime? createdAtRaw = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10);

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            reporte.Movimientos.Add(new ConsultaMovimientoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Fecha = fechaMovFinal ?? DateTime.MinValue,
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + Convert.ToString(reader.GetValue(2)),
                                RazonSocialUbicacion = Convert.ToString(reader.GetValue(3)) ?? "SIN UBICACIÓN",
                                NumeroGuia = Convert.ToString(reader.GetValue(4)) ?? "",
                                Ingreso = reader.GetDecimal(5),
                                Salida = reader.GetDecimal(6),
                                IsAnulado = anulado,
                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(11) ? "" : Convert.ToString(reader.GetValue(11))!,
                                UpdatedAt = reader.IsDBNull(12) ? (DateTime?)null : reader.GetDateTime(12),
                                UsuarioModificador = reader.IsDBNull(13) ? null : Convert.ToString(reader.GetValue(13))
                            });
                        }
                    }
                }

                // 2. TABLA DERECHA: CÓDIGOS ASOCIADOS
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    cc.codigo AS codigo,
    COALESCE(cat.nombre, 'SIN TIPO') AS coleccion_tipo,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
INNER JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
LEFT JOIN categoria_producto cat {nolock} ON rc.categoria_producto_id = cat.id
WHERE md.producto_id = @ProductoId
  AND m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        queryRaw += " AND u.descripcion LIKE @FiltroUbicacion";
                    }

                    queryRaw += " ORDER BY codigo ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@ProductoId", productoId);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        AgregarParametro(cmd, "@FiltroUbicacion", "%" + filtroUbicacionTexto.Trim() + "%");
                    }

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
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
        // 8. CONSULTAR MOVIMIENTOS POR ENTIDAD GENERAL (SIN PRODUCTO)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarMovimientosPorEntidadGeneralAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            string? filtroEntidadTexto = null,
            int? categoriaProductoId = null,
            int almacenId = 1)
        {
            var reporte = new ConsultaMovimientoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                string exprFechaOrden = QueryAdapter.EsMySQL
                    ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                    : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

                // 1. TABLA IZQUIERDA: MOVIMIENTOS GENERALES
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    m.id,
    m.fecha_movimiento,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
    COALESCE(
        pc.razon_social,
        u.descripcion,
        CASE 
            WHEN mp.tipo_movimiento_id = 1 AND m.motivo_producto_id = 4 THEN alm_orig.nombre
            WHEN mp.tipo_movimiento_id = 2 AND m.motivo_producto_id = 10 THEN alm_dest.nombre
            ELSE NULL 
        END,
        'ALMACÉN'
    ) AS entidad_nombre,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE 0 END AS cantidad_ingreso,
    CASE WHEN mp.tipo_movimiento_id = 2 THEN md.cantidad_salida ELSE 0 END AS cantidad_salida,
    m.estado_id,
    m.motivo_producto_id,
    mp.tipo_movimiento_id,
    COALESCE(p.abreviatura, p.descripcion) AS producto_nombre,
    COALESCE(m.almacen_destino_id, COALESCE(m.almacen_origen_id, m.almacen_id)) AS alm_relacionado_id,
    m.created_at,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN productos p {nolock} ON md.producto_id = p.id
LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
LEFT JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
LEFT JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
LEFT JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroEntidadTexto))
                    {
                        queryRaw += " AND (pc.razon_social LIKE @FiltroEntidad OR u.descripcion LIKE @FiltroEntidad OR alm_orig.nombre LIKE @FiltroEntidad OR alm_dest.nombre LIKE @FiltroEntidad)";
                    }

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                    {
                        queryRaw += " AND (rc.categoria_producto_id = @CategoriaId OR rc.categoria_producto_id IS NULL)";
                    }

                    queryRaw += $" ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroEntidadTexto))
                        AgregarParametro(cmd, "@FiltroEntidad", "%" + filtroEntidadTexto.Trim() + "%");

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        AgregarParametro(cmd, "@CategoriaId", categoriaProductoId.Value);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int estadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id"));
                            bool anulado = (estadoId == 2);
                            int almRelId = reader.IsDBNull(reader.GetOrdinal("alm_relacionado_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("alm_relacionado_id"));

                            DateTime? fechaMovRaw = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("fecha_movimiento"));
                            DateTime? createdAtRaw = reader.IsDBNull(reader.GetOrdinal("created_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("created_at"));

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            string prodNombre = reader["producto_nombre"].ToString() ?? "";
                            string entNombre = reader["entidad_nombre"].ToString() ?? "ALMACÉN";

                            reporte.Movimientos.Add(new ConsultaMovimientoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Fecha = fechaMovFinal ?? DateTime.MinValue,
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + reader["registro"].ToString(),
                                RazonSocialUbicacion = $"[{prodNombre}] - {entNombre}",
                                NumeroGuia = reader["guia"].ToString() ?? "",
                                Ingreso = reader.GetDecimal(reader.GetOrdinal("cantidad_ingreso")),
                                Salida = reader.GetDecimal(reader.GetOrdinal("cantidad_salida")),
                                IsAnulado = anulado,
                                AlmacenId = almRelId,
                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(reader.GetOrdinal("usuario_creador")) ? "" : reader["usuario_creador"].ToString()!,
                                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                                UsuarioModificador = reader.IsDBNull(reader.GetOrdinal("usuario_modificador")) ? null : reader["usuario_modificador"].ToString()
                            });
                        }
                    }
                }

                // 2. TABLA DERECHA: CÓDIGOS ASOCIADOS
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    cc.codigo AS codigo,
    CONCAT(COALESCE(p.abreviatura, p.descripcion), ' - ', COALESCE(cat.nombre, 'SIN TIPO')) AS coleccion_tipo,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN productos p {nolock} ON md.producto_id = p.id
INNER JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
LEFT JOIN categoria_producto cat {nolock} ON rc.categoria_producto_id = cat.id
LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
LEFT JOIN almacenes alm_dest {nolock} ON m.almacen_destino_id = alm_dest.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroEntidadTexto))
                    {
                        queryRaw += " AND (pc.razon_social LIKE @FiltroEntidad OR u.descripcion LIKE @FiltroEntidad OR alm_orig.nombre LIKE @FiltroEntidad OR alm_dest.nombre LIKE @FiltroEntidad)";
                    }

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                    {
                        queryRaw += " AND rc.categoria_producto_id = @CategoriaId";
                    }

                    queryRaw += " ORDER BY codigo ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroEntidadTexto))
                        AgregarParametro(cmd, "@FiltroEntidad", "%" + filtroEntidadTexto.Trim() + "%");

                    if (categoriaProductoId.HasValue && categoriaProductoId.Value > 0)
                        AgregarParametro(cmd, "@CategoriaId", categoriaProductoId.Value);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Codigo = reader["codigo"].ToString() ?? "N/A",
                                ColeccionTipo = reader["coleccion_tipo"].ToString() ?? "SIN TIPO",
                                NumeroRegistro = reader["numero_registro"].ToString() ?? "",
                                TipoMovimiento = reader["tipo_mov"].ToString() ?? ""
                            });
                        }
                    }
                }
            }

            return reporte;
        }

        // =========================================================
        // 9. CONSULTAR KÁRDEX POR UBICACIÓN (SIN PRODUCTO)
        // =========================================================
        public async Task<ConsultaMovimientoReporte> ConsultarKardexPorUbicacionSinProductoAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            string? filtroUbicacionTexto = null,
            int almacenId = 1)
        {
            var reporte = new ConsultaMovimientoReporte();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                string exprFechaOrden = QueryAdapter.EsMySQL
                    ? "COALESCE(TIMESTAMP(DATE(m.fecha_movimiento), TIME(m.created_at)), m.fecha_movimiento)"
                    : "DATEADD(day, DATEDIFF(day, 0, m.fecha_movimiento), CAST(CAST(COALESCE(m.created_at, m.fecha_movimiento) AS TIME) AS DATETIME))";

                // 1. TABLA IZQUIERDA: MOVIMIENTOS
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    m.id,
    m.fecha_movimiento,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
    u.descripcion AS ubicacion_nombre,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE 0 END AS cantidad_ingreso,
    CASE WHEN mp.tipo_movimiento_id = 2 THEN md.cantidad_salida ELSE 0 END AS cantidad_salida,
    m.estado_id,
    m.motivo_producto_id,
    mp.tipo_movimiento_id,
    COALESCE(p.abreviatura, p.descripcion) AS producto_nombre,
    m.created_at,
    COALESCE(usr_c.nombres, CAST(m.usuario_id AS CHAR)) AS usuario_creador,
    m.updated_at,
    usr_u.nombres AS usuario_modificador,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN productos p {nolock} ON md.producto_id = p.id
INNER JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN usuarios usr_c {nolock} ON m.usuario_id = usr_c.id
LEFT JOIN usuarios usr_u {nolock} ON m.usuario_update_id = usr_u.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        queryRaw += " AND u.descripcion LIKE @FiltroUbicacion";
                    }

                    queryRaw += $" ORDER BY {exprFechaOrden} ASC, m.id ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                        AgregarParametro(cmd, "@FiltroUbicacion", "%" + filtroUbicacionTexto.Trim() + "%");

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            int estadoId = reader.IsDBNull(7) ? 1 : reader.GetInt32(7);
                            bool anulado = (estadoId == 2);

                            DateTime? fechaMovRaw = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                            DateTime? createdAtRaw = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11);

                            DateTime? fechaMovFinal = fechaMovRaw;
                            if (fechaMovRaw.HasValue && createdAtRaw.HasValue && fechaMovRaw.Value.TimeOfDay == TimeSpan.Zero)
                            {
                                fechaMovFinal = fechaMovRaw.Value.Date.Add(createdAtRaw.Value.TimeOfDay);
                            }

                            string prodNombre = Convert.ToString(reader.GetValue(10)) ?? "";
                            string ubicNombre = Convert.ToString(reader.GetValue(3)) ?? "SIN UBICACIÓN";

                            reporte.Movimientos.Add(new ConsultaMovimientoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
                                Fecha = fechaMovFinal ?? DateTime.MinValue,
                                NumeroRegistro = (anulado ? "❌ ANULADO - " : "") + Convert.ToString(reader.GetValue(2)),
                                RazonSocialUbicacion = $"[{prodNombre}] - {ubicNombre}",
                                NumeroGuia = Convert.ToString(reader.GetValue(4)) ?? "",
                                Ingreso = reader.GetDecimal(5),
                                Salida = reader.GetDecimal(6),
                                IsAnulado = anulado,
                                CreatedAt = createdAtRaw,
                                UsuarioCreador = reader.IsDBNull(12) ? "" : Convert.ToString(reader.GetValue(12))!,
                                UpdatedAt = reader.IsDBNull(13) ? (DateTime?)null : reader.GetDateTime(13),
                                UsuarioModificador = reader.IsDBNull(14) ? null : Convert.ToString(reader.GetValue(14))
                            });
                        }
                    }
                }

                // 2. TABLA DERECHA: CÓDIGOS ASOCIADOS
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string queryRaw = $@"
SELECT DISTINCT
    cc.codigo AS codigo,
    CONCAT(COALESCE(p.abreviatura, p.descripcion), ' - ', COALESCE(cat.nombre, 'SIN TIPO')) AS coleccion_tipo,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN 'ENTRADA' ELSE 'SALIDA' END AS tipo_mov,
    md.id AS movimiento_detalle_id,
    md.producto_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
INNER JOIN productos p {nolock} ON md.producto_id = p.id
INNER JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
INNER JOIN movimiento_codigos mc {nolock} ON mc.movimiento_detalle_id = md.id
INNER JOIN codigos_creados cc {nolock} ON mc.codigo_creado_id = cc.id
INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
LEFT JOIN categoria_producto cat {nolock} ON rc.categoria_producto_id = cat.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
  AND (
     (mp.tipo_movimiento_id = 1 AND COALESCE(m.almacen_destino_id, 1) = @AlmacenId) OR
     (mp.tipo_movimiento_id = 2 AND COALESCE(m.almacen_origen_id, 1) = @AlmacenId)
  )";

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                    {
                        queryRaw += " AND u.descripcion LIKE @FiltroUbicacion";
                    }

                    queryRaw += " ORDER BY codigo ASC";

                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);
                    AgregarParametro(cmd, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmd, "@FechaHasta", fechaHastaFinDia);
                    AgregarParametro(cmd, "@AlmacenId", almacenId);

                    if (!string.IsNullOrWhiteSpace(filtroUbicacionTexto))
                        AgregarParametro(cmd, "@FiltroUbicacion", "%" + filtroUbicacionTexto.Trim() + "%");

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            reporte.Codigos.Add(new ConsultaCodigoItem
                            {
                                MovimientoDetalleId = reader.GetInt32(reader.GetOrdinal("movimiento_detalle_id")),
                                ProductoId = reader.GetInt32(reader.GetOrdinal("producto_id")),
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
        // 10. OBTENER DATOS PARA MATRIZ AVANZADA (3 BLOQUES + ORDEN ACADÉMICO)
        // =========================================================


        // Reemplazar la firma y retorno del método en KardexService.cs:
        public async Task<(
            List<UbicacionMatrizDTO> UbicacionesComerciales,
            List<UbicacionMatrizDTO> AlmacenesReales,
            List<ProductoColumnaDTO> CatalogoProductos,
            List<Almacen> AlmacenesRegistrados,
            Dictionary<int, decimal> IngresosCentralPorProducto)> ObtenerDatosMatrizConsolidadaCompletaAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            int? filtroTipoEdicion = null,
            int? ubicacionIdFiltro = null,
            int almacenId = 1)
        {
            var ubicacionesMap = new Dictionary<int, UbicacionMatrizDTO>();
            var almacenesRealesMap = new Dictionary<int, UbicacionMatrizDTO>();
            var catalogo = new List<ProductoColumnaDTO>();
            var almacenesList = new List<Almacen>();
            var ingresosCentralMap = new Dictionary<int, decimal>();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

                // =========================================================================
                // 🌟 1. CÁLCULO DE INGRESOS REALES DE ALMACÉN CENTRAL (SALDO INICIAL GENERAL)
                // Perspectiva Central: Compras (motivo 1), Stock Inicial (motivo 13) u Otros Ingresos (tipo 1 sin ser dev)
                // =========================================================================
                using (IDbCommand cmdIngCentral = conn.CreateCommand())
                {
                    string qIngCentral = $@"
SELECT 
    md.producto_id,
    COALESCE(SUM(md.cantidad_ingreso), 0) AS total_ingreso_central
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
WHERE mp.tipo_movimiento_id = 1
  AND m.motivo_producto_id NOT IN (2, 3) -- Excluye devoluciones de clientes
  AND COALESCE(m.almacen_destino_id, COALESCE(m.almacen_id, 1)) = 1 -- Almacén Central
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
GROUP BY md.producto_id";

                    cmdIngCentral.CommandText = QueryAdapter.FormatearConsulta(qIngCentral);
                    AgregarParametro(cmdIngCentral, "@FechaHasta", fechaHastaFinDia);

                    using var rdrIng = await ((DbCommand)cmdIngCentral).ExecuteReaderAsync();
                    while (await ((DbDataReader)rdrIng).ReadAsync())
                    {
                        ingresosCentralMap[rdrIng.GetInt32(0)] = rdrIng.GetDecimal(1);
                    }
                }

                // --- 2. CARGA DE ALMACENES REALES (Igual que antes) ---
                using (IDbCommand cmdAlm = conn.CreateCommand())
                {
                    string qAlm = $"SELECT id, nombre, codigo, direccion, estado_id FROM almacenes {nolock} WHERE estado_id = 1 ORDER BY id ASC";
                    cmdAlm.CommandText = QueryAdapter.FormatearConsulta(qAlm);
                    using var rdrAlm = await ((DbCommand)cmdAlm).ExecuteReaderAsync();
                    while (await ((DbDataReader)rdrAlm).ReadAsync())
                    {
                        int aId = rdrAlm.GetInt32(0);
                        string aNom = rdrAlm.GetString(1);
                        almacenesList.Add(new Almacen
                        {
                            Id = aId,
                            Nombre = aNom,
                            Codigo = rdrAlm.IsDBNull(2) ? "" : rdrAlm.GetString(2),
                            Direccion = rdrAlm.IsDBNull(3) ? "" : rdrAlm.GetString(3),
                            EstadoId = rdrAlm.IsDBNull(4) ? 1 : rdrAlm.GetInt32(4)
                        });

                        almacenesRealesMap[aId] = new UbicacionMatrizDTO
                        {
                            UbicacionId = aId,
                            Nombre = aNom.ToUpper().Trim(),
                            TipoUbicacionId = 1,
                            TipoUbicacionNombre = "ALMACEN REAL"
                        };
                    }
                }

                // --- 3. CARGA DE PRODUCTOS (Igual que antes) ---
                using (IDbCommand cmdProd = conn.CreateCommand())
                {
                    string qProd = $@"
SELECT 
    p.id,
    p.abreviatura,
    p.descripcion,
    p.nivel_id,
    COALESCE(n.nombre, 'OTROS') AS nivel_nombre,
    COALESCE(p.grado_id, 0) AS grado_id,
    COALESCE(g.nombre, '') AS grado_nombre,
    COALESCE(p.titulo_curso_id, 0) AS titulo_curso_id,
    COALESCE(tc.nombre, 'VARIOS') AS titulo_curso_nombre
FROM productos p {nolock}
LEFT JOIN niveles n {nolock} ON p.nivel_id = n.id
LEFT JOIN grados g {nolock} ON p.grado_id = g.id
LEFT JOIN titulo_curso tc {nolock} ON p.titulo_curso_id = tc.id
WHERE p.estado_id = 1
ORDER BY 
    CASE WHEN p.nivel_id IS NULL THEN 1 ELSE 0 END ASC,
    p.nivel_id ASC, 
    CASE 
        WHEN g.nombre LIKE '%2%' THEN 2
        WHEN g.nombre LIKE '%3%' THEN 3
        WHEN g.nombre LIKE '%4%' THEN 4
        WHEN g.nombre LIKE '%5%' THEN 5
        WHEN g.nombre LIKE '%1%' THEN 6
        WHEN g.nombre LIKE '%6%' THEN 11
        ELSE 99 
    END ASC,
    p.titulo_curso_id ASC,
    p.grado_id ASC, 
    p.id ASC";

                    cmdProd.CommandText = QueryAdapter.FormatearConsulta(qProd);
                    using var rdrProd = await ((DbCommand)cmdProd).ExecuteReaderAsync();
                    while (await ((DbDataReader)rdrProd).ReadAsync())
                    {
                        int pId = rdrProd.GetInt32(0);
                        string abrevRaw = rdrProd.IsDBNull(1) ? "" : Convert.ToString(rdrProd.GetValue(1)) ?? "";
                        string desc = Convert.ToString(rdrProd.GetValue(2)) ?? "";
                        bool tieneNivel = !rdrProd.IsDBNull(3);
                        string nivelNombre = Convert.ToString(rdrProd.GetValue(4)) ?? "OTROS";
                        int gradoId = rdrProd.GetInt32(5);
                        string gradoNombre = Convert.ToString(rdrProd.GetValue(6)) ?? "";
                        string tituloCursoNombre = Convert.ToString(rdrProd.GetValue(8)) ?? "VARIOS";

                        bool esArticuloSinCodigo = string.IsNullOrWhiteSpace(abrevRaw) || !tieneNivel;

                        if (esArticuloSinCodigo)
                        {
                            catalogo.Add(new ProductoColumnaDTO
                            {
                                ProductoId = pId,
                                Codigo = desc.ToUpper().Trim(),
                                Descripcion = desc,
                                NivelId = 99,
                                NivelNombre = "OTROS",
                                GradoId = 99,
                                GradoNombre = "",
                                FamiliaNombre = "VARIOS",
                                TipoEdicion = "G"
                            });
                            continue;
                        }

                        string abrevUp = abrevRaw.ToUpper();
                        string tipoEdicion = (abrevUp.Contains("-V-V") || abrevUp.Contains("-V") || desc.ToUpper().Contains("VENTA")) ? "V" : "G";
                        if (filtroTipoEdicion == 1 && tipoEdicion != "G") continue;
                        if (filtroTipoEdicion == 2 && tipoEdicion != "V") continue;

                        catalogo.Add(new ProductoColumnaDTO
                        {
                            ProductoId = pId,
                            Codigo = abrevRaw,
                            Descripcion = desc,
                            NivelId = rdrProd.GetInt32(3),
                            NivelNombre = nivelNombre.ToUpper().Trim(),
                            GradoId = gradoId,
                            GradoNombre = gradoNombre,
                            FamiliaNombre = tituloCursoNombre.ToUpper().Trim(),
                            TipoEdicion = tipoEdicion
                        });
                    }
                }

                // --- 4. CARGA DE TODAS LAS UBICACIONES (Igual que antes) ---
                using (IDbCommand cmdUbi = conn.CreateCommand())
                {
                    string qUbi = $@"
SELECT 
    u.id, 
    u.descripcion, 
    COALESCE(u.tipo_ubicacion_id, 3) AS tipo_ubicacion_id, 
    COALESCE(tu.nombre, 'ZONA DE PROMOTORIA') AS tipo_nombre 
FROM ubicaciones u {nolock}
LEFT JOIN tipo_ubicacion tu {nolock} ON u.tipo_ubicacion_id = tu.id
WHERE u.estado_id = 1
ORDER BY u.tipo_ubicacion_id ASC, u.descripcion ASC";

                    cmdUbi.CommandText = QueryAdapter.FormatearConsulta(qUbi);
                    using var rdrUbi = await ((DbCommand)cmdUbi).ExecuteReaderAsync();
                    while (await ((DbDataReader)rdrUbi).ReadAsync())
                    {
                        int uId = rdrUbi.GetInt32(0);
                        if (ubicacionIdFiltro.HasValue && ubicacionIdFiltro.Value > 0 && uId != ubicacionIdFiltro.Value)
                            continue;

                        ubicacionesMap[uId] = new UbicacionMatrizDTO
                        {
                            UbicacionId = uId,
                            Nombre = Convert.ToString(rdrUbi.GetValue(1)) ?? "",
                            TipoUbicacionId = rdrUbi.GetInt32(2),
                            TipoUbicacionNombre = Convert.ToString(rdrUbi.GetValue(3)) ?? "ZONA DE PROMOTORIA"
                        };
                    }
                }

                // --- 5. CARGA DE MOVIMIENTOS Y PERSPECTIVA DE INGRESO PARA SEDES ---
                using (IDbCommand cmdMov = conn.CreateCommand())
                {
                    string qMov = $@"
SELECT 
    m.id,
    m.motivo_producto_id,
    mp.tipo_movimiento_id,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS numero_registro,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS numero_guia,
    COALESCE(alm_orig.nombre, COALESCE(alm_base.nombre, 'ALMACEN PRINCIPAL TRUJILLO')) AS almacen_origen_nombre,
    COALESCE(m.almacen_origen_id, COALESCE(m.almacen_id, 1)) AS alm_origen_id,
    COALESCE(m.almacen_destino_id, COALESCE(m.almacen_id, 1)) AS alm_destino_id,
    m.fecha_movimiento,
    md.producto_id,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE md.cantidad_salida END AS cantidad,
    m.ubicacion_id
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
LEFT JOIN almacenes alm_base {nolock} ON m.almacen_id = alm_base.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 2
ORDER BY m.fecha_movimiento ASC, m.id ASC";

                    cmdMov.CommandText = QueryAdapter.FormatearConsulta(qMov);
                    AgregarParametro(cmdMov, "@FechaDesde", fechaDesde.Date);
                    AgregarParametro(cmdMov, "@FechaHasta", fechaHastaFinDia);

                    using var rdrMov = await ((DbCommand)cmdMov).ExecuteReaderAsync();
                    while (await ((DbDataReader)rdrMov).ReadAsync())
                    {
                        int movId = rdrMov.GetInt32(0);
                        int motivoId = rdrMov.GetInt32(1);
                        int tipoMovId = rdrMov.GetInt32(2); // 1: Entrada, 2: Salida
                        string reg = Convert.ToString(rdrMov.GetValue(3)) ?? "";
                        string guia = Convert.ToString(rdrMov.GetValue(4)) ?? "";
                        string almOrigenNom = Convert.ToString(rdrMov.GetValue(5)) ?? "ALMACEN PRINCIPAL TRUJILLO";
                        int almOrigenId = rdrMov.GetInt32(6);
                        int almDestinoId = rdrMov.GetInt32(7);
                        DateTime fec = rdrMov.IsDBNull(8) ? DateTime.MinValue : rdrMov.GetDateTime(8);
                        int prodId = rdrMov.GetInt32(9);
                        decimal cant = rdrMov.GetDecimal(10);
                        int? uId = rdrMov.IsDBNull(11) ? (int?)null : rdrMov.GetInt32(11);

                        // =========================================================================
                        // 🌟 A) UBICACIONES COMERCIALES (PROMOTORES Y DISTRIBUIDORES)
                        // =========================================================================
                        if (uId.HasValue && ubicacionesMap.ContainsKey(uId.Value))
                        {
                            int bloqueUbi = (tipoMovId == 1) ? ((motivoId == 2 || motivoId == 3) ? 3 : 1) : 2;
                            ubicacionesMap[uId.Value].Movimientos.Add(new MatrizKardexItemDTO
                            {
                                MovimientoId = movId,
                                BloqueTipo = bloqueUbi,
                                OrdenDocumento = reg,
                                NumeroGuia = guia,
                                OrigenAlmacenId = almOrigenId, // 👈 Asignar ID
                                OrigenAlmacen = almOrigenNom,
                                Fecha = fec,
                                ProductoId = prodId,
                                Cantidad = cant
                            });
                        }

                        // =========================================================================
                        // 🌟 B) SEDES REALES (ALMACENES FÍSICOS)
                        // =========================================================================
                        if (tipoMovId == 1) // ENTRADA
                        {
                            if (almacenesRealesMap.ContainsKey(almDestinoId))
                            {
                                // Bloque 1: Ingreso (compras/inicial en central)
                                // Bloque 3: Devoluciones recibidas
                                int bloque = (motivoId == 2 || motivoId == 3) ? 3 : 1;

                                almacenesRealesMap[almDestinoId].Movimientos.Add(new MatrizKardexItemDTO
                                {
                                    MovimientoId = movId,
                                    BloqueTipo = bloque,
                                    OrdenDocumento = reg,
                                    NumeroGuia = guia,
                                    OrigenAlmacenId = almOrigenId, // 👈 Asignar ID
                                    OrigenAlmacen = almOrigenNom,
                                    Fecha = fec,
                                    ProductoId = prodId,
                                    Cantidad = cant
                                });
                            }
                        }
                        else if (tipoMovId == 2) // SALIDA
                        {
                            // Transferencia entre sedes: entra una sola vez en la hoja receptora (Destino)
                            if (almDestinoId != almOrigenId && almacenesRealesMap.ContainsKey(almDestinoId))
                            {
                                almacenesRealesMap[almDestinoId].Movimientos.Add(new MatrizKardexItemDTO
                                {
                                    MovimientoId = movId,
                                    BloqueTipo = 2, // Bloque SALIDAS
                                    OrdenDocumento = reg,
                                    NumeroGuia = guia,
                                    OrigenAlmacen = almOrigenNom,
                                    Fecha = fec,
                                    ProductoId = prodId,
                                    Cantidad = cant
                                });
                            }
                        }
                    }
                }
            }

            return (ubicacionesMap.Values.ToList(), almacenesRealesMap.Values.ToList(), catalogo, almacenesList, ingresosCentralMap);
        }

    }
}