using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Motivo_y_Movimientos;
using AplicativoDeAlmacen.Services.Politicas;
using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class IngresoMovimientoService
    {
        private readonly DatabaseConnection _database;

        public class MovimientoCompletoResult
        {
            public Movimiento Movimiento { get; set; }
            public List<MovimientoDetalle> Detalles { get; set; } = new List<MovimientoDetalle>();
            public List<RangoCodigoItem> Rangos { get; set; } = new List<RangoCodigoItem>();
        }

        public IngresoMovimientoService()
        {
            _database = new DatabaseConnection();
        }


        // =========================================================================
        // 1. CARGAR ENTRADA NORMAL (Con candado estricto de Almacén de Sesión)
        // =========================================================================
        public async Task<MovimientoCompletoResult?> GetMovimientoCompletoAsync(string serie, string numero, int miAlmacenId)
        {
            var result = new MovimientoCompletoResult();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) return null;

            if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

            string query = @"
    SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
           m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion, m.estado_id,
           m.almacen_origen_id, m.almacen_destino_id, m.almacen_id, 
           m.usuario_id, m.usuario_update_id, m.created_at, m.updated_at
    FROM movimientos m WITH (NOLOCK)
    INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
    WHERE m.serie_documento = @serie 
      AND m.numero_documento = @numero
      AND mp.tipo_movimiento_id = 1
      AND m.estado_id = 1
      AND ISNULL(m.almacen_id, ISNULL(m.almacen_destino_id, 1)) = @miAlmacen";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);
                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                result.Movimiento = new Movimiento
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FechaMovimiento = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("fecha_movimiento")),
                    SerieDocumento = reader["serie_documento"].ToString(),
                    NumeroDocumento = reader["numero_documento"].ToString(),
                    MotivoProductoId = reader.IsDBNull(reader.GetOrdinal("motivo_producto_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = reader.IsDBNull(reader.GetOrdinal("persona_comercial_id")) ? null : reader.GetInt32(reader.GetOrdinal("persona_comercial_id")),
                    SerieGuia = reader.IsDBNull(reader.GetOrdinal("serie_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("serie_guia")),
                    NumeroGuia = reader.IsDBNull(reader.GetOrdinal("numero_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("numero_guia")),
                    Observacion = reader.IsDBNull(reader.GetOrdinal("observacion")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacion")),
                    UbicacionId = reader.IsDBNull(reader.GetOrdinal("ubicacion_id")) ? null : reader.GetInt32(reader.GetOrdinal("ubicacion_id")),
                    AlmacenId = reader.IsDBNull(reader.GetOrdinal("almacen_id")) ? null : reader.GetInt32(reader.GetOrdinal("almacen_id")),
                    AlmacenOrigenId = reader.IsDBNull(reader.GetOrdinal("almacen_origen_id")) ? null : reader.GetInt32(reader.GetOrdinal("almacen_origen_id")),
                    AlmacenDestinoId = reader.IsDBNull(reader.GetOrdinal("almacen_destino_id")) ? null : reader.GetInt32(reader.GetOrdinal("almacen_destino_id")),
                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id")),
                    UsuarioId = reader.IsDBNull(reader.GetOrdinal("usuario_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("usuario_id")),
                    UsuarioUpdateId = reader.IsDBNull(reader.GetOrdinal("usuario_update_id")) ? null : reader.GetInt32(reader.GetOrdinal("usuario_update_id")),
                    CreatedAt = reader.IsDBNull(reader.GetOrdinal("created_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }

            string qDet = @"
    SELECT id, producto_id, cantidad_ingreso, costo_unitario 
    FROM movimiento_detalles WITH (NOLOCK)
    WHERE movimiento_id = @movId";

            using (var cmdDet = dbConn.CreateCommand())
            {
                cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDet);
                AgregarParametro(cmdDet, "@movId", result.Movimiento.Id);
                using var rdrDet = await cmdDet.ExecuteReaderAsync();
                while (await rdrDet.ReadAsync())
                {
                    result.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rdrDet.GetInt32(0),
                        ProductoId = rdrDet.GetInt32(1),
                        CantidadIngreso = Convert.ToInt32(rdrDet.GetValue(2)),
                        CostoUnitario = rdrDet.IsDBNull(3) ? (decimal?)null : rdrDet.GetDecimal(3)
                    });
                }
            }

            string qRangos = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id FROM registro_rangos WITH (NOLOCK) WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
            using (var cmdR = dbConn.CreateCommand())
            {
                cmdR.CommandText = QueryAdapter.FormatearConsulta(qRangos);
                AgregarParametro(cmdR, "@movId", result.Movimiento.Id);
                using var rdrR = await cmdR.ExecuteReaderAsync();
                while (await rdrR.ReadAsync())
                {
                    int desdeNum = rdrR.GetInt32(rdrR.GetOrdinal("desde_num"));
                    int hastaNum = rdrR.GetInt32(rdrR.GetOrdinal("hasta_num"));
                    int categoriaId = rdrR.GetInt32(rdrR.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdrR.IsDBNull(rdrR.GetOrdinal("abreviatura_base")) ? string.Empty : rdrR.GetString(rdrR.GetOrdinal("abreviatura_base"));

                    result.Rangos.Add(new RangoCodigoItem
                    {
                        MovimientoDetalleId = rdrR.IsDBNull(rdrR.GetOrdinal("movimiento_detalle_id")) ? 0 : rdrR.GetInt32(rdrR.GetOrdinal("movimiento_detalle_id")),
                        productoId = rdrR.GetInt32(rdrR.GetOrdinal("producto_id")),
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = baseAbrev,
                        DesdeNum = desdeNum,
                        HastaNum = hastaNum,
                        Cantidad = desdeNum == -1 ? "1" : (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeNum == -1 ? baseAbrev : $"{baseAbrev}-{desdeNum:D7}",
                        Hasta = hastaNum == -1 ? baseAbrev : $"{baseAbrev}-{hastaNum:D7}",
                        ColeccionTipo = $"C2026 / {(categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA")}"
                    });
                }
            }

            return result;
        }

        // =========================================================================
        // 2. CARGAR SALIDA DE TRANSFERENCIA POR ID ÚNICO (Para Procesar Recepción)
        // =========================================================================
        public async Task<MovimientoCompletoResult?> GetSalidaParaRecepcionAsync(int movimientoSalidaId)
        {
            var result = new MovimientoCompletoResult();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            // 🌟 1. CONSULTA DE CABECERA MULTI-MOTOR (Permite Entradas [1] y Salidas [2] con Motivo Transferencia 4 u 10)
            string query;
            if (QueryAdapter.EsMySQL)
            {
                query = @"
            SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
                   m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion, m.estado_id,
                   m.almacen_origen_id, m.almacen_destino_id
            FROM movimientos m
            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
            WHERE m.id = @movId 
              AND m.motivo_producto_id IN (4, 10) -- 👈 Acepta tanto Entrada como Salida de Transferencias
              AND m.estado_id = 1";
            }
            else
            {
                query = @"
            SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
                   m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion, m.estado_id,
                   m.almacen_origen_id, m.almacen_destino_id
            FROM movimientos m WITH (NOLOCK)
            INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            WHERE m.id = @movId 
              AND m.motivo_producto_id IN (4, 10)
              AND m.estado_id = 1";
            }

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@movId", movimientoSalidaId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                result.Movimiento = new Movimiento
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FechaMovimiento = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("fecha_movimiento")),
                    SerieDocumento = reader["serie_documento"].ToString(),
                    NumeroDocumento = reader["numero_documento"].ToString(),
                    MotivoProductoId = reader.IsDBNull(reader.GetOrdinal("motivo_producto_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = reader.IsDBNull(reader.GetOrdinal("persona_comercial_id")) ? null : reader.GetInt32(reader.GetOrdinal("persona_comercial_id")),
                    SerieGuia = reader.IsDBNull(reader.GetOrdinal("serie_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("serie_guia")),
                    NumeroGuia = reader.IsDBNull(reader.GetOrdinal("numero_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("numero_guia")),
                    Observacion = reader.IsDBNull(reader.GetOrdinal("observacion")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacion")),
                    UbicacionId = reader.IsDBNull(reader.GetOrdinal("ubicacion_id")) ? null : reader.GetInt32(reader.GetOrdinal("ubicacion_id")),
                    AlmacenOrigenId = reader.IsDBNull(reader.GetOrdinal("almacen_origen_id")) ? null : reader.GetInt32(reader.GetOrdinal("almacen_origen_id")),
                    AlmacenDestinoId = reader.IsDBNull(reader.GetOrdinal("almacen_destino_id")) ? null : reader.GetInt32(reader.GetOrdinal("almacen_destino_id")),
                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id"))
                };
            }

            // 🌟 2. LEER DETALLES COMPATIBLE CON AMBOS MOTORES
            string qDet = QueryAdapter.EsMySQL
                ? @"SELECT id, producto_id, COALESCE(cantidad_salida, cantidad_ingreso) AS cantidad, costo_unitario 
            FROM movimiento_detalles 
            WHERE movimiento_id = @movId"
                : @"SELECT id, producto_id, ISNULL(cantidad_salida, cantidad_ingreso) AS cantidad, costo_unitario 
            FROM movimiento_detalles WITH (NOLOCK)
            WHERE movimiento_id = @movId";

            using (var cmdDet = dbConn.CreateCommand())
            {
                cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDet);
                AgregarParametro(cmdDet, "@movId", result.Movimiento.Id);
                using var rdrDet = await cmdDet.ExecuteReaderAsync();
                while (await rdrDet.ReadAsync())
                {
                    int cantidadTotal = Convert.ToInt32(rdrDet.GetValue(2));
                    result.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rdrDet.GetInt32(0),
                        ProductoId = rdrDet.GetInt32(1),
                        CantidadIngreso = cantidadTotal, // Se asigna para mapear correctamente en la vista
                        CantidadSalida = cantidadTotal,
                        CostoUnitario = rdrDet.IsDBNull(3) ? (decimal?)null : rdrDet.GetDecimal(3)
                    });
                }
            }

            // 🌟 3. LEER RANGOS COMPATIBLE CON AMBOS MOTORES
            string qRangos = QueryAdapter.EsMySQL
                ? @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id 
            FROM registro_rangos 
            WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)"
                : @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id 
            FROM registro_rangos WITH (NOLOCK) 
            WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";

            using (var cmdR = dbConn.CreateCommand())
            {
                cmdR.CommandText = QueryAdapter.FormatearConsulta(qRangos);
                AgregarParametro(cmdR, "@movId", result.Movimiento.Id);
                using var rdrR = await cmdR.ExecuteReaderAsync();
                while (await rdrR.ReadAsync())
                {
                    int desdeNum = rdrR.GetInt32(rdrR.GetOrdinal("desde_num"));
                    int hastaNum = rdrR.GetInt32(rdrR.GetOrdinal("hasta_num"));
                    int categoriaId = rdrR.GetInt32(rdrR.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdrR.IsDBNull(rdrR.GetOrdinal("abreviatura_base")) ? string.Empty : rdrR.GetString(rdrR.GetOrdinal("abreviatura_base"));

                    string desdeText = desdeNum == -1 ? baseAbrev : $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = hastaNum == -1 ? baseAbrev : $"{baseAbrev}-{hastaNum:D7}";

                    string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionTipo = $"C2026 / {tipoTexto}";

                    result.Rangos.Add(new RangoCodigoItem
                    {
                        MovimientoDetalleId = rdrR.IsDBNull(rdrR.GetOrdinal("movimiento_detalle_id")) ? 0 : rdrR.GetInt32(rdrR.GetOrdinal("movimiento_detalle_id")),
                        productoId = rdrR.GetInt32(rdrR.GetOrdinal("producto_id")),
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = baseAbrev,
                        DesdeNum = desdeNum,
                        HastaNum = hastaNum,
                        Cantidad = desdeNum == -1 ? "1" : (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeText,
                        Hasta = hastaText,
                        ColeccionTipo = coleccionTipo
                    });
                }
            }

            return result;
        }

        private async Task ActualizarStockProductoPorKardexAsync(int productoId, int almacenId, DbConnection conn, DbTransaction trans)
        {
            // 🌟 1. CALCULA EL STOCK EN 1 SOLO JOIN
            string queryCalculo = @"
        SELECT COALESCE(
            SUM(CASE 
                WHEN m.almacen_destino_id = @AlmId AND mp.tipo_movimiento_id = 1 
                THEN md.cantidad_ingreso 
                ELSE 0 
            END) -
            SUM(CASE 
                WHEN m.almacen_origen_id = @AlmId AND mp.tipo_movimiento_id = 2 
                THEN md.cantidad_salida 
                ELSE 0 
            END), 0) AS stock_calculado
        FROM movimiento_detalles md WITH (NOLOCK)
        INNER JOIN movimientos m WITH (NOLOCK) ON md.movimiento_id = m.id
        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
        WHERE md.producto_id = @ProdId
          AND m.estado_id = 1
          AND (
              (m.almacen_destino_id = @AlmId AND mp.tipo_movimiento_id = 1)
              OR
              (m.almacen_origen_id = @AlmId AND mp.tipo_movimiento_id = 2)
          )";

            int stockCalculado = 0;
            using (var cmdCalc = conn.CreateCommand())
            {
                cmdCalc.Transaction = trans;
                cmdCalc.CommandText = QueryAdapter.FormatearConsulta(queryCalculo);
                AgregarParametro(cmdCalc, "@ProdId", productoId);
                AgregarParametro(cmdCalc, "@AlmId", almacenId);

                object? result = await cmdCalc.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    stockCalculado = Convert.ToInt32(result);
                }
            }

            // 🌟 2. UPSERT MULTI-MOTOR (Sintaxis limpia según el motor activo)
            string queryUpsert;

            if (QueryAdapter.EsMySQL)
            {
                // 🟢 Sintaxis nativa MariaDB / MySQL
                queryUpsert = @"
            INSERT INTO stock_almacen (producto_id, almacen_id, stock_actual, updated_at)
            VALUES (@ProdId, @AlmId, @Stock, NOW())
            ON DUPLICATE KEY UPDATE 
                stock_actual = @Stock, 
                updated_at = NOW();";
            }
            else
            {
                // 🛡️ Sintaxis nativa SQL Server
                queryUpsert = @"
            IF EXISTS (SELECT 1 FROM stock_almacen WHERE producto_id = @ProdId AND almacen_id = @AlmId)
            BEGIN
                UPDATE stock_almacen 
                SET stock_actual = @Stock, updated_at = GETDATE()
                WHERE producto_id = @ProdId AND almacen_id = @AlmId;
            END
            ELSE
            BEGIN
                INSERT INTO stock_almacen (producto_id, almacen_id, stock_actual, updated_at)
                VALUES (@ProdId, @AlmId, @Stock, GETDATE());
            END";
            }

            using (var cmdUpsert = conn.CreateCommand())
            {
                cmdUpsert.Transaction = trans;
                cmdUpsert.CommandText = QueryAdapter.FormatearConsulta(queryUpsert);
                AgregarParametro(cmdUpsert, "@ProdId", productoId);
                AgregarParametro(cmdUpsert, "@AlmId", almacenId);
                AgregarParametro(cmdUpsert, "@Stock", stockCalculado);

                await cmdUpsert.ExecuteNonQueryAsync();
            }
        }

        public async Task<string> ObtenerDescripcionProductoAsync(int productoId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT descripcion FROM productos WHERE id = @id");
            AgregarParametro(cmd, "@id", productoId);
            var res = await cmd.ExecuteScalarAsync();
            return res?.ToString() ?? "Producto";
        }

        public async Task<Movimiento?> ObtenerUltimoMovimientoRegistradoAsync()
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = "SELECT TOP 1 serie_documento, numero_documento FROM movimientos ORDER BY id DESC";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Movimiento
                {
                    SerieDocumento = reader.GetString(0),
                    NumeroDocumento = reader.GetString(1)
                };
            }
            return null;
        }

        public async Task<List<RangoCodigoItem>> GetRangosByMovimientoDetalleIdAsync(int movimientoDetalleId)
        {
            var lista = new List<RangoCodigoItem>();
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string q = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id
                         FROM registro_rangos WHERE movimiento_detalle_id = @detId";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                var p = cmd.CreateParameter(); p.ParameterName = "@detId"; p.Value = movimientoDetalleId; cmd.Parameters.Add(p);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int desdeNum = rdr.GetInt32(rdr.GetOrdinal("desde_num"));
                    int hastaNum = rdr.GetInt32(rdr.GetOrdinal("hasta_num"));
                    int categoriaId = rdr.GetInt32(rdr.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdr.IsDBNull(rdr.GetOrdinal("abreviatura_base")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("abreviatura_base"));

                    string desdeText = $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = $"{baseAbrev}-{hastaNum:D7}";
                    string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionTipo = $"C2026 / {tipoTexto}";

                    lista.Add(new RangoCodigoItem
                    {
                        MovimientoDetalleId = rdr.IsDBNull(rdr.GetOrdinal("movimiento_detalle_id")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("movimiento_detalle_id")),
                        productoId = rdr.GetInt32(rdr.GetOrdinal("producto_id")),
                        CategoriaProductoId = categoriaId,
                        AbreviaturaBase = baseAbrev,
                        DesdeNum = desdeNum,
                        HastaNum = hastaNum,
                        Cantidad = (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeText,
                        Hasta = hastaText,
                        ColeccionTipo = coleccionTipo
                    });
                }
            }
            return lista;
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        public async Task<List<MotivoProducto>> ObtenerMotivosProductosAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 CORREGIDO: Se usa tipo_movimiento_id = 1 (1 = Entradas)
                string query = @"SELECT id, descripcion 
                         FROM motivo_productos 
                         WHERE tipo_movimiento_id = 1 
                         ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var motivo = new MotivoProducto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion"))
                            };
                            lista.Add(motivo);
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<MovimientoCompletoDTO> GenerarSiguienteCorrelativoAsync(string seriePorDefecto, int miAlmacenId)
        {
            var resultado = new MovimientoCompletoDTO
            {
                Movimiento = new Movimiento
                {
                    SerieDocumento = seriePorDefecto,
                    NumeroDocumento = "0000001"
                }
            };

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 1. Obtener última serie usada en INGRESOS (tipo_movimiento_id = 1)
                string queryUltimaSerie = QueryAdapter.EsMySQL
                    ? @"SELECT m.serie_documento 
                FROM movimientos m
                INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                WHERE mp.tipo_movimiento_id = 1
                  AND COALESCE(m.almacen_id, COALESCE(m.almacen_destino_id, 1)) = @almId
                ORDER BY m.id DESC LIMIT 1"
                    : @"SELECT TOP 1 m.serie_documento 
                FROM movimientos m WITH (NOLOCK)
                INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                WHERE mp.tipo_movimiento_id = 1
                  AND ISNULL(m.almacen_id, ISNULL(m.almacen_destino_id, 1)) = @almId
                ORDER BY m.id DESC";

                string serieActual = seriePorDefecto;
                using (var cmdSerie = dbConn.CreateCommand())
                {
                    cmdSerie.CommandText = QueryAdapter.FormatearConsulta(queryUltimaSerie);
                    AgregarParametro(cmdSerie, "@almId", miAlmacenId);
                    var resSerie = await cmdSerie.ExecuteScalarAsync();
                    if (resSerie != null && resSerie != DBNull.Value && !string.IsNullOrWhiteSpace(resSerie.ToString()))
                    {
                        serieActual = resSerie.ToString()!;
                    }
                }

                // 🌟 2. Obtener el número máximo correlativo dentro de la serie para INGRESOS
                string castInt = QueryAdapter.EsMySQL ? "CAST(m.numero_documento AS SIGNED)" : "CAST(m.numero_documento AS INT)";
                string coalesceFunc = QueryAdapter.EsMySQL ? "COALESCE" : "ISNULL";
                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

                string queryMaxNum = $@"
            SELECT COALESCE(MAX({castInt}), 0)
            FROM movimientos m {nolock}
            INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
            WHERE m.serie_documento = @serie
              AND mp.tipo_movimiento_id = 1
              AND {coalesceFunc}(m.almacen_id, {coalesceFunc}(m.almacen_destino_id, 1)) = @almId";

                int ultimoNumero = 0;
                using (var cmdNum = dbConn.CreateCommand())
                {
                    cmdNum.CommandText = QueryAdapter.FormatearConsulta(queryMaxNum);
                    AgregarParametro(cmdNum, "@serie", serieActual);
                    AgregarParametro(cmdNum, "@almId", miAlmacenId);
                    object? resultObj = await cmdNum.ExecuteScalarAsync();
                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        ultimoNumero = Convert.ToInt32(resultObj);
                    }
                }

                // 🌟 3. Asignación del siguiente número o salto de serie
                if (ultimoNumero >= 9999999)
                {
                    if (int.TryParse(serieActual, out int serieVal))
                    {
                        resultado.Movimiento.SerieDocumento = (serieVal + 1).ToString("D4");
                        resultado.Movimiento.NumeroDocumento = "0000001";
                    }
                    else
                    {
                        resultado.Movimiento.SerieDocumento = serieActual;
                        resultado.Movimiento.NumeroDocumento = "0000001";
                    }
                }
                else
                {
                    resultado.Movimiento.SerieDocumento = serieActual;
                    resultado.Movimiento.NumeroDocumento = (ultimoNumero + 1).ToString("D7");
                }
            }

            return resultado;
        }

        private async Task<int> GuardarCabeceraAsync(Movimiento cabecera, int ubicacionId, int? existingId, int usuarioActivoId, int totalProductos, DbConnection conn, DbTransaction trans)
        {
            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            object valUbicacion = (cabecera.UbicacionId.HasValue && cabecera.UbicacionId.Value > 0)
                ? (object)cabecera.UbicacionId.Value
                : ((ubicacionId > 0) ? (object)ubicacionId : DBNull.Value);

            object valPersona = (cabecera.PersonaComercialId.HasValue && cabecera.PersonaComercialId.Value > 0)
                ? (object)cabecera.PersonaComercialId.Value
                : DBNull.Value;

            DateTime fechaMovimientoFinal = cabecera.FechaMovimiento ?? DateTime.Now;

            // -------------------------------------------------------------
            // EDICIÓN DE INGRESO EXISTENTE
            // -------------------------------------------------------------
            if (existingId.HasValue)
            {
                // 1. Obtener observación previa para el historial
                string observacionPrevia = string.Empty;
                using (var cmdObsPrev = conn.CreateCommand())
                {
                    cmdObsPrev.Transaction = trans;
                    cmdObsPrev.CommandText = QueryAdapter.FormatearConsulta("SELECT COALESCE(observacion, '') FROM movimientos WHERE id = @id");
                    AgregarParametro(cmdObsPrev, "@id", existingId.Value);
                    var resObs = await cmdObsPrev.ExecuteScalarAsync();
                    observacionPrevia = resObs?.ToString() ?? string.Empty;
                }

                // 2. Modificar cabecera (fecha_movimiento se actualiza, created_at y usuario_id quedan intactos)
                string updateCab = $@"
        UPDATE movimientos 
        SET fecha_movimiento = @fecha, 
            motivo_producto_id = @motivoId, 
            ubicacion_id = @ubicacionId, 
            almacen_id = @almId,
            almacen_origen_id = @almOrigen,
            almacen_destino_id = @almDestino,
            persona_comercial_id = @personaId, 
            observacion = @observacion, 
            serie_guia = @serieGuia, 
            numero_guia = @numeroGuia,
            usuario_update_id = @usrUpdateId,
            updated_at = {nowFunc}
        WHERE id = @id";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = QueryAdapter.FormatearConsulta(updateCab);

                    AgregarParametro(cmd, "@fecha", fechaMovimientoFinal);
                    AgregarParametro(cmd, "@motivoId", cabecera.MotivoProductoId);
                    AgregarParametro(cmd, "@ubicacionId", valUbicacion);
                    AgregarParametro(cmd, "@almId", cabecera.AlmacenId);
                    AgregarParametro(cmd, "@almOrigen", cabecera.AlmacenOrigenId);
                    AgregarParametro(cmd, "@almDestino", cabecera.AlmacenDestinoId);
                    AgregarParametro(cmd, "@personaId", valPersona);
                    AgregarParametro(cmd, "@observacion", cabecera.Observacion);
                    AgregarParametro(cmd, "@serieGuia", string.IsNullOrWhiteSpace(cabecera.SerieGuia) ? DBNull.Value : cabecera.SerieGuia);
                    AgregarParametro(cmd, "@numeroGuia", string.IsNullOrWhiteSpace(cabecera.NumeroGuia) ? DBNull.Value : cabecera.NumeroGuia);
                    AgregarParametro(cmd, "@usrUpdateId", usuarioActivoId);
                    AgregarParametro(cmd, "@id", existingId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                // 3. Registrar en tabla histórica de auditoría
                string insertAuditoria = $@"
        INSERT INTO movimientos_auditoria_ediciones
        (movimiento_id, usuario_id, fecha_edicion, motivo_edicion, observacion_previa, observacion_nueva, total_items_nuevos)
        VALUES
        (@movId, @usrId, {nowFunc}, 'EDICIÓN DE INGRESO', @obsPrev, @obsNueva, @itemsCount)";

                using (var cmdAudit = conn.CreateCommand())
                {
                    cmdAudit.Transaction = trans;
                    cmdAudit.CommandText = QueryAdapter.FormatearConsulta(insertAuditoria);
                    AgregarParametro(cmdAudit, "@movId", existingId.Value);
                    AgregarParametro(cmdAudit, "@usrId", usuarioActivoId);
                    AgregarParametro(cmdAudit, "@obsPrev", observacionPrevia);
                    AgregarParametro(cmdAudit, "@obsNueva", cabecera.Observacion ?? string.Empty);
                    AgregarParametro(cmdAudit, "@itemsCount", totalProductos);

                    await cmdAudit.ExecuteNonQueryAsync();
                }

                return existingId.Value;
            }

            // -------------------------------------------------------------
            // NUEVO INGRESO (CREACIÓN)
            // -------------------------------------------------------------
            string queryLock = @"
    SELECT COALESCE(MAX(CAST(m.numero_documento AS INT)), 0) + 1 
    FROM movimientos m
    INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
    WHERE m.serie_documento = @serie 
      AND mp.tipo_movimiento_id = 1
      AND COALESCE(m.almacen_id, m.almacen_destino_id, 1) = @almId";

            int nuevoNumero;
            using (var cmdLock = conn.CreateCommand())
            {
                cmdLock.Transaction = trans;
                cmdLock.CommandText = QueryAdapter.FormatearConsulta(queryLock);
                AgregarParametro(cmdLock, "@serie", cabecera.SerieDocumento);
                AgregarParametro(cmdLock, "@almId", cabecera.AlmacenId ?? 1);
                nuevoNumero = Convert.ToInt32(await cmdLock.ExecuteScalarAsync());
            }

            cabecera.NumeroDocumento = nuevoNumero.ToString("D7");

            string qCab = $@"
    INSERT INTO movimientos 
    (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, 
     almacen_id, almacen_origen_id, almacen_destino_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia, created_at) 
    VALUES 
    (@fecha, @serie, @numero, @motivoId, @ubicacionId, @almId, @almOrigen, @almDestino, @usuarioId, @personaId, @observacion, 1, @serieGuia, @numeroGuia, {nowFunc}); {selectId}";

            using var cmdCab = conn.CreateCommand();
            cmdCab.Transaction = trans;
            cmdCab.CommandText = QueryAdapter.FormatearConsulta(qCab);

            AgregarParametro(cmdCab, "@fecha", fechaMovimientoFinal);
            AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
            AgregarParametro(cmdCab, "@numero", nuevoNumero.ToString("D7"));
            AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
            AgregarParametro(cmdCab, "@ubicacionId", valUbicacion);
            AgregarParametro(cmdCab, "@almId", cabecera.AlmacenId ?? 1);
            AgregarParametro(cmdCab, "@almOrigen", cabecera.AlmacenOrigenId);
            AgregarParametro(cmdCab, "@almDestino", cabecera.AlmacenDestinoId);
            AgregarParametro(cmdCab, "@usuarioId", usuarioActivoId);
            AgregarParametro(cmdCab, "@personaId", valPersona);
            AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
            AgregarParametro(cmdCab, "@serieGuia", string.IsNullOrWhiteSpace(cabecera.SerieGuia) ? DBNull.Value : cabecera.SerieGuia);
            AgregarParametro(cmdCab, "@numeroGuia", string.IsNullOrWhiteSpace(cabecera.NumeroGuia) ? DBNull.Value : cabecera.NumeroGuia);

            int idCabeceraObtenido = Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
            return idCabeceraObtenido;
        }

        private async Task<int> UpsertMovimientoDetalleAsync(int movId, VistaProductoGrid item, DbConnection conn, DbTransaction trans)
        {
            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            using (var cmdCheck = conn.CreateCommand())
            {
                cmdCheck.Transaction = trans;
                cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND producto_id = @prodId");
                AgregarParametro(cmdCheck, "@movId", movId);
                AgregarParametro(cmdCheck, "@prodId", item.ProductoId);
                var res = await cmdCheck.ExecuteScalarAsync();
                if (res != null)
                {
                    int detId = Convert.ToInt32(res);
                    using var cmdUpd = conn.CreateCommand();
                    cmdUpd.Transaction = trans;
                    // 🌟 AQUÍ ES DONDE SE ACTUALIZA EL COSTO EN LA BASE DE DATOS
                    cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_ingreso = @cant, costo_unitario = @costo WHERE id = @detId");
                    AgregarParametro(cmdUpd, "@cant", (int)item.Detalle.CantidadIngreso);
                    AgregarParametro(cmdUpd, "@costo", item.Detalle.CostoUnitario); // 👈 ¡Este es el nuevo costo que tú digites (ej. 95)!
                    AgregarParametro(cmdUpd, "@detId", detId);
                    await cmdUpd.ExecuteNonQueryAsync();
                    return detId;
                }
            }

            using var cmdIns = conn.CreateCommand();
            cmdIns.Transaction = trans;
            cmdIns.CommandText = QueryAdapter.FormatearConsulta($"INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at) VALUES (@movId, @prodId, @cant, 0, @costo, GETDATE()); {selectId}");
            AgregarParametro(cmdIns, "@movId", movId);
            AgregarParametro(cmdIns, "@prodId", item.ProductoId);
            AgregarParametro(cmdIns, "@cant", (int)item.Detalle.CantidadIngreso); // 🌟 Entero estricto
            AgregarParametro(cmdIns, "@costo", item.Detalle.CostoUnitario);
            return Convert.ToInt32(await cmdIns.ExecuteScalarAsync());
        }

        private async Task<HashSet<int>> ObtenerIdsCodigosPorDetalleAsync(int detId, DbConnection conn, DbTransaction trans)
        {
            var set = new HashSet<int>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT codigo_creado_id FROM movimiento_codigos WHERE movimiento_detalle_id = @detId");
            AgregarParametro(cmd, "@detId", detId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) set.Add(rdr.GetInt32(0));
            return set;
        }

        private async Task InsertarRangoAsync(RangoCodigoItem r, int detId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(@"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id) VALUES (@prodId, @catId, @abrev, @desde, @hasta, @detId)");
            AgregarParametro(cmd, "@prodId", r.productoId);
            AgregarParametro(cmd, "@catId", r.CategoriaProductoId);
            AgregarParametro(cmd, "@abrev", r.AbreviaturaBase);
            AgregarParametro(cmd, "@desde", r.DesdeNum);
            AgregarParametro(cmd, "@hasta", r.HastaNum);
            AgregarParametro(cmd, "@detId", detId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> AnularMovimientoCompletoAsync(int movimientoId, IProgress<int>? progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                DateTime fechaMovimiento;
                int almacenDestino = 1;

                using (var cmdMov = dbConn.CreateCommand())
                {
                    cmdMov.Transaction = transaccion;
                    cmdMov.CommandText = QueryAdapter.FormatearConsulta(
                        "SELECT fecha_movimiento, estado_id, COALESCE(almacen_destino_id, almacen_id, 1) FROM movimientos WHERE id = @movId");
                    AgregarParametro(cmdMov, "@movId", movimientoId);

                    using var rdrMov = await cmdMov.ExecuteReaderAsync();
                    if (!await rdrMov.ReadAsync()) throw new Exception("El movimiento no existe.");
                    if (rdrMov.GetInt32(1) == 2) throw new Exception("Este movimiento de ingreso ya está anulado.");

                    fechaMovimiento = rdrMov.IsDBNull(0) ? DateTime.Today : rdrMov.GetDateTime(0);
                    almacenDestino = rdrMov.GetInt32(2);
                }

                // 1. Obtener lista de códigos involucrados
                var codigosAnular = new List<int>();
                using (var cmdCod = dbConn.CreateCommand())
                {
                    cmdCod.Transaction = transaccion;
                    cmdCod.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId");
                    AgregarParametro(cmdCod, "@movId", movimientoId);
                    using var rdrC = await cmdCod.ExecuteReaderAsync();
                    while (await rdrC.ReadAsync()) codigosAnular.Add(rdrC.GetInt32(0));
                }

                // 2. Verificar si tienen movimientos posteriores que impidan la anulación
                foreach (var codId in codigosAnular)
                {
                    bool tienePost = await TieneMovimientosPosterioresAsync(codId, movimientoId, fechaMovimiento, dbConn, transaccion);
                    if (tienePost) throw new Exception($"Rechazado: El código ID {codId} registra movimientos logísticos posteriores.");
                }

                progress?.Report(30);

                // 3. Eliminar rangos asignados en este movimiento
                string sqlEliminarRangos = "DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
                using (var cmdDelR = dbConn.CreateCommand())
                {
                    cmdDelR.Transaction = transaccion;
                    cmdDelR.CommandText = QueryAdapter.FormatearConsulta(sqlEliminarRangos);
                    AgregarParametro(cmdDelR, "@movId", movimientoId);
                    await cmdDelR.ExecuteNonQueryAsync();
                }

                progress?.Report(60);

                // 4. 🌟 REVERSIÓN HISTÓRICA EXACTA CÓDIGO POR CÓDIGO
                foreach (var codId in codigosAnular)
                {
                    var (estadoAnterior, almacenAnterior) = await ObtenerEstadoYAlmacenAnteriorAsync(codId, movimientoId, dbConn, transaccion);

                    using var cmdRevert = dbConn.CreateCommand();
                    cmdRevert.Transaction = transaccion;
                    cmdRevert.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = @est, almacen_id = @alm WHERE id = @codId");
                    AgregarParametro(cmdRevert, "@est", estadoAnterior);
                    AgregarParametro(cmdRevert, "@alm", almacenAnterior);
                    AgregarParametro(cmdRevert, "@codId", codId);
                    await cmdRevert.ExecuteNonQueryAsync();
                }

                // 5. Desvincular de movimiento_codigos
                using (var cmdDelMC = dbConn.CreateCommand())
                {
                    cmdDelMC.Transaction = transaccion;
                    cmdDelMC.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_id = @movId");
                    AgregarParametro(cmdDelMC, "@movId", movimientoId);
                    await cmdDelMC.ExecuteNonQueryAsync();
                }

                // 6. Marcar la cabecera como anulada (estado_id = 2)
                using (var cmdStatus = dbConn.CreateCommand())
                {
                    cmdStatus.Transaction = transaccion;
                    cmdStatus.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimientos SET estado_id = 2 WHERE id = @movId");
                    AgregarParametro(cmdStatus, "@movId", movimientoId);
                    await cmdStatus.ExecuteNonQueryAsync();
                }

                // 7. Recalcular el stock físico del almacén
                using (var cmdProds = dbConn.CreateCommand())
                {
                    cmdProds.Transaction = transaccion;
                    cmdProds.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT producto_id FROM movimiento_detalles WHERE movimiento_id = @movId");
                    AgregarParametro(cmdProds, "@movId", movimientoId);

                    using var rdrP = await cmdProds.ExecuteReaderAsync();
                    var prodIds = new List<int>();
                    while (await rdrP.ReadAsync()) prodIds.Add(rdrP.GetInt32(0));
                    rdrP.Close();

                    foreach (var pid in prodIds)
                    {
                        await ActualizarStockProductoPorKardexAsync(pid, almacenDestino, dbConn, transaccion);
                    }
                }

                progress?.Report(100);
                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception(ex.Message, ex);
            }
        }

        public async Task<List<TransferenciaPendienteDTO>> ObtenerTransferenciasPendientesAsync(int miAlmacenDestinoId)
        {
            var lista = new List<TransferenciaPendienteDTO>();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string query = @"
        SELECT 
            m.id AS MovimientoSalidaId,
            CONCAT(m.serie_documento, '-', m.numero_documento) AS GuiaRemision,
            ISNULL(ao.nombre, 'Almacén Central') AS AlmacenOrigen,
            p.id AS ProductoId,
            p.descripcion AS Producto,
            COUNT(cc.id) AS CantidadEnTransito,
            m.created_at
        FROM movimientos m
        INNER JOIN almacenes ao ON m.almacen_origen_id = ao.id
        INNER JOIN movimiento_detalles md ON md.movimiento_id = m.id
        INNER JOIN productos p ON md.producto_id = p.id
        INNER JOIN movimiento_codigos mc ON mc.movimiento_detalle_id = md.id
        INNER JOIN codigos_creados cc ON mc.codigo_creado_id = cc.id
        WHERE m.almacen_destino_id = @miAlmacen
          AND cc.estado_id = 5 -- 5 = EN TRÁNSITO POR TRANSFERENCIA
        GROUP BY m.id, m.serie_documento, m.numero_documento, ao.nombre, p.id, p.descripcion, m.created_at
        ORDER BY m.created_at DESC";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@miAlmacen", miAlmacenDestinoId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new TransferenciaPendienteDTO
                {
                    MovimientoSalidaId = reader.GetInt32(0),
                    GuiaOrigen = reader.GetString(1),
                    AlmacenOrigenNombre = reader.GetString(2),
                    ProductoId = reader.GetInt32(3),
                    ProductoNombre = reader.GetString(4),
                    CantidadEnTransito = reader.GetInt32(5),
                    FechaEnvio = reader.GetDateTime(6),
                    Seleccionado = false
                });
            }

            return lista;
        }
        public async Task<bool> RegistrarMovimientoCompletoAsync(
        Movimiento cabecera, List<VistaProductoGrid> productos, List<RangoCodigoItem> rangos,int ubicacionId,int? existingMovimientoId = null,
        IProgress<int>? progress = null)

        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
            int rolUsuarioActivo = SesionSistema.UsuarioActual?.RolUsuarioId ?? SesionSistema.UsuarioActual?.Rol?.Id ?? 0;

            // 🛑 CANDADO DE AUDITORÍA: Validar plazo de 5 días hábiles sobre created_at
            if (existingMovimientoId.HasValue)
            {
                DateTime? fechaCreacionOriginal = null;
                using (var cmdFecha = dbConn.CreateCommand())
                {
                    cmdFecha.CommandText = QueryAdapter.FormatearConsulta("SELECT created_at FROM movimientos WHERE id = @id");
                    AgregarParametro(cmdFecha, "@id", existingMovimientoId.Value);
                    var resFecha = await cmdFecha.ExecuteScalarAsync();
                    if (resFecha != null && resFecha != DBNull.Value)
                    {
                        fechaCreacionOriginal = Convert.ToDateTime(resFecha);
                    }
                }

                if (!AuditoriaPoliticas.ValidarPlazoEdicion(fechaCreacionOriginal, rolUsuarioActivo, out string mensajeBloqueo))
                {
                    throw new InvalidOperationException(mensajeBloqueo);
                }
            }
            using var transaccion = dbConn.BeginTransaction();
            int movimientoId = 0;
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";



            try

            {
                // 🌟 PERMITIR MOTIVOS VÁLIDOS DE INGRESO (Compras, Devoluciones, Ajustes, Transferencias, etc.)

                if (cabecera.MotivoProductoId <= 0)
                {
                    throw new InvalidOperationException("Debe seleccionar un motivo de movimiento válido.");
                }

                // 🌟 VALIDACIÓN DE INTEGRIDAD DE ENTRADA (Validado solo con id y tipo_movimiento_id)
                bool esMotivoEntradaValido = false;
                using (var cmdValidaMotivo = dbConn.CreateCommand())
                {
                    cmdValidaMotivo.Transaction = transaccion;
                    cmdValidaMotivo.CommandText = QueryAdapter.FormatearConsulta(
                        "SELECT 1 FROM motivo_productos WHERE id = @motivoId AND tipo_movimiento_id = 1");
                    AgregarParametro(cmdValidaMotivo, "@motivoId", cabecera.MotivoProductoId);

                    var resValida = await cmdValidaMotivo.ExecuteScalarAsync();
                    esMotivoEntradaValido = (resValida != null && resValida != DBNull.Value);
                }

                if (!esMotivoEntradaValido)
                {
                    throw new InvalidOperationException($"El motivo de movimiento ({cabecera.MotivoProductoId}) no es válido para este registro de ingreso.");
                }

                progress?.Report(5);
                // Línea donde se guarda la cabecera:
                movimientoId = await GuardarCabeceraAsync(cabecera, ubicacionId, existingMovimientoId, usuarioActivoId, productos.Count, dbConn, transaccion);


                var codigosPreviosEnBD = new HashSet<int>();
                if (existingMovimientoId.HasValue)
                {
                    using var cmdPrev = dbConn.CreateCommand();
                    cmdPrev.Transaction = transaccion;
                    cmdPrev.CommandText = QueryAdapter.FormatearConsulta("SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId");
                    AgregarParametro(cmdPrev, "@movId", existingMovimientoId.Value);
                    using var rdrPrev = await cmdPrev.ExecuteReaderAsync();
                    while (await rdrPrev.ReadAsync())
                    {
                        if (!rdrPrev.IsDBNull(0)) codigosPreviosEnBD.Add(rdrPrev.GetInt32(0));
                    }

                }
                var rangosPorProducto = rangos.GroupBy(r => r.productoId).ToDictionary(g => g.Key, g => g.ToList());

                // 🚀 FASE A: OBTENER TODOS LOS IDS DE FORMA MASIVA (1 Solo viaje a la BD)
                progress?.Report(15);

                var codigosTextoAProcesar = rangos
                .Where(r => r.DesdeNum == -1)
                .Select(r => NormalizarCodigo(r.AbreviaturaBase))
                .Concat(
                    rangos.Where(r => r.DesdeNum != -1)
                          .SelectMany(r => Enumerable.Range(r.DesdeNum, r.HastaNum - r.DesdeNum + 1)
                                                     .Select(i => NormalizarCodigo($"{r.AbreviaturaBase.TrimEnd('-')}-{i:D7}")))
                )
                .Distinct()
                .ToList();

                // Búsqueda masiva en lote O(1) usando el método que ya tienes en el servicio
                var mapaLookupCodigos = await ObtenerCodigosPorListaAsync(codigosTextoAProcesar);

                var nuevosIdsEnviados = new HashSet<int>(
                    mapaLookupCodigos.Values
                                     .Where(t => t.CodigoObj != null)
                                     .Select(t => t.CodigoObj.Id)
                );

                // 🌟 FASE B: OPTIMIZACIÓN SI NO CAMBIARON CÓDIGOS
                bool sonCodigosExactamenteIguales = existingMovimientoId.HasValue &&
                                                    codigosPreviosEnBD.Count == nuevosIdsEnviados.Count &&
                                                    codigosPreviosEnBD.SetEquals(nuevosIdsEnviados);

                if (sonCodigosExactamenteIguales)
                {
                    foreach (var item in productos) await UpsertMovimientoDetalleAsync(movimientoId, item, dbConn, transaccion);
                    var productosUnicosIguales = productos.Select(p => p.ProductoId).Distinct();
                    int almacenAfectadoIgual = cabecera.AlmacenDestinoId ?? cabecera.AlmacenId ?? 1;
                    foreach (var pid in productosUnicosIguales) await ActualizarStockProductoPorKardexAsync(pid, almacenAfectadoIgual, dbConn, transaccion);

                    progress?.Report(100);
                    await transaccion.CommitAsync();
                    return true;
                }

                // 🌟 FASE C: PROCESAR CÓDIGOS RETIRADOS (VALIDACIÓN DE MOVIMIENTOS POSTERIORES Y REVERSIÓN)
                progress?.Report(30);
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosIdsEnviados.Contains(id)).ToList();

                if (codigosAEliminar.Any())
                {
                    const int checkBatchSize = 1000;
                    var conflictosDetectados = new List<string>();

                    for (int i = 0; i < codigosAEliminar.Count; i += checkBatchSize)
                    {
                        var batchDelCheck = codigosAEliminar.Skip(i).Take(checkBatchSize).ToList();
                        var paramNamesCheck = batchDelCheck.Select((id, idx) => $"@delCheck{idx}").ToList();

                        int? idSalidaOrigenPadre = null;
                        if (cabecera.MotivoProductoId == 4 && cabecera.AlmacenOrigenId.HasValue)
                        {
                            using var cmdFindSalida = dbConn.CreateCommand();
                            cmdFindSalida.Transaction = transaccion;
                            cmdFindSalida.CommandText = QueryAdapter.FormatearConsulta(@"
                            SELECT TOP 1 m.id 
                            FROM movimientos m WITH (NOLOCK)
                            INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_id = m.id
                            WHERE mc.codigo_creado_id IN (" + string.Join(",", paramNamesCheck) + @")
                              AND m.motivo_producto_id = 10
                              AND m.id < @movIdCurrent
                            ORDER BY m.id DESC");

                            AgregarParametro(cmdFindSalida, "@movIdCurrent", movimientoId);
                            for (int k = 0; k < batchDelCheck.Count; k++) AgregarParametro(cmdFindSalida, $"@delCheck{k}", batchDelCheck[k]);

                            var resPadre = await cmdFindSalida.ExecuteScalarAsync();
                            if (resPadre != null && resPadre != DBNull.Value) idSalidaOrigenPadre = Convert.ToInt32(resPadre);
                        }

                        string sqlFuturoDetallado = $@"
                        SELECT 
                            cc.codigo,
                            mp.descripcion AS motivo_desc,
                            m.serie_documento,
                            m.numero_documento,
                            m.fecha_movimiento
                        FROM movimiento_codigos mc WITH (NOLOCK)
                        INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
                        INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
                        INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
                        WHERE mc.codigo_creado_id IN ({string.Join(",", paramNamesCheck)})
                          AND m.id != @movId
                          AND (@movPadreId IS NULL OR m.id != @movPadreId)
                          AND m.estado_id = 1
                          AND (
                              m.fecha_movimiento > @fechaEdicion 
                              OR (m.fecha_movimiento = @fechaEdicion AND m.id > @movId)
                          )";

                        using var cmdFuturo = dbConn.CreateCommand();
                        cmdFuturo.Transaction = transaccion;
                        cmdFuturo.CommandText = QueryAdapter.FormatearConsulta(sqlFuturoDetallado);

                        AgregarParametro(cmdFuturo, "@movId", movimientoId);
                        AgregarParametro(cmdFuturo, "@movPadreId", idSalidaOrigenPadre.HasValue ? (object)idSalidaOrigenPadre.Value : DBNull.Value);
                        DateTime fechaConvertida = cabecera.FechaMovimiento ?? DateTime.Today;

                        for (int k = 0; k < batchDelCheck.Count; k++) AgregarParametro(cmdFuturo, $"@delCheck{k}", batchDelCheck[k]);

                        using var rdrFut = await cmdFuturo.ExecuteReaderAsync();
                        while (await rdrFut.ReadAsync())
                        {
                            string cod = rdrFut.GetString(0);
                            string motivo = rdrFut.IsDBNull(1) ? "Movimiento Posterior" : rdrFut.GetString(1);
                            string serieDoc = rdrFut.IsDBNull(2) ? "" : rdrFut.GetString(2);
                            string numDoc = rdrFut.IsDBNull(3) ? "" : rdrFut.GetString(3);
                            DateTime fechaMov = rdrFut.IsDBNull(4) ? DateTime.MinValue : rdrFut.GetDateTime(4);

                            string detalleConflicto = $"• Código '{cod}': Bloqueado por [{motivo}] en Doc {serieDoc}-{numDoc} ({fechaMov:dd/MM/yyyy})";
                            if (!conflictosDetectados.Contains(detalleConflicto))
                            {
                                conflictosDetectados.Add(detalleConflicto);
                            }
                        }
                        rdrFut.Close();
                    }

                    if (conflictosDetectados.Any())
                    {
                        var muestra = conflictosDetectados.Take(12).ToList();
                        string masInfo = conflictosDetectados.Count > 12 ? $"\n... y {conflictosDetectados.Count - 12} código(s) más con movimientos." : "";
                        throw new InvalidOperationException($"⚠️ Operación Rechazada por Trazabilidad de Kárdex:\n\nNo es posible retirar/eliminar los siguientes códigos porque ya cuentan con movimientos posteriores (transferencias, salidas o ventas) en el sistema:\n\n{string.Join("\n", muestra)}{masInfo}\n\nPara poder retirarlos de este documento, primero debe anular o regularizar los movimientos posteriores indicados.");
                    }

                    var mapaHistorial = new Dictionary<int, (int EstadoId, int AlmacenId)>();
                    for (int i = 0; i < codigosAEliminar.Count; i += checkBatchSize)
                    {
                        var batchDel = codigosAEliminar.Skip(i).Take(checkBatchSize).ToList();
                        var paramNames = batchDel.Select((id, idx) => $"@h{idx}").ToList();

                        string queryHistorialLote = QueryAdapter.EsMySQL
                            ? $@"SELECT mc.codigo_creado_id, m.motivo_producto_id, mp.tipo_movimiento_id,
                           COALESCE(m.almacen_destino_id, m.almacen_id, 1) AS alm_destino,
                           COALESCE(m.almacen_origen_id, m.almacen_id, 1) AS alm_origen
                    FROM movimiento_codigos mc
                    INNER JOIN movimientos m ON mc.movimiento_id = m.id
                    INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                    WHERE mc.codigo_creado_id IN ({string.Join(",", paramNames)}) AND m.id < @movId AND m.estado_id = 1
                    ORDER BY mc.codigo_creado_id, m.fecha_movimiento DESC, m.id DESC"
                            : $@"WITH HistorialOrdenado AS (
                        SELECT mc.codigo_creado_id, m.motivo_producto_id, mp.tipo_movimiento_id,
                               ISNULL(m.almacen_destino_id, ISNULL(m.almacen_id, 1)) AS alm_destino,
                               ISNULL(m.almacen_origen_id, ISNULL(m.almacen_id, 1)) AS alm_origen,
                               ROW_NUMBER() OVER(PARTITION BY mc.codigo_creado_id ORDER BY m.fecha_movimiento DESC, m.id DESC) as rn
                        FROM movimiento_codigos mc INNER JOIN movimientos m ON mc.movimiento_id = m.id INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                        WHERE mc.codigo_creado_id IN ({string.Join(",", paramNames)}) AND m.id < @movId AND m.estado_id = 1
                    ) SELECT codigo_creado_id, motivo_producto_id, tipo_movimiento_id, alm_destino, alm_origen FROM HistorialOrdenado WHERE rn = 1";

                        using var cmdHist = dbConn.CreateCommand();
                        cmdHist.Transaction = transaccion;
                        cmdHist.CommandText = QueryAdapter.FormatearConsulta(queryHistorialLote);
                        AgregarParametro(cmdHist, "@movId", movimientoId);
                        for (int k = 0; k < batchDel.Count; k++) AgregarParametro(cmdHist, $"@h{k}", batchDel[k]);

                        using var rdrH = await cmdHist.ExecuteReaderAsync();
                        while (await rdrH.ReadAsync())
                        {
                            int codIdBD = rdrH.GetInt32(0);
                            if (!mapaHistorial.ContainsKey(codIdBD))
                            {
                                int motivoId = rdrH.GetInt32(1); int tipoMov = rdrH.GetInt32(2); int almDest = rdrH.GetInt32(3); int almOrig = rdrH.GetInt32(4);
                                int estFinal = (motivoId == 10) ? 5 : ((tipoMov == 1 || motivoId == 4) ? 3 : 4);
                                int almFinal = (motivoId == 10 || tipoMov == 1 || motivoId == 4) ? almDest : almOrig;
                                mapaHistorial[codIdBD] = (estFinal, almFinal);
                            }
                        }
                        rdrH.Close();
                    }

                    // 🌟 FASE C: PROCESAR CÓDIGOS RETIRADOS
                    var gruposReversion = new Dictionary<(int EstadoId, int AlmacenId), List<int>>();

                    foreach (var codId in codigosAEliminar)
                    {
                        // 🚚 CASO 1: TRANSFERENCIA ENTRE ALMACENES (Motivo 4)
                        // El código regresa estrictamente a la sede origen en Estado 3
                        if (cabecera.MotivoProductoId == 4)
                        {
                            int almOrigenReal = cabecera.AlmacenOrigenId ?? 1;
                            var claveTransfer = (3, almOrigenReal);
                            if (!gruposReversion.ContainsKey(claveTransfer)) gruposReversion[claveTransfer] = new List<int>();
                            gruposReversion[claveTransfer].Add(codId);
                        }
                        // 🛒 CASO 2: COMPRA ESTRICTA (Motivo 1)
                        // Siempre regresa a Estado 1
                        else if (cabecera.MotivoProductoId == 1)
                        {
                            int almActual = cabecera.AlmacenDestinoId ?? cabecera.AlmacenId ?? 1;
                            var claveCompra = (1, almActual);
                            if (!gruposReversion.ContainsKey(claveCompra)) gruposReversion[claveCompra] = new List<int>();
                            gruposReversion[claveCompra].Add(codId);
                        }
                        // 🔄 CASO 3: MOTIVO 13 (OTROS), MOTIVO 2 (DEVOLUCIONES), MOTIVO 3 (PROMOTORÍA)
                        // Si tiene historial vuelve a su estado previo; si no tiene historial (código nuevo) vuelve a (Estado 1, Almacén Actual)
                        else
                        {
                            int almActual = cabecera.AlmacenDestinoId ?? cabecera.AlmacenId ?? 1;
                            var clave = mapaHistorial.TryGetValue(codId, out var h) ? h : (1, almActual);

                            if (!gruposReversion.ContainsKey(clave)) gruposReversion[clave] = new List<int>();
                            gruposReversion[clave].Add(codId);
                        }
                    }

                    foreach (var kvp in gruposReversion)
                    {
                        int estDestino = kvp.Key.EstadoId; int almDestino = kvp.Key.AlmacenId; var listaIds = kvp.Value;
                        for (int i = 0; i < listaIds.Count; i += checkBatchSize)
                        {
                            var batchDel = listaIds.Skip(i).Take(checkBatchSize).ToList();
                            var paramNames = batchDel.Select((id, idx) => $"@d{idx}").ToList();

                            string queryResetBulk = $"UPDATE codigos_creados SET estado_id = @estDest, almacen_id = @almDest WHERE id IN ({string.Join(",", paramNames)})";
                            using var cmdReset = dbConn.CreateCommand();
                            cmdReset.Transaction = transaccion;
                            cmdReset.CommandText = QueryAdapter.FormatearConsulta(queryResetBulk);
                            AgregarParametro(cmdReset, "@estDest", estDestino); AgregarParametro(cmdReset, "@almDest", almDestino);
                            for (int k = 0; k < batchDel.Count; k++) AgregarParametro(cmdReset, $"@d{k}", batchDel[k]);
                            await cmdReset.ExecuteNonQueryAsync();

                            string queryDelMc = $"DELETE FROM movimiento_codigos WHERE movimiento_id = @movId AND codigo_creado_id IN ({string.Join(",", paramNames)})";
                            using var cmdDelMc = dbConn.CreateCommand();
                            cmdDelMc.Transaction = transaccion;
                            cmdDelMc.CommandText = QueryAdapter.FormatearConsulta(queryDelMc);
                            AgregarParametro(cmdDelMc, "@movId", movimientoId);
                            for (int k = 0; k < batchDel.Count; k++) AgregarParametro(cmdDelMc, $"@d{k}", batchDel[k]);
                            await cmdDelMc.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 🌟 FASE D: PROCESAMIENTO E INSERCIÓN MASIVA DE NUEVOS DETALLES Y CÓDIGOS
                progress?.Report(50);
                int miAlmacenActualId = cabecera.AlmacenDestinoId ?? cabecera.AlmacenId ?? 1;

                foreach (var item in productos)
                {
                    int detalleId = await UpsertMovimientoDetalleAsync(movimientoId, item, dbConn, transaccion);

                    if (existingMovimientoId.HasValue)
                    {
                        using var cmdDelR = dbConn.CreateCommand();
                        cmdDelR.Transaction = transaccion;
                        cmdDelR.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM registro_rangos WHERE movimiento_detalle_id = @detId");
                        AgregarParametro(cmdDelR, "@detId", detalleId);
                        await cmdDelR.ExecuteNonQueryAsync();

                        using var cmdDelC = dbConn.CreateCommand();
                        cmdDelC.Transaction = transaccion;
                        cmdDelC.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_detalle_id = @detId");
                        AgregarParametro(cmdDelC, "@detId", detalleId);
                        await cmdDelC.ExecuteNonQueryAsync();
                    }

                    if (!rangosPorProducto.TryGetValue(item.ProductoId, out var rangosProd))
                    {
                        string sqlInsRangoGenerico = $@"INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at) 
                                         VALUES (@pId, 2, 'SIN_CODIGO', -1, -1, @detId, {nowFunc})";
                        using var cmdRG = dbConn.CreateCommand();
                        cmdRG.Transaction = transaccion;
                        cmdRG.CommandText = QueryAdapter.FormatearConsulta(sqlInsRangoGenerico);
                        AgregarParametro(cmdRG, "@pId", item.ProductoId);
                        AgregarParametro(cmdRG, "@detId", detalleId);
                        await cmdRG.ExecuteNonQueryAsync();
                        continue;
                    }

                    // ✔️ AHORA (Guarda todos los sub-rangos en 1 solo viaje a la BD):
                    await InsertarRangosMasivoAsync(rangosProd, detalleId, dbConn, transaccion);

                    // 2. Extraer los IDs directamente desde el lookup masivo ya cargado en memoria RAM
                    var codigosTextoEsteProd = rangosProd
                    .Where(r => r.DesdeNum == -1)
                    .Select(r => NormalizarCodigo(r.AbreviaturaBase))
                    .Concat(
                        rangosProd.Where(r => r.DesdeNum != -1)
                                  .SelectMany(r => Enumerable.Range(r.DesdeNum, r.HastaNum - r.DesdeNum + 1)
                                                             .Select(i => NormalizarCodigo($"{r.AbreviaturaBase.TrimEnd('-')}-{i:D7}")))
                    )
                    .ToList();

                    var codigosAInsertar = new List<int>();
                    foreach (var cNorm in codigosTextoEsteProd)
                    {
                        if (mapaLookupCodigos.TryGetValue(cNorm, out var tup) && tup.CodigoObj != null)
                        {
                            codigosAInsertar.Add(tup.CodigoObj.Id);
                        }
                    }

                    // 3. Inserción masiva de relaciones y actualización de estado
                    const int bulkSize = 1000;
                    for (int i = 0; i < codigosAInsertar.Count; i += bulkSize)
                    {
                        var batch = codigosAInsertar.Skip(i).Take(bulkSize).ToList();

                        await InsertarMovimientoCodigosMasivoAsync(movimientoId, detalleId, batch, dbConn, transaccion);

                        var codigosParaActualizarEstado = new List<int>();

                        if (existingMovimientoId.HasValue)
                        {
                            var paramNamesBatch = batch.Select((_, idx) => $"@chkPost{idx}").ToList();

                            string sqlFuturoLote = $@"
                            SELECT DISTINCT mc.codigo_creado_id
                            FROM movimiento_codigos mc WITH (NOLOCK)
                            INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
                            WHERE mc.codigo_creado_id IN ({string.Join(",", paramNamesBatch)})
                              AND m.id != @movId
                              AND m.estado_id = 1
                              AND (m.fecha_movimiento > @fechaEdicion OR (m.fecha_movimiento = @fechaEdicion AND m.id > @movId))";

                            var setCodigosConFuturo = new HashSet<int>();
                            using (var cmdFutLote = dbConn.CreateCommand())
                            {
                                cmdFutLote.Transaction = transaccion;
                                cmdFutLote.CommandText = QueryAdapter.FormatearConsulta(sqlFuturoLote);
                                AgregarParametro(cmdFutLote, "@movId", movimientoId);
                                DateTime fechaConvertida = cabecera.FechaMovimiento ?? DateTime.Today;

                                for (int k = 0; k < batch.Count; k++)
                                {
                                    AgregarParametro(cmdFutLote, $"@chkPost{k}", batch[k]);
                                }

                                using var rdrFutLote = await cmdFutLote.ExecuteReaderAsync();
                                while (await rdrFutLote.ReadAsync())
                                {
                                    setCodigosConFuturo.Add(rdrFutLote.GetInt32(0));
                                }
                            }

                            codigosParaActualizarEstado = batch.Where(codId => !setCodigosConFuturo.Contains(codId)).ToList();
                        }
                        else
                        {
                            codigosParaActualizarEstado = batch;
                        }

                        // 🌟 En la Fase D de IngresoMovimientoService SIEMPRE es Estado 3 (Disponible en tu almacén)
                        if (codigosParaActualizarEstado.Any())
                        {
                            var paramUpdate = codigosParaActualizarEstado.Select((_, idx) => $"@uId{idx}").ToList();
                            string queryUpdateCodigos = $"UPDATE codigos_creados SET estado_id = 3, almacen_id = @almActual WHERE id IN ({string.Join(",", paramUpdate)})";

                            using var cmdUpd = dbConn.CreateCommand();
                            cmdUpd.Transaction = transaccion;
                            cmdUpd.CommandText = QueryAdapter.FormatearConsulta(queryUpdateCodigos);
                            AgregarParametro(cmdUpd, "@almActual", miAlmacenActualId);
                            for (int k = 0; k < codigosParaActualizarEstado.Count; k++)
                            {
                                AgregarParametro(cmdUpd, $"@uId{k}", codigosParaActualizarEstado[k]);
                            }
                            await cmdUpd.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 🌟 FASE E: RECALCULAR STOCK FÍSICO EN ALMACÉN
                progress?.Report(95);
                var productosUnicos = productos.Select(p => p.ProductoId).Distinct();
                int almacenAfectado = cabecera.AlmacenDestinoId ?? cabecera.AlmacenId ?? 1;
                foreach (var pid in productosUnicos)
                {
                    await ActualizarStockProductoPorKardexAsync(pid, almacenAfectado, dbConn, transaccion);
                }

                progress?.Report(100);
                transaccion.Commit();
                return true;
            }
            catch (Exception)
            {
                transaccion.Rollback();
                throw;
            }
        }

        private async Task InsertarMovimientoCodigosMasivoAsync(int movId, int detId, List<int> codigosIds, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            const int batchSize = 500;
            string funcionFecha = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            for (int i = 0; i < codigosIds.Count; i += batchSize)
            {
                var batch = codigosIds.Skip(i).Take(batchSize).ToList();
                var sb = new System.Text.StringBuilder();

                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;

                sb.Append($"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES ");

                for (int j = 0; j < batch.Count; j++)
                {
                    sb.Append($"(@movId, @detId, @c{j}, 1, 0, {funcionFecha})");
                    if (j < batch.Count - 1) sb.Append(",");

                    AgregarParametro(cmd, "@c" + j, batch[j]);
                }

                cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
                AgregarParametro(cmd, "@movId", movId);
                AgregarParametro(cmd, "@detId", detId);

                await cmd.ExecuteNonQueryAsync();
                sb.Clear();
            }
        }
        private async Task InsertarRangosMasivoAsync(List<RangoCodigoItem> rangos, int detId, DbConnection conn, DbTransaction trans)
        {
            if (rangos == null || !rangos.Any()) return;

            const int batchSize = 300; // 🌟 300 x 6 = 1,800 parámetros (límite seguro para SQL Server y MySQL)
            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            for (int i = 0; i < rangos.Count; i += batchSize)
            {
                var batch = rangos.Skip(i).Take(batchSize).ToList();
                var sb = new System.Text.StringBuilder();

                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;

                sb.Append("INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at) VALUES ");

                for (int j = 0; j < batch.Count; j++)
                {
                    var r = batch[j];
                    sb.Append($"(@prodId{j}, @catId{j}, @abrev{j}, @desde{j}, @hasta{j}, @detId{j}, {nowFunc})");
                    if (j < batch.Count - 1) sb.Append(",");

                    AgregarParametro(cmd, $"@prodId{j}", r.productoId);
                    AgregarParametro(cmd, $"@catId{j}", r.CategoriaProductoId);
                    AgregarParametro(cmd, $"@abrev{j}", r.AbreviaturaBase);
                    AgregarParametro(cmd, $"@desde{j}", r.DesdeNum);
                    AgregarParametro(cmd, $"@hasta{j}", r.HastaNum);
                    AgregarParametro(cmd, $"@detId{j}", detId);
                }

                cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
                await cmd.ExecuteNonQueryAsync();
                sb.Clear();
            }
        }
        private async Task ActualizarEstadoCodigosMasivoAsync(List<int> codigosIds, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var paramNames = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            for (int i = 0; i < codigosIds.Count; i++)
            {
                string paramName = "@u" + i;
                paramNames.Add(paramName);
                AgregarParametro(cmd, paramName, codigosIds[i]);
            }

            // 🌟 CAMBIO: Apuesta directo a la nueva tabla estados_codigo
            cmd.CommandText = QueryAdapter.FormatearConsulta(
                $"UPDATE codigos_creados SET estado_id = @estado WHERE id IN ({string.Join(",", paramNames)})");

            AgregarParametro(cmd, "@estado", nuevoEstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ActualizarEstadoCodigo(int codigoId, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            // 🌟 CAMBIO: Apuesta directo a la nueva tabla estados_codigo
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = @estado WHERE id = @id");

            AgregarParametro(cmd, "@estado", nuevoEstadoId);
            AgregarParametro(cmd, "@id", codigoId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<(CodigoCreado CodigoObj, int Seq)>> ObtenerIdsCodigosPorRangoAsync(int productoId, string baseLimpia, int categoriaId, int desde, int hasta, DbConnection conn, DbTransaction trans)
        {
            var resultados = new List<(CodigoCreado CodigoObj, int Seq)>();

            if (desde == -1)
            {
                string queryPack = @"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND rc.categoria_producto_id = @categoriaId -- 🌟 FILTRO DE CATEGORÍA AGREGADO
              AND cc.codigo = @codigoExacto";

                using var cmdPack = conn.CreateCommand();
                cmdPack.Transaction = trans;
                cmdPack.CommandText = QueryAdapter.FormatearConsulta(queryPack);

                AgregarParametro(cmdPack, "@productoId", productoId);
                AgregarParametro(cmdPack, "@categoriaId", categoriaId); // 🌟 PARAMETRO DE CATEGORIA
                AgregarParametro(cmdPack, "@codigoExacto", baseLimpia);

                using var readerPack = await cmdPack.ExecuteReaderAsync();
                if (await readerPack.ReadAsync())
                {
                    resultados.Add((
                        new CodigoCreado
                        {
                            Id = readerPack.GetInt32(0),
                            RegistroCodigoId = readerPack.IsDBNull(1) ? 0 : readerPack.GetInt32(1),
                            Codigo = readerPack.GetString(2),
                            EsManual = !readerPack.IsDBNull(3) && readerPack.GetBoolean(3),
                            EstadoId = readerPack.IsDBNull(4) ? 0 : readerPack.GetInt32(4)
                        },
                        -1
                    ));
                }
                return resultados;
            }

            string queryMaster;

            if (QueryAdapter.EsMySQL)
            {
                // 🟢 MySQL: Valida el tipo de libro (categoria_producto_id) y usa REGEXP para números
                queryMaster = @"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id,
                   CAST(RIGHT(cc.codigo, 7) AS SIGNED) as seq
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND rc.categoria_producto_id = @categoriaId -- 🌟 FILTRO DE CATEGORÍA OBLIGATORIO
              AND RIGHT(cc.codigo, 7) REGEXP '^[0-9]+$'
              AND CAST(RIGHT(cc.codigo, 7) AS SIGNED) BETWEEN @desde AND @hasta";
            }
            else
            {
                // 🛡️ SQL Server: Consulta con filtro estricto de Categoria
                queryMaster = @"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id,
                   CAST(RIGHT(cc.codigo, 7) AS INT) as seq
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND rc.categoria_producto_id = @categoriaId -- 🌟 FILTRO DE CATEGORÍA OBLIGATORIO
              AND ISNUMERIC(RIGHT(cc.codigo, 7)) = 1
              AND CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";
            }

            using var cmdQuery = conn.CreateCommand();
            cmdQuery.Transaction = trans;
            cmdQuery.CommandText = QueryAdapter.FormatearConsulta(queryMaster);
            AgregarParametro(cmdQuery, "@productoId", productoId);
            AgregarParametro(cmdQuery, "@categoriaId", categoriaId); // 🌟 PARAMETRO DE CATEGORIA
            AgregarParametro(cmdQuery, "@desde", desde);
            AgregarParametro(cmdQuery, "@hasta", hasta);

            using var reader = await cmdQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultados.Add((
                    new CodigoCreado
                    {
                        Id = reader.GetInt32(0),
                        RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        Codigo = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        EsManual = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                    },
                    reader.GetInt32(5)
                ));
            }

            return resultados;
        }

        public string NormalizarCodigo(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;

            string s = codigo.ToUpperInvariant().Trim();
            s = s.Replace("'", "-");
            s = s.Replace("\u2019", "-").Replace("\u2018", "-");

            int posGuion = s.LastIndexOf('-');
            if (posGuion >= 0)
            {
                string prefijoBase = s.Substring(0, posGuion + 1);
                string parteNumerica = s.Substring(posGuion + 1);

                if (!string.IsNullOrEmpty(parteNumerica) && parteNumerica.All(char.IsDigit))
                {
                    if (int.TryParse(parteNumerica, out int numeroVal))
                    {
                        // Mantiene el prefijo completo (incluyendo el -G-G-) y formatea solo los 7 dígitos
                        s = prefijoBase + numeroVal.ToString("D7");
                    }
                }
            }

            return s;
        }

        public async Task<Dictionary<string, (CodigoCreado CodigoObj, int? ProductoId)>> ObtenerCodigosPorListaAsync(IEnumerable<string> codigos, int? almacenId = null)
        {
            if (codigos == null)
                return new Dictionary<string, (CodigoCreado, int?)>();

            var listaNormalizada = codigos
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => NormalizarCodigo(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resultado = new Dictionary<string, (CodigoCreado, int?)>(
                listaNormalizada.Count,
                StringComparer.OrdinalIgnoreCase);

            if (listaNormalizada.Count == 0)
                return resultado;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            const int batchSize = 500;
            for (int i = 0; i < listaNormalizada.Count; i += batchSize)
            {
                var lote = listaNormalizada.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();

                using var cmdQuery = dbConn.CreateCommand();

                for (int j = 0; j < lote.Count; j++)
                {
                    string pName = "@p" + j;
                    paramNames.Add(pName);
                    var p = cmdQuery.CreateParameter();
                    p.ParameterName = pName;
                    p.Value = lote[j];
                    cmdQuery.Parameters.Add(p);
                }

                string hintIndex = QueryAdapter.EsMySQL ? "" : "WITH (INDEX(IX_codigos_creados_codigo_perf))";

                // 🌟 CONDICIÓN OPCIONAL: Si viene almacenId, agrega la cláusula a la consulta SQL
                string sqlAlmacenFiltro = almacenId.HasValue ? " AND cc.almacen_id = @almId " : "";

                string queryMaster = $@"
            SELECT 
                cc.id, 
                cc.registro_codigo_id, 
                cc.codigo, 
                cc.es_manual, 
                cc.estado_id, 
                cc.almacen_id,
                rc.producto_id
            FROM codigos_creados cc {hintIndex}
            LEFT JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE cc.codigo IN ({string.Join(",", paramNames)}) {sqlAlmacenFiltro}";

                cmdQuery.CommandText = QueryAdapter.FormatearConsulta(queryMaster);

                if (almacenId.HasValue)
                {
                    var pAlm = cmdQuery.CreateParameter();
                    pAlm.ParameterName = "@almId";
                    pAlm.Value = almacenId.Value;
                    cmdQuery.Parameters.Add(pAlm);
                }

                using var reader = await cmdQuery.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string codigoRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    string codigoNorm = NormalizarCodigo(codigoRaw);

                    if (!resultado.ContainsKey(codigoNorm))
                    {
                        var codigoCreado = new CodigoCreado
                        {
                            Id = reader.GetInt32(0),
                            RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            Codigo = codigoRaw,
                            EsManual = !reader.IsDBNull(3) && reader.GetBoolean(3),
                            EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            AlmacenId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                        };

                        int? productoId = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                        resultado.Add(codigoNorm, (codigoCreado, productoId));
                    }
                }
            }

            return resultado;
        }

        public async Task<HashSet<int>> ObtenerCodigosEnMovimientoAsync(IEnumerable<int> codigoIds)
        {
            var set = new HashSet<int>();
            var ids = codigoIds?.Distinct().ToList();
            if (ids == null || !ids.Any()) return set;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            const int batchSize = 1000;
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();
                for (int j = 0; j < batch.Count; j++) paramNames.Add("@p" + j);

                string q = $@"SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE codigo_creado_id IN ({string.Join(',', paramNames)})";
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                for (int j = 0; j < batch.Count; j++)
                {
                    var p = cmd.CreateParameter(); p.ParameterName = "@p" + j; p.Value = batch[j]; cmd.Parameters.Add(p);
                }

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0)) set.Add(rdr.GetInt32(0));
                }
            }
            return set;
        }

        public async Task<bool> RegistrarCodigosImportadosAsync(Movimiento cabecera, List<(int CodigoCreadoId, int ProductoId)> codigosImportados, int usuarioId, int? existingMovimientoId = null)
        {
            if (codigosImportados == null || !codigosImportados.Any()) return false;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaccion = dbConn.BeginTransaction();
            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                int movimientoIdInserted = 0;

                if (existingMovimientoId.HasValue)
                {
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using (var cmdUpdCab = dbConn.CreateCommand())
                    {
                        cmdUpdCab.Transaction = transaccion;
                        cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                        DateTime fechaConvertida = cabecera.FechaMovimiento ?? DateTime.Today;
                        AgregarParametro(cmdUpdCab, "@fecha", fechaConvertida);
                        AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdUpdCab, "@ubicacionId", cabecera.UbicacionId);
                        AgregarParametro(cmdUpdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdUpdCab, "@personaId", cabecera.PersonaComercialId);
                        AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                        AgregarParametro(cmdUpdCab, "@id", existingMovimientoId.Value);
                        await cmdUpdCab.ExecuteNonQueryAsync();
                    }

                    movimientoIdInserted = existingMovimientoId.Value;

                    using (var cmdDel = dbConn.CreateCommand())
                    {
                        cmdDel.Transaction = transaccion;
                        cmdDel.CommandText = QueryAdapter.FormatearConsulta(@"
                            DELETE FROM movimiento_codigos WHERE movimiento_id = @movId;
                            DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId);
                            DELETE FROM movimiento_detalles WHERE movimiento_id = @movId;
                        ");
                        AgregarParametro(cmdDel, "@movId", movimientoIdInserted);
                        await cmdDel.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    string queryCabecera = $@"
                    INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                            motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia, created_at)
                    VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId, @serieGuia, @numeroGuia, GETDATE());
                    {selectId}";

                    using (var cmdCab = dbConn.CreateCommand())
                    {
                        cmdCab.Transaction = transaccion;
                        cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);

                        DateTime fechaConvertida = cabecera.FechaMovimiento ?? DateTime.Today;

                        // 🌟 ACTUALIZACIÓN: Inicia con estado_id = 1 (PROCESADO en la tabla de cabeceras)
                        AgregarParametro(cmdCab, "@estadoId", 1);
                        AgregarParametro(cmdCab, "@fecha", fechaConvertida);
                        AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                        AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                        AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId);
                        AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
                        AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);

                        object resultCab = await cmdCab.ExecuteScalarAsync();
                        if (resultCab == null || resultCab == DBNull.Value) throw new Exception("No se pudo obtener el ID de la cabecera.");
                        movimientoIdInserted = Convert.ToInt32(resultCab);
                    }
                }

                var grupos = codigosImportados.GroupBy(x => x.ProductoId);

                foreach (var grupo in grupos)
                {
                    int productoId = grupo.Key;
                    int cantidad = grupo.Count();

                    int detalleIdInserted = 0;
                    string queryDetalle = $@"
                        INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                        VALUES (@movimientoId, @productoId, @cantidad, 0, 0, GETDATE());
                        {selectId}";

                    using (var cmdDet = dbConn.CreateCommand())
                    {
                        cmdDet.Transaction = transaccion;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                        AgregarParametro(cmdDet, "@movimientoId", movimientoIdInserted);
                        AgregarParametro(cmdDet, "@productoId", productoId);
                        AgregarParametro(cmdDet, "@cantidad", cantidad); // 🌟 Cantidad como int pura
                        object resultDet = await cmdDet.ExecuteScalarAsync();
                        detalleIdInserted = Convert.ToInt32(resultDet);
                    }

                    foreach (var item in grupo)
                    {
                        int codigoId = item.CodigoCreadoId;
                        using (var cmdMovCod = dbConn.CreateCommand())
                        {
                            cmdMovCod.Transaction = transaccion;
                            cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(@"
                                INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at)
                                VALUES (@movId, @detId, @codId, 1, 0, GETDATE());");

                            AgregarParametro(cmdMovCod, "@movId", movimientoIdInserted);
                            AgregarParametro(cmdMovCod, "@detId", detalleIdInserted);
                            AgregarParametro(cmdMovCod, "@codId", codigoId);
                            await cmdMovCod.ExecuteNonQueryAsync();
                        }

                        bool shouldUpdateState = false;
                        if (!existingMovimientoId.HasValue)
                        {
                            shouldUpdateState = true;
                        }
                        else
                        {
                            try
                            {
                                using var cmdCheck = dbConn.CreateCommand();
                                cmdCheck.Transaction = transaccion;
                                cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT estado_id FROM codigos_creados WHERE id = @id");
                                AgregarParametro(cmdCheck, "@id", codigoId);
                                object st = await cmdCheck.ExecuteScalarAsync();
                                int estadoActual = st == null || st == DBNull.Value ? 0 : Convert.ToInt32(st);

                                // 🌟 ACTUALIZACIÓN HÍBRIDA: Acepta códigos nuevos (1) o que retornan de venta/tránsito (4)
                                if (estadoActual == 1 || estadoActual == 4)
                                {
                                    shouldUpdateState = true;
                                }
                            }
                            catch { shouldUpdateState = false; }
                        }

                        if (shouldUpdateState)
                        {
                            using (var cmdUpd = dbConn.CreateCommand())
                            {
                                cmdUpd.Transaction = transaccion;
                                // 🌟 ACTUALIZACIÓN: Pasa a estado 3 (DISPONIBLE EN ALMACÉN) de la tabla estados_codigo
                                cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 3 WHERE id = @id");
                                AgregarParametro(cmdUpd, "@id", codigoId);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }

                await transaccion.CommitAsync();
                return true;
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        private void NormalizarRangosImportados(ObservableCollection<RangoCodigoItem> lista)
        {
            foreach (var item in lista)
            {
                if (string.IsNullOrEmpty(item.Desde) && item.DesdeNum > 0)
                    item.Desde = item.DesdeNum.ToString();

                if (string.IsNullOrEmpty(item.Hasta) && item.HastaNum > 0)
                    item.Hasta = item.HastaNum.ToString();

                if (string.IsNullOrEmpty(item.ColeccionTipo))
                    item.ColeccionTipo = "Importado - N/A";
            }
        }

        public async Task<int> ObtenerCategoriaDesdeBDAsync(int codigoId)
        {
            try
            {
                using var conn = new DatabaseConnection().GetConnection();
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT rc.categoria_producto_id 
                FROM registro_codigos rc 
                JOIN codigos_creados cc ON cc.registro_codigo_id = rc.id 
                WHERE cc.id = @id");

                var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = codigoId; cmd.Parameters.Add(p);
                var res = await cmd.ExecuteScalarAsync();
                return res != null ? Convert.ToInt32(res) : 1;
            }
            catch { return 1; }
        }

        public async Task<string> ObtenerColeccionTipoBDAsync(int codigoCreadoId)
        {
            try
            {
                using var conn = new DatabaseConnection().GetConnection();
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT c.ano, rc.categoria_producto_id 
                FROM codigos_creados cc
                JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                LEFT JOIN colecciones c ON rc.coleccion_id = c.id
                WHERE cc.id = @id");

                var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = codigoCreadoId; cmd.Parameters.Add(p);
                using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    string ano = rdr.IsDBNull(0) ? "" : rdr.GetValue(0).ToString();
                    int cat = rdr.IsDBNull(1) ? 1 : rdr.GetInt32(1);
                    string tipo = cat == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                    if (!string.IsNullOrEmpty(ano)) return $"C{ano} / {tipo}";
                    return tipo;
                }
            }
            catch { }
            return "LIBRO VENTA";
        }

        public List<RangoCodigoItem> GenerarRangosDesdeCodigos(List<VistaCodigoGrid> codigos)
        {
            var resultado = new List<RangoCodigoItem>();

            if (codigos == null || codigos.Count == 0)
                return resultado;

            foreach (var grupoProducto in codigos.GroupBy(x => x.ProductoId))
            {
                int productoId = grupoProducto.Key;
                var secuenciales = new List<VistaCodigoGrid>();
                var alfanumericosPuros = new List<VistaCodigoGrid>();

                foreach (var c in grupoProducto.Where(x => !string.IsNullOrWhiteSpace(x.CodigoUnique)))
                {
                    int posGuion = c.CodigoUnique.LastIndexOf('-');
                    if (posGuion >= 0 && int.TryParse(c.CodigoUnique.Substring(posGuion + 1), out _))
                    {
                        secuenciales.Add(c);
                    }
                    else
                    {
                        alfanumericosPuros.Add(c);
                    }
                }

                if (secuenciales.Any())
                {
                    var gruposBase = secuenciales.Select(c =>
                    {
                        int pos = c.CodigoUnique.LastIndexOf('-');
                        return new
                        {
                            Codigo = c,
                            Abreviatura = c.CodigoUnique.Substring(0, pos),
                            Numero = int.Parse(c.CodigoUnique.Substring(pos + 1))
                        };
                    }).GroupBy(x => x.Abreviatura);

                    foreach (var grupo in gruposBase)
                    {
                        var listaOrdered = grupo.OrderBy(x => x.Numero).ToList();

                        int inicio = listaOrdered[0].Numero;
                        int anterior = listaOrdered[0].Numero;

                        for (int i = 1; i <= listaOrdered.Count; i++)
                        {
                            bool cerrarRango = i == listaOrdered.Count || listaOrdered[i].Numero != anterior + 1;

                            if (cerrarRango)
                            {
                                resultado.Add(ConstruirRangoItem(productoId, grupo.Key, inicio, anterior, listaOrdered[i - 1].Codigo));

                                if (i < listaOrdered.Count)
                                {
                                    inicio = listaOrdered[i].Numero;
                                    anterior = listaOrdered[i].Numero;
                                }
                            }
                            else
                            {
                                anterior = listaOrdered[i].Numero;
                            }
                        }
                    }
                }

                foreach (var alfa in alfanumericosPuros)
                {
                    int categoriaDeducida = (alfa.ColeccionTipo != null && alfa.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")) ? 1 : 2;
                    string tipoTexto = (categoriaDeducida == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionFinal = string.IsNullOrEmpty(alfa.ColeccionTipo) ? $"C26 / {tipoTexto}" : alfa.ColeccionTipo;

                    resultado.Add(new RangoCodigoItem
                    {
                        productoId = productoId,
                        AbreviaturaBase = alfa.CodigoUnique,
                        DesdeNum = -1,
                        HastaNum = -1,
                        Cantidad = "1",
                        Desde = alfa.CodigoUnique,
                        Hasta = alfa.CodigoUnique,
                        ColeccionTipo = coleccionFinal,
                        CategoriaProductoId = categoriaDeducida
                    });
                }
            }
            return resultado;
        }

        private RangoCodigoItem ConstruirRangoItem(int productoId, string prefijo, int inicio, int fin, VistaCodigoGrid itemOriginal)
        {
            int cant = (fin - inicio + 1);
            int categoriaDeducida = 2;

            if ((itemOriginal.CodigoUnique != null && itemOriginal.CodigoUnique.ToUpperInvariant().Contains("-G-")) ||
                (itemOriginal.ColeccionTipo != null && itemOriginal.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")))
            {
                categoriaDeducida = 1;
            }

            string tipoTexto = (categoriaDeducida == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
            string coleccionFinal = string.IsNullOrEmpty(itemOriginal.ColeccionTipo) ? $"C26 / {tipoTexto}" : itemOriginal.ColeccionTipo;

            return new RangoCodigoItem
            {
                productoId = productoId,
                AbreviaturaBase = prefijo,
                DesdeNum = inicio,
                HastaNum = fin,
                Cantidad = cant.ToString(),
                Desde = $"{prefijo}-{inicio:D7}",
                Hasta = $"{prefijo}-{fin:D7}",
                ColeccionTipo = coleccionFinal,
                CategoriaProductoId = categoriaDeducida
            };
        }

        public List<VistaCodigoGrid> ReconstruirCodigosDesdeRangos(IEnumerable<RangoCodigoItem> rangos)
        {
            var lista = new List<VistaCodigoGrid>();
            if (rangos == null) return lista;

            foreach (var rango in rangos)
            {
                if (rango.DesdeNum == -1)
                {
                    lista.Add(new VistaCodigoGrid
                    {
                        ProductoId = rango.productoId,
                        CodigoUnique = rango.Desde,
                        ColeccionTipo = rango.ColeccionTipo,
                        MovCodigo = new MovimientoCodigo { MovimientoDetalleId = rango.MovimientoDetalleId }
                    });
                }
                else
                {
                    // 🚀 Optimización masiva con bucle directo sin overhead de strings innecesarios
                    int desde = rango.DesdeNum;
                    int hasta = rango.HastaNum;
                    string abrev = rango.AbreviaturaBase;
                    string colTipo = rango.ColeccionTipo;
                    int prodId = rango.productoId;
                    int detId = rango.MovimientoDetalleId;

                    for (int i = desde; i <= hasta; i++)
                    {
                        lista.Add(new VistaCodigoGrid
                        {
                            ProductoId = prodId,
                            CodigoUnique = $"{abrev}-{i:D7}",
                            ColeccionTipo = colTipo,
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = detId }
                        });
                    }
                }
            }
            return lista;
        }

        public void SincronizarCantidadesConCodigos(List<VistaProductoGrid> productos, List<VistaCodigoGrid> codigos)
        {
            foreach (var producto in productos)
            {
                int cantidadCodigos = codigos.Count(x => x.ProductoId == producto.ProductoId);
                producto.Detalle ??= new MovimientoDetalle { ProductoId = producto.ProductoId };

                if (cantidadCodigos > 0)
                {
                    producto.Cantidad = cantidadCodigos;
                    producto.Detalle.CantidadIngreso = cantidadCodigos;
                }
                else
                {
                    producto.Detalle.CantidadIngreso = producto.Cantidad;
                }
            }
        }

        public void AgregarRangos(List<RangoCodigoItem> rangosGlobales, List<VistaCodigoGrid> codigos, int productoId, IEnumerable<RangoCodigoItem> rangos)
        {
            foreach (var rango in rangos)
            {
                rango.productoId = productoId;
                rangosGlobales.Add(rango);

                for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                {
                    codigos.Add(new VistaCodigoGrid
                    {
                        ProductoId = productoId,
                        CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                        ColeccionTipo = rango.ColeccionTipo
                    });
                }
            }
        }

        public void AgregarCodigosIndividuales(List<VistaCodigoGrid> listaDestino, int productoId, IEnumerable<VistaCodigoGrid> codigos)
        {
            foreach (var codigo in codigos)
            {
                if (listaDestino.Any(x => x.CodigoUnique.Equals(codigo.CodigoUnique, StringComparison.OrdinalIgnoreCase)))
                    continue;

                codigo.ProductoId = productoId;
                listaDestino.Add(codigo);
            }
        }

        public List<VistaProductoGrid> MergeDuplicateProducts(List<VistaProductoGrid> productos)
        {
            return productos
                .GroupBy(x => x.ProductoId)
                .Select(g => new VistaProductoGrid
                {
                    ProductoId = g.Key,
                    CodigoProducto = g.First().CodigoProducto,
                    Descripcion = g.First().Descripcion,
                    UnidadMedida = g.First().UnidadMedida,

                    Detalle = new MovimientoDetalle
                    {
                        Id = g.Select(x => x.Detalle?.Id ?? 0).FirstOrDefault(id => id > 0),
                        ProductoId = g.Key,
                        CantidadIngreso = g.Sum(x => x.Detalle?.CantidadIngreso ?? 0),
                        CostoUnitario = g.First().Detalle?.CostoUnitario ?? 0
                    }
                })
                .ToList();
        }

        public void ReemplazarRangosProducto(List<RangoCodigoItem> lista, int productoId, IEnumerable<RangoCodigoItem> nuevos)
        {
            lista.RemoveAll(x => x.productoId == productoId);
            foreach (var rango in nuevos)
            {
                rango.productoId = productoId;
                lista.Add(rango);
            }
        }

        public void ActualizarCantidadProducto(VistaProductoGrid producto, int cantidad)
        {
            producto.Detalle ??= new MovimientoDetalle { ProductoId = producto.ProductoId };
            producto.Detalle.CantidadIngreso = cantidad;
            producto.Cantidad = cantidad;
        }

        public List<VistaCodigoGrid> ObtenerCodigosProducto(List<VistaCodigoGrid> codigos, int productoId)
        {
            return codigos.Where(x => x.ProductoId == productoId).ToList();
        }

        public List<RangoCodigoItem> ObtenerRangosProducto(List<RangoCodigoItem> rangos, int productoId)
        {
            return rangos.Where(x => x.productoId == productoId).ToList();
        }

        public async Task<bool> TieneMovimientosPosterioresAsync(int codigoId, int movimientoActualId, DateTime fechaEdicion, DbConnection conn, DbTransaction trans)
        {
            // 🌟 Definición limpia de nolock adaptada al motor
            string nolock = QueryAdapter.EsMySQL ? string.Empty : "WITH (NOLOCK)";

            string query = @"
        SELECT COUNT(*) 
        FROM movimiento_codigos mc " + nolock + @"
        INNER JOIN movimientos m " + nolock + @" ON mc.movimiento_id = m.id
        WHERE mc.codigo_creado_id = @codId 
          AND m.id != @movId 
          AND m.estado_id = 1
          AND (
              m.fecha_movimiento > @fechaEdicion 
              OR (m.fecha_movimiento = @fechaEdicion AND m.id > @movId)
          )";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            AgregarParametro(cmd, "@codId", codigoId);
            AgregarParametro(cmd, "@movId", movimientoActualId);
            AgregarParametro(cmd, "@fechaEdicion", fechaEdicion);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && Convert.ToInt32(result) > 0;
        }


        public async Task<(int EstadoId, int AlmacenId)> ObtenerEstadoYAlmacenAnteriorAsync(int codigoId, int movimientoActualId, DbConnection conn, DbTransaction trans)
        {
            string query;

            if (QueryAdapter.EsMySQL)
            {
                query = @"
            SELECT m.motivo_producto_id, 
                   mp.tipo_movimiento_id,
                   COALESCE(m.almacen_destino_id, m.almacen_id, 1) AS alm_destino,
                   COALESCE(m.almacen_origen_id, m.almacen_id, 1) AS alm_origen
            FROM movimiento_codigos mc
            INNER JOIN movimientos m ON mc.movimiento_id = m.id
            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
            WHERE mc.codigo_creado_id = @codId 
              AND m.id < @movId
              AND m.estado_id = 1
            ORDER BY m.fecha_movimiento DESC, m.id DESC
            LIMIT 1;";
            }
            else
            {
                query = @"
            SELECT TOP 1 m.motivo_producto_id, 
                         mp.tipo_movimiento_id,
                         ISNULL(m.almacen_destino_id, ISNULL(m.almacen_id, 1)) AS alm_destino,
                         ISNULL(m.almacen_origen_id, ISNULL(m.almacen_id, 1)) AS alm_origen
            FROM movimiento_codigos mc WITH (NOLOCK)
            INNER JOIN movimientos m WITH (NOLOCK) ON mc.movimiento_id = m.id
            INNER JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            WHERE mc.codigo_creado_id = @codId 
              AND m.id < @movId
              AND m.estado_id = 1
            ORDER BY m.fecha_movimiento DESC, m.id DESC;";
            }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@codId", codigoId);
                AgregarParametro(cmd, "@movId", movimientoActualId);

                using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    int motivoId = rdr.GetInt32(0);
                    int tipoMovimiento = rdr.GetInt32(1); // 1 = Entrada, 2 = Salida
                    int almDestino = rdr.GetInt32(2);
                    int almOrigen = rdr.GetInt32(3);

                    // 🌟 MATRIZ DE MOTIVOS EXACTA:

                    // Si el movimiento anterior fue Motivo 10 (Salida por Transferencia) -> Vuelve a Estado 5 (En Tránsito)
                    if (motivoId == 10)
                    {
                        return (5, almDestino);
                    }

                    // Si fue una ENTRADA (Tipo 1) o Motivo 4 (Entrada por Transferencia) -> Vuelve a Estado 3 (Disponible)
                    if (tipoMovimiento == 1 || motivoId == 4)
                    {
                        return (3, almDestino);
                    }

                    // Si fue otra Salida Comercial (Venta, Promotoría, etc.) -> Vuelve a Estado 4 (Fuera de Almacén)
                    return (4, almOrigen);
                }

                // Sin historial previo registrado -> Estado inicial de creación
                return (1, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar historial del código {codigoId}: {ex.Message}");
                return (3, 1);
            }
        }
    }
}