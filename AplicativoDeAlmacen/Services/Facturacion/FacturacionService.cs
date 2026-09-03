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
        // 1. GUARDAR NUEVO COMPROBANTE (CON AUDITORÍA Y MULTI-MOTOR)
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
                (tipo_documento, serie_documento, numero_documento, fecha_emision, punto_venta_id, almacen_id,
                 comprador_id, institucion_id, observacion, total_gravado, total_inafecto, 
                 total_exonerado, total_igv, importe_total, porcentaje_igv, fecha_registro, usuario_id, estado_registro)
                VALUES 
                (@TipoDoc, @SerieDoc, @NumDoc, @FecEmi, @PtoVentaId, @AlmId,
                 @CompradorId, @InstId, @Obs, @TotGrav, @TotIna, 
                 @TotExo, @TotIgv, @ImpTot, @PorcIgv, {nowFunc}, @UsuId, 1);
                {selectId}";

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
                    AgregarParametro(cmdCabecera, "@AlmId", cabecera.AlmacenId ?? 1);
                    AgregarParametro(cmdCabecera, "@CompradorId", cabecera.CompradorId);
                    AgregarParametro(cmdCabecera, "@InstId", cabecera.InstitucionId);
                    AgregarParametro(cmdCabecera, "@Obs", cabecera.Observacion);
                    AgregarParametro(cmdCabecera, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCabecera, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCabecera, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCabecera, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCabecera, "@ImpTot", cabecera.ImporteTotal);
                    AgregarParametro(cmdCabecera, "@PorcIgv", cabecera.PorcentajeIgv);
                    AgregarParametro(cmdCabecera, "@UsuId", cabecera.UsuarioId);

                    nuevaCabeceraId = Convert.ToInt32(await cmdCabecera.ExecuteScalarAsync());
                }

                // 2. INSERTAR DETALLES Y CÓDIGOS
                string queryDetalle = $@"
                INSERT INTO facturacion_detalle 
                (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
                 valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total, created_at)
                VALUES 
                (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
                 @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot, {nowFunc});
                {selectId}";

                string queryCodigo = @"
                INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id)
                VALUES (@DetId, @CodCreadoId)";

                int linea = 1;
                foreach (var detalle in cabecera.Detalles)
                {
                    int nuevoDetalleId;
                    using (var cmdDetalle = dbConn.CreateCommand())
                    {
                        cmdDetalle.Transaction = transaction;
                        cmdDetalle.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);

                        AgregarParametro(cmdDetalle, "@CabId", nuevaCabeceraId);
                        AgregarParametro(cmdDetalle, "@MovId", (detalle.MovimientoId > 0) ? (object)detalle.MovimientoId : DBNull.Value);
                        AgregarParametro(cmdDetalle, "@ProdId", detalle.ProductoId);
                        AgregarParametro(cmdDetalle, "@NumLinea", linea++);
                        AgregarParametro(cmdDetalle, "@Cant", detalle.Cantidad);
                        AgregarParametro(cmdDetalle, "@PreUnit", detalle.PrecioUnitario);
                        AgregarParametro(cmdDetalle, "@ValGrav", detalle.ValorGravado);
                        AgregarParametro(cmdDetalle, "@ValIna", detalle.ValorInafecto);
                        AgregarParametro(cmdDetalle, "@ValExo", detalle.ValorExonerado);
                        AgregarParametro(cmdDetalle, "@ValIgv", detalle.ValorIgv);
                        AgregarParametro(cmdDetalle, "@ImpTot", detalle.ImporteTotal);

                        nuevoDetalleId = Convert.ToInt32(await cmdDetalle.ExecuteScalarAsync());
                    }

                    // Inserción de códigos físicos asociados (si tiene)
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
                        }
                    }
                }

                // 3. ACTUALIZAR CORRELATIVO EN TABLA SERIES
                string campoUpdate = cabecera.TipoDocumento == "01" ? "num_fact = num_fact + 1" :
                                     cabecera.TipoDocumento == "02" ? "num_bole = num_bole + 1" :
                                     cabecera.TipoDocumento == "03" ? "num_reci = num_reci + 1" : "";

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
        // 2. ACTUALIZAR COMPROBANTE EXISTENTE (CON AUDITORÍA DE EDICIÓN)
        // =========================================================================
        public async Task ActualizarComprobanteAsync(FacturacionCabecera cabecera, int usuarioModificadorId)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var transaction = dbConn.BeginTransaction();

            try
            {
                string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";
                string nowFunc = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

                // 1. ACTUALIZAR CABECERA
                string queryUpdateCab = $@"
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
                    importe_total = @ImpTot,
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
                    AgregarParametro(cmdCab, "@CompradorId", cabecera.CompradorId);
                    AgregarParametro(cmdCab, "@InstId", cabecera.InstitucionId);
                    AgregarParametro(cmdCab, "@Obs", cabecera.Observacion);
                    AgregarParametro(cmdCab, "@TotGrav", cabecera.TotalGravado);
                    AgregarParametro(cmdCab, "@TotIna", cabecera.TotalInafecto);
                    AgregarParametro(cmdCab, "@TotExo", cabecera.TotalExonerado);
                    AgregarParametro(cmdCab, "@TotIgv", cabecera.TotalIgv);
                    AgregarParametro(cmdCab, "@ImpTot", cabecera.ImporteTotal);
                    AgregarParametro(cmdCab, "@UsrUpdateId", usuarioModificadorId);

                    await cmdCab.ExecuteNonQueryAsync();
                }

                // 2. ELIMINAR DETALLES Y CÓDIGOS ANTERIORES
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
                string queryDetalle = $@"
                INSERT INTO facturacion_detalle 
                (facturacion_cabecera_id, movimiento_id, producto_id, numero_linea, cantidad, precio_unitario, 
                 valor_gravado, valor_inafecto, valor_exonerado, valor_igv, importe_total, created_at)
                VALUES 
                (@CabId, @MovId, @ProdId, @NumLinea, @Cant, @PreUnit, 
                 @ValGrav, @ValIna, @ValExo, @ValIgv, @ImpTot, {nowFunc});
                {selectId}";

                string queryCodigo = "INSERT INTO facturacion_detalle_codigos (facturacion_detalle_id, codigo_creado_id) VALUES (@DetId, @CodCreadoId)";

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
                        }
                    }
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
            string sqlAlm = almacenId.HasValue ? " AND (almacen_id = @AlmId OR almacen_id IS NULL)" : "";

            string queryCab = $@"
            SELECT id, tipo_documento, serie_documento, numero_documento, fecha_emision, punto_venta_id, almacen_id,
                   comprador_id, institucion_id, observacion, total_gravado, total_inafecto, total_exonerado,
                   total_igv, importe_total, porcentaje_igv, fecha_registro, usuario_id, estado_registro,
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
                        TipoDocumento = reader["tipo_documento"].ToString() ?? "01",
                        SerieDocumento = reader["serie_documento"].ToString() ?? "",
                        NumeroDocumento = reader["numero_documento"].ToString() ?? "",
                        FechaEmision = Convert.ToDateTime(reader["fecha_emision"]),
                        PuntoVentaId = Convert.ToInt32(reader["punto_venta_id"]),
                        AlmacenId = reader["almacen_id"] == DBNull.Value ? null : Convert.ToInt32(reader["almacen_id"]),
                        CompradorId = reader["comprador_id"] == DBNull.Value ? null : Convert.ToInt32(reader["comprador_id"]),
                        InstitucionId = reader["institucion_id"] == DBNull.Value ? null : Convert.ToInt32(reader["institucion_id"]),
                        Observacion = reader["observacion"] == DBNull.Value ? string.Empty : reader["observacion"].ToString(),
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

            if (cabecera == null) return null;

            // Cargar Detalles
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

            // Cargar Códigos asociados a cada detalle
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

            // 1. BUSCAR EXISTENCIA DEL CÓDIGO ASOCIADO AL PRODUCTO
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

            // 2. VALIDAR QUE NO HAYA SIDO FACTURADO PREVIAMENTE EN UN COMPROBANTE ACTIVO
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

            // 3. CONSULTAR LA LÍNEA DE TIEMPO DE KÁRDEX DEL CÓDIGO
            string sqlFiltroAlm = almacenId.HasValue ? " AND (m.almacen_origen_id = @AlmId OR m.almacen_destino_id = @AlmId OR m.almacen_id = @AlmId)" : "";

            string queryUltimoMovimiento = $@"
        SELECT {top1} 
            m.id AS movimiento_id, 
            mp.tipo_movimiento_id, 
            mp.descripcion AS motivo_desc,
            m.serie_documento, 
            m.numero_documento
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
                    int tipoMov = Convert.ToInt32(readerMov["tipo_movimiento_id"]); // 1: Entrada, 2: Salida
                    string motivoDesc = readerMov["motivo_desc"].ToString() ?? "";
                    string sDoc = readerMov["serie_documento"].ToString() ?? "";
                    string nDoc = readerMov["numero_documento"].ToString() ?? "";

                    // En un flujo comercial formal, para facturar un libro seríado, este debe haber salido de almacén (Tipo 2 / Estado 4)
                    if (tipoMov != 2 && estadoInterno != 4)
                    {
                        throw new InvalidOperationException($"El código '{codigoCompleto}' figura como '{motivoDesc.ToUpper()}' en Doc {sDoc}-{nDoc}. Debe registrar su salida antes de facturar.");
                    }

                    movimientoIdCapturado = Convert.ToInt32(readerMov["movimiento_id"]);
                }
                else
                {
                    // Si es una venta directa de mostrador y el libro está disponible en almacén (Estado 3 o 4)
                    if (estadoInterno != 3 && estadoInterno != 4)
                    {
                        throw new InvalidOperationException($"El código '{codigoCompleto}' no cuenta con movimientos válidos en kárdex para ser vendido.");
                    }
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
    }
}