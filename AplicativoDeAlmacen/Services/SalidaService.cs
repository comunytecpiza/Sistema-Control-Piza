#nullable enable

using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Motivo_y_Movimientos;
using AplicativoDeAlmacen.Models.Users;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
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
        // 1. GENERAR CORRELATIVO (BOMBA DESACTIVADA: Bloqueo Pesimista)
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

                // Aquí no usamos bloqueo pesimista porque es solo para la vista previa
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
        // 2. OBTENER MOTIVOS DE SALIDA
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

        // =========================================================================
        // 5. REGISTRAR SALIDA COMPLETA TRANSACCIONAL (NUEVO: Validado y UPSERT)
        // =========================================================================
        public async Task<bool> RegistrarSalidaCompletaAsync(
            Movimiento cabecera,
            List<VistaProductoGrid> listaProductos,
            List<VistaCodigoGrid> listaCodigos,
            int usuarioId,
            int estadoId,
            int? existingMovimientoId = null)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaccion = await dbConn.BeginTransactionAsync())
                {
                    try
                    {
                        string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                        int movimientoIdInserted = 0;

                        // 🌟 PASO 1: GENERACIÓN ANTI-COLISIÓN (Si es nuevo)
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
                        }

                        // 🌟 PASO 2: VALIDACIÓN PREVIA ESTRICTA (¿Están en almacén?)
                        // Para salida, los códigos deben estar en estado 3 (En Almacén)
                        var codigosInvalidos = new List<string>();
                        var existingCodigoIds = new HashSet<int>();

                        if (existingMovimientoId.HasValue)
                        {
                            using (var cmdExist = dbConn.CreateCommand())
                            {
                                cmdExist.Transaction = transaccion;
                                cmdExist.CommandText = QueryAdapter.FormatearConsulta("SELECT codigo_creado_id FROM movimiento_codigos WHERE movimiento_id = @movId");
                                AgregarParametro(cmdExist, "@movId", existingMovimientoId.Value);
                                using (var rdrExist = await cmdExist.ExecuteReaderAsync())
                                {
                                    while (await rdrExist.ReadAsync())
                                    {
                                        if (!rdrExist.IsDBNull(0)) existingCodigoIds.Add(rdrExist.GetInt32(0));
                                    }
                                }
                            }
                        }

                        // Validamos el estado real en BD antes de guardar
                        foreach (var cod in listaCodigos)
                        {
                            using (var cmdVal = dbConn.CreateCommand())
                            {
                                cmdVal.Transaction = transaccion;
                                cmdVal.CommandText = QueryAdapter.FormatearConsulta("SELECT estado_id, codigo FROM codigos_creados WHERE id = @id");
                                AgregarParametro(cmdVal, "@id", cod.MovCodigo.CodigoCreadoId);
                                using (var rdr = await cmdVal.ExecuteReaderAsync())
                                {
                                    if (await rdr.ReadAsync())
                                    {
                                        int estadoBd = rdr.GetInt32(0);
                                        string codigoStr = rdr.GetString(1);

                                        // Si no pertenece a esta edición y su estado no es 3, rebota.
                                        if (!existingCodigoIds.Contains(cod.MovCodigo.CodigoCreadoId) && estadoBd != 3)
                                        {
                                            codigosInvalidos.Add($"{codigoStr} (Su estado actual es {estadoBd}, se requiere 3)");
                                        }
                                    }
                                }
                            }
                        }

                        if (codigosInvalidos.Any())
                        {
                            throw new Exception("Error de validación física:\nLos siguientes códigos no están en Almacén:\n" + string.Join("\n", codigosInvalidos.Take(10)));
                        }

                        // 🌟 PASO 3: CABECERA (UPSERT)
                        if (existingMovimientoId.HasValue)
                        {
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
                                AgregarParametro(cmdUpdCab, "@id", existingMovimientoId.Value);
                                await cmdUpdCab.ExecuteNonQueryAsync();
                            }
                            movimientoIdInserted = existingMovimientoId.Value;
                        }
                        else
                        {
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

                        // 🌟 PASO 4: DETALLES (UPSERT)
                        var idsDetallesActivos = new List<int>();

                        foreach (var item in listaProductos)
                        {
                            int idDetalle = 0;

                            // Revisamos si ya existía en este movimiento
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
                                // Actualizamos
                                using (var cmdUpd = dbConn.CreateCommand())
                                {
                                    cmdUpd.Transaction = transaccion;
                                    cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE movimiento_detalles SET cantidad_salida = @cant, costo_unitario = @costo WHERE id = @detId");
                                    AgregarParametro(cmdUpd, "@cant", item.Cantidad);
                                    AgregarParametro(cmdUpd, "@costo", item.Detalle?.CostoUnitario ?? 0);
                                    AgregarParametro(cmdUpd, "@detId", idDetalle);
                                    await cmdUpd.ExecuteNonQueryAsync();
                                }

                                // Limpiamos códigos anteriores para rehacer la relación
                                using (var cmdClean = dbConn.CreateCommand())
                                {
                                    cmdClean.Transaction = transaccion;
                                    cmdClean.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM movimiento_codigos WHERE movimiento_detalle_id = @detId");
                                    AgregarParametro(cmdClean, "@detId", idDetalle);
                                    await cmdClean.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                // Insertamos nuevo detalle
                                string queryDetalle = $@"
                                INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                                VALUES (@movId, @prodId, 0, @cant, @costo, GETDATE());
                                {selectId}";
                                using (var cmdDet = dbConn.CreateCommand())
                                {
                                    cmdDet.Transaction = transaccion;
                                    cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                                    AgregarParametro(cmdDet, "@movId", movimientoIdInserted);
                                    AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                                    AgregarParametro(cmdDet, "@cant", item.Cantidad);
                                    AgregarParametro(cmdDet, "@costo", item.Detalle?.CostoUnitario ?? 0);
                                    idDetalle = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                                }
                            }

                            idsDetallesActivos.Add(idDetalle);

                            // Insertar relación de Códigos y actualizar Estado
                            var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId);
                            foreach (var cod in codigosProd)
                            {
                                using (var cmdCod = dbConn.CreateCommand())
                                {
                                    cmdCod.Transaction = transaccion;
                                    cmdCod.CommandText = QueryAdapter.FormatearConsulta(@"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) VALUES (@movId, @detId, @codId, 0, 1, GETDATE());");
                                    AgregarParametro(cmdCod, "@movId", movimientoIdInserted);
                                    AgregarParametro(cmdCod, "@detId", idDetalle);
                                    AgregarParametro(cmdCod, "@codId", cod.MovCodigo.CodigoCreadoId);
                                    await cmdCod.ExecuteNonQueryAsync();
                                }

                                // Si el código no estaba asociado, actualizar a estado 4 (Salida)
                                if (!existingCodigoIds.Contains(cod.MovCodigo.CodigoCreadoId))
                                {
                                    using (var cmdUpdSt = dbConn.CreateCommand())
                                    {
                                        cmdUpdSt.Transaction = transaccion;
                                        cmdUpdSt.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 4 WHERE id = @id");
                                        AgregarParametro(cmdUpdSt, "@id", cod.MovCodigo.CodigoCreadoId);
                                        await cmdUpdSt.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                        }

                        // 🌟 PASO 5: ELIMINAR HUÉRFANOS (Productos eliminados de la grilla al editar)
                        if (existingMovimientoId.HasValue && idsDetallesActivos.Any())
                        {
                            var paramNames = new List<string>();
                            for (int i = 0; i < idsDetallesActivos.Count; i++) paramNames.Add("@act" + i);

                            string qLimpiar = $@"
                                DELETE FROM movimiento_codigos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)}));
                                DELETE FROM movimiento_detalles WHERE movimiento_id = @movId AND id NOT IN ({string.Join(",", paramNames)});
                            ";

                            using (var cmdLimp = dbConn.CreateCommand())
                            {
                                cmdLimp.Transaction = transaccion;
                                cmdLimp.CommandText = QueryAdapter.FormatearConsulta(qLimpiar);
                                AgregarParametro(cmdLimp, "@movId", movimientoIdInserted);
                                for (int i = 0; i < idsDetallesActivos.Count; i++) AgregarParametro(cmdLimp, "@act" + i, idsDetallesActivos[i]);
                                await cmdLimp.ExecuteNonQueryAsync();
                            }
                        }

                        await transaccion.CommitAsync();
                        return true;
                    }
                    catch (Exception)
                    {
                        await transaccion.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        // =========================================================================
        // 6. MÉTODO RÁPIDO PARA VALIDAR UN CÓDIGO ESCANEADO CON PISTOLA
        // (Similar al de facturación, pero para salidas)
        // =========================================================================
        public async Task<CodigoCreado?> ObtenerCodigoLimpiadoAsync(string codigoBruto)
        {
            if (string.IsNullOrWhiteSpace(codigoBruto)) return null;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string codigoLimpio = codigoBruto.Replace("'", "-").Trim();

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
                            // Usamos el RegistroCodigoId temporalmente para pasar el ProductoId a la vista
                            RegistroCodigoId = reader.GetInt32(3)
                        };
                    }
                }
            }
            return null;
        }

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
            SELECT *
            FROM movimientos
            WHERE serie_documento=@serie
            AND numero_documento=@numero");

                AgregarParametro(cmd, "@serie", serie);
                AgregarParametro(cmd, "@numero", numero);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                resultado.Movimiento = new Movimiento
                {
                    Id = rd.GetInt32(rd.GetOrdinal("id")),
                    FechaMovimiento = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal("fecha_movimiento"))),
                    SerieDocumento = rd["serie_documento"].ToString(),
                    NumeroDocumento = rd["numero_documento"].ToString(),
                    MotivoProductoId = rd.IsDBNull(rd.GetOrdinal("motivo_producto_id"))
                    ? 0
                    : rd.GetInt32(rd.GetOrdinal("motivo_producto_id")),

                                    PersonaComercialId = rd.IsDBNull(rd.GetOrdinal("persona_comercial_id"))
                    ? (int?)null
                    : rd.GetInt32(rd.GetOrdinal("persona_comercial_id")),

                                    UbicacionId = rd.IsDBNull(rd.GetOrdinal("ubicacion_id"))
                    ? (int?)null
                    : rd.GetInt32(rd.GetOrdinal("ubicacion_id")),
                    SerieGuia = rd["serie_guia"]?.ToString(),
                    NumeroGuia = rd["numero_guia"]?.ToString(),
                    Observacion = rd["observacion"]?.ToString()
                };
            }

            // Detalles
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
            SELECT *
            FROM movimiento_detalles
            WHERE movimiento_id=@id");

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