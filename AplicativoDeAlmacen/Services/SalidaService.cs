using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
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

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        // =========================================================================
        // 1. GENERAR CORRELATIVO DE SALIDA (Usa el formato D7 de tu código base)
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

                    object resultObj = await cmd.ExecuteScalarAsync();

                    if (resultObj != null && resultObj != DBNull.Value)
                    {
                        int siguienteNumero = Convert.ToInt32(resultObj);
                        resultado.NumeroDocumento = siguienteNumero.ToString("D7"); // Formato exacto de 7 dígitos
                    }
                }
            }
            return resultado;
        }

        // =========================================================================
        // 2. OBTENER MOTIVOS DE PRODUCTOS FILTRADOS SOLO POR 'salida'
        // =========================================================================
        public async Task<List<MotivoProducto>> ObtenerMotivosSalidaAsync()
        {
            var lista = new List<MotivoProducto>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // Filtrando estrictamente por 'salida' como lo solicitaste
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

        // =========================================================================
        // 3. BUSCADOR ASÍNCRONO DE CLIENTES (Tabla: persona_comercial)
        // =========================================================================
        public async Task<List<PersonaComercial>> BuscarClientesAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // Cambia 'razon_social' o 'id' según las columnas reales de persona_comercial
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
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // =========================================================================
        // 4. BUSCADOR ASÍNCRONO DE UBICACIONES (Tabla: ubicaciones)
        // =========================================================================
        public async Task<List<Ubicacion>> BuscarUbicacionesAsync(string filtro)
        {
            var lista = new List<Ubicacion>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"SELECT id, descripcion, direccion 
                                 FROM ubicaciones 
                                 WHERE descripcion   LIKE @filtro 
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
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("direccion"))
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // =========================================================================
        // 5. REGISTRAR SALIDA COMPLETA TRANSACCIONAL (Usa tus clases Grid del Sistema)
        // =========================================================================
        public async Task<bool> RegistrarSalidaCompletaAsync(
       Movimiento cabecera,
       List<VistaProductoGrid> listaProductos,
       List<VistaCodigoGrid> listaCodigos,
       int usuarioId,
       int estadoId)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaccion = await dbConn.BeginTransactionAsync())
                {
                    try
                    {
                        // 1. INSERTAR CABECERA
                        string queryCabecera = @"
                    INSERT INTO movimientos (fecha_movimiento, serie_documento, numero_documento, motivo_producto_id, 
                           ubicacion_id, usuario_id, persona_comercial_id, serie_guia, numero_guia, 
                           observacion, estado_id, created_at)
                    VALUES (@fecha, @serie, @numero, @motivoId, @ubicacionId, @usuarioId, 
                           @personaId, @serieGuia, @numeroGuia, @observacion, @estadoId, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        int idMovimientoGenerado;
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaccion;
                            cmd.CommandText = QueryAdapter.FormatearConsulta(queryCabecera);
                            AgregarParametro(cmd, "@fecha", cabecera.FechaMovimiento.HasValue
                                ? cabecera.FechaMovimiento.Value.ToDateTime(TimeOnly.MinValue)
                                : DateTime.Now);
                            AgregarParametro(cmd, "@serie", cabecera.SerieDocumento);
                            AgregarParametro(cmd, "@numero", cabecera.NumeroDocumento);
                            AgregarParametro(cmd, "@motivoId", cabecera.MotivoProductoId);
                            AgregarParametro(cmd, "@ubicacionId", cabecera.UbicacionId > 0 ? (object)cabecera.UbicacionId : DBNull.Value);
                            AgregarParametro(cmd, "@usuarioId", usuarioId);
                            AgregarParametro(cmd, "@personaId", cabecera.PersonaComercialId > 0 ? (object)cabecera.PersonaComercialId : DBNull.Value);
                            AgregarParametro(cmd, "@serieGuia", (object)cabecera.SerieGuia ?? DBNull.Value);
                            AgregarParametro(cmd, "@numeroGuia", (object)cabecera.NumeroGuia ?? DBNull.Value);
                            AgregarParametro(cmd, "@observacion", (object)cabecera.Observacion ?? DBNull.Value);
                            AgregarParametro(cmd, "@estadoId", estadoId);

                            idMovimientoGenerado = (int)await cmd.ExecuteScalarAsync();
                        }

                        // 2. PROCESAR PRODUCTOS Y CÓDIGOS
                        foreach (var item in listaProductos)
                        {
                            // Insertar Detalle
                                string queryDetalle = @"
                            INSERT INTO movimiento_detalles (movimiento_id, producto_id, cantidad_ingreso, cantidad_salida, costo_unitario, created_at)
                            VALUES (@movId, @prodId, 0, @cant, @costo, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                            int idDetalle;
                            using (var cmdDet = dbConn.CreateCommand())
                            {
                                cmdDet.Transaction = transaccion;
                                cmdDet.CommandText = QueryAdapter.FormatearConsulta(queryDetalle);
                                AgregarParametro(cmdDet, "@movId", idMovimientoGenerado);
                                AgregarParametro(cmdDet, "@prodId", item.ProductoId);
                                AgregarParametro(cmdDet, "@cant", item.Cantidad);
                                AgregarParametro(cmdDet, "@costo", item.Detalle?.CostoUnitario ?? 0);
                                idDetalle = (int)await cmdDet.ExecuteScalarAsync();
                            }

                            // Insertar Códigos asociados a este producto
                            var codigosProd = listaCodigos.Where(c => c.ProductoId == item.ProductoId);
                            foreach (var cod in codigosProd)
                            {
                           
                                // A. Insertar movimiento_codigos
                                string queryMovCod = @"INSERT INTO movimiento_codigos (movimiento_id, movimiento_detalle_id, codigo_creado_id, cantidad_ingreso, cantidad_salida, created_at)
                                  VALUES (@movId, @detId, @codId, 0, 1, GETDATE());";

                                using (var cmdCod = dbConn.CreateCommand())
                                {
                                    cmdCod.Transaction = transaccion;
                                    cmdCod.CommandText = QueryAdapter.FormatearConsulta(queryMovCod);
                                    AgregarParametro(cmdCod, "@movId", idMovimientoGenerado);
                                    AgregarParametro(cmdCod, "@detId", idDetalle);
                                    AgregarParametro(cmdCod, "@codId", cod.MovCodigo.CodigoCreadoId);
                                    await cmdCod.ExecuteNonQueryAsync();
                                }

                                // B. Actualizar estado del código
                                using (var cmdUpd = dbConn.CreateCommand())
                                {
                                    cmdUpd.Transaction = transaccion;
                                    cmdUpd.CommandText = QueryAdapter.FormatearConsulta("UPDATE codigos_creados SET estado_id = 4 WHERE id = @id");
                                    AgregarParametro(cmdUpd, "@id", cod.MovCodigo.CodigoCreadoId);
                                    await cmdUpd.ExecuteNonQueryAsync();
                                }


                            }

                        }

                        await transaccion.CommitAsync();
                        return true;
                    }
                    catch (Exception)
                    {
                        await transaccion.RollbackAsync();
                        throw; // Lanza el error para que sepas qué falló
                    }
                }
            }
        }



    }
}
