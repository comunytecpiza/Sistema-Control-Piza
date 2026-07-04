using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class IngresoMovimientoService
    {
        private readonly DatabaseConnection _database;

        public IngresoMovimientoService()
        {
            _database = new DatabaseConnection();
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


        public async Task<bool> RegistrarMovimientoCompletoAsync(
     Movimiento cabecera,
     List<VistaProductoGrid> productos,
     List<RangoCodigoItem> rangos,
     int ubicacionId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using (var transaccion = dbConn.BeginTransaction())
                {
                    try
                    {
                        string selectId = QueryAdapter.EsMySQL ? "SELECT LAST_INSERT_ID();" : "SELECT SCOPE_IDENTITY();";

                        // Generar siguiente correlativo DENTRO de la transacción para reducir condiciones de carrera
                        string serieParaGenerar = string.IsNullOrWhiteSpace(cabecera.SerieDocumento) ? "0001" : cabecera.SerieDocumento;
                        using (var cmdGen = dbConn.CreateCommand())
                        {
                            cmdGen.Transaction = transaccion;
                            cmdGen.CommandText = QueryAdapter.FormatearConsulta(@"SELECT COALESCE(MAX(CAST(numero_documento AS INT)), 0) + 1 FROM movimientos WHERE serie_documento = @serie");
                            AgregarParametro(cmdGen, "@serie", serieParaGenerar);
                            object genRes = await cmdGen.ExecuteScalarAsync();
                            int siguienteNumero = genRes != null && genRes != DBNull.Value ? Convert.ToInt32(genRes) : 1;
                            cabecera.NumeroDocumento = siguienteNumero.ToString("D7");
                            cabecera.SerieDocumento = serieParaGenerar;
                        }

                        // =======================================================
                        // PASO 1: Insertar Cabecera del Movimiento
                        // =======================================================
                        string queryCabecera = $@"
                    INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, 
                                            motivo_producto_id, ubicacion_id, usuario_id, persona_comercial_id, observacion, estado_id, serie_guia, numero_guia)
                    VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, @personaId, @observacion, @estadoId, @serieGuia, @numeroGuia);
                    {selectId}";

                        int movimientoIdInserted = 0;
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
                            AgregarParametro(cmdCab, "@usuarioId", cabecera.UsuarioId);
                            AgregarParametro(cmdCab, "@personaId", cabecera.PersonaComercialId);
                            AgregarParametro(cmdCab, "@observacion", cabecera.Observacion);
                            AgregarParametro(cmdCab, "@serieGuia", cabecera.SerieGuia);
                            AgregarParametro(cmdCab, "@numeroGuia", cabecera.NumeroGuia);

                            object resultCab = await cmdCab.ExecuteScalarAsync();
                            if (resultCab == null || resultCab == DBNull.Value) throw new Exception("No se pudo obtener el ID de la cabecera.");
                            movimientoIdInserted = Convert.ToInt32(resultCab);
                        }

                        // =======================================================
                        // PASO 2: Procesar Detalle y Rangos
                        // =======================================================
                        string queryDetalle = $@"
                    INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, costo_unitario)
                    VALUES (@movimientoId, @productoId, @cantidad, @costo);
                    {selectId}";




                        string queryRangos = $@"
                        INSERT INTO registro_rangos (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, usuario_id)
                        VALUES (@productoId, @categoriaProductoId, @abreviaturaBase, @desdeNum, @hastaNum, @movimientoDetalleId, @usuarioId);
                        {selectId}";

                        foreach (var item in productos)
                        {
                            int detalleIdInserted = 0;
                            using (var cmdDet = dbConn.CreateCommand())
                            {
                                cmdDet.Transaction = transaccion;
                                cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                                AgregarParametro(cmdDet, "@movimientoId", movimientoIdInserted);
                                AgregarParametro(cmdDet, "@productoId", item.Detalle.ProductoId);
                                AgregarParametro(cmdDet, "@cantidad", item.Detalle.CantidadIngreso);
                                AgregarParametro(cmdDet, "@costo", item.Detalle.CostoUnitario);

                                object resultDet = await cmdDet.ExecuteScalarAsync();
                                detalleIdInserted = Convert.ToInt32(resultDet);
                            }

                            foreach (var rango in rangos.Where(r => r.productoId == item.Detalle.ProductoId))
                            {

                                // Insertar rango y obtener su ID para la relación
                                int idRangoGenerado = 0;
                                using (var cmdRan = dbConn.CreateCommand())
                                {
                                    cmdRan.Transaction = transaccion;
                                    cmdRan.CommandText = QueryAdapter.FormatearConsulta(queryRangos);
                                    AgregarParametro(cmdRan, "@productoId", rango.productoId);
                                    AgregarParametro(cmdRan, "@categoriaProductoId", rango.CategoriaProductoId);
                                    AgregarParametro(cmdRan, "@abreviaturaBase", rango.AbreviaturaBase);
                                    AgregarParametro(cmdRan, "@desdeNum", rango.DesdeNum);
                                    AgregarParametro(cmdRan, "@hastaNum", rango.HastaNum);
                                    AgregarParametro(cmdRan, "@movimientoDetalleId", detalleIdInserted);
                                    AgregarParametro(cmdRan, "@usuarioId", cabecera.UsuarioId);

                                    idRangoGenerado = Convert.ToInt32(await cmdRan.ExecuteScalarAsync());
                                }
                                //aqui me que sale nulo
                                var idsCodigosFisicos = await ObtenerIdsCodigosPorRangoAsync(rango.AbreviaturaBase, rango.CategoriaProductoId, rango.DesdeNum, rango.HastaNum, dbConn, transaccion);

                                // Dentro del foreach (var idFisico in idsCodigosFisicos)
                                foreach (var idFisico in idsCodigosFisicos)
                                {
                                    // 1. Validar si el ID existe en la tabla antes de insertar
                                    using (var cmdVal = dbConn.CreateCommand())
                                    {
                                        cmdVal.Transaction = transaccion;
                                        cmdVal.CommandText = "SELECT estado_id FROM codigos_creados WHERE id = @codigoId";
                                        AgregarParametro(cmdVal, "@codigoId", idFisico.Id);

                                        var estado = await cmdVal.ExecuteScalarAsync();

                                        if (estado == null || Convert.ToInt32(estado) != 1)
                                        {
                                            throw new Exception($"El código ID {idFisico} no está disponible para salida (Estado actual: {estado}).");
                                        }
                                    }

                                    // 2. Si existe, procedemos con el INSERT como lo corregimos antes
                                    using (var cmdMovCod = dbConn.CreateCommand())
                                    {
                                        cmdMovCod.Transaction = transaccion;
                                        cmdMovCod.CommandText = QueryAdapter.FormatearConsulta(@"
                                        INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at) 
                                        VALUES (@movId, @detId, @codId, 0, 1, GETDATE());");

                                        AgregarParametro(cmdMovCod, "@movId", movimientoIdInserted);
                                        AgregarParametro(cmdMovCod, "@detId", detalleIdInserted);
                                        AgregarParametro(cmdMovCod, "@codId", idFisico.Id);

                                        await cmdMovCod.ExecuteNonQueryAsync();
                                    }
                                    // ... resto del código ...

                                    // B. Actualizar estado del código
                                    using (var cmdUpd = dbConn.CreateCommand())
                                    {
                                        cmdUpd.Transaction = transaccion;
                                        cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 3 WHERE id = @id");
                                        AgregarParametro(cmdUpd, "@id", idFisico.Id);
                                        await cmdUpd.ExecuteNonQueryAsync();
                                    }

                                }
                            }
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
            }
        }
        private async Task<List<CodigoCreado>> ObtenerIdsCodigosPorRangoAsync(string baseLimpia, int categoriaId, int desde, int hasta, DbConnection conn, DbTransaction trans)
        {
            List<CodigoCreado> resultados = new List<CodigoCreado>();

            // El patrón lo pasamos tal cual, la BD se encarga del resto
            string patron = baseLimpia;

            string query = @"
        SELECT cc.Id, cc.registro_codigo_id, cc.Codigo, cc.es_manual, cc.estado_id
        FROM codigos_creados cc
        INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
        WHERE REPLACE(cc.codigo, ' ', '') LIKE REPLACE(@patron, ' ', '') + '%'
          AND rc.categoria_producto_id = @categoriaId
          AND TRY_CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@patron", patron);
                AgregarParametro(cmd, "@categoriaId", categoriaId);
                AgregarParametro(cmd, "@desde", desde);
                AgregarParametro(cmd, "@hasta", hasta);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        resultados.Add(new CodigoCreado
                        {
                            Id = reader.GetInt32(0),
                            RegistroCodigoId = reader.GetInt32(1),
                            Codigo = reader.GetString(2),
                            EsManual = reader.GetBoolean(3), // Asegúrate de que esto sea correcto para tu BD
                            EstadoId = reader.GetInt32(4)
                        });
                    }
                }
            }
            return resultados;
        }
    }
}