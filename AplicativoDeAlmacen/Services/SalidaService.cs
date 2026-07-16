#nullable enable

using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Motivo_y_Movimientos;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class SalidaMovimientoService
    {
        private readonly DatabaseConnection _database;

        public SalidaMovimientoService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        // =========================================================================
        // 1. GENERAR CORRELATIVO (SIN BLOQUEO PESIMISTA PARA VISTA PREVIA)
        // =========================================================================
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

                string query = @"
                    SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 
                    FROM movimientos 
                    WHERE serie_documento = @serie";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@serie", serie);

                    object? resultObj = await cmd.ExecuteScalarAsync();

                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        int siguienteNumero = Convert.ToInt32(resultObj);
                        resultado.NumeroDocumento = siguienteNumero.ToString("D7");
                    }
                }
            }
            return resultado;
        }

        // =========================================================================
        // 2. CATÁLOGOS AUXILIARES
        // =========================================================================
        public async Task<List<MotivoProducto>> ObtenerMotivosSalidaAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, tipo_movimiento 
                                 FROM motivo_productos 
                                 WHERE tipo_movimiento = 'salida' 
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new MotivoProducto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                TipoMovimiento = reader.IsDBNull(reader.GetOrdinal("tipo_movimiento"))
                                    ? null : reader.GetString(reader.GetOrdinal("tipo_movimiento"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<PersonaComercial>> BuscarClientesAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, razon_social, direccion 
                                 FROM personas_comerciales 
                                 WHERE razon_social LIKE @filtro 
                                 ORDER BY razon_social ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new PersonaComercial
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                RazonSocial = reader.GetString(reader.GetOrdinal("razon_social")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<List<Ubicacion>> BuscarUbicacionesAsync(string filtro)
        {
            var lista = new List<Ubicacion>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, direccion 
                                 FROM ubicaciones 
                                 WHERE descripcion LIKE @filtro 
                                 ORDER BY descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", "%" + filtro + "%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new Ubicacion
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        public async Task<CodigoCreado?> ObtenerCodigoLimpiadoAsync(string codigoBruto)
        {
            if (string.IsNullOrWhiteSpace(codigoBruto)) return null;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string codigoLimpio = codigoBruto.Replace("'", "-").Trim();

            // Usamos QueryAdapter.FormatearConsulta para que resuelva el TOP 1 (SQL) a LIMIT 1 (MySQL) si aplica.
            string query = @"
            SELECT TOP 1 cc.id, cc.codigo, cc.estado_id, rc.producto_id 
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON rc.id = cc.registro_codigo_id
            WHERE REPLACE(cc.codigo, '''', '-') = @CodigoLimpio";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@CodigoLimpio", codigoLimpio);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new CodigoCreado
                        {
                            Id = reader.GetInt32(0),
                            Codigo = reader.GetString(1),
                            EstadoId = reader.GetInt32(2),
                            RegistroCodigoId = reader.GetInt32(3) // Usado temporalmente para acarrear ProductoId
                        };
                    }
                }
            }
            return null;
        }

        // =========================================================================
        // 🌟 3. BLOQUE DE INTEGRIDAD HISTÓRICA (COPIADO Y ADAPTADO)
        // =========================================================================

        public async Task<HashSet<int>> ObtenerCodigosEnMovimientoAsync(IEnumerable<int> movIds, DbConnection conn, DbTransaction trans)
        {
            var set = new HashSet<int>();
            var ids = movIds?.Distinct().ToList();
            if (ids == null || !ids.Any()) return set;

            const int batchSize = 1000;
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();
                for (int j = 0; j < batch.Count; j++) paramNames.Add("@p" + j);

                string q = $@"SELECT DISTINCT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id IN ({string.Join(',', paramNames)})";
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                for (int j = 0; j < batch.Count; j++)
                {
                    AgregarParametro(cmd, "@p" + j, batch[j]);
                }

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0)) set.Add(rdr.GetInt32(0));
                }
            }
            return set;
        }

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

        public async Task<int> ObtenerEstadoAnteriorAsync(int codigoId, int movimientoActualId, DbConnection conn, DbTransaction trans)
        {
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

                object? result = await cmd.ExecuteScalarAsync();

                // Si borramos un código de una SALIDA, su estado anterior en la línea de tiempo 
                // era 3 (En Almacén) porque venía de un INGRESO. Retornamos 3 por defecto seguro.
                return 3;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo estado anterior: {ex.Message}");
                return 3;
            }
        }

        private async Task ActualizarEstadoCodigo(int codigoId, int nuevoEstadoId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = @estado WHERE id = @id");
            AgregarParametro(cmd, "@estado", nuevoEstadoId);
            AgregarParametro(cmd, "@id", codigoId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertarMovimientoCodigosSalidaMasivoAsync(int movId, int detId, List<int> codigosIds, DbConnection conn, DbTransaction trans)
        {
            if (codigosIds == null || !codigosIds.Any()) return;

            var values = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            for (int i = 0; i < codigosIds.Count; i++)
            {
                // Para salidas: cantidad_ingreso = 0, cantidad_salida = 1
                values.Add($"(@movId, @detId, @c{i}, 0, 1, GETDATE())");
                AgregarParametro(cmd, "@c" + i, codigosIds[i]);
            }

            cmd.CommandText = QueryAdapter.FormatearConsulta(
                "INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES " + string.Join(",", values));

            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@detId", detId);

            await cmd.ExecuteNonQueryAsync();
        }

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

        private async Task EliminarMovimientoCodigoAsync(int movId, int codId, DbConnection conn, DbTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_id = @movId AND codigo_creado_id = @codId");
            AgregarParametro(cmd, "@movId", movId);
            AgregarParametro(cmd, "@codId", codId);
            await cmd.ExecuteNonQueryAsync();
        }

        // =========================================================================
        // 🌟 4. REGISTRAR SALIDA COMPLETA TRANSACCIONAL (MÁXIMO RENDIMIENTO)
        // =========================================================================
        public async Task<bool> RegistrarSalidaCompletaAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> listaProductos,
            List<VistaCodigoGrid> listaCodigos,
            int usuarioId,
            int estadoId,
            int? existingMovimientoId = null,
            IProgress<int>? progress = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaccion = dbConn.BeginTransaction();
            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                int movimientoIdInserted = 0;

                // 1. GENERACIÓN ANTI-COLISIÓN (Cabecera)
                if (!existingMovimientoId.HasValue)
                {
                    string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "0001" : cabecera.SerieDocumento;
                    string bloqueoConcurrencia = QueryAdapter.EsMySQL ? "FOR UPDATE" : "WITH (UPDLOCK, HOLDLOCK)";

                    using (var cmdGen = dbConn.CreateCommand())
                    {
                        cmdGen.Transaction = transaccion;
                        cmdGen.CommandText = QueryAdapter.FormatearConsulta($@"
                            SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 
                            FROM movimientos {bloqueoConcurrencia} 
                            WHERE serie_documento = @serie");
                        AgregarParametro(cmdGen, "@serie", serieParaGenerar);
                        object? genRes = await cmdGen.ExecuteScalarAsync();
                        int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;
                        cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                        cabecera.SerieDocumento = serieParaGenerar;
                    }

                    string queryCabecera = $@"
                        INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, 
                               ubicacion_id, usuario_id, persona_comercial_id, serie_guia, numero_guia, 
                               observacion, estado_id, created_at)
                        VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, 
                               @personaId, @serieGuia, @numeroGuia, @observacion, @estadoId, GETDATE());
                        {selectId}";

                    using (var cmdCab = dbConn.CreateCommand())
                    {
                        cmdCab.Transaction = transaccion;
                        cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                        DateTime fecha = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
                        AgregarParametro(cmdCab, "@fecha", fecha);
                        AgregarParametro(cmdCab, "@serie", cabecera.SerieDocumento);
                        AgregarParametro(cmdCab, "@numero", cabecera.NumeroDocumento);
                        AgregarParametro(cmdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdCab, "@ubicacionId", cabecera.UbicacionId > 0 ? (object)cabecera.UbicacionId : DBNull.Value);
                        AgregarParametro(cmdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId > 0 ? (object)cabecera.PersonaComercialId : DBNull.Value);
                        AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);
                        AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdCab, "@estadoId", estadoId);
                        object? resultCab = await cmdCab.ExecuteScalarAsync();
                        movimientoIdInserted = Convert.ToInt32(resultCab);
                    }
                }
                else
                {
                    movimientoIdInserted = existingMovimientoId.Value;
                    string updateCab = @"UPDATE movimientos SET fecha_movimiento = @fecha, motivo_producto_id = @motivoId, ubicacion_id = @ubicacionId, usuario_id = @usuarioId, persona_comercial_id = @personaId, observacion = @observacion, serie_guia = @serieGuia, numero_guia = @numeroGuia WHERE id = @id";
                    using (var cmdUpdCab = dbConn.CreateCommand())
                    {
                        cmdUpdCab.Transaction = transaccion;
                        cmdUpdCab.CommandText = QueryAdapter.FormatearConsulta(updateCab);
                        DateTime fecha = cabecera.FechaMovimiento.HasValue ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
                        AgregarParametro(cmdUpdCab, "@fecha", fecha);
                        AgregarParametro(cmdUpdCab, "@motivoId", cabecera.MotivoProductoId);
                        AgregarParametro(cmdUpdCab, "@ubicacionId", cabecera.UbicacionId > 0 ? (object)cabecera.UbicacionId : DBNull.Value);
                        AgregarParametro(cmdUpdCab, "@usuarioId", usuarioId);
                        AgregarParametro(cmdUpdCab, "@personaId", cabecera.PersonaComercialId > 0 ? (object)cabecera.PersonaComercialId : DBNull.Value);
                        AgregarParametro(cmdUpdCab, "@serieGuia", cabecera.SerieGuia);
                        AgregarParametro(cmdUpdCab, "@numeroGuia", cabecera.NumeroGuia);
                        AgregarParametro(cmdUpdCab, "@observacion", cabecera.Observacion);
                        AgregarParametro(cmdUpdCab, "@id", movimientoIdInserted);
                        await cmdUpdCab.ExecuteNonQueryAsync();
                    }
                }

                // 2. RECUPERAR MEMORIA HISTÓRICA
                var codigosPreviosEnBD = existingMovimientoId.HasValue
                    ? await ObtenerCodigosEnMovimientoAsync(new List<int> { movimientoIdInserted }, dbConn, transaccion)
                    : new HashSet<int>();

                // Validamos que los NUEVOS códigos (los que no estaban en la BD) estén físicamente en estado 3
                var codigosInvalidos = new List<string>();
                foreach (var cod in listaCodigos)
                {
                    if (!codigosPreviosEnBD.Contains(cod.MovCodigo.CodigoCreadoId))
                    {
                        using var cmdVal = dbConn.CreateCommand();
                        cmdVal.Transaction = transaccion;
                        cmdVal.CommandText = QueryAdapter.FormatearConsulta("SELECT estado_id, codigo FROM codigos_creados WHERE id = @id");
                        AgregarParametro(cmdVal, "@id", cod.MovCodigo.CodigoCreadoId);
                        using var rdr = await cmdVal.ExecuteReaderAsync();
                        if (await rdr.ReadAsync())
                        {
                            int estadoBd = rdr.GetInt32(0);
                            if (estadoBd != 3) codigosInvalidos.Add($"{rdr.GetString(1)} (Requiere estar en Almacén, pero está en estado {estadoBd})");
                        }
                    }
                }

                if (codigosInvalidos.Any())
                {
                    throw new Exception("Error de validación física:\nLos siguientes códigos no están en Almacén:\n" + string.Join("\n", codigosInvalidos.Take(10)));
                }

                // 3. PROCESAMIENTO HÍBRIDO DE DETALLES
                var idsDetallesActivos = new List<int>();
                var nuevosCodigosIds = new HashSet<int>();
                int procesados = 0;
                int totalProductos = listaProductos.Count;

                foreach (var item in listaProductos)
                {
                    int idDetalle = 0;

                    using (var cmdCheck = dbConn.CreateCommand())
                    {
                        cmdCheck.Transaction = transaccion;
                        cmdCheck.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND producto_id = @prodId");
                        AgregarParametro(cmdCheck, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdCheck, "@prodId", item.ProductoId);
                        object? resDet = await cmdCheck.ExecuteScalarAsync();
                        if (resDet != null && resDet != DBNull.Value) idDetalle = Convert.ToInt32(resDet);
                    }

                    if (idDetalle > 0)
                    {
                        using var cmdUpd = dbConn.CreateCommand();
                        cmdUpd.Transaction = transaccion;
                        cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_salida = @cant, costo_unitario = @costo WHERE id = @detId");
                        AgregarParametro(cmdUpd, "@cant", item.Cantidad);
                        AgregarParametro(cmdUpd, "@costo", item.Detalle?.CostoUnitario ?? 0);
                        AgregarParametro(cmdUpd, "@detId", idDetalle);
                        await cmdUpd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        string queryDetalle = $@"
                            INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                            VALUES (@movId, @prodId, 0, @cant, @costo, GETDATE());
                            {selectId}";
                        using var cmdDet = dbConn.CreateCommand();
                        cmdDet.Transaction = transaccion;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                        AgregarParametro(cmdDet, "@movId", movimientoIdInserted);
                        AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                        AgregarParametro(cmdDet, "@cant", item.Cantidad);
                        AgregarParametro(cmdDet, "@costo", item.Detalle?.CostoUnitario ?? 0);
                        idDetalle = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    idsDetallesActivos.Add(idDetalle);

                    // 🌟 LÓGICA ESCALABLE (Batching solo para libros serializados)
                    var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId).ToList();
                    if (codigosProd.Any())
                    {
                        var codigosNuevosParaInsertar = new List<int>();

                        foreach (var cod in codigosProd)
                        {
                            if (await TieneMovimientosPosterioresAsync(cod.MovCodigo.CodigoCreadoId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion))
                            {
                                throw new Exception($"El código ID {cod.MovCodigo.CodigoCreadoId} cuenta con transacciones posteriores. Operación abortada.");
                            }

                            nuevosCodigosIds.Add(cod.MovCodigo.CodigoCreadoId);

                            // Insertamos solo la "Delta" (Los que no estaban antes)
                            if (!codigosPreviosEnBD.Contains(cod.MovCodigo.CodigoCreadoId))
                            {
                                codigosNuevosParaInsertar.Add(cod.MovCodigo.CodigoCreadoId);
                            }
                        }

                        // Lotes de 500 para inserción/actualización de ESTADO 4 (Salida)
                        var batchSize = 500;
                        for (int i = 0; i < codigosNuevosParaInsertar.Count; i += batchSize)
                        {
                            var batch = codigosNuevosParaInsertar.Skip(i).Take(batchSize).ToList();
                            await InsertarMovimientoCodigosSalidaMasivoAsync(movimientoIdInserted, idDetalle, batch, dbConn, transaccion);
                            await ActualizarEstadoCodigosMasivoAsync(batch, 4, dbConn, transaccion);
                        }
                    }

                    procesados++;
                    progress?.Report((procesados * 100) / totalProductos);
                }

                // 4. LIMPIEZA DE HUÉRFANOS Y REVERSIÓN DE ESTADOS
                // a) Desvincular códigos eliminados por el usuario y regresarlos al Almacén (Estado 3)
                var codigosAEliminar = codigosPreviosEnBD.Where(id => !nuevosCodigosIds.Contains(id)).ToList();
                foreach (var codId in codigosAEliminar)
                {
                    bool tieneFuturo = await TieneMovimientosPosterioresAsync(codId, cabecera.FechaMovimiento?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, dbConn, transaccion);
                    if (tieneFuturo) throw new Exception($"El código ID {codId} no puede eliminarse porque tiene movimientos registrados después de esta fecha.");

                    int estadoAnterior = await ObtenerEstadoAnteriorAsync(codId, movimientoIdInserted, dbConn, transaccion);
                    await ActualizarEstadoCodigo(codId, estadoAnterior, dbConn, transaccion);
                    await EliminarMovimientoCodigoAsync(movimientoIdInserted, codId, dbConn, transaccion);
                }

                // b) Eliminar detalles huérfanos (Ej. Se borró toda una mochila del grid)
                if (existingMovimientoId.HasValue && idsDetallesActivos.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < idsDetallesActivos.Count; i++) paramNames.Add("@act" + i);

                    string qLimpiar = $@"
                        DELETE FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)});
                    ";
                    using var cmdLimp = dbConn.CreateCommand();
                    cmdLimp.Transaction = transaccion;
                    cmdLimp.CommandText = QueryAdapter.FormatearConsulta(qLimpiar);
                    AgregarParametro(cmdLimp, "@movId", movimientoIdInserted);
                    for (int i = 0; i < idsDetallesActivos.Count; i++) AgregarParametro(cmdLimp, "@act" + i, idsDetallesActivos[i]);
                    await cmdLimp.ExecuteNonQueryAsync();
                }

                transaccion.Commit();
                return true;
            }
            catch (Exception)
            {
                transaccion.Rollback();
                throw;
            }
        }

        // =========================================================================
        // 5. OBTENER COMPLETO PARA VISTA/EDICIÓN
        // =========================================================================
        public async Task<MovimientoCompletoDTO?> GetMovimientoCompletoAsync(string serie, string numero)
        {
            var resultado = new MovimientoCompletoDTO();

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            // Cabecera
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT * FROM movimientos
                WHERE serie_documento=@serie AND numero_documento=@numero");

                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);

                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;

                resultado.Movimiento = new Movimiento
                {
                    Id = rd.GetInt32(rd.GetOrdinal("id")),
                    FechaMovimiento = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = rd["serie_documento"].ToString(),
                    NumeroDocumento = rd["numero_documento"].ToString(),
                    MotivoProductoId = rd.IsDBNull(rd.GetOrdinal("motivo_producto_id")) ? 0 : rd.GetInt32(rd.GetOrdinal("motivo_producto_id")),
                    PersonaComercialId = rd.IsDBNull(rd.GetOrdinal("persona_comercial_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("persona_comercial_id")),
                    UbicacionId = rd.IsDBNull(rd.GetOrdinal("ubicacion_id")) ? (int?)null : rd.GetInt32(rd.GetOrdinal("ubicacion_id")),
                    SerieGuia = rd["serie_guia"]?.ToString(),
                    NumeroGuia = rd["numero_guia"]?.ToString(),
                    Observacion = rd["observacion"]?.ToString()
                };
            }

            // Detalles
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                SELECT * FROM movimiento_detalles WHERE movimiento_id=@id");

                AgregarParametro(cmd, "@id", resultado.Movimiento.Id);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    resultado.Detalles.Add(new MovimientoDetalle
                    {
                        Id = rd.GetInt32(rd.GetOrdinal("id")),
                        MovimientoId = rd.GetInt32(rd.GetOrdinal("movimiento_id")),
                        ProductoId = rd.GetInt32(rd.GetOrdinal("producto_id")),
                        CantidadIngreso = rd.IsDBNull(rd.GetOrdinal("cantidad_ingreso")) ? 0 : rd.GetInt32(rd.GetOrdinal("cantidad_ingreso")),
                        CantidadSalida = rd.IsDBNull(rd.GetOrdinal("cantidad_salida")) ? 0 : rd.GetInt32(rd.GetOrdinal("cantidad_salida")),
                        CostoUnitario = rd.IsDBNull(rd.GetOrdinal("costo_unitario")) ? 0 : rd.GetDecimal(rd.GetOrdinal("costo_unitario"))
                    });
                }
            }

            return resultado;
        }
    }
}