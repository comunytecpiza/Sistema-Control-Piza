#nullable enable

using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Facturación.AplicativoDeAlmacen.Models.Facturación; // Tu namespace correcto para los modelos
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Transferencias;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        public async Task<List<string>> LeerCodigosDesdeExcelAsync(string ruta)
        {
            var lista = new List<string>();
            using var workbook = new XLWorkbook(ruta);
            var ws = workbook.Worksheet(1);
            var used = ws.RangeUsed();
            if (used == null) return lista;

            foreach (var row in used.Rows())
            {
                foreach (var cell in row.Cells())
                {
                    var valor = cell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(valor))
                    {
                        // 🌟 LIMPIEZA TOTAL: Reemplazamos apostrófes por guiones
                        // Esto hace que "LMA4 C26'V0000009" se convierta en "LMA4 C26-V0000009"
                        string codigoLimpio = valor.Replace("'", "-");

                        lista.Add(codigoLimpio);
                    }
                }
            }
            return lista;
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
    List<string> codigosAprobados,
    int usuarioId,
    int almacenId,
    string nombreArchivoOrigen,
    IProgress<int> progress = null)
        {
            if (codigosAprobados == null || !codigosAprobados.Any()) return;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (System.Data.Common.DbConnection)conn;
                await dbConn.OpenAsync();

                using (var transaction = dbConn.BeginTransaction())
                {
                    try
                    {
                        int cantidad = codigosAprobados.Count;

                        string codigoDesde = codigosAprobados.First().Replace("'", "-");
                        string codigoHasta = codigosAprobados.Last().Replace("'", "-");

                        int lastDashDesde = codigoDesde.LastIndexOf('-');
                        int desdeNum = (lastDashDesde >= 0 && int.TryParse(codigoDesde.Substring(lastDashDesde + 1), out int dNum)) ? dNum : 0;

                        int lastDashHasta = codigoHasta.LastIndexOf('-');
                        int hastaNum = (lastDashHasta >= 0 && int.TryParse(codigoHasta.Substring(lastDashHasta + 1), out int hNum)) ? hNum : 0;

                        string abreviaturaBase = lastDashDesde >= 0 ? codigoDesde.Substring(0, lastDashDesde) : codigoDesde;
                        string origenRegistro = $"EXCEL: {nombreArchivoOrigen}";

                        // 1. INSERT EN 'registro_codigos'
                        string queryRegistro = @"
                    INSERT INTO registro_codigos 
                    (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id, usuario_id, origen_registro, created_at) 
                    VALUES 
                    (@cId, @pId, @cant, @des, @has, @catId, @uId, @origen, GETDATE());";

                        string selectId = AplicativoDeAlmacen.Data.QueryAdapter.EsMySQL ? " SELECT LAST_INSERT_ID();" : " SELECT SCOPE_IDENTITY();";

                        int registroId;
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = AplicativoDeAlmacen.Data.QueryAdapter.FormatearConsulta(queryRegistro + selectId);

                            AgregarParametro(cmd, "@cId", coleccionId);
                            AgregarParametro(cmd, "@pId", productoId);
                            AgregarParametro(cmd, "@cant", cantidad);
                            AgregarParametro(cmd, "@des", codigoDesde);
                            AgregarParametro(cmd, "@has", codigoHasta);
                            AgregarParametro(cmd, "@catId", categoriaId);
                            AgregarParametro(cmd, "@uId", usuarioId > 0 ? usuarioId : 1);
                            AgregarParametro(cmd, "@origen", origenRegistro);

                            registroId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        
                        // 2. INSERT EN 'registro_rangos' (Quitamos 'cantidad' de la consulta porque es calculada en SQL)
                        string queryRango = @"
                        INSERT INTO registro_rangos 
                        (producto_id, categoria_producto_id, abreviatura_base, desde_num, hasta_num, movimiento_detalle_id, created_at, usuario_id) 
                        VALUES 
                        (@pId, @catId, @abrev, @dNum, @hNum, NULL, GETDATE(), @uId);";

                        using (var cmdRango = dbConn.CreateCommand())
                        {
                            cmdRango.Transaction = transaction;
                            cmdRango.CommandText = AplicativoDeAlmacen.Data.QueryAdapter.FormatearConsulta(queryRango);

                            AgregarParametro(cmdRango, "@pId", productoId);
                            AgregarParametro(cmdRango, "@catId", categoriaId);
                            AgregarParametro(cmdRango, "@abrev", abreviaturaBase);
                            AgregarParametro(cmdRango, "@dNum", desdeNum);
                            AgregarParametro(cmdRango, "@hNum", hastaNum);
                            AgregarParametro(cmdRango, "@uId", usuarioId > 0 ? usuarioId : 1);

                            await cmdRango.ExecuteNonQueryAsync();
                        }

                        // 3. INSERT MASIVO EN 'codigos_creados'
                        int batchSize = 1000;
                        for (int i = 0; i < cantidad; i += batchSize)
                        {
                            int currentBatch = Math.Min(batchSize, cantidad - i);
                            var queryBuilder = new System.Text.StringBuilder(@"
                        INSERT INTO codigos_creados 
                        (registro_codigo_id, codigo, estado_id, condicion_id, almacen_id, usuario_id, origen_creacion, created_at) 
                        VALUES ");

                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = transaction;

                                for (int j = 0; j < currentBatch; j++)
                                {
                                    int idx = i + j;
                                    string paramCod = $"@cod{idx}";
                                    string codigoGenerado = codigosAprobados[idx].Replace("'", "-");

                                    queryBuilder.Append($"({registroId}, {paramCod}, 1, 1, @almId, @usrId, 'EXCEL', GETDATE())");
                                    if (j < currentBatch - 1) queryBuilder.Append(", ");

                                    AgregarParametro(cmd, paramCod, codigoGenerado);
                                }

                                AgregarParametro(cmd, "@almId", almacenId);
                                AgregarParametro(cmd, "@usrId", usuarioId > 0 ? usuarioId : 1);

                                cmd.CommandText = AplicativoDeAlmacen.Data.QueryAdapter.FormatearConsulta(queryBuilder.ToString());
                                await cmd.ExecuteNonQueryAsync();
                            }

                            int pct = ((i + currentBatch) * 100) / cantidad;
                            progress?.Report(pct);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // ==========================================
        // OBTENER HISTORIAL COMPLETO DE TRANSFERENCIAS (BANDEJA CENTRAL/SUB-ALMACÉN)
        // ==========================================
        public async Task<List<TransaccionHeaderDTO>> ObtenerHistorialTransferenciasAsync(int miAlmacenId, DateTime? desde = null, DateTime? hasta = null, string? estadoFiltro = "TODOS")
        {
            var lista = new List<TransaccionHeaderDTO>();

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
            SELECT 
                m.id,
                CONCAT(m.serie_documento, '-', m.numero_documento) AS serie_numero,
                ISNULL(CONCAT(m.serie_guia, '-', m.numero_guia), 'SIN GUÍA') AS guia_remision,
                m.fecha_movimiento,
                ISNULL(m.almacen_origen_id, 1) AS almacen_origen_id,
                ISNULL(ao.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS origen_nombre,
                ISNULL(m.almacen_destino_id, 1) AS almacen_destino_id,
                ISNULL(ad.nombre, 'ALMACÉN CENTRAL TRUJILLO') AS destino_nombre,
                ISNULL(u.nombres, 'SISTEMA') AS emisor_nombre,
                ISNULL(mp.descripcion, 'TRANSFERENCIA') AS motivo_desc,
                ISNULL(m.observacion, '') AS observacion,
                COUNT(DISTINCT md.producto_id) AS total_productos,
                COUNT(mc.id) AS total_codigos,
                MAX(CASE WHEN cc.estado_id = 5 THEN 1 ELSE 0 END) AS es_pendiente
            FROM movimientos m WITH (NOLOCK)
            LEFT JOIN almacenes ao WITH (NOLOCK) ON m.almacen_origen_id = ao.id
            LEFT JOIN almacenes ad WITH (NOLOCK) ON m.almacen_destino_id = ad.id
            LEFT JOIN usuarios u WITH (NOLOCK) ON m.usuario_id = u.id
            LEFT JOIN motivo_productos mp WITH (NOLOCK) ON m.motivo_producto_id = mp.id
            INNER JOIN movimiento_detalles md WITH (NOLOCK) ON md.movimiento_id = m.id
            INNER JOIN movimiento_codigos mc WITH (NOLOCK) ON mc.movimiento_detalle_id = md.id
            INNER JOIN codigos_creados cc WITH (NOLOCK) ON mc.codigo_creado_id = cc.id
            WHERE m.estado_id = 1
              AND (m.motivo_producto_id IN (4, 10)) -- Transferencias
              -- 🌟 FILTRO JERÁRQUICO LOGÍSTICO
              AND (m.almacen_origen_id = @miAlmacen OR m.almacen_destino_id = @miAlmacen)";

                if (desde.HasValue) query += " AND m.fecha_movimiento >= @desde";
                if (hasta.HasValue) query += " AND m.fecha_movimiento <= @hasta";

                query += @"
            GROUP BY m.id, m.serie_documento, m.numero_documento, m.serie_guia, m.numero_guia, 
                     m.fecha_movimiento, m.almacen_origen_id, ao.nombre, m.almacen_destino_id, 
                     ad.nombre, u.nombres, mp.descripcion, m.observacion, m.created_at
            ORDER BY m.created_at DESC";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@miAlmacen", miAlmacenId);
                if (desde.HasValue) AgregarParametro(cmd, "@desde", desde.Value);
                if (hasta.HasValue) AgregarParametro(cmd, "@hasta", hasta.Value);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    bool esPendiente = Convert.ToInt32(rdr.GetValue(13)) == 1;

                    // Filtro dinámico en memoria si selecciona "PENDIENTES" o "RECEPCIONADOS"
                    if (estadoFiltro == "PENDIENTES" && !esPendiente) continue;
                    if (estadoFiltro == "RECEPCIONADOS" && esPendiente) continue;

                    lista.Add(new TransaccionHeaderDTO
                    {
                        MovimientoId = rdr.GetInt32(0),
                        SerieNumero = rdr.GetString(1),
                        GuiaRemision = rdr.GetString(2),
                        FechaMovimiento = rdr.IsDBNull(3) ? DateTime.Today : rdr.GetDateTime(3),
                        AlmacenOrigenId = rdr.GetInt32(4),
                        AlmacenOrigenNombre = rdr.GetString(5),
                        AlmacenDestinoId = rdr.GetInt32(6),
                        AlmacenDestinoNombre = rdr.GetString(7),
                        UsuarioEmisorNombre = rdr.GetString(8),
                        MotivoDescripcion = rdr.GetString(9),
                        Observacion = rdr.GetString(10),
                        TotalProductos = Convert.ToInt32(rdr.GetValue(11)),
                        TotalCodigos = Convert.ToInt32(rdr.GetValue(12)),
                        EsPendiente = esPendiente
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al cargar historial de transferencias: {ex.Message}");
            }

            return lista;
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