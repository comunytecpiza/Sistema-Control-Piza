using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models;

namespace AplicativoDeAlmacen.Services.facturaciòn
{
    public class FacturacionService
    {
        private readonly DatabaseConnection _database;

        public FacturacionService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<int> GuardarComprobanteAsync(FacturacionCabecera cabecera, int serieId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaction = dbConn.BeginTransaction();

            try
            {
                // 1. INSERTAR CABECERA
                string queryCabecera = @"
                INSERT INTO facturacion_cabecera 
                (tipo_documento, serie_documento, numero_documento, fecha_emision, punto_venta_id, 
                 comprador_id, institucion_id, observacion, total_gravado, total_inafecto, 
                 total_exonerado, total_igv, importe_total, porcentaje_igv, fecha_registro, usuario_id, estado_registro)
                VALUES 
                (@TipoDoc, @SerieDoc, @NumDoc, @FecEmi, @PtoVentaId, 
                 @CompradorId, @InstId, @Obs, @TotGrav, @TotIna, 
                 @TotExo, @TotIgv, @ImpTot, @PorcIgv, GETDATE(), @UsuId, @EstReg);
                SELECT SCOPE_IDENTITY();";

                int nuevaCabeceraId;
                using (var cmdCabecera = dbConn.CreateCommand())
                {
                    cmdCabecera.Transaction = transaction;
                    cmdCabecera.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);

                    AgregarParametro(cmdCabecera, "@TipoDoc", cabecera.TipoDocumento);
                    AgregarParametro(cmdCabecera, "@SerieDoc", cabecera.SerieDocumento);
                    AgregarParametro(cmdCabecera, "@NumDoc", cabecera.NumeroDocumento);
                    AgregarParametro(cmdCabecera, "@FecEmi", cabecera.FechaEmision);
                    AgregarParametro(cmdCabecera, "@PtoVentaId", cabecera.PuntoVentaId);
                    AgregarParametro(cmdCabecera, "@CompradorId", (object?)cabecera.CompradorId ?? DBNull.Value);
                    AgregarParametro(cmdCabecera, "@InstId", (object?)cabecera.InstitucionId ?? DBNull.Value);
                    AgregarParametro(cmdCabecera, "@Obs", (object?)cabecera.Observacion ?? DBNull.Value);
                    AgregarParametro(cmdCabecera, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCabecera, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCabecera, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCabecera, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCabecera, "@ImpTot", cabecera.ImporteTotal);
                    AgregarParametro(cmdCabecera, "@PorcIgv", cabecera.PorcentajeIgv);
                    AgregarParametro(cmdCabecera, "@UsuId", cabecera.UsuarioId);
                    AgregarParametro(cmdCabecera, "@EstReg", cabecera.EstadoRegistro ? 1 : 0);

                    nuevaCabeceraId = Convert.ToInt32(await cmdCabecera.ExecuteScalarAsync());
                }

                // 2. INSERTAR DETALLES Y CÓDIGOS
                string queryDetalle = @"
                    INSERT INTO facturacion_detalle 
                    (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
                     valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total)
                    VALUES 
                    (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
                     @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot);
                    SELECT SCOPE_IDENTITY();";

                string queryCodigo = @"
                    INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id)
                    VALUES (@DetId, @CodCreadoId)";

                foreach (var detalle in cabecera.Detalles)
                {
                    int nuevoDetalleId;
                    using (var cmdDetalle = dbConn.CreateCommand())
                    {
                        cmdDetalle.Transaction = transaction;
                        cmdDetalle.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                        AgregarParametro(cmdDetalle, "@CabId", nuevaCabeceraId);
                        AgregarParametro(cmdDetalle, "@MovId", detalle.MovimientoId);
                        AgregarParametro(cmdDetalle, "@ProdId", detalle.ProductoId);
                        AgregarParametro(cmdDetalle, "@NumLinea", detalle.NumeroLinea);
                        AgregarParametro(cmdDetalle, "@Cant", detalle.Cantidad);
                        AgregarParametro(cmdDetalle, "@PreUnit", detalle.PrecioUnitario);
                        AgregarParametro(cmdDetalle, "@ValGrav", detalle.ValorGravado);
                        AgregarParametro(cmdDetalle, "@ValIna", detalle.ValorInafecto);
                        AgregarParametro(cmdDetalle, "@ValExo", detalle.ValorExonerado);
                        AgregarParametro(cmdDetalle, "@ValIgv", detalle.ValorIgv);
                        AgregarParametro(cmdDetalle, "@ImpTot", detalle.ImporteTotal);

                        nuevoDetalleId = Convert.ToInt32(await cmdDetalle.ExecuteScalarAsync());
                    }

                    // 🌟 BARRERA FÍSICA FINAL AL GRABAR
                    foreach (var codigo in detalle.Codigos)
                    {
                        string queryValidacion = "SELECT estado_id, codigo FROM codigos_creados WHERE id = @CodId";
                        using (var cmdVal = dbConn.CreateCommand())
                        {
                            cmdVal.Transaction = transaction;
                            cmdVal.CommandText = QueryAdapter.FormatearConsulta(queryValidacion);
                            AgregarParametro(cmdVal, "@CodId", codigo.CodigoCreadoId);

                            using (var reader = await cmdVal.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    int estadoActual = Convert.ToInt32(reader["estado_id"]);
                                    string codigoTexto = reader["codigo"].ToString();

                                    if (estadoActual != 4)
                                    {
                                        throw new Exception($"El código '{codigoTexto}' no puede ser guardado porque su estado interno ({estadoActual}) no es SALIDA (4).");
                                    }
                                }
                            }
                        }

                        using (var cmdCod = dbConn.CreateCommand())
                        {
                            cmdCod.Transaction = transaction;
                            cmdCod.CommandText = QueryAdapter.FormatearConsulta(queryCodigo);
                            AgregarParametro(cmdCod, "@DetId", nuevoDetalleId);
                            AgregarParametro(cmdCod, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdCod.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 3. ACTUALIZAR CORRELATIVO
                string campoUpdate = cabecera.TipoDocumento == "01" ? "num_fact = num_fact + 1" :
                                     cabecera.TipoDocumento == "02" ? "num_bole = num_bole + 1" :
                                     cabecera.TipoDocumento == "03" ? "num_reci = num_reci + 1" : "";

                if (!string.IsNullOrEmpty(campoUpdate))
                {
                    string queryCorrelativo = $"UPDATE series_documentos SET {campoUpdate} WHERE id = @SerieId";
                    using (var cmdCorr = dbConn.CreateCommand())
                    {
                        cmdCorr.Transaction = transaction;
                        cmdCorr.CommandText = QueryAdapter.FormatearConsulta(queryCorrelativo);
                        AgregarParametro(cmdCorr, "@SerieId", serieId);
                        await cmdCorr.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                return nuevaCabeceraId;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception("Error al guardar el comprobante: " + ex.Message);
            }
        }

        public async Task ActualizarComprobanteAsync(FacturacionCabecera cabecera)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaction = dbConn.BeginTransaction();

            try
            {
                // 1. ACTUALIZAR CABECERA
                string queryUpdateCab = @"
                UPDATE facturacion_cabecera SET 
                    tipo_documento = @TipoDoc, 
                    fecha_emision = @FecEmi, 
                    comprador_id = @CompradorId, 
                    institucion_id = @InstId, 
                    observacion = @Obs, 
                    total_gravado = @TotGrav, 
                    total_inafecto = @TotIna, 
                    total_exonerado = @TotExo, 
                    total_igv = @TotIgv, 
                    importe_total = @ImpTot
                WHERE id = @CabId";

                using (var cmdCab = dbConn.CreateCommand())
                {
                    cmdCab.Transaction = transaction;
                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryUpdateCab);

                    AgregarParametro(cmdCab, "@CabId", cabecera.Id);
                    AgregarParametro(cmdCab, "@TipoDoc", cabecera.TipoDocumento);
                    AgregarParametro(cmdCab, "@FecEmi", cabecera.FechaEmision);
                    AgregarParametro(cmdCab, "@CompradorId", (object?)cabecera.CompradorId ?? DBNull.Value);
                    AgregarParametro(cmdCab, "@InstId", (object?)cabecera.InstitucionId ?? DBNull.Value);
                    AgregarParametro(cmdCab, "@Obs", (object?)cabecera.Observacion ?? DBNull.Value);
                    AgregarParametro(cmdCab, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCab, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCab, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCab, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCab, "@ImpTot", cabecera.ImporteTotal);

                    await cmdCab.ExecuteNonQueryAsync();
                }

                // 2. ELIMINAR DETALLES ACTUALES
                string queryDelCod = "DELETE FROM facturacion_detalle_codigos WHERE facturacion_detalle_id IN (SELECT id FROM facturacion_detalle WHERE facturacion_cabecera_id = @CabId)";
                string queryDelDet = "DELETE FROM facturacion_detalle WHERE facturacion_cabecera_id = @CabId";

                using (var cmdDel = dbConn.CreateCommand())
                {
                    cmdDel.Transaction = transaction;
                    cmdDel.CommandText = QueryAdapter.FormatearConsulta(queryDelCod);
                    AgregarParametro(cmdDel, "@CabId", cabecera.Id);
                    await cmdDel.ExecuteNonQueryAsync();

                    cmdDel.CommandText = QueryAdapter.FormatearConsulta(queryDelDet);
                    await cmdDel.ExecuteNonQueryAsync();
                }

                // 3. RE-INSERTAR DETALLES
                string queryDetalle = @"
                INSERT INTO facturacion_detalle 
                (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
                 valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total)
                VALUES 
                (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
                 @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot);
                SELECT SCOPE_IDENTITY();";

                string queryCodigo = "INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id) VALUES (@DetId, @CodCreadoId)";

                foreach (var detalle in cabecera.Detalles)
                {
                    int nuevoDetalleId;
                    using (var cmdDet = dbConn.CreateCommand())
                    {
                        cmdDet.Transaction = transaction;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                        AgregarParametro(cmdDet, "@CabId", cabecera.Id);
                        AgregarParametro(cmdDet, "@MovId", detalle.MovimientoId);
                        AgregarParametro(cmdDet, "@ProdId", detalle.ProductoId);
                        AgregarParametro(cmdDet, "@NumLinea", detalle.NumeroLinea);
                        AgregarParametro(cmdDet, "@Cant", detalle.Cantidad);
                        AgregarParametro(cmdDet, "@PreUnit", detalle.PrecioUnitario);
                        AgregarParametro(cmdDet, "@ValGrav", detalle.ValorGravado);
                        AgregarParametro(cmdDet, "@ValIna", detalle.ValorInafecto);
                        AgregarParametro(cmdDet, "@ValExo", detalle.ValorExonerado);
                        AgregarParametro(cmdDet, "@ValIgv", detalle.ValorIgv);
                        AgregarParametro(cmdDet, "@ImpTot", detalle.ImporteTotal);

                        nuevoDetalleId = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    foreach (var codigo in detalle.Codigos)
                    {
                        string queryValidacion = "SELECT estado_id, codigo FROM codigos_creados WHERE id = @CodId";
                        using (var cmdVal = dbConn.CreateCommand())
                        {
                            cmdVal.Transaction = transaction;
                            cmdVal.CommandText = QueryAdapter.FormatearConsulta(queryValidacion);
                            AgregarParametro(cmdVal, "@CodId", codigo.CodigoCreadoId);

                            using (var reader = await cmdVal.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    int estadoActual = Convert.ToInt32(reader["estado_id"]);
                                    string codigoTexto = reader["codigo"].ToString();

                                    if (estadoActual != 4)
                                    {
                                        throw new Exception($"Error de integridad. El código '{codigoTexto}' fue modificado a estado {estadoActual}. Solo se permiten códigos en SALIDA (4). Edición cancelada.");
                                    }
                                }
                            }
                        }

                        using (var cmdCod = dbConn.CreateCommand())
                        {
                            cmdCod.Transaction = transaction;
                            cmdCod.CommandText = QueryAdapter.FormatearConsulta(queryCodigo);
                            AgregarParametro(cmdCod, "@DetId", nuevoDetalleId);
                            AgregarParametro(cmdCod, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdCod.ExecuteNonQueryAsync();
                        }
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception("Error al actualizar el comprobante: " + ex.Message);
            }
        }

        public async Task<ValidacionCodigoResult> ValidarCodigoParaVentaAsync(int productoId, string codigoDigitado)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 1. BUSCAR EL CÓDIGO 
                string queryExistencia = @"
                    SELECT cc.id, cc.codigo, cc.estado_id 
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    WHERE cc.codigo LIKE @Codigo AND rc.producto_id = @ProductoId";

                int codigoCreadoId = 0;
                string codigoCompleto = "";
                int estadoInterno = 0;

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryExistencia);
                    AgregarParametro(cmd, "@Codigo", "%" + codigoDigitado);
                    AgregarParametro(cmd, "@ProductoId", productoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        int contador = 0;
                        while (await reader.ReadAsync())
                        {
                            codigoCreadoId = Convert.ToInt32(reader["id"]);
                            codigoCompleto = reader["codigo"].ToString();
                            estadoInterno = Convert.ToInt32(reader["estado_id"]);
                            contador++;
                        }

                        if (contador == 0)
                            throw new Exception($"El código '{codigoDigitado}' no existe para el producto seleccionado.");

                        if (contador > 1)
                            throw new Exception($"Hay múltiples códigos que terminan en '{codigoDigitado}'. Por favor digite más números para ser específico.");

                        // NOTA: Ya no validamos el estado = 4 aquí todavía. Primero vemos Kardex.
                    }
                }

                // 2. VERIFICAR DOBLE VENTA
                string queryVendido = @"
                SELECT TOP 1 1 
                FROM facturacion_detalle_codigos fdc
                INNER JOIN facturacion_detalle fd ON fdc.facturacion_detalle_id = fd.id
                INNER JOIN facturacion_cabecera fc ON fd.facturacion_cabecera_id = fc.id
                WHERE fdc.codigo_creado_id = @CodigoId AND fc.estado_registro = 1";
                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryVendido);
                    AgregarParametro(cmd, "@CodigoId", codigoCreadoId);

                    if (await cmd.ExecuteScalarAsync() != null)
                    {
                        throw new Exception($"El código '{codigoCompleto}' ya ha sido facturado en un comprobante activo.");
                    }
                }

                // 3. VERIFICAR ÚLTIMO MOVIMIENTO KARDEX (LA LÍNEA DE TIEMPO)
                string queryUltimoMovimiento = @"
                SELECT TOP 1 mp.tipo_movimiento, m.id as movimiento_id 
                FROM movimiento_codigos mc
                INNER JOIN movimiento_detalles md ON mc.movimiento_detalle_id = md.id
                INNER JOIN movimientos m ON md.movimiento_id = m.id
                INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
                WHERE mc.codigo_creado_id = @CodigoId
                ORDER BY m.fecha_movimiento DESC, m.created_at DESC";

                int movimientoIdCapturado = 0;

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryUltimoMovimiento);
                    AgregarParametro(cmd, "@CodigoId", codigoCreadoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string tipoUltimoMovimiento = reader["tipo_movimiento"].ToString().ToLower();

                            // 🌟 CONDICIÓN 1: Si no es salida, le avisamos que el último movimiento no lo permite
                            if (tipoUltimoMovimiento != "salida")
                            {
                                throw new Exception($"El código '{codigoCompleto}' no está disponible. Su último movimiento registrado fue una '{tipoUltimoMovimiento.ToUpper()}'. Debe registrar una SALIDA primero.");
                            }

                            movimientoIdCapturado = Convert.ToInt32(reader["movimiento_id"]);
                        }
                        else
                        {
                            throw new Exception($"El código '{codigoCompleto}' no tiene ningún movimiento registrado en Kardex. No ha salido de almacén.");
                        }
                    }
                }

                // 🌟 CONDICIÓN 2: El Kardex dice SALIDA, ¡pero el estado interno no es 4!
                if (estadoInterno != 4)
                {
                    throw new Exception($"¡ERROR INTERNO! El último movimiento en Kardex indica SALIDA, pero el estado interno del código '{codigoCompleto}' es ({estadoInterno}). Revise el historial de edición de este código.");
                }

                return new ValidacionCodigoResult
                {
                    Id = codigoCreadoId,
                    CodigoCompleto = codigoCompleto,
                    MovimientoId = movimientoIdCapturado
                };
            }
        }

        public async Task<LectoraResultDTO> ProcesarCodigoPorLectoraAsync(string codigoEscaneado)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string codigoLimpio = codigoEscaneado.Replace("'", "-").Trim();

                string queryPrincipal = @"
            SELECT TOP 1 
                p.id as producto_id, p.descripcion, p.precio_unitario,
                cc.id as codigo_creado_id, cc.codigo as codigo_string, cc.estado_id,
                m.id as movimiento_id, mp.tipo_movimiento
            FROM codigos_creados cc
            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
            INNER JOIN productos p ON rc.producto_id = p.id
            INNER JOIN movimiento_codigos mc ON mc.codigo_creado_id = cc.id
            INNER JOIN movimiento_detalles md ON mc.movimiento_detalle_id = md.id
            INNER JOIN movimientos m ON md.movimiento_id = m.id
            INNER JOIN motivo_productos mp ON m.motivo_producto_id = mp.id
            WHERE REPLACE(cc.codigo, '''', '-') = @CodigoLimpio
            ORDER BY m.fecha_movimiento DESC, m.created_at DESC";

                LectoraResultDTO resultado = null;
                string tipoMovimiento = "";
                int estadoInterno = 0;

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryPrincipal);
                    AgregarParametro(cmd, "@CodigoLimpio", codigoLimpio);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            resultado = new LectoraResultDTO
                            {
                                ProductoId = Convert.ToInt32(reader["producto_id"]),
                                DescripcionProducto = reader["descripcion"].ToString(),
                                PrecioUnitario = reader["precio_unitario"] != DBNull.Value ? Convert.ToDecimal(reader["precio_unitario"]) : 0,
                                UnidadMedida = "UNIDAD",
                                CodigoCreadoId = Convert.ToInt32(reader["codigo_creado_id"]),
                                CodigoCompleto = reader["codigo_string"].ToString(),
                                MovimientoId = Convert.ToInt32(reader["movimiento_id"])
                            };
                            tipoMovimiento = reader["tipo_movimiento"].ToString().ToLower();
                            estadoInterno = Convert.ToInt32(reader["estado_id"]);
                        }
                    }
                }

                if (resultado == null)
                    throw new Exception($"El código '{codigoEscaneado}' no existe en el sistema o no tiene movimientos en Kardex.");

                // 🌟 VALIDACIÓN EN ORDEN CORRECTO
                // 1. Primero evaluamos la línea de tiempo (Kardex)
                if (tipoMovimiento != "salida")
                    throw new Exception($"El código '{resultado.CodigoCompleto}' no está disponible. Su último movimiento en Kardex fue '{tipoMovimiento.ToUpper()}'.");

                // 2. Si el Kardex dice salida, evaluamos que el estado no esté corrompido
                if (estadoInterno != 4)
                    throw new Exception($"¡ATENCIÓN! El Kardex indica SALIDA, pero el código '{resultado.CodigoCompleto}' tiene un estado interno corrupto ({estadoInterno}). Solicite revisión.");

                // 3. Verificamos si no se vendió
                string queryVendido = "SELECT TOP 1 1 FROM facturacion_detalle_codigos fdc INNER JOIN facturacion_cabecera fc ON fdc.facturacion_detalle_id = fc.id WHERE fdc.codigo_creado_id = @CodigoId AND fc.estado_registro = 1";
                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryVendido);
                    AgregarParametro(cmd, "@CodigoId", resultado.CodigoCreadoId);
                    if (await cmd.ExecuteScalarAsync() != null)
                        throw new Exception($"El código '{resultado.CodigoCompleto}' ya ha sido facturado en un comprobante activo.");
                }

                return resultado;
            }
        }

        public async Task<FacturacionCabecera> ObtenerComprobantePorNumeroAsync(string serie, string numero)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            FacturacionCabecera cabecera = null;

            string queryCab = "SELECT * FROM facturacion_cabecera WHERE serie_documento = @Serie AND numero_documento = @Numero";
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryCab);
                AgregarParametro(cmd, "@Serie", serie);
                AgregarParametro(cmd, "@Numero", numero);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        cabecera = new FacturacionCabecera
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            TipoDocumento = reader["tipo_documento"].ToString(),
                            SerieDocumento = reader["serie_documento"].ToString(),
                            NumeroDocumento = reader["numero_documento"].ToString(),
                            FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
                            PuntoVentaId = Convert.ToInt32(reader["punto_venta_id"]),
                            CompradorId = reader["comprador_id"] != DBNull.Value ? Convert.ToInt32(reader["comprador_id"]) : null,
                            InstitucionId = reader["institucion_id"] != DBNull.Value ? Convert.ToInt32(reader["institucion_id"]) : null,
                            Observacion = reader["observacion"] != DBNull.Value ? reader["observacion"].ToString() : string.Empty,
                            TotalGravado = Convert.ToDecimal(reader["total_gravado"]),
                            TotalInafecto = Convert.ToDecimal(reader["total_inafecto"]),
                            TotalExonerado = Convert.ToDecimal(reader["total_exonerado"]),
                            TotalIgv = Convert.ToDecimal(reader["total_igv"]),
                            ImporteTotal = Convert.ToDecimal(reader["importe_total"]),
                            PorcentajeIgv = Convert.ToDecimal(reader["porcentaje_igv"]),
                            FechaRegistro = Convert.ToDateTime(reader["fecha_registro"]),
                            UsuarioId = Convert.ToInt32(reader["usuario_id"]),
                            EstadoRegistro = Convert.ToBoolean(reader["estado_registro"])
                        };
                    }
                }
            }

            if (cabecera == null) return null;

            string queryDet = "SELECT * FROM facturacion_detalle WHERE facturacion_cabecera_id = @CabId";
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryDet);
                AgregarParametro(cmd, "@CabId", cabecera.Id);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var detalle = new FacturacionDetalle
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            FacturacionCabeceraId = Convert.ToInt32(reader["facturacion_cabecera_id"]),
                            MovimientoId = Convert.ToInt32(reader["movimiento_id"]),
                            ProductoId = Convert.ToInt32(reader["producto_id"]),
                            NumeroLinea = Convert.ToInt32(reader["numero_linea"]),
                            Cantidad = Convert.ToDecimal(reader["cantidad"]),
                            PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                            ValorGravado = Convert.ToDecimal(reader["valor_gravado"]),
                            ValorInafecto = Convert.ToDecimal(reader["valor_inafecto"]),
                            ValorExonerado = Convert.ToDecimal(reader["valor_exonerado"]),
                            ValorIgv = Convert.ToDecimal(reader["valor_igv"]),
                            ImporteTotal = Convert.ToDecimal(reader["importe_total"])
                        };
                        cabecera.Detalles.Add(detalle);
                    }
                }
            }

            foreach (var det in cabecera.Detalles)
            {
                string queryCod = @"
                    SELECT dc.id, dc.codigo_creado_id, cc.codigo 
                    FROM facturacion_detalle_codigos dc
                    INNER JOIN codigos_creados cc ON dc.codigo_creado_id = cc.id
                    WHERE dc.facturacion_detalle_id = @DetId";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryCod);
                    AgregarParametro(cmd, "@DetId", det.Id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            det.Codigos.Add(new FacturacionDetalleCodigos
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                FacturacionDetalleId = det.Id,
                                CodigoCreadoId = Convert.ToInt32(reader["codigo_creado_id"]),
                                CodigoTexto = reader["codigo"].ToString()
                            });
                        }
                    }
                }
            }

            return cabecera;
        }

        public async Task AnularComprobanteAsync(int idCabecera)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            try
            {
                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE facturacion_cabecera SET estado_registro = 0 WHERE id = @id");
                    AgregarParametro(cmd, "@id", idCabecera);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("No se pudo anular el comprobante. Error: " + ex.Message);
            }
        }
    }
}