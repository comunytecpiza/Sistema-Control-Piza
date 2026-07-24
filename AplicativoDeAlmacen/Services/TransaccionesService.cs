using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;
using global::AplicativoDeAlmacen.Data;
using global::AplicativoDeAlmacen.Models.Transferencias;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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

        // ==========================================
        // 1. CONTAR PENDIENTES (PARA EL BADGE DE NOTIFICACIÓN)
        // ==========================================
        public async Task<int> ContarTransferenciasPendientesAsync(int miAlmacenId)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 CONSULTA BLINDADA DE NOTIFICACIONES PENDIENTES
                string query = @"
            SELECT COUNT(DISTINCT m.id)
            FROM movimientos m WITH (NOLOCK)
            INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
            INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
            INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            WHERE m.estado_id = 1
              AND m.motivo_producto_id IN (4, 10)     -- Motivo Transferencia
              AND cc.estado_id = 5                    -- Estado: EN TRÁNSITO
              AND m.almacen_destino_id = @miAlmacen   -- 👈 El almacén destino es mi sede actual
              AND m.almacen_origen_id != @miAlmacen"; 
        
        using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);

                object result = await cmd.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error contando transferencias: {ex.Message}");
                return 0;
            }
        }


        // =========================================================================
        // OBTENER HISTORIAL COMPLETO DE TRANSFERENCIAS (AMBOS LADOS: ENVIADOS Y RECIBIDOS)
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

                string query = @"
            SELECT 
                m.id,
                CONCAT(m.serie_documento, '-', m.numero_documento) AS serie_numero,
                ISNULL(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS guia_remision,
                m.fecha_movimiento,
                ISNULL(m.almacen_origen_id, 1) AS almacen_origen_id,
                ISNULL(ao.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS origen_nombre,
                ISNULL(m.almacen_destino_id, 1) AS almacen_destino_id,
                ISNULL(ad.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS destino_nombre,
                ISNULL(u.nombres, 'SISTEMA') AS emisor_nombre,
                ISNULL(mp.descripcion, 'TRANSFERENCIA') AS motivo_desc,
                ISNULL(m.observacion, '') AS observacion,
                COUNT(DISTINCT md.producto_id) AS total_productos,
                COUNT(mc.id) AS total_codigos,
                MAX(CASE WHEN cc.estado_id = 5 THEN 1 ELSE 0 END) AS es_pendiente
            FROM movimientos m WITH (NOLOCK)
            LEFT JOIN almacenes ao WITH (NOLOCK) ON m.almacen_origen_id = ao.id
            LEFT JOIN almacenes ad WITH (NOLOCK) ON m.almacen_destino_id = ad.id
            LEFT JOIN usuarios u WITH (NOLOCK) ON m.usuario_id = u.id
            LEFT JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
            INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
            INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            WHERE m.estado_id = 1
              AND (m.motivo_producto_id IN (4, 10)) -- Transferencias
              -- 🌟 FILTRO CLAVE: Muestra si SOY EL ORIGEN (Emisor) O EL DESTINO (Receptor)
              AND (m.almacen_origen_id = @miAlmacen OR m.almacen_destino_id = @miAlmacen)";

                if (desde.HasValue) query += " AND m.fecha_movimiento >= @desde";
                if (hasta.HasValue) query += " AND m.fecha_movimiento <= @hasta";

                query += @"
            GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                     m.fecha_movimiento, m.almacen_origen_id, ao.nombre, m.almacen_destino_id, 
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
                    bool esPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1;

                    // Filtro en memoria si el usuario selecciona PENDIENTES o RECEPCIONADOS
                    if (estadoFiltro == "PENDIENTES" && !esPendiente) continue;
                    if (estadoFiltro == "RECEPCIONADOS" && esPendiente) continue;

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
                        EsPendiente = esPendiente
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar historial de transferencias: {ex.Message}");
            }

            return lista;
        }
        // ==========================================
        // 2. OBTENER LISTADO DE LA BANDEJA (PENDIENTES + RECIENTES)
        // ==========================================
        public async Task<List<TransaccionHeaderDTO>> ObtenerBandejaTransaccionesAsync(int miAlmacenId, int limiteRegistros = 20)
            {
                var lista = new List<TransaccionHeaderDTO>();

                try
                {
                    using var conn = _database.GetConnection();
                    var dbConn = (DbConnection)conn;
                    await dbConn.OpenAsync();

                    // Consulta que obtiene tanto lo PENDIENTE (Estado 5) como lo RECIBIDO (Estado 3)
                    string topClause = QueryAdapter.EsMySQL ? "" : $"TOP ({limiteRegistros})";
                    string limitClause = QueryAdapter.EsMySQL ? $"LIMIT {limiteRegistros}" : "";

                    string query = $@"
                    SELECT {topClause}
                        m.id,
                        CONCAT(m.serie_documento, '-', m.numero_documento) AS serie_numero,
                        ISNULL(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS guia_remision,
                        m.fecha_movimiento,
                        ISNULL(m.almacen_origen_id, 1) AS almacen_origen_id,
                        ISNULL(ao.nombre, 'ALMACÉN CENTRAL') AS origen_nombre,
                        ISNULL(m.almacen_destino_id, 1) AS almacen_destino_id,
                        ISNULL(ad.nombre, 'ALMACÉN CENTRAL') AS destino_nombre,
                        ISNULL(u.nombres, 'SISTEMA') AS emisor_nombre,
                        ISNULL(mp.descripcion, 'TRANSFERENCIA') AS motivo_desc,
                        ISNULL(m.observacion, '') AS observacion,
                        COUNT(DISTINCT md.producto_id) AS total_productos,
                        COUNT(mc.id) AS total_codigos,
                        MAX(CASE WHEN cc.estado_id = 5 THEN 1 ELSE 0 END) AS es_pendiente
                    FROM movimientos m WITH (NOLOCK)
                    LEFT JOIN almacenes ao WITH (NOLOCK) ON m.almacen_origen_id = ao.id
                    LEFT JOIN almacenes ad WITH (NOLOCK) ON m.almacen_destino_id = ad.id
                    LEFT JOIN usuarios u WITH (NOLOCK) ON m.usuario_id = u.id
                    LEFT JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                    INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
                    INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
                    INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                    WHERE m.almacen_destino_id = @miAlmacen
                      AND m.estado_id = 1
                      AND (m.motivo_producto_id IN (4, 10)) -- Motivos de Transferencia
                    GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                             m.fecha_movimiento, m.almacen_origen_id, ao.nombre, m.almacen_destino_id, 
                             ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
                    ORDER BY es_pendiente DESC, m.created_at DESC
                    {limitClause}";

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
                            EsPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error al obtener la bandeja de transacciones: {ex.Message}");
                }

                return lista;
            }

            // ==========================================
            // 3. OBTENER EL DETALLE COMPLETO DE UNA TRANSACCIÓN (PRODUCTOS Y CÓDIGOS)
            // ==========================================
            public async Task<List<TransaccionDetalleDTO>> ObtenerDetalleTransaccionAsync(int movimientoId)
            {
                var detalles = new List<TransaccionDetalleDTO>();

                try
                {
                    using var conn = _database.GetConnection();
                    var dbConn = (DbConnection)conn;
                    await dbConn.OpenAsync();

                    // A. Cargar los detalles del producto
                    string qDetalles = @"
                    SELECT md.id, md.producto_id, p.abreviatura, p.descripcion, 
                           ISNULL(md.cantidad_salida, md.cantidad_ingreso) AS cantidad, 
                           md.costo_unitario
                    FROM movimiento_detalles md WITH (NOLOCK)
                    INNER JOIN productos p WITH (NOLOCK) ON md.producto_id = p.id
                    WHERE md.movimiento_id = @movId";

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
                        string qCodigos = @"
                        SELECT cc.codigo
                        FROM movimiento_codigos mc WITH (NOLOCK)
                        INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                        WHERE mc.movimiento_detalle_id = @detId";

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
