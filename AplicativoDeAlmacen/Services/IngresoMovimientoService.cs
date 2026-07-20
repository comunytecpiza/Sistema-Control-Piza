using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Collections.ObjectModel;

namespace AplicativoDeAlmacen.Services
{
    public class IngresoMovimientoService
    {
        private readonly DatabaseConnection _database;

        // Resultado que devuelve la entidad Movimiento (EF) y las colecciones relacionadas
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

        public async Task<MovimientoCompletoResult?> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var result = new MovimientoCompletoResult();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(numero)) return null;

            if (int.TryParse(numero, out int numVal)) numero = numVal.ToString("D7");

            string query = @"
            SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
                   m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion, m.estado_id
            FROM movimientos m
            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id -- 🌟 ESTA LÍNEA ES OBLIGATORIA
            WHERE m.serie_documento = @serie 
            AND m.numero_documento = @numero
            AND mp.tipo_movimiento = 'entrada'";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                result.Movimiento = new Movimiento
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FechaMovimiento = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = reader["serie_documento"].ToString(),
                    NumeroDocumento = reader["numero_documento"].ToString(),
                    MotivoProductoId = reader.IsDBNull(reader.GetOrdinal("motivo_producto_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = reader.IsDBNull(reader.GetOrdinal("persona_comercial_id")) ? null : reader.GetInt32(reader.GetOrdinal("persona_comercial_id")),
                    SerieGuia = reader.IsDBNull(reader.GetOrdinal("serie_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("serie_guia")),
                    NumeroGuia = reader.IsDBNull(reader.GetOrdinal("numero_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("numero_guia")),
                    Observacion = reader.IsDBNull(reader.GetOrdinal("observacion")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacion")),
                    UbicacionId = reader.IsDBNull(reader.GetOrdinal("ubicacion_id")) ? null : reader.GetInt32(reader.GetOrdinal("ubicacion_id")),
                    EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32(reader.GetOrdinal("estado_id"))
                };
            }

            // Detalles
            string qDet = @"SELECT id, producto_id, cantidad_ingreso, costo_unitario FROM movimiento_detalles WHERE movimiento_id = @movId";
            using (var cmdDet = dbConn.CreateCommand())
            {
                cmdDet.CommandText = QueryAdapter.FormatearConsulta(qDet);
                var pdet = cmdDet.CreateParameter(); pdet.ParameterName = "@movId"; pdet.Value = result.Movimiento.Id; cmdDet.Parameters.Add(pdet);
                using var rdrDet = await cmdDet.ExecuteReaderAsync();
                while (await rdrDet.ReadAsync())
                {
                    result.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rdrDet.GetInt32(0),
                        ProductoId = rdrDet.GetInt32(1),
                        CantidadIngreso = rdrDet.GetDecimal(2),
                        CostoUnitario = rdrDet.IsDBNull(3) ? (decimal?)null : rdrDet.GetDecimal(3)
                    });
                }
            }

            // Rangos asociados
            string qRangos = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
            using (var cmdR = dbConn.CreateCommand())
            {
                cmdR.Transaction = cmdR.Transaction;
                cmdR.CommandText = QueryAdapter.FormatearConsulta(qRangos);
                var pr = cmdR.CreateParameter(); pr.ParameterName = "@movId"; pr.Value = result.Movimiento.Id; cmdR.Parameters.Add(pr);
                using var rdrR = await cmdR.ExecuteReaderAsync();
                while (await rdrR.ReadAsync())
                {
                    int desdeNum = rdrR.GetInt32(rdrR.GetOrdinal("desde_num"));
                    int hastaNum = rdrR.GetInt32(rdrR.GetOrdinal("hasta_num"));
                    int categoriaId = rdrR.GetInt32(rdrR.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdrR.IsDBNull(rdrR.GetOrdinal("abreviatura_base")) ? string.Empty : rdrR.GetString(rdrR.GetOrdinal("abreviatura_base"));

                    string desdeText = desdeNum == -1 ? baseAbrev : $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = hastaNum == -1 ? baseAbrev : $"{baseAbrev}-{hastaNum:D7}";

                    string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : (categoriaId == 2 ? "LIBRO VENTA" : "OTROS");
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

        private async Task ActualizarStockProductoPorKardexAsync(int productoId, DbConnection conn, DbTransaction trans)
        {
            // Esta consulta es un "recalculador" atómico: suma las entradas y resta las salidas activas
            string queryUpdate = @"
        UPDATE productos 
        SET cantidad = (
            SELECT COALESCE(SUM(md.cantidad_ingreso - md.cantidad_salida), 0)
            FROM movimiento_detalles md
            INNER JOIN movimientos m ON md.movimiento_id = m.id
            WHERE md.producto_id = @ProdId AND m.estado_id != 5
        )
        WHERE id = @ProdId";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryUpdate);

            var p = cmd.CreateParameter(); p.ParameterName = "@ProdId"; p.Value = productoId; cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync();
        }
        // Añade esto a IngresoMovimientoService.cs
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

                string query = @"SELECT id, descripcion, tipo_movimiento 
                                 FROM motivo_productos 
                                 WHERE tipo_movimiento = 'entrada' 
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
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                TipoMovimiento = reader.IsDBNull(reader.GetOrdinal("tipo_movimiento"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("tipo_movimiento"))
                            };
                            lista.Add(motivo);
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<Movimiento> GenerarSiguienteCorrelativoAsync(string seriePorDefecto)
        {
            var resultado = new Movimiento
            {
                SerieDocumento = seriePorDefecto,
                NumeroDocumento = "0000001"
            };

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 1. Buscamos cuál es la ÚLTIMA serie que se ha estado usando en el sistema
                string queryUltimaSerie = @"
                SELECT TOP 1 serie_documento 
                FROM movimientos m
                INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                WHERE mp.tipo_movimiento = 'entrada'
                ORDER BY m.id DESC";

                string serieActual = seriePorDefecto;
                using (var cmdSerie = dbConn.CreateCommand())
                {
                    cmdSerie.CommandText = QueryAdapter.FormatearConsulta(queryUltimaSerie);
                    var resSerie = await cmdSerie.ExecuteScalarAsync();
                    if (resSerie != null && resSerie != DBNull.Value)
                    {
                        serieActual = resSerie.ToString();
                    }
                }

                // 2. Obtenemos el número máximo registrado para esa serie específica
                string queryMaxNum = @"
                    SELECT COALESCE(MAX(CAST(m.numero_documento AS INT)), 0)
                    FROM movimientos m
                    INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                    WHERE m.serie_documento = @serie 
                    AND mp.tipo_movimiento = 'salida'"; // 🌟 LA CLAVE: Filtrar solo salidas

                int ultimoNumero = 0;
                using (var cmdNum = dbConn.CreateCommand())
                {
                    cmdNum.CommandText = QueryAdapter.FormatearConsulta(queryMaxNum);
                    AgregarParametro(cmdNum, "@serie", serieActual);
                    object? resultObj = await cmdNum.ExecuteScalarAsync();
                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        ultimoNumero = Convert.ToInt32(resultObj);
                    }
                }

                // 3. 🌟 REGLA DE ORO: EVALUACIÓN DE DESBORDE DE SERIE (9,999,999)
                if (ultimoNumero >= 9999999)
                {
                    // Si el número llegó al límite, intentamos parsear la serie (Ej: "0001" -> 1)
                    if (int.TryParse(serieActual, out int numeroSerieVal))
                    {
                        int siguienteSerieInt = numeroSerieVal + 1;
                        resultado.SerieDocumento = siguienteSerieInt.ToString("D4"); // Pasa a "0002"
                        resultado.NumeroDocumento = "0000001"; // Reinicia el conteo
                    }
                    else
                    {
                        // Fallback por si la serie tiene letras por algún motivo extraño
                        resultado.SerieDocumento = serieActual;
                        resultado.NumeroDocumento = "0000001";
                    }
                }
                else
                {
                    // Flujo normal: Mantiene la serie actual e incrementa el número en 1
                    resultado.SerieDocumento = serieActual;
                    resultado.NumeroDocumento = (ultimoNumero + 1).ToString("D7");
                }
            }
            return resultado;
        }

        private async Task<int> GuardarCabeceraAsync(Movimiento cabecera, int ubicacionId, int? existingId, DbConnection conn, DbTransaction trans)
        {
            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
            
            if (existingId.HasValue)
            {
                string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                AgregarParametro(cmd, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today);
                AgregarParametro(cmd, "@motivoId", cabecera.MotivoProductoId);
                AgregarParametro(cmd, "@ubicacionId", ubicacionId);
                AgregarParametro(cmd, "@personaId", cabecera.PersonaComercialId);
                AgregarParametro(cmd, "@observacion", cabecera.Observacion);
                AgregarParametro(cmd, "@serieGuia", cabecera.SerieGuia);
                AgregarParametro(cmd, "@numeroGuia", cabecera.NumeroGuia);
                AgregarParametro(cmd, "@id", existingId.Value);
                await cmd.ExecuteNonQueryAsync();
                return existingId.Value;
            }


            string queryLock = "SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 FROM movimientos WITH (TABLOCKX, HOLDLOCK) WHERE serie_documento = @serie";
            int nuevoNumero;

            using (var cmdLock = conn.CreateCommand())
            {
                cmdLock.Transaction = trans;
                cmdLock.CommandText = QueryAdapter.FormatearConsulta(queryLock);
                AgregarParametro(cmdLock, "@serie", cabecera.SerieDocumento);
                nuevoNumero = Convert.ToInt32(await cmdLock.ExecuteScalarAsync());
            }

            cabecera.NumeroDocumento = nuevoNumero.ToString("D7");

            cabecera.NumeroDocumento = nuevoNumero.ToString("D7");
            string qCab = $@"INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia) 
                     VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, 1, @personaId, @observacion, 1, @serieGuia, @numeroGuia); {selectId}";

            using var cmdCab = conn.CreateCommand();
            cmdCab.Transaction = trans;
            cmdCab.CommandText = QueryAdapter.FormatearConsulta(qCab);

            AgregarParametro(cmdCab, "@fecha", cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today);
            AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
            // Asignamos el número calculado de forma segura
            AgregarParametro(cmdCab, "@numero", nuevoNumero.ToString("D7"));
            AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
            AgregarParametro(cmdCab, "@ubicacionId", ubicacionId);
            AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
            AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
            AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
            AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);

            try
            {
                return Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
            }
            catch (DbException ex) when (ex.Message.Contains("PRIMARY KEY") || ex.Message.Contains("UNIQUE") || ex.ErrorCode == 2627)
            {
                // 🚨 Si otra transacción ganó el número en el mismo instante, lanzamos un aviso controlado para que el usuario reintente
                throw new Exception("Conflicto de Concurrencia: Otro usuario registró un movimiento simultáneamente con el mismo correlativo. Por favor, intente guardar el documento nuevamente.");
            }

            
        }

        private async Task<int> UpsertMovimientoDetalleAsync(int movId, VistaProductoGrid item, DbConnection conn, DbTransaction trans)
        {
            string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

            // Buscar si existe
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
                    cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_ingreso = @cant, costo_unitario = @costo WHERE id = @detId");
                    AgregarParametro(cmdUpd, "@cant", item.Detalle.CantidadIngreso);
                    AgregarParametro(cmdUpd, "@costo", item.Detalle.CostoUnitario);
                    AgregarParametro(cmdUpd, "@detId", detId);
                    await cmdUpd.ExecuteNonQueryAsync();
                    return detId;
                }
            }

            // Insertar nuevo
            using var cmdIns = conn.CreateCommand();
            cmdIns.Transaction = trans;
            cmdIns.CommandText = QueryAdapter.FormatearConsulta($"INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, costo_unitario) VALUES (@movId, @prodId, @cant, @costo); {selectId}");
            AgregarParametro(cmdIns, "@movId", movId);
            AgregarParametro(cmdIns, "@prodId", item.ProductoId);
            AgregarParametro(cmdIns, "@cant", item.Detalle.CantidadIngreso);
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

        private async Task InsertarMovimientoCodigoAsync(int movId, int detId, int codId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso) VALUES (@movId, @detId, @codId, 1)");
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@detId", detId);
            AgregarParametro(cmd, "@codId", codId);
            await cmd.ExecuteNonQueryAsync();
        }
        // Agrega este método vacío en tu clase IngresoMovimientoService
        public async Task<bool> AnularMovimientoCompletoAsync(int movimientoId, IProgress<int>? progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                DateTime fechaMovimiento;
                using (var cmdMov = dbConn.CreateCommand())
                {
                    cmdMov.Transaction = transaccion;
                    cmdMov.CommandText = QueryAdapter.FormatearConsulta("SELECT fecha_movimiento, estado_id FROM movimientos WHERE id = @movId");
                    AgregarParametro(cmdMov, "@movId", movimientoId);
                    using var rdrMov = await cmdMov.ExecuteReaderAsync();
                    if (!await rdrMov.ReadAsync()) throw new Exception("El movimiento no existe.");
                    if (rdrMov.GetInt32(1) == 5) throw new Exception("Este movimiento ya está anulado.");
                    fechaMovimiento = rdrMov.IsDBNull(0) ? DateTime.Today : rdrMov.GetDateTime(0);
                }

                // Generar tabla de control
                using (var cmdCreate = dbConn.CreateCommand())
                {
                    cmdCreate.Transaction = transaccion;
                    cmdCreate.CommandText = QueryAdapter.FormatearConsulta("CREATE TABLE #temp_codigos_anular (codigo_creado_id INT NOT NULL PRIMARY KEY);");
                    await cmdCreate.ExecuteNonQueryAsync();
                }

                using (var cmdPopulate = dbConn.CreateCommand())
                {
                    cmdPopulate.Transaction = transaccion;
                    cmdPopulate.CommandText = QueryAdapter.FormatearConsulta("INSERT INTO #temp_codigos_anular (codigo_creado_id) SELECT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId;");
                    AgregarParametro(cmdPopulate, "@movId", movimientoId);
                    await cmdPopulate.ExecuteNonQueryAsync();
                }

                // 🛑 CANDADO REGLA DE ORO 1: Si se dio entrada a los códigos, NO se puede anular si ya salieron (EstadoId != 3)
                string sqlValidarEstados = @"
                    SELECT COUNT(*) 
                    FROM codigos_creados cc
                    INNER JOIN #temp_codigos_anular tmp ON tmp.codigo_creado_id = cc.id
                    WHERE cc.estado_id != 3"; // 3 = En Almacén. Si cambió a 4 (Salida) se congela la operación.

                using (var cmdCheckStock = dbConn.CreateCommand())
                {
                    cmdCheckStock.Transaction = transaccion;
                    cmdCheckStock.CommandText = QueryAdapter.FormatearConsulta(sqlValidarEstados);
                    int enMovimiento = Convert.ToInt32(await cmdCheckStock.ExecuteScalarAsync());
                    if (enMovimiento > 0)
                    {
                        throw new Exception($"Operación Denegada: Hay {enMovimiento} códigos de este ingreso que ya registran Despachos o Salidas activas en Almacén.");
                    }
                }

                // 🛑 CANDADO REGLA DE ORO 2: Línea de tiempo
                using (var cmdCheck = dbConn.CreateCommand())
                {
                    cmdCheck.Transaction = transaccion;
                    cmdCheck.CommandText = QueryAdapter.FormatearConsulta(@"
                        SELECT COUNT(*) FROM movimiento_codigos mc
                        INNER JOIN #temp_codigos_anular tmp ON tmp.codigo_creado_id = mc.codigo_creado_id
                        INNER JOIN movimientos m ON m.id = mc.movimiento_id
                        WHERE m.fecha_movimiento > @fechaMov OR (m.fecha_movimiento = @fechaMov AND m.id > @movId)");

                    AgregarParametro(cmdCheck, "@fechaMov", fechaMovimiento);
                    AgregarParametro(cmdCheck, "@movId", movimientoId);

                    int posteriores = Convert.ToInt32(await cmdCheck.ExecuteScalarAsync());
                    if (posteriores > 0) throw new Exception($"Rechazado: {posteriores} códigos tienen transacciones logísticas posteriores.");
                }

                progress?.Report(40);

                // Revertimos el estado de los códigos a 1 (Disponible / Registrado) ya que borramos su ingreso físico
                string sqlRevertir = @"
                    UPDATE cc SET cc.estado_id = 1
                    FROM codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf))
                    INNER JOIN #temp_codigos_anular tmp ON tmp.codigo_creado_id = cc.id";

                using (var cmdRevert = dbConn.CreateCommand())
                {
                    cmdRevert.Transaction = transaccion;
                    cmdRevert.CommandText = QueryAdapter.FormatearConsulta(sqlRevertir);
                    await cmdRevert.ExecuteNonQueryAsync();
                }

                // Marcar movimiento como Anulado
                using (var cmdStatus = dbConn.CreateCommand())
                {
                    cmdStatus.Transaction = transaccion;
                    cmdStatus.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimientos SET estado_id = 5 WHERE id = @movId");
                    AgregarParametro(cmdStatus, "@movId", movimientoId);
                    await cmdStatus.ExecuteNonQueryAsync();
                }

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
                        await ActualizarStockProductoPorKardexAsync(pid, dbConn, transaccion);
                    }
                }
                progress?.Report(100);
                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception(ex.Message);
            }
        }

        public async Task<bool> RegistrarMovimientoCompletoAsync(Movimiento cabecera, List<VistaProductoGrid> productos, List<RangoCodigoItem> rangos, int ubicacionId, int? existingMovimientoId = null, IProgress<int> progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                // 1. GUARDAR O ACTUALIZAR LA CABECERA DEL MOVIMIENTO
                int movimientoId = await GuardarCabeceraAsync(cabecera, ubicacionId, existingMovimientoId, dbConn, transaccion);

                // 2. CAPTURAR HISTORIAL EN CASO DE EDICIÓN
                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { existingMovimientoId.Value })
                    : new HashSet<int>();

                var rangosPorProducto = rangos.GroupBy(r => r.productoId).ToDictionary(g => g.Key, g => g.ToList());

                int totalCodigos = rangos.Sum(r => r.DesdeNum == -1 ? 1 : (r.HastaNum - r.DesdeNum + 1));
                if (totalCodigos == 0) totalCodigos = 1;

                int codigosProcesadosGlobal = 0;
                int ultimoPorcentajeReportado = -1;
                var nuevosCodigosIds = new HashSet<int>();
                var todosLosCodigosAValidar = new List<int>();

                // 🌟 [PASO DE BLINDAJE INDUSTRIAL 1]: Recopilar todos los IDs de códigos que se pretenden procesar
                foreach (var item in productos)
                {
                    if (!rangosPorProducto.TryGetValue(item.ProductoId, out var rangosProd)) continue;

                    foreach (var r in rangosProd)
                    {
                        var encontrados = await ObtenerIdsCodigosPorRangoAsync(r.productoId, r.AbreviaturaBase, r.CategoriaProductoId, r.DesdeNum, r.HastaNum, dbConn, transaccion);
                        foreach (var t in encontrados)
                        {
                            // Si estamos editando el mismo documento, ignoramos sus propios códigos previos
                            if (!codigosPreviosEnBD.Contains(t.CodigoObj.Id))
                            {
                                todosLosCodigosAValidar.Add(t.CodigoObj.Id);
                            }
                        }
                    }
                }

                // 🌟 [PASO DE BLINDAJE INDUSTRIAL 2]: Validación atómica masiva en bloques de 1,000 items
                if (todosLosCodigosAValidar.Any())
                {
                    using (var cmdCreateCheck = dbConn.CreateCommand())
                    {
                        cmdCreateCheck.Transaction = transaccion;
                        cmdCreateCheck.CommandText = "CREATE TABLE #temp_nuevos_ingresos_check (id INT NOT NULL PRIMARY KEY);";
                        await cmdCreateCheck.ExecuteNonQueryAsync();
                    }

                    try
                    {
                        // 🚀 SOLUCIÓN INDUSTRIAL: Aplicamos .Distinct() para que no colapse la PK de la tabla temporal
                        var codigosUnicosAValidar = todosLosCodigosAValidar.Distinct().ToList();

                        const int insertBatchSize = 1000;
                        for (int i = 0; i < codigosUnicosAValidar.Count; i += insertBatchSize)
                        {
                            var batchCheck = codigosUnicosAValidar.Skip(i).Take(insertBatchSize).ToList();
                            var sbCheck = new System.Text.StringBuilder("INSERT INTO #temp_nuevos_ingresos_check (id) VALUES ");
                            using var cmdInsCheck = dbConn.CreateCommand();
                            cmdInsCheck.Transaction = transaccion;

                            for (int j = 0; j < batchCheck.Count; j++)
                            {
                                sbCheck.Append($"(@chk{j})");
                                if (j < batchCheck.Count - 1) sbCheck.Append(",");
                                AgregarParametro(cmdInsCheck, "@chk" + j, batchCheck[j]);
                            }

                            cmdInsCheck.CommandText = sbCheck.ToString();
                            await cmdInsCheck.ExecuteNonQueryAsync();
                        }

                        // Cruzamos la tabla temporal contra el índice maestro buscando duplicados activos
                        string sqlVerificarDuplicados = @"
                SELECT cc.codigo, cc.estado_id 
                FROM codigos_creados cc
                INNER JOIN #temp_nuevos_ingresos_check tmp ON tmp.id = cc.id
                LEFT JOIN movimiento_codigos mc ON cc.id = mc.codigo_creado_id AND mc.movimiento_id = @currentMovId
                WHERE cc.estado_id IN (3, 4)
                AND mc.codigo_creado_id IS NULL";

                        using var cmdVerify = dbConn.CreateCommand();
                        cmdVerify.Transaction = transaccion;
                        cmdVerify.CommandText = QueryAdapter.FormatearConsulta(sqlVerificarDuplicados);
                        AgregarParametro(cmdVerify, "@currentMovId", existingMovimientoId ?? -1);

                        var listaConflictos = new List<string>();
                        using (var rdrVerify = await cmdVerify.ExecuteReaderAsync())
                        {
                            while (await rdrVerify.ReadAsync())
                            {
                                string codConflicto = rdrVerify.GetString(0);
                                int estConflicto = rdrVerify.GetInt32(1);
                                string nombreEstado = estConflicto == 3 ? "EN ALMACÉN" : "DESPACHADO/SALIDA";
                                listaConflictos.Add($"- {codConflicto} ({nombreEstado})");
                            }
                        }

                        if (listaConflictos.Any())
                        {
                            throw new Exception("Operación Cancelada por Seguridad de Stock.\n\n" +
                                                "Se detectaron códigos que ya cuentan con un ingreso activo en el sistema:\n" +
                                                string.Join("\n", listaConflictos) + "\n\n" +
                                                "Por favor, revise el detalle de ítems antes de reintentar.");
                        }
                    }
                    finally
                    {
                        using var cmdDropCheck = dbConn.CreateCommand();
                        cmdDropCheck.Transaction = transaccion;
                        cmdDropCheck.CommandText = "DROP TABLE IF EXISTS #temp_nuevos_ingresos_check;";
                        await cmdDropCheck.ExecuteNonQueryAsync();
                    }
                }

                // =========================================================================
                // FLUJO DE PERSISTENCIA ORIGINAL (Mantiene tu excelente rendimiento intacto)
                // =========================================================================
                foreach (var item in productos)
                {
                    int detalleId = await UpsertMovimientoDetalleAsync(movimientoId, item, dbConn, transaccion);

                    if (existingMovimientoId.HasValue)
                    {
                        string sqlLimpiarCodigos = "DELETE FROM movimiento_codigos WHERE movimiento_detalle_id = @detId";
                        using (var cmdDel = dbConn.CreateCommand())
                        {
                            cmdDel.Transaction = transaccion;
                            cmdDel.CommandText = QueryAdapter.FormatearConsulta(sqlLimpiarCodigos);
                            AgregarParametro(cmdDel, "@detId", detalleId);
                            await cmdDel.ExecuteNonQueryAsync();
                        }
                    }

                    if (!rangosPorProducto.TryGetValue(item.ProductoId, out var rangosProd))
                        continue;

                    var codigosAInsertar = new List<int>(totalCodigos);

                    foreach (var r in rangosProd)
                    {
                        await InsertarRangoAsync(r, detalleId, dbConn, transaccion);

                        var encontrados = await ObtenerIdsCodigosPorRangoAsync(r.productoId, r.AbreviaturaBase, r.CategoriaProductoId, r.DesdeNum, r.HastaNum, dbConn, transaccion);

                        foreach (var t in encontrados)
                        {
                            codigosAInsertar.Add(t.CodigoObj.Id);
                            nuevosCodigosIds.Add(t.CodigoObj.Id);
                        }

                        codigosProcesadosGlobal += encontrados.Count;

                        int nuevoPorcentaje = (codigosProcesadosGlobal * 100) / totalCodigos;
                        if (nuevoPorcentaje > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = nuevoPorcentaje;
                            progress?.Report(nuevoPorcentaje);
                        }
                    }

                    const int bulkSize = 1000;
                    for (int i = 0; i < codigosAInsertar.Count; i += bulkSize)
                    {
                        var batch = codigosAInsertar.Skip(i).Take(bulkSize).ToList();
                        await InsertarMovimientoCodigosMasivoAsync(movimientoId, detalleId, batch, dbConn, transaccion);
                        await ActualizarEstadoCodigosMasivoAsync(batch, 3, dbConn, transaccion); // 3 = En Almacén
                    }
                }

                // PROCESAR REMOCIONES CRONOLÓGICAS (Solo si es Edición)
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();
                if (codigosAEliminar.Any())
                {
                    foreach (var codId in codigosAEliminar)
                    {
                        bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);
                        if (tieneFuturo)
                            throw new Exception($"Operación rechazada: El código ID {codId} cuenta con transacciones logísticas posteriores en kárdex.");

                        int estadoAnterior = await ObtenerEstadoAnteriorAsync(codId, movimientoId, dbConn, transaccion);
                        await ActualizarEstadoCodigo(codId, estadoAnterior, dbConn, transaccion);
                    }

                    const int deleteBatchSize = 1000;
                    for (int i = 0; i < codigosAEliminar.Count; i += deleteBatchSize)
                    {
                        var batchDel = codigosAEliminar.Skip(i).Take(deleteBatchSize).ToList();
                        string queryDel = $"DELETE FROM movimiento_codigos WHERE movimiento_id = @movId AND codigo_creado_id IN ({string.Join(",", batchDel)})";
                        using var cmdDel = dbConn.CreateCommand();
                        cmdDel.Transaction = transaccion;
                        cmdDel.CommandText = queryDel;
                        var p = cmdDel.CreateParameter(); p.ParameterName = "@movId"; p.Value = movimientoId; cmdDel.Parameters.Add(p);
                        await cmdDel.ExecuteNonQueryAsync();
                    }
                }

                var productosUnicos = productos.Select(p => p.ProductoId).Distinct();
                foreach (var pid in productosUnicos)
                {
                    await ActualizarStockProductoPorKardexAsync(pid, dbConn, transaccion);
                }
                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception($"Falla en la persistencia del Kárdex: {ex.Message}");
            }
        }

        // 🌟 ESTE MÉTODO INSERTA VARIOS CÓDIGOS DE UNA SOLA VEZ (BATCH)
        private async Task InsertarMovimientoCodigosMasivoAsync(int movId, int detId, List<int> codigosIds, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var sb = new System.Text.StringBuilder();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            sb.Append("INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES ");

            for (int i = 0; i < codigosIds.Count; i++)
            {
                sb.Append($"(@movId, @detId, @c{i}, 1, 0, GETDATE())");
                if (i < codigosIds.Count - 1) sb.Append(",");

                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta(sb.ToString());
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@detId", detId);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// ACTUALIZACIÓN EN GRUPO: Modifica el estado_id de múltiples códigos en una sola petición.
        /// Utilizado para optimizar el rendimiento (Batching) al procesar lotes de inserción.
        /// </summary>
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

            // El string final queda formateado de manera segura: WHERE id IN (@u0, @u1, @u2...)
            cmd.CommandText = QueryAdapter.FormatearConsulta(
                $"UPDATE codigos_creados SET estado_id = @estado WHERE id IN ({string.Join(",", paramNames)})");

            AgregarParametro(cmd, "@estado", nuevoEstadoId);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// ACTUALIZACIÓN INDIVIDUAL: Cambia el estado de un código específico. 
        /// Utilizado exclusivamente para revertir transacciones de elementos removidos.
        /// </summary>
        private async Task ActualizarEstadoCodigo(int codigoId, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
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
            FROM codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf))
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND rc.categoria_producto_id = @categoriaId
              AND cc.codigo = @codigoExacto";

                using var cmdPack = conn.CreateCommand();
                cmdPack.Transaction = trans;
                cmdPack.CommandText = QueryAdapter.FormatearConsulta(queryPack);

                AgregarParametro(cmdPack, "@productoId", productoId);
                AgregarParametro(cmdPack, "@categoriaId", categoriaId);
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

            // =========================================================================
            // 🚀 OPTIMIZACIÓN LOGÍSTICA PARA VOLÚMENES GRANDES (28,000+ REGISTROS)
            // =========================================================================
            // 1. Crear una tabla temporal ligera en la sesión de la transacción
            string sqlCrearTabla = "CREATE TABLE #temp_rango_busqueda (codigo_buscado VARCHAR(50) NOT NULL PRIMARY KEY);";
            using (var cmdTable = conn.CreateCommand())
            {
                cmdTable.Transaction = trans;
                cmdTable.CommandText = sqlCrearTabla;
                await cmdTable.ExecuteNonQueryAsync();
            }

            try
            {
                // 2. Insertar los 28,000 códigos en bloques limpios utilizando texto estructurado
                int totalCodigos = hasta - desde + 1;
                const int batchSize = 1000;

                for (int i = 0; i < totalCodigos; i += batchSize)
                {
                    int chunk = Math.Min(batchSize, totalCodigos - i);
                    var sbInsert = new System.Text.StringBuilder("INSERT INTO #temp_rango_busqueda (codigo_buscado) VALUES ");
                    using var cmdIns = conn.CreateCommand();
                    cmdIns.Transaction = trans;

                    for (int j = 0; j < chunk; j++)
                    {
                        int correlativo = desde + i + j;
                        string parametroNombre = $"@p{j}";
                        sbInsert.Append($"({parametroNombre})");
                        if (j < chunk - 1) sbInsert.Append(",");

                        // 🌟 CORREGIDO: Cambiado paramparametroNombre por parametroNombre
                        AgregarParametro(cmdIns, parametroNombre, $"{baseLimpia}-{correlativo:D7}");
                    }

                    cmdIns.CommandText = sbInsert.ToString();
                    await cmdIns.ExecuteNonQueryAsync();
                }

                // 3. INNER JOIN Maestro directo contra tu índice compuesto sin usar el pesado operador 'IN'
                string queryMaster = @"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id
            FROM #temp_rango_busqueda tmp
            INNER JOIN codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf)) ON cc.codigo = tmp.codigo_buscado
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id = @productoId
              AND rc.categoria_producto_id = @categoriaId";

                using var cmdQuery = conn.CreateCommand();
                cmdQuery.Transaction = trans;
                cmdQuery.CommandText = QueryAdapter.FormatearConsulta(queryMaster);
                AgregarParametro(cmdQuery, "@productoId", productoId);
                AgregarParametro(cmdQuery, "@categoriaId", categoriaId);

                using var reader = await cmdQuery.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string codigoRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    string codigoNorm = NormalizarCodigo(codigoRaw);

                    if (codigoNorm.Length >= 7)
                    {
                        string colaStr = codigoNorm.Substring(codigoNorm.Length - 7);
                        if (int.TryParse(colaStr, out int seq))
                        {
                            resultados.Add((
                                new CodigoCreado
                                {
                                    Id = reader.GetInt32(0),
                                    RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                    Codigo = codigoRaw,
                                    EsManual = !reader.IsDBNull(3) && reader.GetBoolean(3),
                                    EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                                },
                                seq));
                        }
                    }
                }
            }
            finally
            {
                // 4. Limpieza preventiva
                using var cmdDrop = conn.CreateCommand();
                cmdDrop.Transaction = trans;
                cmdDrop.CommandText = "DROP TABLE IF EXISTS #temp_rango_busqueda;";
                await cmdDrop.ExecuteNonQueryAsync();
            }

            return resultados;
        }

        // Normaliza un código: elimina espacios y comillas, y pasa a mayúsculas
        public string NormalizarCodigo(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;

            // 1. Convertimos a mayúsculas y quitamos los espacios duplicados o en los extremos
            string s = codigo.ToUpperInvariant().Trim();

            // 2. Homologamos apóstrofes convirtiéndolos en guiones normales
            s = s.Replace("'", "-");
            s = s.Replace("\u2019", "-").Replace("\u2018", "-");

            // 🌟 3. NORMALIZACIÓN SIMÉTRICA DE ESPACIOS
            // Si el Excel venía compactado (ej: "LMA4C26-V-0009498"), forzamos el espacio reglamentario 
            // después de "LMA4" para que calce exactamente con el formato de la Base de Datos: "LMA4 C26-V-0009498"
            if (s.StartsWith("LMA4") && !s.StartsWith("LMA4 "))
            {
                s = "LMA4 " + s.Substring(4);
            }

            // =========================================================================
            // 🌟 FORMATEADOR DE 7 DÍGITOS (Sincronización estricta de ceros)
            // =========================================================================
            int posGuion = s.LastIndexOf('-');
            if (posGuion >= 0)
            {
                string prefijoBase = s.Substring(0, posGuion + 1);
                string parteNumerica = s.Substring(posGuion + 1);

                if (!string.IsNullOrEmpty(parteNumerica) && parteNumerica.All(char.IsDigit))
                {
                    if (int.TryParse(parteNumerica, out int numeroVal))
                    {
                        s = prefijoBase + numeroVal.ToString("D7");
                    }
                }
            }

            return s;
        }

        public async Task<Dictionary<string, (CodigoCreado CodigoObj, int? ProductoId)>> ObtenerCodigosPorListaAsync(IEnumerable<string> codigos)
        {
            if (codigos == null)
                return new Dictionary<string, (CodigoCreado, int?)>();

            // ====================================================
            // 1. PREPARAR Y NORMALIZAR LISTA DE CORRIDO EN RAM
            // ====================================================
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

            // ====================================================
            // 🚀 TABLA TEMPORAL EN MEMORIA (Alineada a tus Índices)
            // ====================================================
            // Usamos la sintaxis oficial de SQL Server (#) con una clave primaria 
            // para que el motor indexe en RAM los 60,000 registros instantáneamente.
            string sqlCrearTabla = "CREATE TABLE #temp_codigos_bulk (codigo_norm VARCHAR(50) NOT NULL PRIMARY KEY);";

            using (var cmdTmp = dbConn.CreateCommand())
            {
                cmdTmp.CommandText = QueryAdapter.FormatearConsulta(sqlCrearTabla);
                await cmdTmp.ExecuteNonQueryAsync();
            }

            try
            {
                // ====================================================
                // 2. INSERCIÓN MASIVA CONTROLADA (ESTRICTAMENTE 1,000 FILAS)
                // ====================================================
                // Bajamos a 1000 para cumplir con la regla estricta de SQL Server y evitar el error de fila
                const int batchInsertSize = 1000;

                for (int i = 0; i < listaNormalizada.Count; i += batchInsertSize)
                {
                    int cantidad = Math.Min(batchInsertSize, listaNormalizada.Count - i);
                    List<string> lote = listaNormalizada.GetRange(i, cantidad);

                    var sbInsert = new System.Text.StringBuilder("INSERT INTO #temp_codigos_bulk (codigo_norm) VALUES ");
                    using var cmdInsert = dbConn.CreateCommand();

                    for (int j = 0; j < lote.Count; j++)
                    {
                        sbInsert.Append($"(@txt{j})");
                        if (j < lote.Count - 1) sbInsert.Append(",");

                        var p = cmdInsert.CreateParameter();
                        p.ParameterName = "@txt" + j;
                        p.Value = lote[j];
                        cmdInsert.Parameters.Add(p);
                    }

                    cmdInsert.CommandText = sbInsert.ToString();
                    await cmdInsert.ExecuteNonQueryAsync();
                }

                // ====================================================
                // 3. INNER JOIN MAESTRO SOBRE TUS ÍNDICES COMPUESTOS
                // ====================================================
                // Gracias a tus índices con INCLUDE, esta consulta vuela porque cruza la 
                // tabla temporal contra los árboles indexados en memoria, sin buscar en las tablas físicas.
                string queryMaster = @"
                SELECT 
                    cc.id, 
                    cc.registro_codigo_id, 
                    cc.codigo, 
                    cc.es_manual, 
                    cc.estado_id, 
                    rc.producto_id
                FROM #temp_codigos_bulk tmp
                INNER JOIN codigos_creados cc WITH (INDEX(IX_codigos_creados_codigo_perf)) 
                    ON cc.codigo = tmp.codigo_norm -- 🌟 ¡FALTABA ESTA CONDICIÓN DE UNIÓN!
                LEFT JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id";

                using var cmdQuery = dbConn.CreateCommand();
                cmdQuery.CommandText = QueryAdapter.FormatearConsulta(queryMaster);

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
                            EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                        };

                        int? productoId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                        resultado.Add(codigoNorm, (codigoCreado, productoId));
                    }
                }
            }
            finally
            {
                // 🛡️ LIMPIEZA ABSOLUTA DE RECURSOS TEMPORALES EN SQL SERVER
                using var cmdDrop = dbConn.CreateCommand();
                cmdDrop.CommandText = "DROP TABLE IF EXISTS #temp_codigos_bulk;";
                await cmdDrop.ExecuteNonQueryAsync();
            }

            return resultado;
        }

        // Comprueba en la tabla movimiento_codigos qué códigos (por id) ya están asociados a algún movimiento.
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
            

        public async Task<bool> RegistrarCodigosImportadosAsync(  Movimiento cabecera, List<(int CodigoCreadoId, int ProductoId)> codigosImportados, int usuarioId, int? existingMovimientoId = null)
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
                    // Actualizar cabecera
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using (var cmdUpdCab = dbConn.CreateCommand())
                    {
                        cmdUpdCab.Transaction = transaccion;
                        cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                        DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue
                            ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                            : DateTime.Today;
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

                    // Limpiar detalles antiguos para reinsertar
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

                        DateTime fechaConvertida = cabecera.FechaMovimiento.HasValue
                            ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                            : DateTime.Today;

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

                // Agrupar códigos por producto para crear detalles
                var grupos = codigosImportados.GroupBy(x => x.ProductoId);

                foreach (var grupo in grupos)
                {
                    int productoId = grupo.Key;
                    int cantidad = grupo.Count();

                    // Insertar detalle
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
                        AgregarParametro(cmdDet, "@cantidad", cantidad);
                        object resultDet = await cmdDet.ExecuteScalarAsync();
                        detalleIdInserted = Convert.ToInt32(resultDet);
                    }

                    // Para cada código del grupo insertar movimiento_codigos y actualizar estado del código
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

                        // Actualizar estado del código a 3 (En almacén) para ingresos
                        // Comportamiento: si estamos creando un movimiento nuevo, actualizar siempre.
                        // Si estamos editando, actualizar SOLO si el código tenía estado 1 (existente pero no ingresado)
                        // y no estaba previamente asociado al movimiento. Para ello consultamos el estado actual.
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
                                if (estadoActual == 1)
                                {
                                    // No comprobamos pertenencia aquí porque, al insertar la relación
                                    // acabamos de añadirlo al movimiento; asumimos que si estaba en estado 1
                                    // es un código "nuevo" y debe marcarse como ingresado.
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
                                cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 3 WHERE id = @id");
                                AgregarParametro(cmdUpd, "@id", codigoId);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }

                transaccion.Commit();
                return true;
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }


        private void NormalizarRangosImportados(ObservableCollection<RangoCodigoItem> lista)
        {
            foreach (var item in lista)
            {
                // 1. Asegurar que las descripciones de texto coincidan con los números
                if (string.IsNullOrEmpty(item.Desde) && item.DesdeNum > 0)
                    item.Desde = item.DesdeNum.ToString(); // Ajusta según tu formato

                if (string.IsNullOrEmpty(item.Hasta) && item.HastaNum > 0)
                    item.Hasta = item.HastaNum.ToString();

                // 2. Si el objeto no tiene ColeccionTipo pero tiene otros datos, infiérelo
                if (string.IsNullOrEmpty(item.ColeccionTipo))
                    item.ColeccionTipo = "Importado - N/A";
            }
        }

        // =======================================================
        // LÓGICA DE BASE DE DATOS EXTRAÍDA DE LA VISTA
        // =======================================================

        // EN EL MÉTODO ObtenerCategoriaDesdeBDAsync:
        public async Task<int> ObtenerCategoriaDesdeBDAsync(int codigoId)
        {
            try
            {
                // 🌟 SOLUCIÓN: Usar solo DatabaseConnection y QueryAdapter
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

        // EN EL MÉTODO ObtenerColeccionTipoBDAsync:
        public async Task<string> ObtenerColeccionTipoBDAsync(int codigoCreadoId)
        {
            try
            {
                // 🌟 SOLUCIÓN: Usar solo DatabaseConnection y QueryAdapter
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

            // Agrupamos por producto para procesar de forma ordenada
            foreach (var grupoProducto in codigos.GroupBy(x => x.ProductoId))
            {
                int productoId = grupoProducto.Key;

                // Dividimos los códigos en dos flujos: Secuenciales (con guion) y Alfanuméricos puros (sin guion)
                var secuenciales = new List<VistaCodigoGrid>();
                var alfanumericosPuros = new List<VistaCodigoGrid>();

                foreach (var c in grupoProducto.Where(x => !string.IsNullOrWhiteSpace(x.CodigoUnique)))
                {
                    // Si el código contiene un guion y la parte final es numérica, se procesa como secuencial
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

                // ----------------------------------------------------------------------
                // CAMINO A: PROCESAR SECUENCIALES (Agrupación por rangos matemáticos)
                // ----------------------------------------------------------------------
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

                // ----------------------------------------------------------------------
                // CAMINO B: PROCESAR ALFANUMÉRICOS PUROS (Rango Unitario por cada uno)
                // ----------------------------------------------------------------------
                foreach (var alfa in alfanumericosPuros)
                {
                    int categoriaDeducida = (alfa.ColeccionTipo != null && alfa.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")) ? 1 : 2;
                    string tipoTexto = (categoriaDeducida == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
                    string coleccionFinal = string.IsNullOrEmpty(alfa.ColeccionTipo) ? $"C26 / {tipoTexto}" : alfa.ColeccionTipo;

                    resultado.Add(new RangoCodigoItem
                    {
                        productoId = productoId,
                        AbreviaturaBase = alfa.CodigoUnique,
                        // Usamos -1 como indicador interno en memoria de que es un código alfanumérico puro sin número correlativo
                        DesdeNum = -1,
                        HastaNum = -1,
                        Cantidad = "1",
                        // 🌟 CORRECCIÓN DIRECTA: Asignamos el código puro tal como viene, sin guiones ni ceros adicionales
                        Desde = alfa.CodigoUnique,
                        Hasta = alfa.CodigoUnique,
                        ColeccionTipo = coleccionFinal,
                        CategoriaProductoId = categoriaDeducida
                    });
                }
            }

            return resultado;
        }

        // Métodito ayudante privado para no repetir código al instanciar el objeto plano
        private RangoCodigoItem ConstruirRangoItem(int productoId, string prefijo, int inicio, int fin, VistaCodigoGrid itemOriginal)
        {
            int cant = (fin - inicio + 1);
            int categoriaDeducida = 2; // Venta por defecto

            if ((itemOriginal.CodigoUnique != null && itemOriginal.CodigoUnique.ToUpperInvariant().Contains("-G-")) ||
                (itemOriginal.ColeccionTipo != null && itemOriginal.ColeccionTipo.ToUpperInvariant().Contains("GUÍA")))
            {
                categoriaDeducida = 1; // Guía
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

            foreach (var rango in rangos)
            {
                // 🌟 SI ES ALFANUMÉRICO PURO (DesdeNum == -1), agregamos el código tal cual
                if (rango.DesdeNum == -1)
                {
                    lista.Add(new VistaCodigoGrid
                    {
                        ProductoId = rango.productoId,
                        CodigoUnique = rango.Desde, // Usamos el string original (sin ceros)
                        ColeccionTipo = rango.ColeccionTipo,
                        MovCodigo = new MovimientoCodigo { MovimientoDetalleId = rango.MovimientoDetalleId }
                    });
                }
                else
                {
                    // ES SECUENCIAL: Mantenemos tu lógica de reconstrucción de rangos con ceros
                    for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                    {
                        lista.Add(new VistaCodigoGrid
                        {
                            ProductoId = rango.productoId,
                            CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                            ColeccionTipo = rango.ColeccionTipo,
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = rango.MovimientoDetalleId }
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
                // Contamos cuántos códigos reales existen en el listado para este producto específico
                int cantidadCodigos = codigos.Count(x => x.ProductoId == producto.ProductoId);

                producto.Detalle ??= new MovimientoDetalle { ProductoId = producto.ProductoId };

                // 🌟 LÓGICA HÍBRIDA ESCALABLE:
                // Si hay códigos asociados, la cantidad del producto la manda el conteo físico (Libros).
                if (cantidadCodigos > 0)
                {
                    producto.Cantidad = cantidadCodigos;
                    producto.Detalle.CantidadIngreso = cantidadCodigos;
                }
                else
                {
                    // Si el conteo es 0, significa que es un producto sin código (Mochilas, Cuadernos).
                    // Mantenemos intacta la cantidad digitada manualmente por el usuario.
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
                if (listaDestino.Any(x =>
                    x.CodigoUnique.Equals(codigo.CodigoUnique,
                    StringComparison.OrdinalIgnoreCase)))
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
                        Id = g.Select(x => x.Detalle?.Id ?? 0)
                              .FirstOrDefault(id => id > 0),

                        ProductoId = g.Key,

                        CantidadIngreso =
                            g.Sum(x => x.Detalle?.CantidadIngreso ?? 0),

                        CostoUnitario =
                            g.First().Detalle?.CostoUnitario ?? 0
                    }
                })
                .ToList();
        }

        public void ReemplazarRangosProducto(List<RangoCodigoItem> lista, int productoId,  IEnumerable<RangoCodigoItem> nuevos)
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
            producto.Detalle ??= new MovimientoDetalle
            {
                ProductoId = producto.ProductoId
            };

            producto.Detalle.CantidadIngreso = cantidad;

            producto.Cantidad = cantidad;
        }

        public List<VistaCodigoGrid> ObtenerCodigosProducto( List<VistaCodigoGrid> codigos, int productoId)
        {
            return codigos
                .Where(x => x.ProductoId == productoId)
                .ToList();
        }

        public List<RangoCodigoItem> ObtenerRangosProducto( List<RangoCodigoItem> rangos, int productoId)
        {
            return rangos
                .Where(x => x.productoId == productoId)
                .ToList();
        }

        /// <summary>
        /// VALIDACIÓN LOOK-AHEAD: Comprueba si un código tiene transacciones registradas 
        /// con una fecha posterior a la del movimiento actual en edición.
        /// </summary>
        public async Task<bool> TieneMovimientosPosterioresAsync(int codigoId, DateTime fechaEdicion, DbConnection conn, DbTransaction trans)
        {
            string query = @"
        SELECT COUNT(*) 
        FROM movimiento_codigos mc
        JOIN movimientos m ON mc.movimiento_id = m.id
        WHERE mc.codigo_creado_id = @codId 
        AND m.fecha_movimiento > @fechaEdicion";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            AgregarParametro(cmd, "@codId", codigoId);
            AgregarParametro(cmd, "@fechaEdicion", fechaEdicion);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && Convert.ToInt32(result) > 0;
        }

        /// <summary>
        /// REVERSIÓN HISTÓRICA: Busca el estado_id que poseía el código en su movimiento 
        /// inmediato anterior. Si no registra movimientos previos, devuelve 1 (Registrado).
        /// </summary>
        public async Task<int> ObtenerEstadoAnteriorAsync(int codigoId, int movimientoActualId, DbConnection conn, DbTransaction trans)
        {
            // NOTA: Si usas MySQL de forma nativa, QueryAdapter se encargará, o puedes usar LIMIT 1 al final.
            string query = @"
        SELECT TOP 1 m.motivo_producto_id 
        FROM movimiento_codigos mc
        JOIN movimientos m ON mc.movimiento_id = m.id
        WHERE mc.codigo_creado_id = @codId 
        AND m.id < @movId
        ORDER BY m.fecha_movimiento DESC, m.id DESC";

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@codId", codigoId);
                AgregarParametro(cmd, "@movId", movimientoActualId);

                object result = await cmd.ExecuteScalarAsync();

                // Mapeo lógico: Si el motivo previo fue una entrada (ej: compra), su estado era 3.
                // Si no hay historial, regresa al estado inicial 1.
                if (result != null && result != DBNull.Value)
                {
                    int motivoId = Convert.ToInt32(result);
                    // Aquí puedes mapear según tu tabla de motivos. Si el motivo id = 1 es compra, el estado era 3.
                    // Si el motivo previo infiere una salida, su estado era 4.
                    return motivoId == 1 ? 3 : 4;
                }
                return 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo estado anterior para código {codigoId}: {ex.Message}");
                return 1;
            }
        }
    }
}