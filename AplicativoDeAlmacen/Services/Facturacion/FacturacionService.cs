#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Models.Documentos;
using System.Linq;

namespace AplicativoDeAlmacen.Services.facturaciòn
{
    public class FacturacionService
    {
        private readonly DatabaseConnection _database;

        public FacturacionService()
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
        // 1. GUARDAR NUEVO COMPROBANTE
        // =========================================================================
        public async Task<int> GuardarComprobanteAsync(FacturacionCabecera cabecera, int serieId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaction = dbConn.BeginTransaction();

            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                // 1. INSERTAR CABECERA
                string queryCabecera = $@"
                    INSERT INTO facturacion_cabecera 
                    (empresa_id, tipo_documento, serie_documento, numero_documento, fecha_emision, punto_venta_id, almacen_id,
                     comprador_id, institucion_id, observacion, total_gravado, total_inafecto, 
                     total_exonerado, total_igv, importe_total, monto_delivery, moneda_id, condicion_pago_id, 
                     porcentaje_igv, fecha_registro, usuario_id, estado_registro)
                    VALUES 
                    (@EmpresaId, @TipoDoc, @SerieDoc, @NumDoc, @FecEmi, @PtoVentaId, @AlmId,
                     @CompradorId, @InstId, @Obs, @TotGrav, @TotIna, 
                     @TotExo, @TotIgv, @ImpTot, @MontoDelivery, @MonedaId, @CondPagoId, 
                     @PorcIgv, {nowFunc}, @UsuId, 1);
                    {selectId}";

                int nuevaCabeceraId;
                using (var cmdCabecera = dbConn.CreateCommand())
                {
                    cmdCabecera.Transaction = transaction;
                    cmdCabecera.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);

                    AgregarParametro(cmdCabecera, "@EmpresaId", cabecera.EmpresaId);
                    AgregarParametro(cmdCabecera, "@TipoDoc", cabecera.TipoDocumento);
                    AgregarParametro(cmdCabecera, "@SerieDoc", cabecera.SerieDocumento);
                    AgregarParametro(cmdCabecera, "@NumDoc", cabecera.NumeroDocumento);
                    AgregarParametro(cmdCabecera, "@FecEmi", cabecera.FechaEmision);
                    AgregarParametro(cmdCabecera, "@PtoVentaId", cabecera.PuntoVentaId);
                    AgregarParametro(cmdCabecera, "@AlmId", cabecera.AlmacenId ?? 1);
                    AgregarParametro(cmdCabecera, "@CompradorId", cabecera.CompradorId);
                    AgregarParametro(cmdCabecera, "@InstId", cabecera.InstitucionId);
                    AgregarParametro(cmdCabecera, "@Obs", cabecera.Observacion);
                    AgregarParametro(cmdCabecera, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCabecera, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCabecera, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCabecera, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCabecera, "@ImpTot", cabecera.ImporteTotal);
                    AgregarParametro(cmdCabecera, "@MontoDelivery", cabecera.MontoDelivery);
                    AgregarParametro(cmdCabecera, "@MonedaId", cabecera.MonedaId > 0 ? cabecera.MonedaId : 1);
                    AgregarParametro(cmdCabecera, "@CondPagoId", cabecera.CondicionPagoId ?? 1);
                    AgregarParametro(cmdCabecera, "@PorcIgv", cabecera.PorcentajeIgv);
                    AgregarParametro(cmdCabecera, "@UsuId", cabecera.UsuarioId);

                    nuevaCabeceraId = Convert.ToInt32(await cmdCabecera.ExecuteScalarAsync());
                }

                cabecera.Id = nuevaCabeceraId;

                // 2. INSERTAR PAGOS (SIN NULLs)
                if (cabecera.Pagos != null && cabecera.Pagos.Count > 0)
                {
                    string queryPago = @"
                        INSERT INTO facturacion_pagos_detalle 
                        (facturacion_cabecera_id, medio_pago_id, monto, numero_operacion, observacion)
                        VALUES 
                        (@CabId, @MedioId, @Monto, @NumOp, @Obs);";

                    foreach (var pago in cabecera.Pagos)
                    {
                        using var cmdPago = dbConn.CreateCommand();
                        cmdPago.Transaction = transaction;
                        cmdPago.CommandText = QueryAdapter.FormatearConsulta(queryPago);
                        AgregarParametro(cmdPago, "@CabId", nuevaCabeceraId);
                        AgregarParametro(cmdPago, "@MedioId", pago.MedioPagoId > 0 ? pago.MedioPagoId : 1);
                        AgregarParametro(cmdPago, "@Monto", pago.Monto);
                        AgregarParametro(cmdPago, "@NumOp", string.IsNullOrWhiteSpace(pago.NumeroOperacion) ? string.Empty : pago.NumeroOperacion.Trim());
                        AgregarParametro(cmdPago, "@Obs", "VENTA MANUAL");
                        await cmdPago.ExecuteNonQueryAsync();
                    }
                }

                // 3. INSERTAR DETALLES Y CÓDIGOS
                // 🌟 E. RE-INSERTAR DETALLES Y SUS CÓDIGOS
                string queryDetalle = $@"
    INSERT INTO facturacion_detalle 
    (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
     valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total)
    VALUES 
    (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
     @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot);
    {selectId}";

                string queryCodigo = "INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id) VALUES (@DetId, @CodCreadoId);";
                string queryUpdKardex = "UPDATE codigos_creados SET estado_id = 4 WHERE id = @CodCreadoId;";

                int linea = 1;
                foreach (var detalle in cabecera.Detalles)
                {
                    int nuevoDetalleId;
                    using (var cmdDet = dbConn.CreateCommand())
                    {
                        cmdDet.Transaction = transaction;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                        AgregarParametro(cmdDet, "@CabId", cabecera.Id);
                        AgregarParametro(cmdDet, "@MovId", (detalle.MovimientoId > 0) ? (object)detalle.MovimientoId : DBNull.Value);
                        AgregarParametro(cmdDet, "@ProdId", detalle.ProductoId);
                        AgregarParametro(cmdDet, "@NumLinea", linea++);
                        AgregarParametro(cmdDet, "@Cant", detalle.Cantidad);
                        AgregarParametro(cmdDet, "@PreUnit", detalle.PrecioUnitario);
                        AgregarParametro(cmdDet, "@ValGrav", detalle.ValorGravado);
                        AgregarParametro(cmdDet, "@ValIna", detalle.ValorInafecto);
                        AgregarParametro(cmdDet, "@ValExo", detalle.ValorExonerado);
                        AgregarParametro(cmdDet, "@ValIgv", detalle.ValorIgv);
                        AgregarParametro(cmdDet, "@ImpTot", detalle.ImporteTotal);

                        nuevoDetalleId = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    if (detalle.Codigos != null && detalle.Codigos.Count > 0)
                    {
                        foreach (var codigo in detalle.Codigos)
                        {
                            using var cmdCod = dbConn.CreateCommand();
                            cmdCod.Transaction = transaction;
                            cmdCod.CommandText = QueryAdapter.FormatearConsulta(queryCodigo);
                            AgregarParametro(cmdCod, "@DetId", nuevoDetalleId);
                            AgregarParametro(cmdCod, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdCod.ExecuteNonQueryAsync();

                            using var cmdKardex = dbConn.CreateCommand();
                            cmdKardex.Transaction = transaction;
                            cmdKardex.CommandText = QueryAdapter.FormatearConsulta(queryUpdKardex);
                            AgregarParametro(cmdKardex, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdKardex.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 4. ACTUALIZAR CORRELATIVO
                string campoUpdate = cabecera.TipoDocumento switch
                {
                    "01" => "num_fact = num_fact + 1",
                    "02" or "03" => "num_bole = num_bole + 1",
                    _ => "num_reci = num_reci + 1"
                };

                if (!string.IsNullOrEmpty(campoUpdate))
                {
                    string queryCorrelativo = $"UPDATE series_documentos SET {campoUpdate} WHERE id = @SerieId";
                    using var cmdCorr = dbConn.CreateCommand();
                    cmdCorr.Transaction = transaction;
                    cmdCorr.CommandText = QueryAdapter.FormatearConsulta(queryCorrelativo);
                    AgregarParametro(cmdCorr, "@SerieId", serieId);
                    await cmdCorr.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                return nuevaCabeceraId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception("Error al guardar el comprobante: " + ex.Message, ex);
            }
        }

        // =========================================================================
        // 2. ACTUALIZAR COMPROBANTE CON AUDITORÍA COMPLETA
        // =========================================================================
        public async Task ActualizarComprobanteAsync(FacturacionCabecera cabecera, int usuarioModificadorId, string motivoEdicion = "EDICIÓN DE COMPROBANTE")
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaction = dbConn.BeginTransaction();

            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                // 🌟 A. CAPTURAR DATOS PREVIOS PARA LA AUDITORÍA
                decimal importePrevio = 0.00m;
                string observacionPrevia = string.Empty;

                string queryPrev = "SELECT importe_total, COALESCE(observacion, '') FROM facturacion_cabecera WHERE id = @CabId;";
                using (var cmdPrev = dbConn.CreateCommand())
                {
                    cmdPrev.Transaction = transaction;
                    cmdPrev.CommandText = QueryAdapter.FormatearConsulta(queryPrev);
                    AgregarParametro(cmdPrev, "@CabId", cabecera.Id);
                    using var rdrPrev = await cmdPrev.ExecuteReaderAsync();
                    if (await rdrPrev.ReadAsync())
                    {
                        importePrevio = rdrPrev.GetDecimal(0);
                        observacionPrevia = rdrPrev.GetString(1);
                    }
                }

                // 🌟 B. ACTUALIZAR CABECERA
                string queryUpdateCab = $@"
            UPDATE facturacion_cabecera SET 
                tipo_documento = @TipoDoc, 
                fecha_emision = @FecEmi, 
                punto_venta_id = @PtoVentaId,
                almacen_id = @AlmId,
                comprador_id = @CompradorId, 
                institucion_id = @InstId, 
                observacion = @Obs, 
                total_gravado = @TotGrav, 
                total_inafecto = @TotIna, 
                total_exonerado = @TotExo, 
                total_igv = @TotIgv, 
                importe_total = @ImpTot,
                monto_delivery = @MontoDelivery,
                usuario_update_id = @UsrUpdateId,
                updated_at = {nowFunc}
            WHERE id = @CabId";

                using (var cmdCab = dbConn.CreateCommand())
                {
                    cmdCab.Transaction = transaction;
                    cmdCab.CommandText = QueryAdapter.FormatearConsulta(queryUpdateCab);

                    AgregarParametro(cmdCab, "@CabId", cabecera.Id);
                    AgregarParametro(cmdCab, "@TipoDoc", cabecera.TipoDocumento);
                    AgregarParametro(cmdCab, "@FecEmi", cabecera.FechaEmision);
                    AgregarParametro(cmdCab, "@PtoVentaId", cabecera.PuntoVentaId);
                    AgregarParametro(cmdCab, "@AlmId", cabecera.AlmacenId ?? 1);
                    AgregarParametro(cmdCab, "@CompradorId", cabecera.CompradorId);
                    AgregarParametro(cmdCab, "@InstId", cabecera.InstitucionId);
                    AgregarParametro(cmdCab, "@Obs", cabecera.Observacion);
                    AgregarParametro(cmdCab, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCab, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCab, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCab, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCab, "@ImpTot", cabecera.ImporteTotal);
                    AgregarParametro(cmdCab, "@MontoDelivery", cabecera.MontoDelivery);
                    AgregarParametro(cmdCab, "@UsrUpdateId", usuarioModificadorId);

                    await cmdCab.ExecuteNonQueryAsync();
                }

                // 🌟 C. BORRAR Y REINSERTAR PAGOS (SIN created_at)
                string queryDelPagos = "DELETE FROM facturacion_pagos_detalle WHERE facturacion_cabecera_id = @CabId;";
                using (var cmdDelPagos = dbConn.CreateCommand())
                {
                    cmdDelPagos.Transaction = transaction;
                    cmdDelPagos.CommandText = QueryAdapter.FormatearConsulta(queryDelPagos);
                    AgregarParametro(cmdDelPagos, "@CabId", cabecera.Id);
                    await cmdDelPagos.ExecuteNonQueryAsync();
                }

                if (cabecera.Pagos != null && cabecera.Pagos.Count > 0)
                {
                    // ✅ CORREGIDO: columnas exactas sin created_at
                    string queryPago = @"
                INSERT INTO facturacion_pagos_detalle 
                (facturacion_cabecera_id, medio_pago_id, monto, numero_operacion, observacion)
                VALUES 
                (@CabId, @MedioId, @Monto, @NumOp, @Obs);";

                    foreach (var pago in cabecera.Pagos)
                    {
                        using var cmdPago = dbConn.CreateCommand();
                        cmdPago.Transaction = transaction;
                        cmdPago.CommandText = QueryAdapter.FormatearConsulta(queryPago);
                        AgregarParametro(cmdPago, "@CabId", cabecera.Id);
                        AgregarParametro(cmdPago, "@MedioId", pago.MedioPagoId > 0 ? pago.MedioPagoId : 1);
                        AgregarParametro(cmdPago, "@Monto", pago.Monto);
                        AgregarParametro(cmdPago, "@NumOp", string.IsNullOrWhiteSpace(pago.NumeroOperacion) ? string.Empty : pago.NumeroOperacion.Trim());
                        AgregarParametro(cmdPago, "@Obs", "VENTA MANUAL");
                        await cmdPago.ExecuteNonQueryAsync();
                    }
                }

                // 🌟 D. BORRAR DETALLES ANTERIORES
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

                // 🌟 E. REINSERTAR DETALLES Y CÓDIGOS (SIN created_at)
                string queryDetalle = $@"
            INSERT INTO facturacion_detalle 
            (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
             valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total)
            VALUES 
            (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
             @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot);
            {selectId}";

                string queryCodigo = "INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id) VALUES (@DetId, @CodCreadoId);";
                string queryUpdKardex = "UPDATE codigos_creados SET estado_id = 4 WHERE id = @CodCreadoId;";

                int linea = 1;
                foreach (var detalle in cabecera.Detalles)
                {
                    int nuevoDetalleId;
                    using (var cmdDet = dbConn.CreateCommand())
                    {
                        cmdDet.Transaction = transaction;
                        cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                        AgregarParametro(cmdDet, "@CabId", cabecera.Id);
                        AgregarParametro(cmdDet, "@MovId", (detalle.MovimientoId > 0) ? (object)detalle.MovimientoId : DBNull.Value);
                        AgregarParametro(cmdDet, "@ProdId", detalle.ProductoId);
                        AgregarParametro(cmdDet, "@NumLinea", linea++);
                        AgregarParametro(cmdDet, "@Cant", detalle.Cantidad);
                        AgregarParametro(cmdDet, "@PreUnit", detalle.PrecioUnitario);
                        AgregarParametro(cmdDet, "@ValGrav", detalle.ValorGravado);
                        AgregarParametro(cmdDet, "@ValIna", detalle.ValorInafecto);
                        AgregarParametro(cmdDet, "@ValExo", detalle.ValorExonerado);
                        AgregarParametro(cmdDet, "@ValIgv", detalle.ValorIgv);
                        AgregarParametro(cmdDet, "@ImpTot", detalle.ImporteTotal);

                        nuevoDetalleId = Convert.ToInt32(await cmdDet.ExecuteScalarAsync());
                    }

                    if (detalle.Codigos != null && detalle.Codigos.Count > 0)
                    {
                        foreach (var codigo in detalle.Codigos)
                        {
                            using var cmdCod = dbConn.CreateCommand();
                            cmdCod.Transaction = transaction;
                            cmdCod.CommandText = QueryAdapter.FormatearConsulta(queryCodigo);
                            AgregarParametro(cmdCod, "@DetId", nuevoDetalleId);
                            AgregarParametro(cmdCod, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdCod.ExecuteNonQueryAsync();

                            using var cmdKardex = dbConn.CreateCommand();
                            cmdKardex.Transaction = transaction;
                            cmdKardex.CommandText = QueryAdapter.FormatearConsulta(queryUpdKardex);
                            AgregarParametro(cmdKardex, "@CodCreadoId", codigo.CodigoCreadoId);
                            await cmdKardex.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 🌟 F. INSERTAR AUDITORÍA
                string queryAuditoria = $@"
            INSERT INTO facturacion_auditoria_ediciones
            (facturacion_cabecera_id, usuario_id, fecha_edicion, motivo_edicion, 
             observacion_previa, observacion_nueva, total_items_nuevos, importe_previo, importe_nuevo)
            VALUES
            (@CabId, @UsrId, {nowFunc}, @Motivo, 
             @ObsPrev, @ObsNueva, @TotItems, @ImpPrev, @ImpNuevo);";

                using (var cmdAudit = dbConn.CreateCommand())
                {
                    cmdAudit.Transaction = transaction;
                    cmdAudit.CommandText = QueryAdapter.FormatearConsulta(queryAuditoria);

                    AgregarParametro(cmdAudit, "@CabId", cabecera.Id);
                    AgregarParametro(cmdAudit, "@UsrId", usuarioModificadorId);
                    AgregarParametro(cmdAudit, "@Motivo", string.IsNullOrWhiteSpace(motivoEdicion) ? "EDICIÓN GENERAL" : motivoEdicion.Trim());
                    AgregarParametro(cmdAudit, "@ObsPrev", string.IsNullOrWhiteSpace(observacionPrevia) ? string.Empty : observacionPrevia);
                    AgregarParametro(cmdAudit, "@ObsNueva", string.IsNullOrWhiteSpace(cabecera.Observacion) ? string.Empty : cabecera.Observacion.Trim());
                    AgregarParametro(cmdAudit, "@TotItems", cabecera.Detalles.Count);
                    AgregarParametro(cmdAudit, "@ImpPrev", importePrevio);
                    AgregarParametro(cmdAudit, "@ImpNuevo", cabecera.ImporteTotal);

                    await cmdAudit.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception("Error al actualizar el comprobante: " + ex.Message, ex);
            }
        }

        // =========================================================
        // 3. CONSULTAS Y VALIDACIONES
        // =========================================================
        public async Task<FacturacionCabecera?> ObtenerComprobantePorNumeroAsync(string serie, string numero, int? almacenId = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            FacturacionCabecera? cabecera = null;

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string sqlAlm = almacenId.HasValue ? " AND (almacen_id = @AlmId OR punto_venta_id = @AlmId)" : "";

            string queryCab = $@"
                SELECT id, empresa_id, tipo_documento, serie_documento, numero_documento, fecha_emision, punto_venta_id, almacen_id,
                       comprador_id, institucion_id, observacion, total_gravado, total_inafecto, total_exonerado,
                       total_igv, importe_total, monto_delivery, porcentaje_igv, fecha_registro, usuario_id, estado_registro,
                       usuario_update_id, updated_at
                FROM facturacion_cabecera {nolock}
                WHERE serie_documento = @Serie AND numero_documento = @Numero {sqlAlm}";

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryCab);
                AgregarParametro(cmd, "@Serie", serie);
                AgregarParametro(cmd, "@Numero", numero);
                if (almacenId.HasValue) AgregarParametro(cmd, "@AlmId", almacenId.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    cabecera = new FacturacionCabecera
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        EmpresaId = reader["empresa_id"] == DBNull.Value ? null : Convert.ToInt32(reader["empresa_id"]),
                        TipoDocumento = reader["tipo_documento"].ToString() ?? "01",
                        SerieDocumento = reader["serie_documento"].ToString() ?? "",
                        NumeroDocumento = reader["numero_documento"].ToString() ?? "",
                        FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
                        PuntoVentaId = reader["punto_venta_id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["punto_venta_id"]),
                        AlmacenId = reader["almacen_id"] == DBNull.Value ? null : Convert.ToInt32(reader["almacen_id"]),
                        CompradorId = reader["comprador_id"] == DBNull.Value ? null : Convert.ToInt32(reader["comprador_id"]),
                        InstitucionId = reader["institucion_id"] == DBNull.Value ? null : Convert.ToInt32(reader["institucion_id"]),
                        Observacion = reader["observacion"] == DBNull.Value ? string.Empty : reader["observacion"].ToString(),
                        TotalGravado = Convert.ToDecimal(reader["total_gravado"]),
                        TotalInafecto = Convert.ToDecimal(reader["total_inafecto"]),
                        TotalExonerado = Convert.ToDecimal(reader["total_exonerado"]),
                        TotalIgv = Convert.ToDecimal(reader["total_igv"]),
                        ImporteTotal = Convert.ToDecimal(reader["importe_total"]),
                        MontoDelivery = reader["monto_delivery"] == DBNull.Value ? 0.00m : Convert.ToDecimal(reader["monto_delivery"]),
                        PorcentajeIgv = reader["porcentaje_igv"] == DBNull.Value ? 18.00m : Convert.ToDecimal(reader["porcentaje_igv"]),
                        FechaRegistro = Convert.ToDateTime(reader["fecha_registro"]),
                        UsuarioId = Convert.ToInt32(reader["usuario_id"]),
                        EstadoRegistro = Convert.ToBoolean(reader["estado_registro"])
                    };
                }
            }

            if (cabecera == null) return null;


            // 1. Cargar Pagos Registrados
            string queryPagos = $@"
                SELECT fp.id, fp.medio_pago_id, mp.nombre AS medio_pago_nombre, fp.monto, fp.numero_operacion
                FROM facturacion_pago_detalle fp {nolock}
                INNER JOIN medios_pago mp {nolock} ON fp.medio_pago_id = mp.id
                WHERE fp.facturacion_cabecera_id = @CabId;";

            try
            {
                using var cmdPagos = dbConn.CreateCommand();
                cmdPagos.CommandText = QueryAdapter.FormatearConsulta(queryPagos);
                AgregarParametro(cmdPagos, "@CabId", cabecera.Id);

                using var readerPagos = await cmdPagos.ExecuteReaderAsync();
                while (await readerPagos.ReadAsync())
                {
                    cabecera.Pagos.Add(new FacturacionPagoDetalle
                    {
                        Id = Convert.ToInt32(readerPagos["id"]),
                        FacturacionCabeceraId = cabecera.Id,
                        MedioPagoId = Convert.ToInt32(readerPagos["medio_pago_id"]),
                        Monto = Convert.ToDecimal(readerPagos["monto"]),
                        NumeroOperacion = readerPagos["numero_operacion"] == DBNull.Value ? string.Empty : readerPagos["numero_operacion"].ToString()!
                    });
                }
            }
            catch
            {
                // Manejo de registros legacy
            }

            // 2. Cargar Detalles
            string queryDet = $"SELECT * FROM facturacion_detalle {nolock} WHERE facturacion_cabecera_id = @CabId ORDER BY numero_linea ASC";
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryDet);
                AgregarParametro(cmd, "@CabId", cabecera.Id);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    cabecera.Detalles.Add(new FacturacionDetalle
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        FacturacionCabeceraId = Convert.ToInt32(reader["facturacion_cabecera_id"]),
                        MovimientoId = reader["movimiento_id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["movimiento_id"]),
                        ProductoId = Convert.ToInt32(reader["producto_id"]),
                        NumeroLinea = Convert.ToInt32(reader["numero_linea"]),
                        Cantidad = Convert.ToDecimal(reader["cantidad"]),
                        PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                        ValorGravado = Convert.ToDecimal(reader["valor_gravado"]),
                        ValorInafecto = Convert.ToDecimal(reader["valor_inafecto"]),
                        ValorExonerado = Convert.ToDecimal(reader["valor_exonerado"]),
                        ValorIgv = Convert.ToDecimal(reader["valor_igv"]),
                        ImporteTotal = Convert.ToDecimal(reader["importe_total"])
                    });
                }
            }

            // 3. Cargar Códigos asociados a cada detalle
            foreach (var det in cabecera.Detalles)
            {
                string queryCod = $@"
                    SELECT dc.id, dc.codigo_creado_id, cc.codigo 
                    FROM facturacion_detalle_codigos dc {nolock}
                    INNER JOIN codigos_creados cc {nolock} ON dc.codigo_creado_id = cc.id
                    WHERE dc.facturacion_detalle_id = @DetId";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryCod);
                AgregarParametro(cmd, "@DetId", det.Id);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    det.Codigos.Add(new FacturacionDetalleCodigos
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        FacturacionDetalleId = det.Id,
                        CodigoCreadoId = Convert.ToInt32(reader["codigo_creado_id"]),
                        CodigoTexto = reader["codigo"].ToString() ?? ""
                    });
                }
            }

            return cabecera;
        }

        // =========================================================
        // 4. ANULAR COMPROBANTE CON AUDITORÍA
        // =========================================================
        public async Task AnularComprobanteAsync(int idCabecera, int usuarioAnulacionId, string? motivo = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            string queryAnular = $@"
                UPDATE facturacion_cabecera SET 
                    estado_registro = 0,
                    usuario_anulacion_id = @UsrId,
                    fecha_anulacion = {nowFunc},
                    motivo_anulacion = @Motivo
                WHERE id = @id";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryAnular);
            AgregarParametro(cmd, "@id", idCabecera);
            AgregarParametro(cmd, "@UsrId", usuarioAnulacionId);
            AgregarParametro(cmd, "@Motivo", motivo);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ValidacionCodigoResult> ValidarCodigoParaVentaAsync(int productoId, string codigoDigitado, int? almacenId = null)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string codigoLimpio = codigoDigitado.Trim().Replace("'", "-");
            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            string top1 = QueryAdapter.EsMySQL ? "" : "TOP 1";
            string limit1 = QueryAdapter.EsMySQL ? "LIMIT 1" : "";

            string queryExistencia = $@"
                SELECT {top1} cc.id, cc.codigo, cc.estado_id, cc.almacen_id
                FROM codigos_creados cc {nolock}
                INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
                WHERE rc.producto_id = @ProductoId
                  AND (
                      cc.codigo = @CodigoExacto 
                      OR cc.codigo LIKE @CodigoSufijo
                      OR REPLACE(cc.codigo, '''', '-') = @CodigoExacto
                  )
                {limit1}";

            int codigoCreadoId = 0;
            string codigoCompleto = "";
            int estadoInterno = 0;
            int? codigoAlmacenId = null;

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryExistencia);
                AgregarParametro(cmd, "@ProductoId", productoId);
                AgregarParametro(cmd, "@CodigoExacto", codigoLimpio);
                AgregarParametro(cmd, "@CodigoSufijo", "%" + codigoLimpio);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    codigoCreadoId = Convert.ToInt32(reader["id"]);
                    codigoCompleto = reader["codigo"].ToString() ?? "";
                    estadoInterno = Convert.ToInt32(reader["estado_id"]);
                    codigoAlmacenId = reader["almacen_id"] == DBNull.Value ? null : Convert.ToInt32(reader["almacen_id"]);
                }
                else
                {
                    throw new InvalidOperationException($"El código '{codigoDigitado}' no existe para el producto seleccionado.");
                }
            }

            string queryVendido = $@"
                SELECT {top1} fc.serie_documento, fc.numero_documento
                FROM facturacion_detalle_codigos fdc {nolock}
                INNER JOIN facturacion_detalle fd {nolock} ON fdc.facturacion_detalle_id = fd.id
                INNER JOIN facturacion_cabecera fc {nolock} ON fd.facturacion_cabecera_id = fc.id
                WHERE fdc.codigo_creado_id = @CodigoId 
                  AND fc.estado_registro = 1
                {limit1}";

            using (var cmdVend = dbConn.CreateCommand())
            {
                cmdVend.CommandText = QueryAdapter.FormatearConsulta(queryVendido);
                AgregarParametro(cmdVend, "@CodigoId", codigoCreadoId);

                using var rdrVend = await cmdVend.ExecuteReaderAsync();
                if (await rdrVend.ReadAsync())
                {
                    string sDoc = rdrVend.GetString(0);
                    string nDoc = rdrVend.GetString(1);
                    throw new InvalidOperationException($"El código '{codigoCompleto}' ya fue facturado en el comprobante activo N° {sDoc}-{nDoc}.");
                }
            }

            string sqlFiltroAlm = almacenId.HasValue
                ? " AND (m.almacen_origen_id = @AlmId OR m.almacen_destino_id = @AlmId OR m.almacen_id = @AlmId)"
                : "";

            string queryUltimoMovimiento = $@"
                SELECT {top1} 
                    m.id AS movimiento_id, 
                    mp.tipo_movimiento_id, 
                    m.motivo_producto_id,
                    mp.descripcion AS motivo_desc,
                    m.serie_documento, 
                    m.numero_documento,
                    m.fecha_movimiento
                FROM movimiento_codigos mc {nolock}
                INNER JOIN movimientos m {nolock} ON mc.movimiento_id = m.id
                INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
                WHERE mc.codigo_creado_id = @CodigoId
                  AND m.estado_id = 1
                  {sqlFiltroAlm}
                ORDER BY m.fecha_movimiento DESC, m.id DESC
                {limit1}";

            int movimientoIdCapturado = 0;

            using (var cmdMov = dbConn.CreateCommand())
            {
                cmdMov.CommandText = QueryAdapter.FormatearConsulta(queryUltimoMovimiento);
                AgregarParametro(cmdMov, "@CodigoId", codigoCreadoId);
                if (almacenId.HasValue) AgregarParametro(cmdMov, "@AlmId", almacenId.Value);

                using var readerMov = await cmdMov.ExecuteReaderAsync();
                if (await readerMov.ReadAsync())
                {
                    int tipoMov = Convert.ToInt32(readerMov["tipo_movimiento_id"]);
                    int motivoId = Convert.ToInt32(readerMov["motivo_producto_id"]);
                    string motivoDesc = readerMov["motivo_desc"].ToString() ?? "";
                    string sDoc = readerMov["serie_documento"].ToString() ?? "";
                    string nDoc = readerMov["numero_documento"].ToString() ?? "";

                    if (tipoMov == 1)
                    {
                        throw new InvalidOperationException(
                            $"El código '{codigoCompleto}' figura con reingreso/entrada ({motivoDesc.ToUpper()}) en Doc {sDoc}-{nDoc}. " +
                            "No es una salida neta; debe registrar un despacho de salida vigente antes de facturar.");
                    }

                    if (tipoMov == 2 && motivoId == 10)
                    {
                        throw new InvalidOperationException(
                            $"El código '{codigoCompleto}' se encuentra en traslado entre almacenes (Doc {sDoc}-{nDoc}). " +
                            "No puede ser facturado hasta que complete su recepción y posterior salida comercial.");
                    }

                    if (estadoInterno != 4)
                    {
                        throw new InvalidOperationException(
                            $"El código '{codigoCompleto}' no figura en estado DESPACHADO (Estado actual: {estadoInterno}). " +
                            "Verifique que la salida comercial se encuentre debidamente procesada.");
                    }

                    movimientoIdCapturado = Convert.ToInt32(readerMov["movimiento_id"]);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"El código '{codigoCompleto}' no registra ningún movimiento en kárdex. No se puede facturar.");
                }
            }

            return new ValidacionCodigoResult
            {
                Id = codigoCreadoId,
                CodigoCompleto = codigoCompleto,
                MovimientoId = movimientoIdCapturado
            };
        }

        public async Task<LectoraResultDTO> ProcesarCodigoPorLectoraAsync(string codigoEscaneado)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string codigoLimpio = codigoEscaneado.Replace("'", "-").Trim();

            string topClause = QueryAdapter.EsMySQL ? "" : "TOP 1";
            string limitClause = QueryAdapter.EsMySQL ? "LIMIT 1" : "";
            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            string sql = $@"
                SELECT {topClause}
                    cc.id AS codigo_id,
                    cc.codigo,
                    cc.estado_id,
                    cc.almacen_id,
                    rc.producto_id,
                    rc.categoria_producto_id,
                    COALESCE(cp.nombre, 'SIN CATEGORÍA') AS categoria_producto,
                    p.descripcion,
                    COALESCE(p.precio_unitario, 0) AS precio_unitario,
                    CASE WHEN cc.estado_id = 4 THEN 1 ELSE 0 END AS tiene_salida
                FROM codigos_creados cc {nolock}
                INNER JOIN registro_codigos rc {nolock} ON cc.registro_codigo_id = rc.id
                INNER JOIN productos p {nolock} ON rc.producto_id = p.id
                LEFT JOIN categoria_producto cp {nolock} ON rc.categoria_producto_id = cp.id
                WHERE cc.codigo = @codigo 
                   OR REPLACE(cc.codigo, '''', '-') = @codigo
                {limitClause};";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);
            cmd.CommandTimeout = 5;

            var p = cmd.CreateParameter();
            p.ParameterName = "@codigo";
            p.Value = codigoLimpio;
            cmd.Parameters.Add(p);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new InvalidOperationException($"El código '{codigoLimpio}' no existe en la base de datos.");

            return new LectoraResultDTO
            {
                CodigoCreadoId = Convert.ToInt32(reader["codigo_id"]),
                CodigoCompleto = reader["codigo"]?.ToString() ?? string.Empty,
                EstadoId = Convert.ToInt32(reader["estado_id"]),
                AlmacenId = reader["almacen_id"] == DBNull.Value ? null : Convert.ToInt32(reader["almacen_id"]),
                ProductoId = Convert.ToInt32(reader["producto_id"]),
                CategoriaProductoId = Convert.ToInt32(reader["categoria_producto_id"]),
                CategoriaProducto = reader["categoria_producto"]?.ToString() ?? string.Empty,
                DescripcionProducto = reader["descripcion"]?.ToString() ?? string.Empty,
                PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                UnidadMedida = "PACK",
                MovimientoId = 0,
                TipoMovimiento = string.Empty,
                TieneSalida = Convert.ToInt32(reader["tiene_salida"]) == 1
            };
        }

        public async Task<Dictionary<int, string>> ObtenerComprobantesActivosPorCodigosAsync(
            IEnumerable<int> codigosIds,
            DbConnection conn,
            DbTransaction? trans = null)
        {
            var resultado = new Dictionary<int, string>();
            var listaIds = codigosIds?.Distinct().ToList();

            if (listaIds == null || !listaIds.Any())
                return resultado;

            const int batchSize = 1000;
            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            for (int i = 0; i < listaIds.Count; i += batchSize)
            {
                var lote = listaIds.Skip(i).Take(batchSize).ToList();
                var paramNames = new List<string>();

                using var cmd = conn.CreateCommand();
                if (trans != null) cmd.Transaction = trans;

                for (int j = 0; j < lote.Count; j++)
                {
                    string pName = "@cId" + j;
                    paramNames.Add(pName);
                    AgregarParametro(cmd, pName, lote[j]);
                }

                string query = $@"
                    SELECT 
                        fdc.codigo_creado_id, 
                        fc.tipo_documento, 
                        fc.serie_documento, 
                        fc.numero_documento
                    FROM facturacion_detalle_codigos fdc {nolock}
                    INNER JOIN facturacion_detalle fd {nolock} ON fdc.facturacion_detalle_id = fd.id
                    INNER JOIN facturacion_cabecera fc {nolock} ON fd.facturacion_cabecera_id = fc.id
                    WHERE fdc.codigo_creado_id IN ({string.Join(",", paramNames)})
                      AND fc.estado_registro = 1";

                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int codId = Convert.ToInt32(reader["codigo_creado_id"]);
                    if (!resultado.ContainsKey(codId))
                    {
                        string tipo = reader["tipo_documento"].ToString() switch
                        {
                            "01" => "FACTURA",
                            "02" => "BOLETA",
                            "03" => "RECIBO",
                            _ => "COMPROBANTE"
                        };
                        string serie = reader["serie_documento"].ToString() ?? "";
                        string numero = reader["numero_documento"].ToString() ?? "";

                        resultado[codId] = $"{tipo} {serie}-{numero}";
                    }
                }
            }

            return resultado;
        }

        public async Task RegistrarPagoDetalleAsync(int cabeceraId, int medioPagoId, decimal monto, string? numeroOperacion)
        {
            using var conn = _database.GetConnection();
            var dbConn = (System.Data.Common.DbConnection)conn;
            await dbConn.OpenAsync();

            string sql = @"
                INSERT INTO facturacion_pagos_detalle (
                    facturacion_cabecera_id, medio_pago_id, monto, numero_operacion, observacion
                ) VALUES (
                    @cabId, @medioId, @monto, @numOp, 'VENTA MANUAL'
                );";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = Data.QueryAdapter.FormatearConsulta(sql);

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@cabId"; p1.Value = cabeceraId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@medioId"; p2.Value = medioPagoId; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@monto"; p3.Value = monto; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@numOp"; p4.Value = string.IsNullOrWhiteSpace(numeroOperacion) ? string.Empty : numeroOperacion.Trim(); cmd.Parameters.Add(p4);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<SerieDocumento>> ObtenerTodasLasSeriesAsync()
        {
            var lista = new List<SerieDocumento>();
            string query = @"
        SELECT s.id, s.ubicacion_id, s.empresa_id, s.num_seri, s.tip_seri, 
               s.num_fact, s.num_bole, s.num_reci, s.fec_regi, s.cod_usua, s.est_regi,
               e.razon_social AS empresa_razon_social
        FROM series_documentos s
        LEFT JOIN empresas e ON s.empresa_id = e.id
        WHERE s.est_regi = 1
        ORDER BY s.num_seri";

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var serie = new SerieDocumento
                {
                    Id = Convert.ToInt32(reader["id"]),
                    UbicacionId = reader["ubicacion_id"] != DBNull.Value ? Convert.ToInt32(reader["ubicacion_id"]) : 0,
                    EmpresaId = reader["empresa_id"] != DBNull.Value ? Convert.ToInt32(reader["empresa_id"]) : null,
                    NumeroSerie = reader["num_seri"].ToString() ?? "",
                    TipoSerie = reader["tip_seri"] != DBNull.Value ? reader["tip_seri"].ToString()! : "",
                    CorrelativoFactura = Convert.ToInt32(reader["num_fact"]),
                    CorrelativoBoleta = Convert.ToInt32(reader["num_bole"]),
                    CorrelativoRecibo = Convert.ToInt32(reader["num_reci"]),
                    FechaRegistro = reader["fec_regi"] != DBNull.Value ? Convert.ToDateTime(reader["fec_regi"]) : DateTime.Now,
                    CodigoUsuario = reader["cod_usua"] != DBNull.Value ? reader["cod_usua"].ToString() : "SYS",
                    EstadoId = Convert.ToInt32(reader["est_regi"])
                };

                if (serie.EmpresaId.HasValue && reader["empresa_razon_social"] != DBNull.Value)
                {
                    serie.Empresa = new Empresa
                    {
                        Id = serie.EmpresaId.Value,
                        RazonSocial = reader["empresa_razon_social"].ToString()!
                    };
                }

                lista.Add(serie);
            }
            return lista;
        }

        
        // =========================================================
        // CONSULTAR TRAZABILIDAD Y DATOS FISCALES POR CÓDIGO
        // =========================================================
        public async Task<HistorialVentaCodigoDTO?> ObtenerHistorialContablePorCodigoAsync(
            int productoId,
            string codigoEscaneado,
            int categoriaProductoId,
            int almacenId = 1)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            // 1. Resolver abreviatura del producto
            string abreviaturaBase = "";
            using (var cmdAbrev = dbConn.CreateCommand())
            {
                cmdAbrev.CommandText = QueryAdapter.FormatearConsulta($"SELECT COALESCE(abreviatura, '') FROM productos {nolock} WHERE id = @prodId");
                AgregarParametro(cmdAbrev, "@prodId", productoId);
                var resAbrev = await cmdAbrev.ExecuteScalarAsync();
                abreviaturaBase = resAbrev?.ToString()?.Trim() ?? "";
            }

            string codigoLimpio = codigoEscaneado.Trim().Replace("'", "-");
            if (int.TryParse(codigoLimpio, out int numParsed) && !string.IsNullOrEmpty(abreviaturaBase))
            {
                codigoLimpio = $"{abreviaturaBase}-{numParsed:D7}";
            }
            else if (!codigoLimpio.Contains("-") && !string.IsNullOrEmpty(abreviaturaBase))
            {
                codigoLimpio = $"{abreviaturaBase}-{codigoLimpio}";
            }

            // 🔒 CANDADO ESTRICTO DE SEDE: Exige que el comprobante pertenezca al almacén de la sesión activa
            string filtroSedeVenta = " AND (fc.almacen_id = @AlmId OR (fc.almacen_id IS NULL AND u.almacen_id = @AlmId)) ";

            // 2. Consulta de Cabecera y Comprobante Fiscal
            string sqlVenta = QueryAdapter.EsMySQL
                ? $@"SELECT 
                        fc.id,
                        COALESCE(d.des_docu, fc.tipo_documento) AS tipo_doc_desc,
                        CONCAT(fc.serie_documento, '-', fc.numero_documento) AS serie_numero,
                        fc.fecha_emision,
                        COALESCE(p_comp.razon_social, CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno), 'CLIENTES VARIOS') AS cliente_nombre,
                        COALESCE(p_comp.ruc, p_comp.dni, '') AS cliente_doc,
                        COALESCE(p_inst.razon_social, 'SIN COLEGIO') AS institucion_nombre,
                        COALESCE(u.descripcion, 'SIN SEDE') AS punto_venta_nombre,
                        fc.importe_total,
                        fc.estado_registro
                    FROM facturacion_detalle_codigos fdc
                    INNER JOIN facturacion_detalle fd ON fdc.facturacion_detalle_id = fd.id
                    INNER JOIN facturacion_cabecera fc ON fd.facturacion_cabecera_id = fc.id
                    INNER JOIN codigos_creados cc ON fdc.codigo_creado_id = cc.id
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    LEFT JOIN documentos d ON fc.tipo_documento = d.cod_docu
                    LEFT JOIN personas_comerciales p_comp ON fc.comprador_id = p_comp.id
                    LEFT JOIN personas_comerciales p_inst ON fc.institucion_id = p_inst.id
                    LEFT JOIN ubicaciones u ON fc.punto_venta_id = u.id
                    WHERE rc.producto_id = @ProdId
                      AND rc.categoria_producto_id = @CatId
                      AND (cc.codigo = @Cod OR REPLACE(cc.codigo, '''', '-') = @Cod)
                      AND fc.estado_registro = 1
                      {filtroSedeVenta}
                    ORDER BY fc.id DESC LIMIT 1;"
                : $@"SELECT TOP 1
                        fc.id,
                        COALESCE(d.des_docu, fc.tipo_documento) AS tipo_doc_desc,
                        CONCAT(fc.serie_documento, '-', fc.numero_documento) AS serie_numero,
                        fc.fecha_emision,
                        COALESCE(p_comp.razon_social, CONCAT(p_comp.nombres, ' ', p_comp.apellido_paterno), 'CLIENTES VARIOS') AS cliente_nombre,
                        COALESCE(p_comp.ruc, p_comp.dni, '') AS cliente_doc,
                        COALESCE(p_inst.razon_social, 'SIN COLEGIO') AS institucion_nombre,
                        COALESCE(u.descripcion, 'SIN SEDE') AS punto_venta_nombre,
                        fc.importe_total,
                        fc.estado_registro
                    FROM facturacion_detalle_codigos fdc WITH (NOLOCK)
                    INNER JOIN facturacion_detalle fd WITH (NOLOCK) ON fdc.facturacion_detalle_id = fd.id
                    INNER JOIN facturacion_cabecera fc WITH (NOLOCK) ON fd.facturacion_cabecera_id = fc.id
                    INNER JOIN codigos_creados cc WITH (NOLOCK) ON fdc.codigo_creado_id = cc.id
                    INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
                    LEFT JOIN documentos d WITH (NOLOCK) ON fc.tipo_documento = d.cod_docu
                    LEFT JOIN personas_comerciales p_comp WITH (NOLOCK) ON fc.comprador_id = p_comp.id
                    LEFT JOIN personas_comerciales p_inst WITH (NOLOCK) ON fc.institucion_id = p_inst.id
                    LEFT JOIN ubicaciones u WITH (NOLOCK) ON fc.punto_venta_id = u.id
                    WHERE rc.producto_id = @ProdId
                      AND rc.categoria_producto_id = @CatId
                      AND (cc.codigo = @Cod OR REPLACE(cc.codigo, '''', '-') = @Cod)
                      AND fc.estado_registro = 1
                      {filtroSedeVenta}
                    ORDER BY fc.id DESC;";

            HistorialVentaCodigoDTO? resultado = null;

            using (var cmdVenta = dbConn.CreateCommand())
            {
                cmdVenta.CommandText = QueryAdapter.FormatearConsulta(sqlVenta);
                AgregarParametro(cmdVenta, "@ProdId", productoId);
                AgregarParametro(cmdVenta, "@CatId", categoriaProductoId);
                AgregarParametro(cmdVenta, "@Cod", codigoLimpio);
                AgregarParametro(cmdVenta, "@AlmId", almacenId);

                using var rdrV = await cmdVenta.ExecuteReaderAsync();
                if (await rdrV.ReadAsync())
                {
                    resultado = new HistorialVentaCodigoDTO
                    {
                        FacturacionCabeceraId = rdrV.GetInt32(0),
                        TipoDocumento = rdrV.GetString(1),
                        SerieNumero = rdrV.GetString(2),
                        FechaEmision = rdrV.IsDBNull(3) ? null : rdrV.GetDateTime(3),
                        ClienteNombre = rdrV.GetString(4),
                        ClienteNumeroDoc = rdrV.GetString(5),
                        InstitucionColegio = rdrV.GetString(6),
                        PuntoVentaNombre = rdrV.GetString(7),
                        ImporteTotalComprobante = rdrV.GetDecimal(8),
                        ComprobanteActivo = rdrV.GetInt32(9) == 1
                    };
                }
            }

            if (resultado == null)
            {
                resultado = new HistorialVentaCodigoDTO
                {
                    ClienteNombre = "SIN VENTA / NO FACTURADO",
                    InstitucionColegio = "-",
                    SerieNumero = "[ NO FACTURADO ]",
                    CanalesPago = "-"
                };
            }

            // 3. Pagos Desglosados
            if (resultado.FacturacionCabeceraId > 0)
            {
                using var cmdPagos = dbConn.CreateCommand();
                cmdPagos.CommandText = QueryAdapter.FormatearConsulta(@"
SELECT COALESCE(mp.nombre, 'OTRO MEDIO'), fpd.monto, COALESCE(fpd.numero_operacion, ''), COALESCE(fpd.observacion, '')
FROM facturacion_pago_detalle fpd
LEFT JOIN medios_pago mp ON fpd.medio_pago_id = mp.id
WHERE fpd.facturacion_cabecera_id = @CabId");
                AgregarParametro(cmdPagos, "@CabId", resultado.FacturacionCabeceraId);

                using var rdrP = await cmdPagos.ExecuteReaderAsync();
                while (await rdrP.ReadAsync())
                {
                    resultado.DetallePagosFiscales.Add(new PagoComprobanteItemDTO
                    {
                        MedioPago = rdrP.GetString(0),
                        Monto = rdrP.GetDecimal(1),
                        NumeroOperacion = rdrP.GetString(2),
                        Observacion = rdrP.GetString(3)
                    });
                }
                resultado.CanalesPago = resultado.DetallePagosFiscales.Any()
                    ? string.Join(", ", resultado.DetallePagosFiscales.Select(p => p.MedioPago))
                    : "EFECTIVO";
            }

            // 4. Movimientos de Almacén (Kárdex)
            var kardexService = new KardexService();
            resultado.MovimientosKardex = await kardexService.ObtenerHistorialCompletoPorCodigoAsync(
                productoId, codigoEscaneado, categoriaProductoId, almacenId, incluirAnulados: true);

            return resultado;
        }
    }
}