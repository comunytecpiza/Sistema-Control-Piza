using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using LiveChartsCore.Defaults;
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
namespace AplicativoDeAlmacen.Services.Reportes
{
    public class ReporteExcelService
    {

        public async Task ExportarSaldosProductosAsync(
        List<SaldoProductoItem> datos)
        {
            await Task.Run(() =>
            {

                using var wb =
                    new XLWorkbook();

                var ws =
                    wb.Worksheets.Add(
                        "Saldos Productos");

                ws.Cell(1, 1).Value = "Código";
                ws.Cell(1, 2).Value = "Descripción";
                ws.Cell(1, 3).Value = "Stock Inicial";
                ws.Cell(1, 4).Value = "Entradas";
                ws.Cell(1, 5).Value = "Salidas";
                ws.Cell(1, 6).Value = "Stock Final";

                int fila = 2;

                foreach (var x in datos)
                {
                    ws.Cell(fila, 1).Value = x.Codigo;
                    ws.Cell(fila, 2).Value = x.Descripcion;
                    ws.Cell(fila, 3).Value = x.StockInicial;
                    ws.Cell(fila, 4).Value = x.TotalIngresos;
                    ws.Cell(fila, 5).Value = x.TotalSalidas;
                    ws.Cell(fila, 6).Value = x.StockFinal;

                    fila++;
                }

                ws.Range(
                    1,
                    1,
                    fila - 1,
                    6)

                    .CreateTable();

                ws.Columns()
                    .AdjustToContents();

                string ruta = Path.Combine(

                    Path.GetTempPath(),

                    $"SaldosProductos_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"

                );

                wb.SaveAs(ruta);

                Process.Start(

                    new ProcessStartInfo
                    {
                        FileName = ruta,

                        UseShellExecute = true
                    });

            });

        }


        public void ExportarKardex(KardexFisicoReporte reporte)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kardex");

            // ==========================================
            // 1. TÍTULO PRINCIPAL (Fila 2)
            // ==========================================
            ws.Range("A2:I2").Merge();
            ws.Cell(2, 1).Value = "KARDEX ALMACÉN CENTRAL"; // Puedes concatenar el producto/fecha aquí si lo tienes
            ws.Cell(2, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Style.Font.FontSize = 12;
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ==========================================
            // 2. CABECERAS (Fila 3)
            // ==========================================
            int filaCabecera = 3;
            var cabeceras = new[] { "Fecha", "Tipo", "Documento", "Razón Social", "Ingreso", "Ing. Dev.", "Salida", "Sal. Dev.", "Saldo Final" };

            for (int i = 0; i < cabeceras.Length; i++)
            {
                ws.Cell(filaCabecera, i + 1).Value = cabeceras[i];
            }

            // Estilo de la cabecera (Fondo amarillo, negrita, bordes superior e inferior)
            var rangoCabecera = ws.Range(filaCabecera, 1, filaCabecera, 9);
            rangoCabecera.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699"); // Color amarillo oro claro
            rangoCabecera.Style.Font.Bold = true;
            rangoCabecera.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangoCabecera.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            rangoCabecera.Style.Border.BottomBorder = XLBorderStyleValues.Medium;

            // ==========================================
            // 3. DATOS DEL KARDEX (A partir de la fila 4)
            // ==========================================
            int fila = 4;
            foreach (var item in reporte.Detalles)
            {
                ws.Cell(fila, 1).Value = item.Fecha;
                ws.Cell(fila, 2).Value = item.Tipo;
                ws.Cell(fila, 3).Value = item.Registro;
                ws.Cell(fila, 4).Value = item.RazonSocialUbicacion;

                // Valores numéricos
                ws.Cell(fila, 5).Value = item.IngresoNormal;
                ws.Cell(fila, 6).Value = item.IngresoDevolucion;
                ws.Cell(fila, 7).Value = item.SalidaNormal;
                ws.Cell(fila, 8).Value = item.SalidaDevolucion;
                ws.Cell(fila, 9).Value = item.SaldoFinal;

                // Formato de miles y 2 decimales
                ws.Range(fila, 5, fila, 9).Style.NumberFormat.Format = "#,##0";

                // MAGIA VISUAL: Si es el SALDO INICIAL, pintamos todo de rojo
                if (!string.IsNullOrEmpty(item.Tipo) && item.Tipo.ToUpper().Contains("SALDO INICIAL"))
                {
                    ws.Range(fila, 1, fila, 9).Style.Font.FontColor = XLColor.Red;
                    ws.Range(fila, 1, fila, 9).Style.Font.Bold = true;
                }

                fila++;
            }



            // ==========================================
            // 4. FILA DE TOTALES (Justo debajo de los datos)
            // ==========================================
            var rangoTotales = ws.Range(fila, 1, fila, 9);

            // Borde superior grueso y texto rojo en negrita
            rangoTotales.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            rangoTotales.Style.Font.FontColor = XLColor.Red;
            rangoTotales.Style.Font.Bold = true;

            // Formato de miles y decimales para los totales
            ws.Range(fila, 5, fila, 9).Style.NumberFormat.Format = "#,##0";

            // Asignamos los totales directamente en las columnas correspondientes
            ws.Cell(fila, 5).Value = reporte.TotalIngresos;
            ws.Cell(fila, 6).Value = reporte.TotalDevIngresos;
            ws.Cell(fila, 7).Value = reporte.TotalSalidas;
            ws.Cell(fila, 8).Value = reporte.TotalDevSalidas;
            ws.Cell(fila, 9).Value = reporte.StockFinal;

            // ==========================================
            // 5. AJUSTES FINALES Y EXPORTACIÓN
            // ==========================================
            ws.Columns().AdjustToContents();

            string ruta = Path.Combine(Path.GetTempPath(), $"Kardex_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);

            Process.Start(new ProcessStartInfo
            {
                FileName = ruta,
                UseShellExecute = true
            });
        }
        public void ExportarKardexConCodigos(
            string nombreProducto,
            string tipoProducto,
            string unidadMedida,
            string origenDestino,
            DateTime desde,
            DateTime hasta,
            List<ConsultaMovimientoItem> movimientos,
            bool mostrarEnviados,
            bool mostrarDevueltos,
            bool incluirVendidos)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kardex");

            // ==========================================
            // 1. CABECERA INFORMATIVA
            // ==========================================
            ws.Cell("A2").Value = "Producto";
            ws.Cell("B2").Value = nombreProducto;
            ws.Range("B2:F2").Merge();

            ws.Cell("A3").Value = "Tipo";
            ws.Cell("B3").Value = tipoProducto;
            ws.Range("B3:F3").Merge();

            ws.Cell("A4").Value = "U. Medida";
            ws.Cell("B4").Value = unidadMedida;
            ws.Range("B4:F4").Merge();

            ws.Cell("A5").Value = "Origen / Destino";
            ws.Cell("B5").Value = origenDestino;
            ws.Range("B5:F5").Merge();

            ws.Cell("A6").Value = "Periodo";
            ws.Cell("B6").Value = $"Del {desde:dd/MM/yyyy} Al {hasta:dd/MM/yyyy}";
            ws.Range("B6:F6").Merge();

            var rangoEtiquetas = ws.Range("A2:A6");
            rangoEtiquetas.Style.Font.Bold = true;
            rangoEtiquetas.Style.Fill.BackgroundColor = XLColor.LightGray;

            // ==========================================
            // 2. CABECERA DE LA TABLA PRINCIPAL
            // ==========================================
            int filaActual = 9;

            ws.Cell(filaActual, 1).Value = "Fecha";
            ws.Cell(filaActual, 2).Value = "# Registro";
            ws.Cell(filaActual, 3).Value = "Razón Social / Ubicación";
            ws.Cell(filaActual, 4).Value = "# Guía";
            ws.Cell(filaActual, 5).Value = "Ingreso";
            ws.Cell(filaActual, 6).Value = "Salida";

            var headerRange = ws.Range(filaActual, 1, filaActual, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC000");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            filaActual = 10;
            int filaInicioDatos = filaActual;
            var codigosVendidosList = new List<(string Codigo, string Tipo)>();

            // ==========================================
            // 3. LLENADO DE MOVIMIENTOS
            // ==========================================
            if (movimientos != null && movimientos.Any())
            {
                foreach (var mov in movimientos)
                {
                    bool esSalida = mov.Salida > 0;
                    var colorFila = esSalida ? XLColor.Red : XLColor.Black;

                    ws.Cell(filaActual, 1).Value = mov.Fecha;
                    ws.Cell(filaActual, 1).Style.DateFormat.Format = "dd/MM/yyyy";

                    ws.Cell(filaActual, 2).Value = mov.NumeroRegistro;
                    ws.Cell(filaActual, 3).Value = mov.RazonSocialUbicacion;
                    ws.Cell(filaActual, 4).Value = mov.NumeroGuia;

                    ws.Cell(filaActual, 5).Value = mov.Ingreso;
                    ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";

                    ws.Cell(filaActual, 6).Value = mov.Salida;
                    ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";

                    ws.Range(filaActual, 1, filaActual, 6).Style.Font.FontColor = colorFila;
                    filaActual++;

                    // Mostrar códigos en la tabla solo si su check correspondiente está marcado
                    bool mostrarCodigosEnTabla = (esSalida && mostrarEnviados) || (!esSalida && mostrarDevueltos);

                    if (mostrarCodigosEnTabla && mov.CodigosAsociados != null && mov.CodigosAsociados.Any())
                    {
                        foreach (var cod in mov.CodigosAsociados)
                        {
                            var celda = ws.Cell(filaActual, 2);
                            celda.Value = $"CODIGO {cod.Codigo} - {cod.ColeccionTipo}";
                            celda.Style.Font.FontColor = colorFila;
                            filaActual++;
                        }
                    }

                    // Acumular para el bloque inferior si se marcó Vendidos
                    if (esSalida && incluirVendidos && mov.CodigosAsociados != null)
                    {
                        foreach (var cod in mov.CodigosAsociados)
                        {
                            codigosVendidosList.Add((cod.Codigo, cod.ColeccionTipo));
                        }
                    }
                }
            }

            int filaFinDatos = filaActual - 1;

            // ==========================================
            // 4. FILA DE TOTALES
            // ==========================================
            ws.Range(filaActual, 1, filaActual, 6).Style.Border.TopBorder = XLBorderStyleValues.Thin;

            ws.Cell(filaActual, 4).Value = "TOTAL";
            ws.Cell(filaActual, 4).Style.Font.Bold = true;
            ws.Cell(filaActual, 4).Style.Font.FontColor = XLColor.Red;
            ws.Cell(filaActual, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            var celdaTotalIngreso = ws.Cell(filaActual, 5);
            celdaTotalIngreso.Style.Font.Bold = true;
            celdaTotalIngreso.Style.Font.FontColor = XLColor.Red;
            celdaTotalIngreso.Style.NumberFormat.Format = "#,##0";

            var celdaTotalSalida = ws.Cell(filaActual, 6);
            celdaTotalSalida.Style.Font.Bold = true;
            celdaTotalSalida.Style.Font.FontColor = XLColor.Red;
            celdaTotalSalida.Style.NumberFormat.Format = "#,##0";

            if (filaFinDatos >= filaInicioDatos)
            {
                celdaTotalIngreso.FormulaA1 = $"SUM(E{filaInicioDatos}:E{filaFinDatos})";
                celdaTotalSalida.FormulaA1 = $"SUM(F{filaInicioDatos}:F{filaFinDatos})";
            }
            else
            {
                celdaTotalIngreso.Value = 0;
                celdaTotalSalida.Value = 0;
            }

            filaActual += 2;

            // ==========================================
            // 5. SECCIÓN ESTILIZADA DE CÓDIGOS VENDIDOS
            // ==========================================
            if (incluirVendidos && codigosVendidosList.Any())
            {
                var bannerRango = ws.Range(filaActual, 2, filaActual, 4);
                bannerRango.Merge();
                bannerRango.Value = $"📦 TOTAL CÓDIGOS VENDIDOS = {codigosVendidosList.Count:N0}";
                bannerRango.Style.Font.Bold = true;
                bannerRango.Style.Font.FontSize = 11;
                bannerRango.Style.Font.FontColor = XLColor.White;
                bannerRango.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
                bannerRango.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                filaActual++;

                ws.Cell(filaActual, 2).Value = "N°";
                ws.Cell(filaActual, 3).Value = "Código Físico / QR";
                ws.Cell(filaActual, 4).Value = "Colección / Tipo";

                var subHeader = ws.Range(filaActual, 2, filaActual, 4);
                subHeader.Style.Font.Bold = true;
                subHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
                subHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                subHeader.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                subHeader.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                filaActual++;

                int filaInicioVendidos = filaActual;
                int contador = 1;

                foreach (var item in codigosVendidosList)
                {
                    ws.Cell(filaActual, 2).Value = contador++;
                    ws.Cell(filaActual, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(filaActual, 3).Value = item.Codigo;
                    ws.Cell(filaActual, 4).Value = item.Tipo;

                    ws.Range(filaActual, 2, filaActual, 4).Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");
                    filaActual++;
                }

                var rangoTablaVendidos = ws.Range(filaInicioVendidos, 2, filaActual - 1, 4);
                rangoTablaVendidos.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                rangoTablaVendidos.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            }

            ws.Columns().AdjustToContents();

            // ==========================================
            // 6. GUARDAR Y ABRIR ARCHIVO
            // ==========================================
            string nombreArchivo = Path.Combine(Path.GetTempPath(), $"Kardex_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(nombreArchivo);

            Process.Start(new ProcessStartInfo
            {
                FileName = nombreArchivo,
                UseShellExecute = true
            });
        }
        public void ExportarKardexUbicacion(ConsultaMovimientoReporte reporte, string nombreProducto, string nombreUbicacion, DateTime desde, DateTime hasta)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kardex");

            // 1. TÍTULO GENERAL
            ws.Range("A1:F1").Merge().Value = "KARDEX DE PRODUCTOS X UBICACION";
            ws.Range("A1:F1").Style.Font.Bold = true;
            ws.Range("A1:F1").Style.Font.FontSize = 14;
            ws.Range("A1:F1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 2. CABECERA DE INFORMACION (Estilo moderno)
            ws.Cell(3, 1).Value = "Producto:"; ws.Cell(3, 2).Value = nombreProducto;
            ws.Cell(4, 1).Value = "U. Medida:"; ws.Cell(4, 2).Value = "PACKS";
            ws.Cell(5, 1).Value = "Ubicación:"; ws.Cell(5, 2).Value = nombreUbicacion;
            ws.Cell(6, 1).Value = "Periodo:"; ws.Cell(6, 2).Value = $"Del {desde:dd/MM/yyyy} Al {hasta:dd/MM/yyyy}";

            ws.Range("A3:A6").Style.Fill.BackgroundColor = XLColor.FromHtml("#B3E5FC"); // Azul claro
            ws.Range("A3:A6").Style.Font.Bold = true;

            // 3. CABECERA DE TABLA
            var cabeceras = new[] { "Fecha", "# Documento", "Procedencia / Razón Social", "Ingreso", "Salida", "Saldo" };
            for (int i = 0; i < cabeceras.Length; i++)
            {
                ws.Cell(8, i + 1).Value = cabeceras[i];
            }
            ws.Range("A8:F8").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
            ws.Range("A8:F8").Style.Font.Bold = true;

            // 4. DATOS Y CODIGOS INTEGRADOS
            int fila = 9;
            decimal saldoAcumulado = 0;

            foreach (var mov in reporte.Movimientos)
            {
                ws.Cell(fila, 1).Value = mov.Fecha;
                ws.Cell(fila, 2).Value = mov.NumeroRegistro;
                ws.Cell(fila, 3).Value = mov.RazonSocialUbicacion;

                // Formato condicional: Azul si es entrada
                ws.Cell(fila, 4).Value = mov.Ingreso;
                if (mov.Ingreso > 0) ws.Cell(fila, 4).Style.Font.FontColor = XLColor.Blue;

                ws.Cell(fila, 5).Value = mov.Salida;

                saldoAcumulado += (mov.Ingreso - mov.Salida);
                ws.Cell(fila, 6).Value = saldoAcumulado;

                fila++;

                // Sub-detalle de códigos integrados
                var codigos = reporte.Codigos.Where(c => c.NumeroRegistro == mov.NumeroRegistro);
                foreach (var cod in codigos)
                {
                    ws.Cell(fila, 1).Value = $"CODIGO {cod.Codigo} - {cod.ColeccionTipo}";
                    ws.Range(fila, 1, fila, 6).Merge().Style.Font.Italic = true;
                    ws.Range(fila, 1, fila, 6).Style.Font.FontColor = XLColor.Gray;
                    fila++;
                }
                // Espacio entre movimientos para estética
                fila++;
            }


            // CABECERA DETALLE (A la derecha, columna 8)
            ws.Cell(1, 8).Value = "DETALLE DE CÓDIGOS";
            ws.Range("H1:I1").Merge().Style.Fill.BackgroundColor = XLColor.DarkRed;
            ws.Range("H1:I1").Style.Font.FontColor = XLColor.White;

            ws.Cell(2, 8).Value = "Código";
            ws.Cell(2, 9).Value = "Colección/Tipo";
            ws.Range("H2:I2").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");

            int filaDet = 3;
            foreach (var cod in reporte.Codigos)
            {
                ws.Cell(filaDet, 8).Value = cod.Codigo;
                ws.Cell(filaDet, 9).Value = cod.ColeccionTipo;
                filaDet++;
            }

            ws.Columns().AdjustToContents();

            string ruta = Path.Combine(Path.GetTempPath(), $"KardexUbicacion_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }

        // ======================================================================================
        // EXPORTACIÓN: KARDEX VALORIZADO (FORMATO SUNAT 13.1)
        // ======================================================================================
        public void ExportarKardexValorizadoSunat(KardexValorizadoReporte reporte, string nombreProducto, DateTime desde, DateTime hasta)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Formato 13.1");

            // 1. TÍTULO GENERAL
            ws.Cell(1, 1).Value = $"KARDEX VALORIZADO ALMACEN CENTRAL - {nombreProducto} - DEL {desde:dd/MM/yyyy} AL {hasta:dd/MM/yyyy}";
            ws.Range("A1:M1").Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // 2. CABECERAS COMPLEJAS (Estilo SUNAT)
            ws.Cell(3, 1).Value = "Documento de Traslado, Comprobante de Pago, Documento Interno o Similar";
            ws.Range("A3:D4").Merge().Style.Fill.BackgroundColor = XLColor.LightBlue;

            ws.Cell(3, 5).Value = "Tipo de Operación";
            ws.Range("E3:E4").Merge().Style.Fill.BackgroundColor = XLColor.LightBlue;

            ws.Cell(3, 6).Value = "Entradas";
            ws.Range("F3:H3").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#D9D2E9"); // Morado claro

            ws.Cell(3, 9).Value = "Salidas";
            ws.Range("I3:K3").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE5CD"); // Naranja claro

            ws.Cell(3, 12).Value = "Saldo Final";
            ws.Range("L3:N3").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAD3"); // Verde claro

            // Sub Cabeceras (Entradas, Salidas, Saldos)
            string[] subCabeceras = { "Cantidad", "C. Unitario", "C. Total" };

            // Entradas
            ws.Cell(4, 6).Value = subCabeceras[0]; ws.Cell(4, 7).Value = subCabeceras[1]; ws.Cell(4, 8).Value = subCabeceras[2];
            ws.Range("F4:H4").Style.Fill.BackgroundColor = XLColor.FromHtml("#D9D2E9");

            // Salidas
            ws.Cell(4, 9).Value = subCabeceras[0]; ws.Cell(4, 10).Value = subCabeceras[1]; ws.Cell(4, 11).Value = subCabeceras[2];
            ws.Range("I4:K4").Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE5CD");

            // Saldos
            ws.Cell(4, 12).Value = subCabeceras[0]; ws.Cell(4, 13).Value = subCabeceras[1]; ws.Cell(4, 14).Value = subCabeceras[2];
            ws.Range("L4:N4").Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAD3");

            // Cabeceras de Documento
            string[] docCabeceras = { "Fecha", "Tipo", "Serie", "Número", "Motivo / Razón Social" };
            for (int i = 0; i < docCabeceras.Length; i++)
            {
                ws.Cell(5, i + 1).Value = docCabeceras[i];
                ws.Cell(5, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
            }

            // Aplicar bordes
            var rngCabecera = ws.Range("A3:N5");
            rngCabecera.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rngCabecera.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rngCabecera.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rngCabecera.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rngCabecera.Style.Font.Bold = true;

            // 3. VACIADO DE DATOS
            int fila = 6;

            // Fila de Saldo Inicial
            ws.Cell(fila, 1).Value = desde.ToString("dd/MM/yyyy");
            ws.Cell(fila, 3).Value = "SALDO INICIAL";
            ws.Cell(fila, 5).Value = "16";
            ws.Cell(fila, 12).Value = 0;
            ws.Cell(fila, 13).Value = 0;
            ws.Cell(fila, 14).Value = 0;
            ws.Range(fila, 1, fila, 14).Style.Font.FontColor = XLColor.Red;
            ws.Range(fila, 1, fila, 14).Style.Font.Bold = true;
            fila++;

            foreach (var item in reporte.Detalles)
            {
                ws.Cell(fila, 1).Value = item.Fecha?.ToString("dd/MM/yyyy");
                ws.Cell(fila, 2).Value = item.Registro.Contains("G") ? "09" : "00";

                var partesDoc = item.Registro.Split('-');
                ws.Cell(fila, 3).Value = partesDoc.Length > 0 ? partesDoc[0] : "";
                ws.Cell(fila, 4).Value = partesDoc.Length > 1 ? partesDoc[1] : item.Registro;
                ws.Cell(fila, 5).Value = item.Tipo;

                var formatoNumero = "#,##0";

                if (item.IngresoFisico > 0)
                {
                    ws.Cell(fila, 6).Value = item.IngresoFisico;
                    ws.Cell(fila, 7).Value = item.CostoUnitario;
                    ws.Cell(fila, 8).Value = item.IngresoValorado;
                }

                if (item.SalidaFisico > 0)
                {
                    ws.Cell(fila, 9).Value = item.SalidaFisico;
                    ws.Cell(fila, 10).Value = item.CostoUnitario;
                    ws.Cell(fila, 11).Value = item.SalidaValorado;
                }

                ws.Cell(fila, 12).Value = item.SaldoFisico;
                ws.Cell(fila, 13).Value = item.CostoPromedio;
                ws.Cell(fila, 14).Value = item.SaldoValorado;

                ws.Range(fila, 6, fila, 14).Style.NumberFormat.Format = formatoNumero;
                fila++;
            }

            // 4. TOTALES FINALES
            ws.Cell(fila, 5).Value = "TOTALES";
            ws.Cell(fila, 5).Style.Font.Bold = true;
            ws.Cell(fila, 5).Style.Font.FontColor = XLColor.Red;
            ws.Cell(fila, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Cell(fila, 6).Value = reporte.TotalIngresoFisico;
            ws.Cell(fila, 8).Value = reporte.TotalIngresoValorado;
            ws.Cell(fila, 9).Value = reporte.TotalSalidaFisico;
            ws.Cell(fila, 11).Value = reporte.TotalSalidaValorado;
            ws.Cell(fila, 12).Value = reporte.StockFinalFisico;
            ws.Cell(fila, 13).Value = reporte.StockFinalFisico > 0 ? Math.Round(reporte.SaldoFinalValorado / reporte.StockFinalFisico, 2) : 0;
            ws.Cell(fila, 14).Value = reporte.SaldoFinalValorado;

            var rngTotales = ws.Range(fila, 6, fila, 14);
            rngTotales.Style.Font.Bold = true;
            rngTotales.Style.Font.FontColor = XLColor.Red;
            rngTotales.Style.NumberFormat.Format = "#,##0";

            ws.Range(6, 1, fila, 14).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(6, 1, fila, 14).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            ws.Columns().AdjustToContents();

            string ruta = Path.Combine(Path.GetTempPath(), $"KardexValorizado_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }

        public void ExportarAnalisisVelas(List<FinancialPoint> velas, string nombreProducto, DateTime desde, DateTime hasta)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Trading Inventario");

            // Cabecera Principal
            ws.Cell(1, 1).Value = $"ANÁLISIS DE VOLATILIDAD DIARIA - {nombreProducto}";
            ws.Range("A1:F1").Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(2, 1).Value = $"Periodo: {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
            ws.Range("A2:F2").Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Cabeceras de Tabla
            string[] headers = { "Fecha", "Stock Apertura", "Máximo Diario (Ingresos)", "Mínimo Diario", "Stock Cierre", "Tendencia" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(4, i + 1).Value = headers[i];
                ws.Cell(4, i + 1).Style.Fill.BackgroundColor = XLColor.DarkSlateGray;
                ws.Cell(4, i + 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(4, i + 1).Style.Font.Bold = true;
            }

            int fila = 5;
            foreach (var vela in velas)
            {
                ws.Cell(fila, 1).Value = vela.Date.ToString("dd/MM/yyyy");
                ws.Cell(fila, 2).Value = vela.Open;
                ws.Cell(fila, 3).Value = vela.High;
                ws.Cell(fila, 4).Value = vela.Low;
                ws.Cell(fila, 5).Value = vela.Close;

                // Indicador visual en texto
                if (vela.Close > vela.Open)
                {
                    ws.Cell(fila, 6).Value = "🟢 ALZA (Ingresos)";
                    ws.Cell(fila, 6).Style.Font.FontColor = XLColor.Green;
                }
                else if (vela.Close < vela.Open)
                {
                    ws.Cell(fila, 6).Value = "🔴 BAJA (Salidas)";
                    ws.Cell(fila, 6).Style.Font.FontColor = XLColor.Red;
                }
                else
                {
                    ws.Cell(fila, 6).Value = "⚪ NEUTRO";
                }

                fila++;
            }

            ws.Range(4, 1, fila - 1, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 1, fila - 1, 6).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Columns().AdjustToContents();

            string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AnalisisVelas_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }


        public void ExportarComprobanteImpresion(
    string tipoDoc, string serieNumero, string fecha, string cliente,
    string dIdentidad, string institucion, string localidadZona,
    string observacion, string usuario, List<ItemGridDTO> items,
    decimal opGravadas, decimal opExoneradas, decimal igv, decimal totalVenta)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Comprobante");

            // ==========================================
            // 1. CABECERA GENERAL (Igual a tu imagen)
            // ==========================================
            ws.Cell("A1").Value = "REGISTRO DE DOCUMENTOS";
            ws.Range("A1:E1").Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            string[] labels = { "Documento", "Fecha", "Cliente", "D. Identidad", "Institución", "Localidad / Zona", "Observación", "Usuario" };
            string[] values = { $"{tipoDoc} N° {serieNumero}", fecha, cliente, dIdentidad, institucion, localidadZona, observacion, usuario };

            for (int i = 0; i < labels.Length; i++)
            {
                ws.Cell(i + 2, 1).Value = labels[i];
                ws.Cell(i + 2, 2).Value = values[i];
                ws.Range(i + 2, 2, i + 2, 5).Merge(); // Combinamos para que el texto largo quepa

                // Bordes a la cabecera
                ws.Range(i + 2, 1, i + 2, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // Negrita a los labels
            ws.Range("A2:A9").Style.Font.Bold = true;

            // ==========================================
            // 2. CABECERA DE LA GRILLA (Fila 11)
            // ==========================================
            int fila = 11;
            ws.Cell(fila, 1).Value = "Producto";
            ws.Cell(fila, 2).Value = "U. Medida";
            ws.Cell(fila, 3).Value = "Cantidad";
            ws.Cell(fila, 4).Value = "P. Unitario";
            ws.Cell(fila, 5).Value = "Importe";

            var rngHeaders = ws.Range(fila, 1, fila, 5);
            rngHeaders.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699"); // Amarillo
            rngHeaders.Style.Font.Bold = true;
            rngHeaders.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rngHeaders.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            rngHeaders.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            fila++;

            // ==========================================
            // 3. VACIADO DE ITEMS Y CÓDIGOS AGRUPADOS
            // ==========================================
            foreach (var item in items)
            {
                // Fila principal del producto
                ws.Cell(fila, 1).Value = item.DescripcionProducto;
                ws.Cell(fila, 2).Value = item.UnidadMedida;
                ws.Cell(fila, 3).Value = item.CanProd;
                ws.Cell(fila, 4).Value = item.PreUnit;
                ws.Cell(fila, 5).Value = item.ImpTota;

                ws.Range(fila, 3, fila, 5).Style.NumberFormat.Format = "#,##0";
                fila++;

                // Fila agrupada de códigos ( { COD1 } ; { COD2 } )
                if (item.Codigos != null && item.Codigos.Any())
                {
                    // Unimos todos los códigos con formato { CODIGO } separados por ;
                    string textoCodigos = string.Join(" ; ", item.Codigos.Select(c => $"{{ {c.CodigoString} }}"));

                    ws.Cell(fila, 1).Value = textoCodigos;
                    var rngCodigos = ws.Range(fila, 1, fila, 5).Merge();
                    rngCodigos.Style.Alignment.WrapText = true; // Permite salto de línea si son muchos códigos
                    rngCodigos.Style.Font.Italic = true;
                    rngCodigos.Style.Font.FontSize = 9;
                    rngCodigos.Style.Border.BottomBorder = XLBorderStyleValues.Thin; // Separador sutil
                    fila++;
                }
                else
                {
                    // Si no tiene códigos, solo ponemos la línea divisoria
                    ws.Range(fila - 1, 1, fila - 1, 5).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                }
            }

            // ==========================================
            // 4. TOTALES FINALES
            // ==========================================
            fila++;
            ws.Cell(fila, 4).Value = "Op. Gravadas"; ws.Cell(fila, 5).Value = opGravadas; fila++;
            ws.Cell(fila, 4).Value = "Op. Exoneradas"; ws.Cell(fila, 5).Value = opExoneradas; fila++;
            ws.Cell(fila, 4).Value = "I.G.V."; ws.Cell(fila, 5).Value = igv; fila++;

            ws.Cell(fila, 4).Value = "Total Venta";
            ws.Cell(fila, 5).Value = totalVenta;
            ws.Range(fila, 4, fila, 5).Style.Font.Bold = true;

            ws.Range(fila - 3, 5, fila, 5).Style.NumberFormat.Format = "#,##0";

            // ==========================================
            // 5. AUTOAJUSTE Y EXPORTACIÓN
            // ==========================================
            ws.Column(1).Width = 50; // Columna Producto ancha
            ws.Column(2).Width = 15;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 12;
            ws.Column(5).Width = 12;

            string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Comprobante_{serieNumero}_{DateTime.Now:HHmmss}.xlsx");
            wb.SaveAs(ruta);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }


        public void GenerarReporteIngreso(
            string numeroRegistro, string fecha, string motivo, string razonSocial,
            string direccion, string ubicacion, string guia, string observacion,
            List<VistaProductoGrid> productosGridList,
            List<VistaCodigoGrid> codigosGridList,
            List<RangoCodigoItem> rangosProcesadosGlobal)
        {
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Ingreso de Productos");

                // Título
                ws.Range("A1:E1").Merge();
                ws.Cell(1, 1).Value = "INGRESO DE PRODUCTOS - ALMACEN CENTRAL";
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cabecera de datos
                int r = 3;
                void PutKV(string key, string val)
                {
                    ws.Cell(r, 1).Value = key;
                    ws.Cell(r, 2).Value = val;
                    var rng = ws.Range(r, 1, r, 5);
                    rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rng.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    r++;
                }

                PutKV("# Registro", numeroRegistro);
                PutKV("Fecha", fecha);
                PutKV("Motivo", motivo);
                PutKV("Razón Social", razonSocial);
                PutKV("Dirección", direccion);
                PutKV("Ubicación", ubicacion);
                PutKV("# Guía", guia);
                PutKV("Observación", observacion);

                // Encabezado tabla productos
                int headerRow = r + 1;
                ws.Cell(headerRow, 1).Value = "Producto";
                ws.Cell(headerRow, 2).Value = "U. Medida";
                ws.Cell(headerRow, 3).Value = "Cantidad";
                ws.Cell(headerRow, 4).Value = "C. Unitario";
                ws.Range(headerRow, 1, headerRow, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
                ws.Range(headerRow, 1, headerRow, 4).Style.Font.Bold = true;
                ws.Range(headerRow, 1, headerRow, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                int fila = headerRow + 1;

                foreach (var p in productosGridList)
                {
                    ws.Cell(fila, 1).Value = p.Descripcion ?? p.CodigoProducto;
                    ws.Cell(fila, 2).Value = p.UnidadMedida;
                    ws.Cell(fila, 3).Value = p.Detalle?.CantidadIngreso ?? 0;
                    ws.Cell(fila, 4).Value = p.Detalle?.CostoUnitario ?? 0;
                    ws.Range(fila, 1, fila, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    fila++;

                    var codigos = codigosGridList.Where(c => c.ProductoId == p.ProductoId).ToList();

                    if ((codigos == null || codigos.Count == 0) && rangosProcesadosGlobal != null)
                    {
                        var rangosFallback = rangosProcesadosGlobal.Where(rg => rg.productoId == p.ProductoId).ToList();
                        foreach (var rg in rangosFallback)
                        {
                            for (int seq = rg.DesdeNum; seq <= rg.HastaNum; seq++)
                            {
                                codigos.Add(new VistaCodigoGrid { CodigoUnique = $"{rg.AbreviaturaBase}-{seq:D7}", ColeccionTipo = rg.ColeccionTipo, ProductoId = rg.productoId });
                            }
                        }
                    }

                    foreach (var code in codigos)
                    {
                        ws.Cell(fila, 1).Value = "";
                        ws.Cell(fila, 2).Value = code.CodigoUnique;
                        ws.Cell(fila, 3).Value = code.ColeccionTipo ?? string.Empty;
                        ws.Range(fila, 2, fila, 3).Style.Font.FontColor = XLColor.FromHtml("#333333");
                        fila++;
                    }
                    fila++; // Espacio entre productos
                }

                ws.Columns(1, 4).AdjustToContents();
                ws.Column(1).Width = 70;
                ws.Column(2).Width = 15;
                ws.Column(3).Width = 12;
                ws.Column(4).Width = 14;

                string ruta = Path.Combine(Path.GetTempPath(), $"IngresoProductos_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                wb.SaveAs(ruta);
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generando Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public void GenerarReporteSalida(
    string numeroRegistro, string fecha, string motivo, string cliente,
    string direccion, string ubicacion, string guia, string observacion,
    List<VistaProductoGrid> productosGridList,
    List<VistaCodigoGrid> codigosGridList)
        {
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Salida de Productos");

                // Título
                ws.Range("A1:E1").Merge();
                ws.Cell(1, 1).Value = "SALIDA DE PRODUCTOS - ALMACEN CENTRAL";
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cabecera de datos
                int r = 3;
                void PutKV(string key, string val)
                {
                    ws.Cell(r, 1).Value = key;
                    ws.Cell(r, 2).Value = val;
                    ws.Range(r, 1, r, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    r++;
                }

                PutKV("# Operación", numeroRegistro);
                PutKV("Fecha", fecha);
                PutKV("Motivo", motivo);
                PutKV("Cliente", cliente);
                PutKV("Dirección", direccion);
                PutKV("Ubicación", ubicacion);
                PutKV("# Guía", guia);
                PutKV("Observación", observacion);

                // Encabezado tabla productos
                int headerRow = r + 1;
                ws.Cell(headerRow, 1).Value = "Producto";
                ws.Cell(headerRow, 2).Value = "U. Medida";
                ws.Cell(headerRow, 3).Value = "Cantidad";
                ws.Cell(headerRow, 4).Value = "C. Unitario";
                ws.Range(headerRow, 1, headerRow, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#DC2626"); // Rojo Salidas
                ws.Range(headerRow, 1, headerRow, 4).Style.Font.FontColor = XLColor.White;
                ws.Range(headerRow, 1, headerRow, 4).Style.Font.Bold = true;

                int fila = headerRow + 1;
                foreach (var p in productosGridList)
                {
                    ws.Cell(fila, 1).Value = p.Descripcion;
                    ws.Cell(fila, 2).Value = p.UnidadMedida;
                    ws.Cell(fila, 3).Value = p.Detalle?.CantidadSalida ?? 0; // 🌟 AQUI USAMOS CantidadSalida
                    ws.Cell(fila, 4).Value = p.Detalle?.CostoUnitario ?? 0;
                    fila++;

                    var codigos = codigosGridList.Where(c => c.ProductoId == p.ProductoId);
                    foreach (var code in codigos)
                    {
                        ws.Cell(fila, 2).Value = code.CodigoUnique;
                        ws.Cell(fila, 3).Value = code.ColeccionTipo;
                        fila++;
                    }
                }

                ws.Columns().AdjustToContents();
                string ruta = Path.Combine(Path.GetTempPath(), $"Salida_{numeroRegistro}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                wb.SaveAs(ruta);
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generando Excel: {ex.Message}");
            }
        }


        public void ExportarHistorialPorCodigo(string nombreAlmacen, string codigoCompleto, string descripcionProducto, List<KardexFisicoItem> historial)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Historial Código");
                worksheet.ShowGridLines = true;

                // 🌟 TÍTULO PRINCIPAL
                worksheet.Cell("A1").Value = $"HISTORIAL {nombreAlmacen.ToUpper()} - CODIGO {codigoCompleto}";
                worksheet.Range("A1:G1").Merge();
                worksheet.Cell("A1").Style.Font.Bold = true;
                worksheet.Cell("A1").Style.Font.FontSize = 14;
                worksheet.Cell("A1").Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                worksheet.Cell("A1").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#E5E7EB");

                // 🌟 DATOS DE CABECERA (Producto)
                worksheet.Cell("A3").Value = "Producto";
                worksheet.Cell("B3").Value = descripcionProducto;
                worksheet.Range("B3:G3").Merge();
                worksheet.Cell("A3").Style.Font.Bold = true;
                worksheet.Cell("A3").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F3F4F6");

                // 🌟 ENCABEZADOS DE LA GRILLA DE MOVIMIENTOS
                int filaInicio = 5;
                worksheet.Cell(filaInicio, 1).Value = "Fecha";
                worksheet.Cell(filaInicio, 2).Value = "# Registro";
                worksheet.Cell(filaInicio, 3).Value = "Razón Social / Ubicación";
                worksheet.Cell(filaInicio, 4).Value = "# Guía";
                worksheet.Cell(filaInicio, 5).Value = "Producto";
                worksheet.Cell(filaInicio, 6).Value = "Ingreso";
                worksheet.Cell(filaInicio, 7).Value = "Salida";

                var headerRange = worksheet.Range(filaInicio, 1, filaInicio, 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.Black;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FCD34D");
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // 🌟 LLENADO DE FILAS
                int filaActual = filaInicio + 1;
                decimal totalIngresos = 0;
                decimal totalSalidas = 0;

                foreach (var item in historial)
                {
                    worksheet.Cell(filaActual, 1).Value = item.Fecha?.ToString("dd/MM/yyyy") ?? "";
                    worksheet.Cell(filaActual, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    worksheet.Cell(filaActual, 2).Value = item.Registro;
                    worksheet.Cell(filaActual, 2).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    worksheet.Cell(filaActual, 3).Value = item.RazonSocialUbicacion;

                    worksheet.Cell(filaActual, 4).Value = item.Guia;
                    worksheet.Cell(filaActual, 4).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    worksheet.Cell(filaActual, 5).Value = descripcionProducto;

                    worksheet.Cell(filaActual, 6).Value = item.IngresoNormal;
                    worksheet.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";

                    worksheet.Cell(filaActual, 7).Value = item.SalidaNormal;
                    worksheet.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";

                    totalIngresos += item.IngresoNormal;
                    totalSalidas += item.SalidaNormal;

                    filaActual++;
                }

                // 🌟 FILA DE TOTALES
                worksheet.Cell(filaActual, 5).Value = "";
                worksheet.Cell(filaActual, 6).Value = totalIngresos;
                worksheet.Cell(filaActual, 6).Style.Font.Bold = true;
                worksheet.Cell(filaActual, 6).Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                worksheet.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";

                worksheet.Cell(filaActual, 7).Value = totalSalidas;
                worksheet.Cell(filaActual, 7).Style.Font.Bold = true;
                worksheet.Cell(filaActual, 7).Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                worksheet.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";

                // Bordes y ajustes
                var tableRange = worksheet.Range(filaInicio, 1, filaActual, 7);
                tableRange.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                tableRange.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                worksheet.Columns().AdjustToContents();

                // 🌟 CREACIÓN Y APERTURA DE ARCHIVO TEMPORAL (Sin diálogo previo de guardado)
                string archivoTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Historial_Codigo_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                workbook.SaveAs(archivoTemp);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(archivoTemp) { UseShellExecute = true });
            }
        }
    }
}