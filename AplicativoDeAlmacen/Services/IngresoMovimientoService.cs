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

        public async Task<MovimientoCompletoResult> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var result = new MovimientoCompletoResult();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            bool numeroEsEntero = int.TryParse(numero, out int numeroIntVal);

            string query = @"
                SELECT m.id, m.fecha_movimiento, m.serie_documento, m.numero_documento, m.motivo_producto_id, m.ubicacion_id,
                       m.persona_comercial_id, m.serie_guia, m.numero_guia, m.observacion,
                       mp.descripcion AS motivo_desc, pc.razon_social, u.descripcion AS ubicacion_desc
                FROM movimientos m
                LEFT JOIN motivo_productos mp ON mp.id = m.motivo_producto_id
                LEFT JOIN personas_comerciales pc ON pc.id = m.persona_comercial_id
                LEFT JOIN ubicaciones u ON u.id = m.ubicacion_id
                WHERE (m.numero_documento = @numero" + (numeroEsEntero ? " OR TRY_CAST(m.numero_documento AS INT) = @numeroInt" : "") + ")";

            if (!string.IsNullOrEmpty(serie)) query += " AND m.serie_documento = @serie";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@numero"; p1.Value = numero; cmd.Parameters.Add(p1);
                if (numeroEsEntero) { var pInt = cmd.CreateParameter(); pInt.ParameterName = "@numeroInt"; pInt.Value = numeroIntVal; cmd.Parameters.Add(pInt); }
                if (!string.IsNullOrEmpty(serie)) { var p2 = cmd.CreateParameter(); p2.ParameterName = "@serie"; p2.Value = serie; cmd.Parameters.Add(p2); }

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                // Mapear a la entidad Movimiento existente
                var mov = new Movimiento
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FechaMovimiento = reader.IsDBNull(reader.GetOrdinal("fecha_movimiento")) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = reader["serie_documento"] as string,
                    NumeroDocumento = reader["numero_documento"] as string,
                    MotivoProductoId = reader.IsDBNull(reader.GetOrdinal("motivo_producto_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = reader.IsDBNull(reader.GetOrdinal("persona_comercial_id")) ? null : reader.GetInt32(reader.GetOrdinal("persona_comercial_id")),
                    SerieGuia = reader.IsDBNull(reader.GetOrdinal("serie_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("serie_guia")),
                    NumeroGuia = reader.IsDBNull(reader.GetOrdinal("numero_guia")) ? string.Empty : reader.GetString(reader.GetOrdinal("numero_guia")),
                    Observacion = reader.IsDBNull(reader.GetOrdinal("observacion")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacion")),
                    UbicacionId = reader.IsDBNull(reader.GetOrdinal("ubicacion_id")) ? null : reader.GetInt32(reader.GetOrdinal("ubicacion_id"))
                };

                result.Movimiento = mov;
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

            // Rangos asociados por detalle
            string qRangos = @"SELECT id, producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId)";
            using (var cmdR = dbConn.CreateCommand())
            {
                cmdR.CommandText = QueryAdapter.FormatearConsulta(qRangos);
                var pr = cmdR.CreateParameter(); pr.ParameterName = "@movId"; pr.Value = result.Movimiento.Id; cmdR.Parameters.Add(pr);
                using var rdrR = await cmdR.ExecuteReaderAsync();
                while (await rdrR.ReadAsync())
                {
                    int desdeNum = rdrR.GetInt32(rdrR.GetOrdinal("desde_num"));
                    int hastaNum = rdrR.GetInt32(rdrR.GetOrdinal("hasta_num"));
                    int categoriaId = rdrR.GetInt32(rdrR.GetOrdinal("categoria_producto_id"));
                    string baseAbrev = rdrR.IsDBNull(rdrR.GetOrdinal("abreviatura_base")) ? string.Empty : rdrR.GetString(rdrR.GetOrdinal("abreviatura_base"));

                    // Construir representación textual del rango (Desde/Hasta) y el tipo de colección
                    string desdeText = $"{baseAbrev}-{desdeNum:D7}";
                    string hastaText = $"{baseAbrev}-{hastaNum:D7}";
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
                        Cantidad = (hastaNum - desdeNum + 1).ToString(),
                        Desde = desdeText,
                        Hasta = hastaText,
                        ColeccionTipo = coleccionTipo
                    });
                }
            }

            return result;
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

        public async Task<Movimiento> GenerarSiguienteCorrelativoAsync(string serie)
        {
            var resultado = new Movimiento
            {
                SerieDocumento = serie,
                NumeroDocumento = "0000001"
            };

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // CAMBIO MULTI-MOTOR: ISNULL -> COALESCE
                string query = @"
                    SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 
                    FROM movimientos 
                    WHERE serie_documento = @serie";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@serie", serie);

                    object resultObj = await cmd.ExecuteScalarAsync();

                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        int siguienteNumero = Convert.ToInt32(resultObj);
                        resultado.NumeroDocumento = siguienteNumero.ToString("D7");
                    }
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


            string queryLock = "SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 FROM movimientos WITH (UPDLOCK, HOLDLOCK) WHERE serie_documento = @serie";
            int nuevoNumero;

            using (var cmdLock = conn.CreateCommand())
            {
                cmdLock.Transaction = trans;
                cmdLock.CommandText = QueryAdapter.FormatearConsulta(queryLock);
                AgregarParametro(cmdLock, "@serie", cabecera.SerieDocumento);
                nuevoNumero = Convert.ToInt32(await cmdLock.ExecuteScalarAsync());
            }

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

            return Convert.ToInt32(await cmdCab.ExecuteScalarAsync());
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
        private async Task EliminarMovimientoCodigoAsync(int detId, int codId, DbConnection conn, DbTransaction trans)
        {
            // Método vacío para evitar borrar datos físicos
            await Task.CompletedTask;
        }

        public async Task<bool> RegistrarMovimientoCompletoAsync(
    Movimiento cabecera,
    List<VistaProductoGrid> productos,
    List<RangoCodigoItem> rangos,
    int ubicacionId,
    int? existingMovimientoId = null,
    IProgress<int> progress = null) // 🌟 Progreso para la UI
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var transaccion = dbConn.BeginTransaction();

            try
            {
                // 1. CABECERA
                int movimientoId = await GuardarCabeceraAsync(cabecera, ubicacionId, existingMovimientoId, dbConn, transaccion);

                // 2. OBTENER CÓDIGOS ACTUALES EN BASE DE DATOS (Si es edición)
                // Pasamos el ID del movimiento existente de forma correcta al helper
                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { existingMovimientoId.Value })
                    : new HashSet<int>();

                int totalProductos = productos.Count;
                int procesados = 0;

                // ✅ CORRECCIÓN CS0103: Declaramos el HashSet aquí para acumular la totalidad
                // de los códigos procesados a lo largo de todos los productos de este documento.
                var nuevosCodigosIds = new HashSet<int>();

                foreach (var item in productos)
                {
                    int detalleId = await UpsertMovimientoDetalleAsync(movimientoId, item, dbConn, transaccion);
                    var rangosProd = rangos.Where(r => r.productoId == item.ProductoId).ToList();

                    var codigosAInsertar = new List<int>();
                    foreach (var r in rangosProd)
                    {
                        await InsertarRangoAsync(r, detalleId, dbConn, transaccion);
                        var encontrados = await ObtenerIdsCodigosPorRangoAsync(r.productoId, r.AbreviaturaBase, r.CategoriaProductoId, r.DesdeNum, r.HastaNum, dbConn, transaccion);

                        foreach (var t in encontrados)
                        {
                            // 🌟 VALIDACIÓN DE INTEGRIDAD LOOK-AHEAD (Para ítems nuevos/existentes)
                            if (await TieneMovimientosPosterioresAsync(t.CodigoObj.Id, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion))
                            {
                                throw new Exception($"El código {t.CodigoObj.Codigo} tiene movimientos posteriores registrados. No se puede modificar.");
                            }

                            codigosAInsertar.Add(t.CodigoObj.Id);
                            nuevosCodigosIds.Add(t.CodigoObj.Id); // ✅ Guardamos en el set de persistencia global
                        }
                    }

                    // 4. INSERCIÓN MASIVA DE NUEVOS (Por cada detalle de producto)
                    var batchSize = 500;
                    for (int i = 0; i < codigosAInsertar.Count; i += batchSize)
                    {
                        var batch = codigosAInsertar.Skip(i).Take(batchSize).ToList();
                        await InsertarMovimientoCodigosMasivoAsync(movimientoId, detalleId, batch, dbConn, transaccion);
                        await ActualizarEstadoCodigosMasivoAsync(batch, 3, dbConn, transaccion); // 3 = Ingresado/Entrada
                    }

                    procesados++;
                    progress?.Report((procesados * 100) / totalProductos);
                }

                // 🌟 3. PROCESAR ELIMINADOS (Ejecución Post-Bucle)
                // Evaluamos la diferencia real entre lo que había en la BD y lo que el usuario conservó en la UI
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();

                foreach (var codId in codigosAEliminar)
                {
                    // A. Validación: ¿Tiene futuro este código?
                    bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);
                    if (tieneFuturo)
                    {
                        throw new Exception($"El código ID {codId} no puede eliminarse de este registro porque cuenta con movimientos cronológicamente posteriores.");
                    }

                    // B. Reversión: ¿A qué estado debe volver según su línea de tiempo?
                    int estadoAnterior = await ObtenerEstadoAnteriorAsync(codId, movimientoId, dbConn, transaccion);

                    // C. Aplicar reversión individualizada en codigos_creados
                    await ActualizarEstadoCodigo(codId, estadoAnterior, dbConn, transaccion);

                    // D. Borrar el registro físico de vinculación en movimiento_codigos
                    // Nota: Pasamos el 'movimientoId' general para limpiar la relación rota en esta edición
                    await EliminarMovimientoCodigoAsync(movimientoId, codId, dbConn, transaccion);
                }

                transaccion.Commit();
                return true;
            }
            catch (Exception ex)
            {
                transaccion.Rollback();
                throw new Exception($"Error en transacción: {ex.Message}");
            }
        }

        // 🌟 ESTE MÉTODO INSERTA VARIOS CÓDIGOS DE UNA SOLA VEZ (BATCH)
        private async Task InsertarMovimientoCodigosMasivoAsync(int movId, int detId, List<int> codigosIds, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var values = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            for (int i = 0; i < codigosIds.Count; i++)
            {
                values.Add($"(@movId, @detId, @c{i}, 1, 0, GETDATE())");
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta(
                "INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES " + string.Join(",", values));

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
                paramNames.Add("@c" + i);
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

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
        private async Task<List<(CodigoCreado CodigoObj, int Seq)>> ObtenerIdsCodigosPorRangoAsync( int productoId, string baseLimpia, int categoriaId,int desde, int hasta, DbConnection conn,DbTransaction trans)
        {
            var resultados = new List<(CodigoCreado CodigoObj, int Seq)>();

            string prefijo = (baseLimpia ?? "").Length >= 3
                ? baseLimpia.Substring(0, 3) + "%"
                : "%";

            string query = @"
            SELECT
                cc.id,
                cc.registro_codigo_id,
                cc.codigo,
                cc.es_manual,
                cc.estado_id
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc
                ON rc.id = cc.registro_codigo_id
            WHERE rc.producto_id=@productoId
            AND rc.categoria_producto_id=@categoriaId
            AND cc.codigo LIKE @prefijo";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@productoId", productoId);
                AgregarParametro(cmd, "@categoriaId", categoriaId);
                AgregarParametro(cmd, "@prefijo", prefijo);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string codigoRaw = reader.IsDBNull(2) ? "" : reader.GetString(2);

                        string codigoNorm = NormalizarCodigo(codigoRaw);

                        System.Diagnostics.Debug.WriteLine("RAW  : " + codigoRaw);
                        System.Diagnostics.Debug.WriteLine("NORM : " + codigoNorm);

                        if (codigoNorm.Length >= 7)
                        {
                            string colaStr = codigoNorm.Substring(codigoNorm.Length - 7);

                            System.Diagnostics.Debug.WriteLine("COLA : " + colaStr);

                            if (int.TryParse(colaStr, out int seq))
                            {
                                System.Diagnostics.Debug.WriteLine("SEQ  : " + seq);

                                if (seq >= desde && seq <= hasta)
                                {
                                    resultados.Add((
                                        new CodigoCreado
                                        {
                                            Id = reader.GetInt32(0),
                                            RegistroCodigoId = reader.GetInt32(1),
                                            Codigo = codigoRaw,
                                            EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                                            EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                                        },
                                        seq));
                                }
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("TOTAL = " + resultados.Count);
                }
            }

            return resultados;
        }

        // Normaliza un código: elimina espacios y comillas, y pasa a mayúsculas
        public string NormalizarCodigo(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;
            // quitar espacios, comillas simples/dobles, apóstrofes tipográficos y distintas variedades de guion/dash
            var s = codigo.Replace(" ", "")
                          .Replace("'", "")
                          .Replace("\"", "")
                          .Replace("\u2019", "")
                          .Replace("\u2018", "")
                          .Replace("-", "")
                          .Replace("\u2010", "")
                          .Replace("\u2011", "")
                          .Replace("\u2012", "")
                          .Replace("\u2013", "")
                          .Replace("\u2014", "")
                          .Replace("\u2015", "")
                          .Replace("\u200B", ""); // zero-width space
            return s.ToUpperInvariant();
        }

        public async Task<Dictionary<string, (CodigoCreado CodigoObj, int? ProductoId)>> ObtenerCodigosPorListaAsync(IEnumerable<string> codigos)
        {
            var resultado = new Dictionary<string, (CodigoCreado, int?)>(StringComparer.OrdinalIgnoreCase);

            var listaRaw = codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (!listaRaw.Any()) return resultado;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            // Normalizamos todos los códigos de entrada para el cruce rápido en memoria RAM
            var targetNorms = new HashSet<string>(listaRaw.Select(NormalizarCodigo), StringComparer.OrdinalIgnoreCase);

            // Procesamos en lotes de 1000 para evitar desbordar los límites de parámetros de SQL Server / MySQL
            const int batchSize = 1000;
            for (int i = 0; i < listaRaw.Count; i += batchSize)
            {
                var loteActual = listaRaw.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();

                for (int j = 0; j < loteActual.Count; j++) paramNames.Add("@c" + j);

                // 🌟 CONSULTA BLINDADA: Comparamos el código normalizado de forma directa por string
                // Ya no dependemos de TRY_CAST o RIGHT de longitud fija que rompía los alfanuméricos puros
                string queryExact = $@"
            SELECT cc.id, cc.registro_codigo_id, cc.codigo, cc.es_manual, cc.estado_id, rc.producto_id
            FROM codigos_creados cc
            LEFT JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE cc.codigo IN ({string.Join(',', paramNames)})";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryExact);
                    for (int j = 0; j < loteActual.Count; j++)
                    {
                        AgregarParametro(cmd, "@c" + j, loteActual[j]);
                    }

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string codigoRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        string codNorm = NormalizarCodigo(codigoRaw);

                        if (targetNorms.Contains(codNorm) && !resultado.ContainsKey(codNorm))
                        {
                            var obj = new CodigoCreado
                            {
                                Id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                                RegistroCodigoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                Codigo = codigoRaw,
                                EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                                EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                            };
                            int? prodId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);

                            resultado[codNorm] = (obj, prodId);
                        }
                    }
                }
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
                int cantidad = codigos.Count(x => x.ProductoId == producto.ProductoId);

                producto.Cantidad = cantidad;

                producto.Detalle ??= new MovimientoDetalle
                {
                    ProductoId = producto.ProductoId
                };

                producto.Detalle.CantidadIngreso = cantidad;
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