using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Transferencias;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class TransaccionesService
    {
        private readonly DatabaseConnection _database;

        public TransaccionesService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =========================================================================
        // 1. CONTAR TRANSFERENCIAS PENDIENTES DE RECEPCIÓN
        // =========================================================================
        public async Task<int> ContarTransferenciasPendientesAsync(int miAlmacenId)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query;
                if (QueryAdapter.EsMySQL)
                {
                    query = @"
                        SELECT COUNT(DISTINCT m.id)
                        FROM movimientos m
                        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        WHERE m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2 -- Solo Salidas de Transferencia
                          AND m.motivo_producto_id IN (4, 10)
                          AND m.almacen_destino_id = @miAlmacen
                          AND COALESCE(m.almacen_id, m.almacen_origen_id, 1) != @miAlmacen
                          AND NOT EXISTS (
                              SELECT 1 
                              FROM movimientos m_ing
                              INNER JOIN motivo_productos mp_ing ON m_ing.motivo_producto_id = mp_ing.id
                              INNER JOIN movimiento_detalles md_ing ON md_ing.movimiento_id = m_ing.id
                              INNER JOIN movimiento_codigos mc_ing ON mc_ing.movimiento_detalle_id = md_ing.id
                              WHERE mp_ing.tipo_movimiento_id = 1 -- Entrada
                                AND m_ing.motivo_producto_id IN (4, 10)
                                AND COALESCE(m_ing.almacen_id, m_ing.almacen_destino_id, 1) = @miAlmacen
                                AND mc_ing.codigo_creado_id IN (
                                    SELECT mc_sub.codigo_creado_id 
                                    FROM movimiento_detalles md_sub
                                    INNER JOIN movimiento_codigos mc_sub ON mc_sub.movimiento_detalle_id = md_sub.id
                                    WHERE md_sub.movimiento_id = m.id
                                )
                          )";
                }
                else
                {
                    query = @"
                        SELECT COUNT(DISTINCT m.id)
                        FROM movimientos m WITH (NOLOCK)
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        WHERE m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2
                          AND m.motivo_producto_id IN (4, 10)
                          AND m.almacen_destino_id = @miAlmacen
                          AND ISNULL(m.almacen_id, ISNULL(m.almacen_origen_id, 1)) != @miAlmacen
                          AND NOT EXISTS (
                              SELECT 1 
                              FROM movimientos m_ing WITH (NOLOCK)
                              INNER JOIN motivo_productos mp_ing WITH (NOLOCK) ON m_ing.motivo_producto_id = mp_ing.id
                              INNER JOIN movimiento_detalles md_ing WITH (NOLOCK) ON md_ing.movimiento_id = m_ing.id
                              INNER JOIN movimiento_codigos mc_ing WITH (NOLOCK) ON mc_ing.movimiento_detalle_id = md_ing.id
                              WHERE mp_ing.tipo_movimiento_id = 1
                                AND m_ing.motivo_producto_id IN (4, 10)
                                AND ISNULL(m_ing.almacen_id, ISNULL(m_ing.almacen_destino_id, 1)) = @miAlmacen
                                AND mc_ing.codigo_creado_id IN (
                                    SELECT mc_sub.codigo_creado_id 
                                    FROM movimiento_detalles md_sub WITH (NOLOCK)
                                    INNER JOIN movimiento_codigos mc_sub WITH (NOLOCK) ON mc_sub.movimiento_detalle_id = md_sub.id
                                    WHERE md_sub.movimiento_id = m.id
                                )
                          )";
                }

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);

                object result = await cmd.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error contando transferencias pendientes: {ex.Message}");
                return 0;
            }
        }

        // =========================================================================
        // 2. OBTENER HISTORIAL COMPLETO DE TRANSFERENCIAS (ENVIADOS Y RECIBIDOS)
        // =========================================================================
        public async Task<List<TransaccionHeaderDTO>> ObtenerHistorialTransferenciasAsync(
            int miAlmacenId,
            DateTime? desde = null,
            DateTime? hasta = null,
            string? estadoFiltro = "TODOS")
        {
            var lista = new List<TransaccionHeaderDTO>();

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query;
                if (QueryAdapter.EsMySQL)
                {
                    query = @"
                        SELECT 
                            m.id AS MovimientoSalidaId,
                            CONCAT(m.serie_documento, '-', m.numero_documento) AS SerieNumeroSalida,
                            COALESCE(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS GuiaRemision,
                            m.fecha_movimiento,
                            COALESCE(m.almacen_origen_id, m.almacen_id) AS AlmacenOrigenId,
                            COALESCE(ao.nombre, 'ALMACÉN CENTRAL') AS OrigenNombre,
                            COALESCE(m.almacen_destino_id, 1) AS AlmacenDestinoId,
                            COALESCE(ad.nombre, 'ALMACÉN CENTRAL') AS DestinoNombre,
                            COALESCE(u.nombres, 'SISTEMA') AS EmisorNombre,
                            COALESCE(mp.descripcion, 'TRANSFERENCIA') AS MotivoDesc,
                            COALESCE(m.observacion, '') AS Observacion,
                            COUNT(DISTINCT md.producto_id) AS TotalProductos,
                            COUNT(mc.id) AS TotalCodigos,
                            
                            CASE 
                                WHEN EXISTS (
                                    SELECT 1 
                                    FROM movimientos m_ing
                                    INNER JOIN motivo_productos mp_ing ON m_ing.motivo_producto_id = mp_ing.id
                                    INNER JOIN movimiento_detalles md_ing ON md_ing.movimiento_id = m_ing.id
                                    INNER JOIN movimiento_codigos mc_ing ON mc_ing.movimiento_detalle_id = md_ing.id
                                    WHERE mp_ing.tipo_movimiento_id = 1
                                      AND m_ing.motivo_producto_id IN (4, 10)
                                      AND COALESCE(m_ing.almacen_id, m_ing.almacen_destino_id, 1) = m.almacen_destino_id
                                      AND mc_ing.codigo_creado_id IN (
                                          SELECT mc_sub.codigo_creado_id 
                                          FROM movimiento_detalles md_sub
                                          INNER JOIN movimiento_codigos mc_sub ON mc_sub.movimiento_detalle_id = md_sub.id
                                          WHERE md_sub.movimiento_id = m.id
                                      )
                                ) THEN 0 
                                ELSE 1   
                            END AS EsPendiente,

                            (
                                SELECT m_ing.id 
                                FROM movimientos m_ing
                                INNER JOIN motivo_productos mp_ing ON m_ing.motivo_producto_id = mp_ing.id
                                INNER JOIN movimiento_detalles md_ing ON md_ing.movimiento_id = m_ing.id
                                INNER JOIN movimiento_codigos mc_ing ON mc_ing.movimiento_detalle_id = md_ing.id
                                WHERE mp_ing.tipo_movimiento_id = 1
                                  AND m_ing.motivo_producto_id IN (4, 10)
                                  AND COALESCE(m_ing.almacen_id, m_ing.almacen_destino_id, 1) = m.almacen_destino_id
                                  AND mc_ing.codigo_creado_id IN (
                                      SELECT mc_sub.codigo_creado_id 
                                      FROM movimiento_detalles md_sub
                                      INNER JOIN movimiento_codigos mc_sub ON mc_sub.movimiento_detalle_id = md_sub.id
                                      WHERE md_sub.movimiento_id = m.id
                                  )
                                ORDER BY m_ing.id DESC
                                LIMIT 1
                            ) AS MovimientoEntradaId,

                            (
                                SELECT CONCAT(m_ing2.serie_documento, '-', m_ing2.numero_documento)
                                FROM movimientos m_ing2
                                INNER JOIN motivo_productos mp_ing2 ON m_ing2.motivo_producto_id = mp_ing2.id
                                INNER JOIN movimiento_detalles md_ing2 ON md_ing2.movimiento_id = m_ing2.id
                                INNER JOIN movimiento_codigos mc_ing2 ON mc_ing2.movimiento_detalle_id = md_ing2.id
                                WHERE mp_ing2.tipo_movimiento_id = 1
                                  AND m_ing2.motivo_producto_id IN (4, 10)
                                  AND COALESCE(m_ing2.almacen_id, m_ing2.almacen_destino_id, 1) = m.almacen_destino_id
                                  AND mc_ing2.codigo_creado_id IN (
                                      SELECT mc_sub.codigo_creado_id 
                                      FROM movimiento_detalles md_sub
                                      INNER JOIN movimiento_codigos mc_sub ON mc_sub.movimiento_detalle_id = md_sub.id
                                      WHERE md_sub.movimiento_id = m.id
                                  )
                                ORDER BY m_ing2.id DESC
                                LIMIT 1
                            ) AS SerieNumeroEntrada

                        FROM movimientos m
                        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        LEFT JOIN almacenes ao ON COALESCE(m.almacen_origen_id, m.almacen_id) = ao.id
                        LEFT JOIN almacenes ad ON m.almacen_destino_id = ad.id
                        LEFT JOIN usuarios u ON m.usuario_id = u.id
                        INNER JOIN movimiento_detalles md ON md.movimiento_id = m.id
                        INNER JOIN movimiento_codigos mc ON mc.movimiento_detalle_id = md.id
                        WHERE m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2
                          AND (m.motivo_producto_id IN (4, 10))
                          AND (COALESCE(m.almacen_origen_id, m.almacen_id) = @miAlmacen OR m.almacen_destino_id = @miAlmacen)";
                }
                else
                {
                    query = @"
                        SELECT 
                            m.id AS MovimientoSalidaId,
                            CONCAT(m.serie_documento, '-', m.numero_documento) AS SerieNumeroSalida,
                            ISNULL(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS GuiaRemision,
                            m.fecha_movimiento,
                            ISNULL(m.almacen_origen_id, m.almacen_id) AS AlmacenOrigenId,
                            ISNULL(ao.nombre, 'ALMACÉN CENTRAL') AS OrigenNombre,
                            ISNULL(m.almacen_destino_id, 1) AS AlmacenDestinoId,
                            ISNULL(ad.nombre, 'ALMACÉN CENTRAL') AS DestinoNombre,
                            ISNULL(u.nombres, 'SISTEMA') AS EmisorNombre,
                            ISNULL(mp.descripcion, 'TRANSFERENCIA') AS MotivoDesc,
                            ISNULL(m.observacion, '') AS Observacion,
                            COUNT(DISTINCT md.producto_id) AS TotalProductos,
                            COUNT(mc.id) AS TotalCodigos,
                            
                            CASE 
                                WHEN EXISTS (
                                    SELECT 1 
                                    FROM movimientos m_ing WITH (NOLOCK)
                                    INNER JOIN motivo_productos mp_ing WITH (NOLOCK) ON m_ing.motivo_producto_id = mp_ing.id
                                    INNER JOIN movimiento_detalles md_ing WITH (NOLOCK) ON md_ing.movimiento_id = m_ing.id
                                    INNER JOIN movimiento_codigos mc_ing WITH (NOLOCK) ON mc_ing.movimiento_detalle_id = md_ing.id
                                    WHERE mp_ing.tipo_movimiento_id = 1
                                      AND m_ing.motivo_producto_id IN (4, 10)
                                      AND ISNULL(m_ing.almacen_id, ISNULL(m_ing.almacen_destino_id, 1)) = m.almacen_destino_id
                                      AND mc_ing.codigo_creado_id IN (
                                          SELECT mc_sub.codigo_creado_id 
                                          FROM movimiento_detalles md_sub WITH (NOLOCK)
                                          INNER JOIN movimiento_codigos mc_sub WITH (NOLOCK) ON mc_sub.movimiento_detalle_id = md_sub.id
                                          WHERE md_sub.movimiento_id = m.id
                                      )
                                ) THEN 0
                                ELSE 1
                            END AS EsPendiente,

                            (
                                SELECT TOP 1 m_ing.id 
                                FROM movimientos m_ing WITH (NOLOCK)
                                INNER JOIN motivo_productos mp_ing WITH (NOLOCK) ON m_ing.motivo_producto_id = mp_ing.id
                                INNER JOIN movimiento_detalles md_ing WITH (NOLOCK) ON md_ing.movimiento_id = m_ing.id
                                INNER JOIN movimiento_codigos mc_ing WITH (NOLOCK) ON mc_ing.movimiento_detalle_id = md_ing.id
                                WHERE mp_ing.tipo_movimiento_id = 1
                                  AND m_ing.motivo_producto_id IN (4, 10)
                                  AND ISNULL(m_ing.almacen_id, ISNULL(m_ing.almacen_destino_id, 1)) = m.almacen_destino_id
                                  AND mc_ing.codigo_creado_id IN (
                                      SELECT mc_sub.codigo_creado_id 
                                      FROM movimiento_detalles md_sub WITH (NOLOCK)
                                      INNER JOIN movimiento_codigos mc_sub WITH (NOLOCK) ON mc_sub.movimiento_detalle_id = md_sub.id
                                      WHERE md_sub.movimiento_id = m.id
                                  )
                                ORDER BY m_ing.id DESC
                            ) AS MovimientoEntradaId,

                            (
                                SELECT TOP 1 CONCAT(m_ing2.serie_documento, '-', m_ing2.numero_documento)
                                FROM movimientos m_ing2 WITH (NOLOCK)
                                INNER JOIN motivo_productos mp_ing2 WITH (NOLOCK) ON m_ing2.motivo_producto_id = mp_ing2.id
                                INNER JOIN movimiento_detalles md_ing2 WITH (NOLOCK) ON md_ing2.movimiento_id = m_ing2.id
                                INNER JOIN movimiento_codigos mc_ing2 WITH (NOLOCK) ON mc_ing2.movimiento_detalle_id = md_ing2.id
                                WHERE mp_ing2.tipo_movimiento_id = 1
                                  AND m_ing2.motivo_producto_id IN (4, 10)
                                  AND ISNULL(m_ing2.almacen_id, ISNULL(m_ing2.almacen_destino_id, 1)) = m.almacen_destino_id
                                  AND mc_ing2.codigo_creado_id IN (
                                      SELECT mc_sub.codigo_creado_id 
                                      FROM movimiento_detalles md_sub WITH (NOLOCK)
                                      INNER JOIN movimiento_codigos mc_sub WITH (NOLOCK) ON mc_sub.movimiento_detalle_id = md_sub.id
                                      WHERE md_sub.movimiento_id = m.id
                                  )
                                ORDER BY m_ing2.id DESC
                            ) AS SerieNumeroEntrada

                        FROM movimientos m WITH (NOLOCK)
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        LEFT JOIN almacenes ao WITH (NOLOCK) ON ISNULL(m.almacen_origen_id, m.almacen_id) = ao.id
                        LEFT JOIN almacenes ad WITH (NOLOCK) ON m.almacen_destino_id = ad.id
                        LEFT JOIN usuarios u WITH (NOLOCK) ON m.usuario_id = u.id
                        INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
                        INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
                        WHERE m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2
                          AND (m.motivo_producto_id IN (4, 10))
                          AND (ISNULL(m.almacen_origen_id, m.almacen_id) = @miAlmacen OR m.almacen_destino_id = @miAlmacen)";
                }

                if (desde.HasValue) query += " AND m.fecha_movimiento >= @desde";
                if (hasta.HasValue) query += " AND m.fecha_movimiento <= @hasta";

                query += @"
                        GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                                 m.fecha_movimiento, m.almacen_origen_id, m.almacen_id, ao.nombre, m.almacen_destino_id, 
                                 ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
                        ORDER BY m.created_at DESC";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);
                if (desde.HasValue) AgregarParametro(cmd, "@desde", desde.Value);
                if (hasta.HasValue) AgregarParametro(cmd, "@hasta", hasta.Value);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int movSalidaId = rdr.GetInt32(0);
                    string serieNumSalida = rdr.GetString(1);
                    string guia = rdr.GetString(2);
                    DateTime fecha = rdr.IsDBNull(3) ? DateTime.Today : rdr.GetDateTime(3);
                    int origId = rdr.GetInt32(4);
                    string origNom = rdr.GetString(5);
                    int destId = rdr.GetInt32(6);
                    string destNom = rdr.GetString(7);
                    string emisorNom = rdr.GetString(8);
                    string motivoDesc = rdr.GetString(9);
                    string obs = rdr.GetString(10);
                    int totProds = Convert.ToInt32(rdr.GetValue(11));
                    int totCods = Convert.ToInt32(rdr.GetValue(12));
                    bool esPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1;

                    int? movEntradaId = rdr.IsDBNull(14) ? (int?)null : rdr.GetInt32(14);
                    string serieNumEntrada = rdr.IsDBNull(15) ? string.Empty : rdr.GetString(15);

                    bool soyElEmisor = (origId == miAlmacenId);

                    string serieNumMostrar;
                    int idMovimientoAbrir;

                    if (soyElEmisor)
                    {
                        serieNumMostrar = serieNumSalida;
                        idMovimientoAbrir = movSalidaId;
                    }
                    else
                    {
                        if (esPendiente)
                        {
                            serieNumMostrar = "[ PENDIENTE ]";
                            idMovimientoAbrir = movSalidaId;
                        }
                        else
                        {
                            serieNumMostrar = string.IsNullOrEmpty(serieNumEntrada) ? serieNumSalida : serieNumEntrada;
                            idMovimientoAbrir = movEntradaId ?? movSalidaId;
                        }
                    }

                    if (estadoFiltro == "PENDIENTES" && !esPendiente) continue;
                    if (estadoFiltro == "RECEPCIONADOS" && esPendiente) continue;

                    lista.Add(new TransaccionHeaderDTO
                    {
                        MovimientoId = idMovimientoAbrir,
                        SerieNumero = serieNumMostrar,
                        GuiaRemision = guia,
                        FechaMovimiento = fecha,
                        AlmacenOrigenId = origId,
                        AlmacenOrigenNombre = origNom,
                        AlmacenDestinoId = destId,
                        AlmacenDestinoNombre = destNom,
                        UsuarioEmisorNombre = emisorNom,
                        MotivoDescripcion = motivoDesc,
                        Observacion = obs,
                        TotalProductos = totProds,
                        TotalCodigos = totCods,
                        EsPendiente = esPendiente,
                        SoyElEmisor = soyElEmisor
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar historial de transferencias: {ex.Message}");
            }

            return lista;
        }

        // =========================================================================
        // 3. OBTENER BANDEJA NOTIFICADORA (POPUP / TARJETAS)
        // =========================================================================
        public async Task<List<TransaccionHeaderDTO>> ObtenerBandejaTransaccionesAsync(int miAlmacenId, int limiteRegistros = 20)
        {
            var lista = new List<TransaccionHeaderDTO>();

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string topClause = QueryAdapter.EsMySQL ? "" : $"TOP ({limiteRegistros})";
                string limitClause = QueryAdapter.EsMySQL ? $"LIMIT {limiteRegistros}" : "";

                string query;
                if (QueryAdapter.EsMySQL)
                {
                    query = $@"
                        SELECT 
                            m.id AS MovimientoSalidaId,
                            CONCAT(m.serie_documento, '-', m.numero_documento) AS SerieNumero,
                            COALESCE(CONCAT(m.serie_guia, '-', m.numero_guia), '0000-0000000') AS GuiaRemision,
                            m.fecha_movimiento,
                            COALESCE(m.almacen_origen_id, m.almacen_id) AS AlmacenOrigenId,
                            COALESCE(ao.nombre, 'ALMACÉN CENTRAL') AS OrigenNombre,
                            COALESCE(m.almacen_destino_id, 1) AS AlmacenDestinoId,
                            COALESCE(ad.nombre, 'ALMACÉN CENTRAL') AS DestinoNombre,
                            COALESCE(u.nombres, 'SISTEMA') AS EmisorNombre,
                            COALESCE(mp.descripcion, 'TRANSFERENCIA') AS MotivoDesc,
                            COALESCE(m.observacion, '') AS Observacion,
                            COUNT(DISTINCT md.producto_id) AS TotalProductos,
                            COUNT(mc.id) AS TotalCodigos,
                            
                            CASE 
                                WHEN EXISTS (
                                    SELECT 1 
                                    FROM movimientos m_ing
                                    INNER JOIN motivo_productos mp_ing ON m_ing.motivo_producto_id = mp_ing.id
                                    INNER JOIN movimiento_detalles md_ing ON md_ing.movimiento_id = m_ing.id
                                    INNER JOIN movimiento_codigos mc_ing ON mc_ing.movimiento_detalle_id = md_ing.id
                                    WHERE mp_ing.tipo_movimiento_id = 1
                                      AND m_ing.motivo_producto_id IN (4, 10)
                                      AND COALESCE(m_ing.almacen_id, m_ing.almacen_destino_id, 1) = @miAlmacen
                                      AND mc_ing.codigo_creado_id IN (
                                          SELECT mc_sub.codigo_creado_id 
                                          FROM movimiento_detalles md_sub
                                          INNER JOIN movimiento_codigos mc_sub ON mc_sub.movimiento_detalle_id = md_sub.id
                                          WHERE md_sub.movimiento_id = m.id
                                      )
                                ) THEN 0
                                ELSE 1
                            END AS EsPendiente

                        FROM movimientos m
                        INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        LEFT JOIN almacenes ao ON COALESCE(m.almacen_origen_id, m.almacen_id) = ao.id
                        LEFT JOIN almacenes ad ON m.almacen_destino_id = ad.id
                        LEFT JOIN usuarios u ON m.usuario_id = u.id
                        INNER JOIN movimiento_detalles md ON md.movimiento_id = m.id
                        INNER JOIN movimiento_codigos mc ON mc.movimiento_detalle_id = md.id
                        WHERE m.almacen_destino_id = @miAlmacen
                          AND m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2
                          AND (m.motivo_producto_id IN (4, 10))
                          AND COALESCE(m.almacen_id, m.almacen_origen_id, 1) != @miAlmacen
                        GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                                 m.fecha_movimiento, m.almacen_origen_id, m.almacen_id, ao.nombre, m.almacen_destino_id, 
                                 ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
                        ORDER BY EsPendiente DESC, m.created_at DESC
                        {limitClause}";
                }
                else
                {
                    query = $@"
                        SELECT {topClause}
                            m.id AS MovimientoSalidaId,
                            CONCAT(m.serie_documento, '-', m.numero_documento) AS SerieNumero,
                            ISNULL(CONCAT(m.serie_guia, '-', m.numero_guia), '0000-0000000') AS GuiaRemision,
                            m.fecha_movimiento,
                            ISNULL(m.almacen_origen_id, m.almacen_id) AS AlmacenOrigenId,
                            ISNULL(ao.nombre, 'ALMACÉN CENTRAL') AS OrigenNombre,
                            ISNULL(m.almacen_destino_id, 1) AS AlmacenDestinoId,
                            ISNULL(ad.nombre, 'ALMACÉN CENTRAL') AS DestinoNombre,
                            ISNULL(u.nombres, 'SISTEMA') AS EmisorNombre,
                            ISNULL(mp.descripcion, 'TRANSFERENCIA') AS MotivoDesc,
                            ISNULL(m.observacion, '') AS Observacion,
                            COUNT(DISTINCT md.producto_id) AS TotalProductos,
                            COUNT(mc.id) AS TotalCodigos,
                            
                            CASE 
                                WHEN EXISTS (
                                    SELECT 1 
                                    FROM movimientos m_ing WITH (NOLOCK)
                                    INNER JOIN motivo_productos mp_ing WITH (NOLOCK) ON m_ing.motivo_producto_id = mp_ing.id
                                    INNER JOIN movimiento_detalles md_ing WITH (NOLOCK) ON md_ing.movimiento_id = m_ing.id
                                    INNER JOIN movimiento_codigos mc_ing WITH (NOLOCK) ON mc_ing.movimiento_detalle_id = md_ing.id
                                    WHERE mp_ing.tipo_movimiento_id = 1
                                      AND m_ing.motivo_producto_id IN (4, 10)
                                      AND ISNULL(m_ing.almacen_id, ISNULL(m_ing.almacen_destino_id, 1)) = @miAlmacen
                                      AND mc_ing.codigo_creado_id IN (
                                          SELECT mc_sub.codigo_creado_id 
                                          FROM movimiento_detalles md_sub WITH (NOLOCK)
                                          INNER JOIN movimiento_codigos mc_sub WITH (NOLOCK) ON mc_sub.movimiento_detalle_id = md_sub.id
                                          WHERE md_sub.movimiento_id = m.id
                                      )
                                ) THEN 0
                                ELSE 1
                            END AS EsPendiente

                        FROM movimientos m WITH (NOLOCK)
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        LEFT JOIN almacenes ao WITH (NOLOCK) ON ISNULL(m.almacen_origen_id, m.almacen_id) = ao.id
                        LEFT JOIN almacenes ad WITH (NOLOCK) ON m.almacen_destino_id = ad.id
                        LEFT JOIN usuarios u WITH (NOLOCK) ON m.usuario_id = u.id
                        INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
                        INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
                        WHERE m.almacen_destino_id = @miAlmacen
                          AND m.estado_id = 1
                          AND mp.tipo_movimiento_id = 2
                          AND (m.motivo_producto_id IN (4, 10))
                          AND ISNULL(m.almacen_id, ISNULL(m.almacen_origen_id, 1)) != @miAlmacen
                        GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                                 m.fecha_movimiento, m.almacen_origen_id, m.almacen_id, ao.nombre, m.almacen_destino_id, 
                                 ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
                        ORDER BY EsPendiente DESC, m.created_at DESC";
                }

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    lista.Add(new TransaccionHeaderDTO
                    {
                        MovimientoId = rdr.GetInt32(0),
                        SerieNumero = rdr.GetString(1),
                        GuiaRemision = rdr.GetString(2),
                        FechaMovimiento = rdr.IsDBNull(3) ? DateTime.Today : rdr.GetDateTime(3),
                        AlmacenOrigenId = rdr.GetInt32(4),
                        AlmacenOrigenNombre = rdr.GetString(5),
                        AlmacenDestinoId = rdr.GetInt32(6),
                        AlmacenDestinoNombre = rdr.GetString(7),
                        UsuarioEmisorNombre = rdr.GetString(8),
                        MotivoDescripcion = rdr.GetString(9),
                        Observacion = rdr.GetString(10),
                        TotalProductos = Convert.ToInt32(rdr.GetValue(11)),
                        TotalCodigos = Convert.ToInt32(rdr.GetValue(12)),
                        EsPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1,
                        SoyElEmisor = false
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al obtener la bandeja de transacciones: {ex.Message}");
            }

            return lista;
        }

        // =========================================================================
        // 4. OBTENER DETALLE COMPLETO DE UNA TRANSACCIÓN (PRODUCTOS Y CÓDIGOS)
        // =========================================================================
        public async Task<List<TransaccionDetalleDTO>> ObtenerDetalleTransaccionAsync(int movimientoId)
        {
            var detalles = new List<TransaccionDetalleDTO>();

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // A. Cargar los detalles del producto
                string qDetalles;
                if (QueryAdapter.EsMySQL)
                {
                    qDetalles = @"
                        SELECT md.id, md.producto_id, p.abreviatura, p.descripcion, 
                               COALESCE(md.cantidad_salida, md.cantidad_ingreso) AS cantidad, 
                               md.costo_unitario
                        FROM movimiento_detalles md
                        INNER JOIN productos p ON md.producto_id = p.id
                        WHERE md.movimiento_id = @movId";
                }
                else
                {
                    qDetalles = @"
                        SELECT md.id, md.producto_id, p.abreviatura, p.descripcion, 
                               ISNULL(md.cantidad_salida, md.cantidad_ingreso) AS cantidad, 
                               md.costo_unitario
                        FROM movimiento_detalles md WITH (NOLOCK)
                        INNER JOIN productos p WITH (NOLOCK) ON md.producto_id = p.id
                        WHERE md.movimiento_id = @movId";
                }

                using (var cmdDet = dbConn.CreateCommand())
                {
                    cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDetalles);
                    AgregarParametro(cmdDet, "@movId", movimientoId);

                    using var rdrDet = await cmdDet.ExecuteReaderAsync();
                    while (await rdrDet.ReadAsync())
                    {
                        detalles.Add(new TransaccionDetalleDTO
                        {
                            DetalleId = rdrDet.GetInt32(0),
                            ProductoId = rdrDet.GetInt32(1),
                            ProductoCodigo = rdrDet.IsDBNull(2) ? rdrDet.GetInt32(1).ToString() : rdrDet.GetString(2),
                            ProductoDescripcion = rdrDet.GetString(3),
                            Cantidad = Convert.ToInt32(rdrDet.GetValue(4)),
                            CostoUnitario = rdrDet.IsDBNull(5) ? 0m : rdrDet.GetDecimal(5)
                        });
                    }
                }

                // B. Cargar los códigos únicos asociados a cada detalle
                foreach (var det in detalles)
                {
                    string qCodigos;
                    if (QueryAdapter.EsMySQL)
                    {
                        qCodigos = @"
                            SELECT cc.codigo
                            FROM movimiento_codigos mc
                            INNER JOIN codigos_creados cc ON mc.codigo_creado_id = cc.id
                            WHERE mc.movimiento_detalle_id = @detId";
                    }
                    else
                    {
                        qCodigos = @"
                            SELECT cc.codigo
                            FROM movimiento_codigos mc WITH (NOLOCK)
                            INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                            WHERE mc.movimiento_detalle_id = @detId";
                    }

                    using var cmdCod = dbConn.CreateCommand();
                    cmdCod.CommandText = QueryAdapter.FormatearConsulta(qCodigos);
                    AgregarParametro(cmdCod, "@detId", det.DetalleId);

                    using var rdrCod = await cmdCod.ExecuteReaderAsync();
                    while (await rdrCod.ReadAsync())
                    {
                        det.CodigosUnicos.Add(rdrCod.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al obtener el detalle de la transacción {movimientoId}: {ex.Message}");
            }

            return detalles;
        }
    }
}