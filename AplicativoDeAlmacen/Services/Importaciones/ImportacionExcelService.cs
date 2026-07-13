#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using System.IO;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Facturación.AplicativoDeAlmacen.Models.Facturación; // Tu namespace correcto para los modelos

namespace AplicativoDeAlmacen.Services.Importaciones
{
    public class ImportacionExcelService
    {
        private readonly DataConnection.DatabaseConnection _database;
        private readonly AplicativoDeAlmacen.Services.facturaciòn.FacturacionService _facturacionService;

        public ImportacionExcelService()
        {
            _database = new DataConnection.DatabaseConnection();
            _facturacionService = new AplicativoDeAlmacen.Services.facturaciòn.FacturacionService();
        }

        // ==========================================
        // FUNCIÓN ÚNICA DE AGREGAR PARÁMETRO (Estaba duplicada)
        // ==========================================
        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<string>> LeerCodigosDesdeExcelAsync(string rutaArchivo)
        {
            var codigos = new List<string>();

            await Task.Run(() =>
            {
                using var stream = new System.IO.FileStream(
                    rutaArchivo,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite);

                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                foreach (var row in ws.RowsUsed())
                {
                    foreach (var cell in row.CellsUsed())
                    {
                        string codigo = cell.GetString().Trim();

                        if (!string.IsNullOrWhiteSpace(codigo))
                            codigos.Add(codigo);
                    }
                }
            });

            return codigos.Distinct().ToList();
        }

        public async Task<List<string>> ObtenerCodigosDuplicadosAsync(List<string> codigosExcel)
        {
            var duplicados = new List<string>();

            if (!codigosExcel.Any())
                return duplicados;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM codigos_creados
                WHERE codigo=@codigo";

            var p = cmd.CreateParameter();
            p.ParameterName = "@codigo";
            cmd.Parameters.Add(p);

            foreach (var codigo in codigosExcel)
            {
                p.Value = codigo;

                int existe = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (existe > 0)
                    duplicados.Add(codigo);
            }

            return duplicados;
        }

        public async Task GuardarCodigosImportadosTransactionAsync(
            int coleccionId,
            int productoId,
            int categoriaId,
            List<string> codigosValidos)
        {
            if (!codigosValidos.Any())
                throw new Exception("No hay códigos para guardar.");

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var trans = dbConn.BeginTransaction();

            try
            {
                int loteId;

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.Transaction = trans;

                    cmd.CommandText = @"
                        INSERT INTO registro_codigos
                        (
                        coleccion_id,
                        producto_id,
                        categoria_producto_id,
                        cantidad,
                        desde,
                        hasta
                        )
                        OUTPUT INSERTED.id
                        VALUES
                        (
                        @coleccion,
                        @producto,
                        @categoria,
                        @cantidad,
                        @desde,
                        @hasta
                        )";

                    AgregarParametro(cmd, "@coleccion", coleccionId);
                    AgregarParametro(cmd, "@producto", productoId);
                    AgregarParametro(cmd, "@categoria", categoriaId);
                    AgregarParametro(cmd, "@cantidad", codigosValidos.Count);
                    AgregarParametro(cmd, "@desde", codigosValidos.First());
                    AgregarParametro(cmd, "@hasta", codigosValidos.Last());

                    loteId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.Transaction = trans;

                    cmd.CommandText = @"
                        INSERT INTO codigos_creados
                        (
                        registro_codigo_id,
                        codigo,
                        estado_id,
                        es_manual
                        )
                        VALUES
                        (
                        @registro,
                        @codigo,
                        1,
                        0
                        )";

                    var pRegistro = cmd.CreateParameter();
                    pRegistro.ParameterName = "@registro";
                    cmd.Parameters.Add(pRegistro);

                    var pCodigo = cmd.CreateParameter();
                    pCodigo.ParameterName = "@codigo";
                    cmd.Parameters.Add(pCodigo);

                    foreach (var codigo in codigosValidos)
                    {
                        pRegistro.Value = loteId;
                        pCodigo.Value = codigo;

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Lee el archivo Excel de Nisira, extrae las filas y agrupa la información
        /// en Cabecera -> Detalle -> Códigos.
        /// </summary>
        public async Task<List<ImportacionCabeceraDTO>> LeerExcelVentasAgrupadoAsync(string rutaArchivo)
        {
            var cabecerasAgrupadas = new List<ImportacionCabeceraDTO>();

            await Task.Run(() =>
            {
                using var stream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                // 1. Estructura temporal para lectura plana usando tu clase FilaPlanaExcel externa
                var filasRaw = new List<FilaPlanaExcel>();

                foreach (var row in ws.RowsUsed().Skip(1)) // Saltar cabecera
                {
                    try
                    {
                        var fila = new FilaPlanaExcel
                        {
                            Documento = row.Cell(1).GetString().Trim(),
                            Serie = row.Cell(2).GetString().Trim(),
                            Numero = row.Cell(3).GetString().Trim(),
                            RazonSocial = row.Cell(6).GetString().Trim(),
                            Moneda = row.Cell(7).GetString().Trim(),
                            Fecha = row.Cell(8).GetDateTime(),
                            Afecto = row.Cell(9).GetValue<decimal>(),
                            IGV = row.Cell(10).GetValue<decimal>(),
                            Exonerado = row.Cell(11).GetValue<decimal>(),
                            Importe = row.Cell(12).GetValue<decimal>(),
                            Producto = row.Cell(13).GetString().Trim(),
                            Precio = row.Cell(14).GetValue<decimal>(),
                            Institucion = row.Cell(15).GetString().Trim(),
                            CodigoInterno = row.Cell(17).GetString().Trim(),
                            Cantidad = row.Cell(18).GetValue<int>()
                        };

                        if (!string.IsNullOrEmpty(fila.Numero))
                        {
                            filasRaw.Add(fila);
                        }
                    }
                    catch { /* Ignorar filas vacías o mal formateadas al final */ }
                }

                // 2. MAGIA LINQ: Agrupar en Cabecera -> Detalles -> Códigos
                cabecerasAgrupadas = filasRaw
                    .GroupBy(c => new { c.Documento, c.Serie, c.Numero })
                    .Select(gCabecera =>
                    {
                        var primeraFila = gCabecera.First();

                        var cabecera = new ImportacionCabeceraDTO
                        {
                            DocumentoExcel = gCabecera.Key.Documento,
                            Serie = gCabecera.Key.Serie,
                            Numero = gCabecera.Key.Numero,
                            Fecha = primeraFila.Fecha,
                            RazonSocialExcel = primeraFila.RazonSocial,
                            ClienteExcel = primeraFila.Institucion,
                            Moneda = primeraFila.Moneda,
                            Afecto = primeraFila.Afecto,
                            Exonerado = primeraFila.Exonerado,
                            IGV = primeraFila.IGV,
                            Total = primeraFila.Importe
                        };

                        cabecera.Detalles = gCabecera
                            .GroupBy(d => new { d.Producto, d.Precio })
                            .Select(gDetalle => new ImportacionDetalleDTO
                            {
                                DescripcionExcel = gDetalle.Key.Producto,
                                PrecioUnitario = gDetalle.Key.Precio,
                                Cantidad = gDetalle.Sum(x => x.Cantidad),
                                Importe = gDetalle.Sum(x => x.Precio * x.Cantidad),

                                Codigos = gDetalle
                                    .Where(c => !string.IsNullOrEmpty(c.CodigoInterno))
                                    .Select(c => new ImportacionCodigoDTO
                                    {
                                        CodigoExcel = c.CodigoInterno
                                    }).ToList()
                            }).ToList();

                        return cabecera;
                    }).ToList();
            });

            return cabecerasAgrupadas;
        }

        /// <summary>
        /// Realiza el cruce masivo con la Base de Datos para verificar que todo exista 
        /// (Clientes, Productos) y que los códigos estén en estado SALIDA.
        /// </summary>
        /// <summary>
        /// Realiza el cruce masivo con la Base de Datos para verificar que todo exista 
        /// (Clientes, Productos) y que los códigos estén en estado SALIDA.
        /// </summary>
        /// <summary>
        /// Realiza el cruce masivo con la Base de Datos para verificar que todo exista 
        /// (Clientes, Productos) y que los códigos estén en estado SALIDA.
        /// </summary>
        public async Task ValidarDatosImportacionAsync(List<ImportacionCabeceraDTO> comprobantes)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();

            foreach (var cabecera in comprobantes)
            {
                // 🌟 NUEVO: 0. VERIFICAR SI EL COMPROBANTE YA EXISTE (EVITAR DUPLICADOS)
                cmd.CommandText = "SELECT TOP 1 1 FROM facturacion_cabecera WHERE serie_documento = @serie AND numero_documento = @numero AND estado_registro = 1";
                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@serie", cabecera.Serie);
                // Aseguramos el mismo formato de 7 dígitos que usa el sistema
                AgregarParametro(cmd, "@numero", cabecera.Numero.PadLeft(7, '0'));

                if (await cmd.ExecuteScalarAsync() != null)
                {
                    // Si ya existe, lo invalidamos, ponemos el mensaje y SALTAMOS a la siguiente factura
                    // Esto hará que se pinte de rojo y el botón "Transferir" lo ignore automáticamente.
                    cabecera.EsValido = false;
                    cabecera.MensajeError = "¡Comprobante ya registrado previamente en el sistema! ";
                    continue;
                }


                // 1. Validar Pagador (Razon Social)
                cmd.CommandText = @"
                    SELECT TOP 1 id, ISNULL(razon_social, nombres) AS nombre_mostrar 
                    FROM personas_comerciales 
                    WHERE ISNULL(razon_social, '') LIKE @razon 
                       OR LTRIM(RTRIM(ISNULL(nombres, '') + ' ' + ISNULL(apellido_paterno, ''))) LIKE @razon
                       OR ISNULL(nombre_comercial, '') LIKE @razon";

                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@razon", "%" + cabecera.RazonSocialExcel.Trim() + "%");

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        cabecera.PagadorSistemaId = Convert.ToInt32(reader["id"]);
                        cabecera.RazonSocialSistema = reader["nombre_mostrar"]?.ToString() ?? cabecera.RazonSocialExcel;
                    }
                    else
                    {
                        cabecera.EsValido = false;
                        cabecera.MensajeError += "Razón Social no encontrada. ";
                    }
                }

                // 2. Validar Colegio/Institución
                cmd.CommandText = @"
                    SELECT TOP 1 id, ISNULL(razon_social, nombres) AS nombre_mostrar 
                    FROM personas_comerciales 
                    WHERE ISNULL(razon_social, '') LIKE @inst 
                       OR LTRIM(RTRIM(ISNULL(nombres, '') + ' ' + ISNULL(apellido_paterno, ''))) LIKE @inst
                       OR ISNULL(nombre_comercial, '') LIKE @inst";

                cmd.Parameters.Clear();
                AgregarParametro(cmd, "@inst", "%" + cabecera.ClienteExcel.Trim() + "%");

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        cabecera.ColegioSistemaId = Convert.ToInt32(reader["id"]);
                        cabecera.ClienteSistema = reader["nombre_mostrar"]?.ToString() ?? cabecera.ClienteExcel;
                    }
                    else
                    {
                        cabecera.EsValido = false;
                        cabecera.MensajeError += "Colegio no encontrado. ";
                    }
                }

                // 3. Validar Detalles (Productos)
                foreach (var detalle in cabecera.Detalles)
                {
                    cmd.CommandText = "SELECT TOP 1 id, descripcion FROM productos WHERE descripcion = @prod";
                    cmd.Parameters.Clear();
                    AgregarParametro(cmd, "@prod", detalle.DescripcionExcel.Trim());

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            detalle.ProductoSistemaId = Convert.ToInt32(reader["id"]);
                            detalle.DescripcionSistema = reader["descripcion"].ToString();
                        }
                        else
                        {
                            detalle.EsValido = false;
                            cabecera.EsValido = false;
                            cabecera.MensajeError += $"Producto '{detalle.DescripcionExcel}' no encontrado. ";
                        }
                    }

                    // 4. Validar Códigos (Kardex)
                    if (detalle.ProductoSistemaId.HasValue)
                    {
                        foreach (var codigo in detalle.Codigos)
                        {
                            try
                            {
                                var resultadoKardex = await _facturacionService.ValidarCodigoParaVentaAsync(detalle.ProductoSistemaId.Value, codigo.CodigoExcel);

                                codigo.CodigoCreadoId = resultadoKardex.Id;
                                codigo.MovimientoKardexId = resultadoKardex.MovimientoId;
                            }
                            catch (Exception ex)
                            {
                                codigo.EsValido = false;
                                codigo.Error = ex.Message;

                                detalle.EsValido = false;
                                cabecera.EsValido = false;
                            }
                        }
                    }
                }
            }
        }



        /// <summary>
        /// Guarda en BD todos los comprobantes validados con distribución impositiva exacta por línea.
        /// </summary>
        public async Task<int> TransferirComprobantesValidosAsync(List<ImportacionCabeceraDTO> comprobantesValidos, int idUsuario)
        {
            int countExito = 0;
            var procesables = comprobantesValidos.Where(c => c.EsValido).ToList();

            foreach (var excelCab in procesables)
            {
                var cabeceraDB = new FacturacionCabecera
                {
                    TipoDocumento = excelCab.DocumentoExcel.ToUpper().Contains("FACTURA") ? "01" :
                                    excelCab.DocumentoExcel.ToUpper().Contains("BOLETA") ? "02" : "03",
                    SerieDocumento = excelCab.Serie,
                    NumeroDocumento = excelCab.Numero.PadLeft(7, '0'),
                    FechaEmision = excelCab.Fecha,
                    PuntoVentaId = 1, // Cambiar por el ID de sede dinámico si corresponde
                    CompradorId = excelCab.PagadorSistemaId,
                    InstitucionId = excelCab.ColegioSistemaId,
                    Observacion = "Importado desde Excel Nisira",
                    TotalGravado = excelCab.Afecto,
                    TotalExonerado = excelCab.Exonerado,
                    TotalIgv = excelCab.IGV,
                    ImporteTotal = excelCab.Total,
                    PorcentajeIgv = 18.00m,
                    EstadoRegistro = true,
                    UsuarioId = idUsuario
                };

                int lineIndex = 1; // Control del correlativo de líneas del documento

                foreach (var excelDet in excelCab.Detalles)
                {
                    // 🌟 FACTOR DE PROPORCIÓN: Calcula cuánto pesa este ítem en el total del documento
                    decimal proporcion = excelCab.Total > 0 ? excelDet.Importe / excelCab.Total : 0;

                    var detalleDB = new FacturacionDetalle
                    {
                        ProductoId = excelDet.ProductoSistemaId!.Value,
                        Cantidad = excelDet.Cantidad,
                        PrecioUnitario = excelDet.PrecioUnitario,
                        ImporteTotal = excelDet.Importe,
                        NumeroLinea = lineIndex++, // Asigna 1, 2, 3... correlativamente

                        // 🌟 DISTRIBUCIÓN MATEMÁTICA EXACTA AL CENTAVO
                        ValorGravado = Math.Round(excelCab.Afecto * proporcion, 2),
                        ValorExonerado = Math.Round(excelCab.Exonerado * proporcion, 2),
                        ValorIgv = Math.Round(excelCab.IGV * proporcion, 2),
                        ValorInafecto = 0,


                        MovimientoId = excelDet.Codigos.First().MovimientoKardexId!.Value
                    };

                    foreach (var excelCod in excelDet.Codigos)
                    {
                        detalleDB.Codigos.Add(new FacturacionDetalleCodigos
                        {
                            CodigoCreadoId = excelCod.CodigoCreadoId!.Value
                        });
                    }

                    cabeceraDB.Detalles.Add(detalleDB);
                }

                try
                {
                    int serieId = await ObtenerIdSerieAsync(cabeceraDB.SerieDocumento);
                    await _facturacionService.GuardarComprobanteAsync(cabeceraDB, serieId);
                    countExito++;
                }
                catch (Exception ex)
                {
                    excelCab.EsValido = false;
                    excelCab.MensajeError = "Error al Transferir: " + ex.Message;
                    throw new Exception($"Falló la inserción del documento {cabeceraDB.SerieDocumento}-{cabeceraDB.NumeroDocumento}. Detalle técnico SQL: {ex.Message}");
                }
            }

            return countExito;
        }

        private async Task<int> ObtenerIdSerieAsync(string numeroSerie)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 id FROM series_documentos WHERE num_seri = @serie";
            AgregarParametro(cmd, "@serie", numeroSerie);

            object? result = await cmd.ExecuteScalarAsync();
            if (result == null) throw new Exception($"La serie '{numeroSerie}' no está registrada en el sistema.");

            return Convert.ToInt32(result);
        }
    }
}