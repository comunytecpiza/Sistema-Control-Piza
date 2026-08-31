using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using LiveChartsCore.Defaults;
using System;


using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Data;
using System.Diagnostics;

using System.Threading.Tasks;

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
        public void ExportarKardexUbicacion(ConsultaMovimientoReporte reporte, string nombreProducto, string nombreUbicacion, DateTime desde, DateTime hasta, bool incluirCodigosPorFila = true, bool incluirTablaLateral = true)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kardex");

            ws.Range("A1:G1").Merge().Value = "KARDEX DE PRODUCTOS X UBICACION";
            ws.Range("A1:G1").Style.Font.Bold = true;
            ws.Range("A1:G1").Style.Font.FontSize = 14;
            ws.Range("A1:G1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(3, 1).Value = "Producto:"; ws.Cell(3, 2).Value = nombreProducto;
            ws.Cell(4, 1).Value = "U. Medida:"; ws.Cell(4, 2).Value = "PACKS";
            ws.Cell(5, 1).Value = "Ubicación:"; ws.Cell(5, 2).Value = string.IsNullOrWhiteSpace(nombreUbicacion) ? "TODAS" : nombreUbicacion;
            ws.Cell(6, 1).Value = "Periodo:"; ws.Cell(6, 2).Value = $"Del {desde:dd/MM/yyyy} Al {hasta:dd/MM/yyyy}";

            ws.Range("A3:A6").Style.Fill.BackgroundColor = XLColor.FromHtml("#B3E5FC");
            ws.Range("A3:A6").Style.Font.Bold = true;

            var cabeceras = new[] { "Fecha", "# Documento", "Guía", "Procedencia / Razón Social", "Ingreso", "Salida", "Saldo" };
            for (int i = 0; i < cabeceras.Length; i++) ws.Cell(8, i + 1).Value = cabeceras[i];
            ws.Range("A8:G8").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
            ws.Range("A8:G8").Style.Font.Bold = true;
            ws.Range("A8:G8").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int fila = 9;
            decimal saldoAcumulado = 0;

            foreach (var mov in reporte.Movimientos)
            {
                string regLimpio = mov.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "";

                ws.Cell(fila, 1).Value = mov.Fecha.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(fila, 2).Value = mov.NumeroRegistro;
                ws.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(fila, 3).Value = mov.NumeroGuia;
                ws.Cell(fila, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(fila, 4).Value = mov.RazonSocialUbicacion;

                ws.Cell(fila, 5).Value = mov.Ingreso;
                ws.Cell(fila, 5).Style.NumberFormat.Format = "#,##0";
                if (mov.Ingreso > 0) ws.Cell(fila, 5).Style.Font.FontColor = XLColor.Blue;

                ws.Cell(fila, 6).Value = mov.Salida;
                ws.Cell(fila, 6).Style.NumberFormat.Format = "#,##0";
                if (mov.Salida > 0) ws.Cell(fila, 6).Style.Font.FontColor = XLColor.Red;

                if (!mov.IsAnulado) saldoAcumulado += (mov.Ingreso - mov.Salida);
                ws.Cell(fila, 7).Value = saldoAcumulado;
                ws.Cell(fila, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(fila, 7).Style.Font.Bold = true;

                fila++;

                if (incluirCodigosPorFila)
                {
                    var codigos = reporte.Codigos
                        .Where(c => (mov.MovimientoDetalleId > 0 && c.MovimientoDetalleId == mov.MovimientoDetalleId)
                                 || (c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) && c.ProductoId == mov.ProductoId))
                        .ToList();

                    foreach (var cod in codigos)
                    {
                        ws.Cell(fila, 1).Value = $"CODIGO {cod.Codigo} - {cod.ColeccionTipo}";
                        ws.Range(fila, 1, fila, 7).Merge().Style.Font.Italic = true;
                        ws.Range(fila, 1, fila, 7).Style.Font.FontColor = XLColor.FromHtml("#334155");
                        fila++;
                    }
                }
            }

            // 🌟 TABLA LATERAL DERECHA (Condicional)
            if (incluirTablaLateral && reporte.Codigos.Any())
            {
                ws.Cell(1, 9).Value = "DETALLE DE CÓDIGOS AUDITADOS";
                ws.Range("I1:J1").Merge().Style.Fill.BackgroundColor = XLColor.DarkRed;
                ws.Range("I1:J1").Style.Font.FontColor = XLColor.White;
                ws.Range("I1:J1").Style.Font.Bold = true;
                ws.Range("I1:J1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(2, 9).Value = "Código";
                ws.Cell(2, 10).Value = "Colección / Tipo";
                ws.Range("I2:J2").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
                ws.Range("I2:J2").Style.Font.Bold = true;

                int filaDet = 3;
                foreach (var cod in reporte.Codigos)
                {
                    ws.Cell(filaDet, 9).Value = cod.Codigo;
                    ws.Cell(filaDet, 10).Value = cod.ColeccionTipo;
                    filaDet++;
                }
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 22;

            string ruta = Path.Combine(Path.GetTempPath(), $"KardexUbicacion_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
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


        public void ExportarKardexGeneralEntidad(
    string entidadNombre,
    DateTime desde,
    DateTime hasta,
    List<ConsultaMovimientoItem> movimientos,
    List<ConsultaCodigoItem> todosLosCodigos,
    bool mostrarEnviados,
    bool mostrarDevueltos,
    bool incluirVendidos)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Balance General");

            // 1. CABECERA PRINCIPAL AUDITORÍA
            ws.Range("A2:G2").Merge().Value = "ESTADO DE CUENTA Y TRAZABILIDAD POR ENTIDAD";
            ws.Range("A2:G2").Style.Font.Bold = true;
            ws.Range("A2:G2").Style.Font.FontSize = 13;
            ws.Range("A2:G2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range("A2:G2").Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            ws.Range("A2:G2").Style.Font.FontColor = XLColor.White;

            ws.Cell("A3").Value = "Entidad / Destino:"; ws.Cell("B3").Value = entidadNombre; ws.Range("B3:G3").Merge();
            ws.Cell("A4").Value = "Periodo:"; ws.Cell("B4").Value = $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}"; ws.Range("B4:G4").Merge();
            ws.Cell("A5").Value = "Generado el:"; ws.Cell("B5").Value = $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}"; ws.Range("B5:G5").Merge();

            var rangoEtiquetas = ws.Range("A3:A5");
            rangoEtiquetas.Style.Font.Bold = true;
            rangoEtiquetas.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            ws.Range("A3:G5").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range("A3:G5").Style.Border.InsideBorder = XLBorderStyleValues.Hair;

            int filaActual = 7;
            decimal granTotalEnviado = 0;
            decimal granTotalDevuelto = 0;

            var gruposProducto = movimientos.GroupBy(m => m.ProductoId).ToList();

            foreach (var grupo in gruposProducto)
            {
                string productoTitulo = grupo.FirstOrDefault()?.RazonSocialUbicacion.Split('-')[0].Trim() ?? $"Producto ID: {grupo.Key}";

                // Banner del Producto
                var bannerProd = ws.Range(filaActual, 1, filaActual, 7);
                bannerProd.Merge().Value = $"📦 PRODUCTO: {productoTitulo.ToUpper()}";
                bannerProd.Style.Font.Bold = true;
                bannerProd.Style.Fill.BackgroundColor = XLColor.FromHtml("#3B82F6");
                bannerProd.Style.Font.FontColor = XLColor.White;
                filaActual++;

                // Cabecera de Tabla
                ws.Cell(filaActual, 1).Value = "Fecha";
                ws.Cell(filaActual, 2).Value = "# Comprobante";
                ws.Cell(filaActual, 3).Value = "# Guía";
                ws.Cell(filaActual, 4).Value = "Procedencia / Destino";
                ws.Cell(filaActual, 5).Value = "Enviado";
                ws.Cell(filaActual, 6).Value = "Devuelto";
                ws.Cell(filaActual, 7).Value = "En Poder (Saldo)";

                var subHeader = ws.Range(filaActual, 1, filaActual, 7);
                subHeader.Style.Font.Bold = true;
                subHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
                subHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                subHeader.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                subHeader.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                filaActual++;

                int filaInicioSeccion = filaActual;
                decimal saldoProdAcumulado = 0;

                foreach (var mov in grupo)
                {
                    bool esSalida = mov.Salida > 0;
                    saldoProdAcumulado += (mov.Salida - mov.Ingreso);

                    ws.Cell(filaActual, 1).Value = mov.Fecha.ToString("dd/MM/yyyy HH:mm");
                    ws.Cell(filaActual, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(filaActual, 2).Value = mov.NumeroRegistro;
                    ws.Cell(filaActual, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(filaActual, 3).Value = mov.NumeroGuia;
                    ws.Cell(filaActual, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(filaActual, 4).Value = mov.RazonSocialUbicacion;

                    ws.Cell(filaActual, 5).Value = mov.Salida;
                    ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";
                    if (mov.Salida > 0) ws.Cell(filaActual, 5).Style.Font.FontColor = XLColor.Red;

                    ws.Cell(filaActual, 6).Value = mov.Ingreso;
                    ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";
                    if (mov.Ingreso > 0) ws.Cell(filaActual, 6).Style.Font.FontColor = XLColor.Green;

                    ws.Cell(filaActual, 7).Value = saldoProdAcumulado;
                    ws.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(filaActual, 7).Style.Font.Bold = true;
                    ws.Cell(filaActual, 7).Style.Font.FontColor = XLColor.Blue;

                    filaActual++;

                    // Códigos asociados a este movimiento
                    bool imprimirCodigos = (esSalida && mostrarEnviados) || (!esSalida && mostrarDevueltos);
                    if (imprimirCodigos && mov.CodigosAsociados != null && mov.CodigosAsociados.Any())
                    {
                        foreach (var cod in mov.CodigosAsociados)
                        {
                            ws.Cell(filaActual, 2).Value = $"• {cod.Codigo} ({cod.ColeccionTipo})";
                            ws.Range(filaActual, 2, filaActual, 4).Merge().Style.Font.Italic = true;
                            ws.Range(filaActual, 2, filaActual, 4).Style.Font.FontColor = XLColor.Gray;
                            filaActual++;
                        }
                    }
                }

                // Subtotales del producto
                decimal totalProdSal = grupo.Sum(x => x.Salida);
                decimal totalProdIng = grupo.Sum(x => x.Ingreso);
                granTotalEnviado += totalProdSal;
                granTotalDevuelto += totalProdIng;

                ws.Cell(filaActual, 4).Value = "SUBTOTAL PRODUCTO:";
                ws.Cell(filaActual, 4).Style.Font.Bold = true;
                ws.Cell(filaActual, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(filaActual, 5).Value = totalProdSal;
                ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 5).Style.Font.Bold = true;

                ws.Cell(filaActual, 6).Value = totalProdIng;
                ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 6).Style.Font.Bold = true;

                ws.Cell(filaActual, 7).Value = saldoProdAcumulado;
                ws.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 7).Style.Font.Bold = true;
                ws.Cell(filaActual, 7).Style.Font.FontColor = XLColor.Blue;

                ws.Range(filaActual, 1, filaActual, 7).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                ws.Range(filaActual, 1, filaActual, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                filaActual += 2;
            }

            // 🌟 RESUMEN CONTABLE FINAL
            var celdaGranTotal = ws.Range(filaActual, 1, filaActual, 7);
            celdaGranTotal.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
            celdaGranTotal.Style.Font.FontColor = XLColor.White;
            celdaGranTotal.Style.Font.Bold = true;

            ws.Cell(filaActual, 4).Value = "GRAN TOTAL CONSOLIDADO:";
            ws.Cell(filaActual, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Cell(filaActual, 5).Value = granTotalEnviado;
            ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";

            ws.Cell(filaActual, 6).Value = granTotalDevuelto;
            ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";

            ws.Cell(filaActual, 7).Value = (granTotalEnviado - granTotalDevuelto);
            ws.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";
            filaActual += 2;

            // Sección opcional de Códigos Pendientes en su poder
            if (incluirVendidos && todosLosCodigos != null && todosLosCodigos.Any())
            {
                var codigosNetosEnPoder = todosLosCodigos
                    .GroupBy(c => c.Codigo)
                    .Where(g => g.Count(x => x.TipoMovimiento == "SALIDA") > g.Count(x => x.TipoMovimiento == "ENTRADA"))
                    .Select(g => g.First())
                    .ToList();

                if (codigosNetosEnPoder.Any())
                {
                    var bannerCodigos = ws.Range(filaActual, 2, filaActual, 4);
                    bannerCodigos.Merge().Value = $"📋 BALANCE CONSOLIDADO DE CÓDIGOS PENDIENTES EN PODER ({codigosNetosEnPoder.Count})";
                    bannerCodigos.Style.Font.Bold = true;
                    bannerCodigos.Style.Fill.BackgroundColor = XLColor.FromHtml("#4338CA");
                    bannerCodigos.Style.Font.FontColor = XLColor.White;
                    bannerCodigos.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    filaActual++;

                    ws.Cell(filaActual, 2).Value = "N°";
                    ws.Cell(filaActual, 3).Value = "Código QR / Físico";
                    ws.Cell(filaActual, 4).Value = "Producto / Colección";
                    ws.Range(filaActual, 2, filaActual, 4).Style.Font.Bold = true;
                    ws.Range(filaActual, 2, filaActual, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E7FF");
                    filaActual++;

                    int n = 1;
                    foreach (var cod in codigosNetosEnPoder)
                    {
                        ws.Cell(filaActual, 2).Value = n++;
                        ws.Cell(filaActual, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(filaActual, 3).Value = cod.Codigo;
                        ws.Cell(filaActual, 4).Value = cod.ColeccionTipo;
                        filaActual++;
                    }
                }
            }

            ws.Columns().AdjustToContents();

            string ruta = Path.Combine(Path.GetTempPath(), $"EstadoCuenta_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });
        }


        // 🌟 1. REPORTE ESPECÍFICO (Filtrado por un solo producto)
        public void ExportarKardexUbicacionEspecifico(ConsultaMovimientoReporte reporte, string producto, string ubicacion, DateTime desde, DateTime hasta)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                FileName = $"Kardex_Ubicacion_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (saveDialog.ShowDialog() != true) return;

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Kardex Específico");

            // Cabecera del Reporte
            ws.Cell("A1").Value = "KARDEX POR UBICACIÓN (ESPECÍFICO)";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;

            ws.Cell("A2").Value = $"Producto: {producto}";
            ws.Cell("A3").Value = $"Ubicación: {(string.IsNullOrWhiteSpace(ubicacion) ? "TODAS" : ubicacion)}";
            ws.Cell("A4").Value = $"Periodo: Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";

            int fila = 6;
            string[] headers = { "Fecha", "Documento", "Guía", "Ubicación / Entidad", "Ingreso", "Salida", "Saldo Acumulado", "Códigos Registrados" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(fila, i + 1).Value = headers[i];
                ws.Cell(fila, i + 1).Style.Font.Bold = true;
                ws.Cell(fila, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                ws.Cell(fila, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            fila++;
            decimal saldoCorrelativo = 0;

            foreach (var mov in reporte.Movimientos)
            {
                string regLimpio = mov.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "";

                // 🌟 Filtro exacto de códigos por detalle y producto de la fila
                var codigosMov = reporte.Codigos
                    .Where(c => (mov.MovimientoDetalleId > 0 && c.MovimientoDetalleId == mov.MovimientoDetalleId)
                             || (c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) && c.ProductoId == mov.ProductoId))
                    .Select(c => c.Codigo)
                    .ToList();

                if (!mov.IsAnulado)
                {
                    saldoCorrelativo += (mov.Ingreso - mov.Salida);
                }

                ws.Cell(fila, 1).Value = mov.Fecha.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(fila, 2).Value = mov.NumeroRegistro;
                ws.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(fila, 3).Value = mov.NumeroGuia;
                ws.Cell(fila, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(fila, 4).Value = mov.RazonSocialUbicacion;

                ws.Cell(fila, 5).Value = mov.Ingreso;
                ws.Cell(fila, 5).Style.NumberFormat.Format = "#,##0";
                if (mov.Ingreso > 0) ws.Cell(fila, 5).Style.Font.FontColor = XLColor.Green;

                ws.Cell(fila, 6).Value = mov.Salida;
                ws.Cell(fila, 6).Style.NumberFormat.Format = "#,##0";
                if (mov.Salida > 0) ws.Cell(fila, 6).Style.Font.FontColor = XLColor.Red;

                ws.Cell(fila, 7).Value = saldoCorrelativo;
                ws.Cell(fila, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(fila, 7).Style.Font.Bold = true;

                ws.Cell(fila, 8).Value = string.Join(", ", codigosMov);

                fila++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(saveDialog.FileName);

            Process.Start(new ProcessStartInfo { FileName = saveDialog.FileName, UseShellExecute = true });
        }

        // 🌟 2. REPORTE GENERAL (Sin producto fijo / Varios productos en la misma ubicación)
        public void ExportarKardexUbicacionGeneral(ConsultaMovimientoReporte reporte, string nombreUbicacion, DateTime desde, DateTime hasta, bool incluirCodigosPorFila = true, bool incluirTablaLateral = true)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kardex Productos x Ubicación");

            ws.Range("A1:G1").Merge().Value = "KARDEX DE PRODUCTOS POR UBICACIÓN";
            ws.Range("A1:G1").Style.Font.Bold = true;
            ws.Range("A1:G1").Style.Font.FontSize = 14;
            ws.Range("A1:G1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range("A1:G1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            ws.Range("A1:G1").Style.Font.FontColor = XLColor.White;

            ws.Cell(3, 1).Value = "Ubicación:";
            ws.Cell(3, 2).Value = string.IsNullOrWhiteSpace(nombreUbicacion) ? "TODAS" : nombreUbicacion;
            ws.Range("B3:G3").Merge();

            ws.Cell(4, 1).Value = "Periodo:";
            ws.Cell(4, 2).Value = $"Del {desde:dd/MM/yyyy} Al {hasta:dd/MM/yyyy}";
            ws.Range("B4:G4").Merge();

            ws.Range("A3:A4").Style.Fill.BackgroundColor = XLColor.FromHtml("#B3E5FC");
            ws.Range("A3:A4").Style.Font.Bold = true;

            int filaActual = 6;
            var gruposPorProducto = reporte.Movimientos.GroupBy(m => m.ProductoId).ToList();

            foreach (var grupo in gruposPorProducto)
            {
                string tituloProd = grupo.FirstOrDefault()?.RazonSocialUbicacion.Split('-')[0].Trim() ?? $"Producto {grupo.Key}";

                var bannerProd = ws.Range(filaActual, 1, filaActual, 7);
                bannerProd.Merge().Value = $"📦 PRODUCTO: {tituloProd.ToUpper()}";
                bannerProd.Style.Font.Bold = true;
                bannerProd.Style.Fill.BackgroundColor = XLColor.FromHtml("#3B82F6");
                bannerProd.Style.Font.FontColor = XLColor.White;
                filaActual++;

                var cabeceras = new[] { "Fecha", "# Documento", "Guía", "Procedencia / Ubicación", "Ingreso", "Salida", "Saldo Producto" };
                for (int i = 0; i < cabeceras.Length; i++) ws.Cell(filaActual, i + 1).Value = cabeceras[i];
                ws.Range(filaActual, 1, filaActual, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
                ws.Range(filaActual, 1, filaActual, 7).Style.Font.Bold = true;
                ws.Range(filaActual, 1, filaActual, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                filaActual++;

                decimal saldoProd = 0;

                foreach (var mov in grupo)
                {
                    string regLimpio = mov.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? "";

                    if (!mov.IsAnulado) saldoProd += (mov.Ingreso - mov.Salida);

                    ws.Cell(filaActual, 1).Value = mov.Fecha.ToString("dd/MM/yyyy HH:mm");
                    ws.Cell(filaActual, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(filaActual, 2).Value = mov.NumeroRegistro;
                    ws.Cell(filaActual, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(filaActual, 3).Value = mov.NumeroGuia;
                    ws.Cell(filaActual, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(filaActual, 4).Value = mov.RazonSocialUbicacion;

                    ws.Cell(filaActual, 5).Value = mov.Ingreso;
                    ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";
                    if (mov.Ingreso > 0) ws.Cell(filaActual, 5).Style.Font.FontColor = XLColor.Blue;

                    ws.Cell(filaActual, 6).Value = mov.Salida;
                    ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";
                    if (mov.Salida > 0) ws.Cell(filaActual, 6).Style.Font.FontColor = XLColor.Red;

                    ws.Cell(filaActual, 7).Value = saldoProd;
                    ws.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(filaActual, 7).Style.Font.Bold = true;

                    filaActual++;

                    if (incluirCodigosPorFila)
                    {
                        var codigosEsteItem = reporte.Codigos
                            .Where(c => (mov.MovimientoDetalleId > 0 && c.MovimientoDetalleId == mov.MovimientoDetalleId)
                                     || (c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) && c.ProductoId == mov.ProductoId))
                            .ToList();

                        foreach (var cod in codigosEsteItem)
                        {
                            ws.Cell(filaActual, 1).Value = $"CODIGO {cod.Codigo} - {cod.ColeccionTipo}";
                            ws.Range(filaActual, 1, filaActual, 7).Merge().Style.Font.Italic = true;
                            ws.Range(filaActual, 1, filaActual, 7).Style.Font.FontColor = XLColor.FromHtml("#334155");
                            filaActual++;
                        }
                    }
                }

                ws.Cell(filaActual, 4).Value = "TOTAL PRODUCTO:";
                ws.Cell(filaActual, 4).Style.Font.Bold = true;
                ws.Cell(filaActual, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(filaActual, 5).Value = grupo.Sum(x => x.Ingreso);
                ws.Cell(filaActual, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 5).Style.Font.Bold = true;
                ws.Cell(filaActual, 6).Value = grupo.Sum(x => x.Salida);
                ws.Cell(filaActual, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 6).Style.Font.Bold = true;
                ws.Cell(filaActual, 7).Value = saldoProd;
                ws.Cell(filaActual, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(filaActual, 7).Style.Font.Bold = true;

                ws.Range(filaActual, 1, filaActual, 7).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                ws.Range(filaActual, 1, filaActual, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                filaActual += 2;
            }

            // 🌟 TABLA LATERAL DERECHA (Condicional)
            if (incluirTablaLateral && reporte.Codigos.Any())
            {
                ws.Cell(1, 9).Value = "DETALLE DE CÓDIGOS AUDITADOS";
                ws.Range("I1:K1").Merge().Style.Fill.BackgroundColor = XLColor.DarkRed;
                ws.Range("I1:K1").Style.Font.FontColor = XLColor.White;
                ws.Range("I1:K1").Style.Font.Bold = true;
                ws.Range("I1:K1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(2, 9).Value = "Código QR / Físico";
                ws.Cell(2, 10).Value = "Producto / Colección";
                ws.Cell(2, 11).Value = "# Documento";
                ws.Range("I2:K2").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
                ws.Range("I2:K2").Style.Font.Bold = true;
                ws.Range("I2:K2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int filaDet = 3;
                foreach (var cod in reporte.Codigos)
                {
                    ws.Cell(filaDet, 9).Value = cod.Codigo;
                    ws.Cell(filaDet, 10).Value = cod.ColeccionTipo;
                    ws.Cell(filaDet, 11).Value = cod.NumeroRegistro;
                    ws.Cell(filaDet, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    filaDet++;
                }

                var rangoLateral = ws.Range(1, 9, filaDet - 1, 11);
                rangoLateral.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                rangoLateral.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 22;
            ws.Column(8).Width = 4;

            string ruta = Path.Combine(Path.GetTempPath(), $"KardexProductosUbicacion_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }



        // =========================================================================
        // 🌟 MÉTODO ÚNICO PARA GENERAR LA MATRIZ (INDIVIDUAL O CONSOLIDADO)
        // =========================================================================
        public void GenerarLibroMatrizCompletoConResumen(
    string campanaTexto,
    List<ProductoColumnaDTO> catalogoProductos,
    List<UbicacionMatrizDTO> ubicaciones,
    bool soloUnaUbicacion = false)
        {
            using var wb = new XLWorkbook();

            var prodsConMov = ubicaciones.SelectMany(u => u.Movimientos).Select(m => m.ProductoId).ToHashSet();
            var columnasProductos = catalogoProductos.Where(p => prodsConMov.Contains(p.ProductoId) || catalogoProductos.Count <= 35).ToList();
            if (!columnasProductos.Any()) columnasProductos = catalogoProductos.Take(30).ToList();

            // 1. Diccionario de nombres seguros de hoja para enlazar hipervínculos exactos
            var mapNombresHojas = new Dictionary<int, string>();
            foreach (var ub in ubicaciones)
            {
                string nHoja = ub.Nombre.ToUpper().Trim();
                if (nHoja.Length > 28) nHoja = nHoja.Substring(0, 28);
                foreach (char c in new[] { ':', '\\', '/', '?', '*', '[', ']' }) nHoja = nHoja.Replace(c, '_');
                mapNombresHojas[ub.UbicacionId] = nHoja;
            }

            // 2. RESUMEN GENERAL (Si son múltiples sedes)
            if (!soloUnaUbicacion && ubicaciones.Count > 1)
            {
                var wsResumen = wb.Worksheets.Add("RESUMEN GENERAL");
                wsResumen.TabColor = XLColor.FromHtml("#047857");
                ConstruirHojaResumenConsolidada(wsResumen, campanaTexto, ubicaciones, columnasProductos, mapNombresHojas);
            }

            // 3. Hojas individuales por cada Ubicación
            foreach (var ub in ubicaciones)
            {
                if (!soloUnaUbicacion && !ub.Movimientos.Any()) continue;

                string nombreHoja = mapNombresHojas[ub.UbicacionId];
                var ws = wb.Worksheets.Add(nombreHoja);

                if (ub.TipoUbicacionId == 4)
                    ws.TabColor = XLColor.FromHtml("#3B82F6"); // 🔵 Distribuidor
                else if (ub.TipoUbicacionId == 3)
                    ws.TabColor = XLColor.FromHtml("#F59E0B"); // 🟡 Promotoría
                else
                    ws.TabColor = XLColor.FromHtml("#64748B"); // ⚪ Almacén

                ConstruirHojaMatrizIndividual(ws, ub.Nombre, campanaTexto, ub.Movimientos, columnasProductos, ub.TipoUbicacionId, !soloUnaUbicacion);
            }

            string ruta = Path.Combine(Path.GetTempPath(), $"Matriz_Liquidacion_{campanaTexto}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            wb.SaveAs(ruta);
            Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });
        }

        // =========================================================================
        // 🌟 1. RESUMEN CON HIPERVÍNCULOS EN TEXTO NEGRO Y SIN SUBRAYADO
        // =========================================================================
        private void ConstruirHojaResumenConsolidada(
            IXLWorksheet ws,
            string campanaTexto,
            List<UbicacionMatrizDTO> ubicaciones,
            List<ProductoColumnaDTO> columnasProductos,
            Dictionary<int, string> mapNombresHojas)
        {
            ws.ShowGridLines = true;

            // Título Principal
            ws.Range("B1:H1").Merge().Value = $"RESUMEN GENERAL DE LIBROS CAMPAÑA {campanaTexto.ToUpper()}";
            ws.Range("B1:H1").Style.Font.Bold = true;
            ws.Range("B1:H1").Style.Font.FontSize = 14;
            ws.Range("B1:H1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Cabecera Fila 3 y 4
            ws.Cell(3, 1).Value = "";
            ws.Cell(3, 2).Value = "ZONA / SEDE";
            ws.Range(3, 2, 4, 2).Merge().Style.Font.Bold = true;
            ws.Range(3, 2, 4, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Range(3, 2, 4, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            int colIndex = 3;
            var mapColumnaProducto = new Dictionary<int, int>();
            var gruposNivel = columnasProductos.GroupBy(p => p.NivelNombre).ToList();
            var columnasSubtotales = new List<(int ColSubtotal, int ColInicio, int ColFin)>();

            foreach (var grupoNivel in gruposNivel)
            {
                int colInicioNivel = colIndex;
                var gruposFamilia = grupoNivel.GroupBy(p => p.FamiliaNombre).ToList();

                foreach (var familia in gruposFamilia)
                {
                    int colInicioFamilia = colIndex;
                    foreach (var prod in familia)
                    {
                        mapColumnaProducto[prod.ProductoId] = colIndex;
                        string codigoLimpio = prod.Codigo.Replace("-V-V-", " V ").Replace("-G-G-", " G ").Replace("-G-G", " G").Replace("-V-V", " V").Trim();
                        ws.Cell(4, colIndex).Value = codigoLimpio;
                        ws.Cell(4, colIndex).Style.Font.Bold = true;
                        ws.Cell(4, colIndex).Style.Font.FontSize = 8.5;
                        ws.Cell(4, colIndex).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText(true);
                        ws.Cell(4, colIndex).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        colIndex++;
                    }

                    int colSubtotal = colIndex;
                    columnasSubtotales.Add((colSubtotal, colInicioFamilia, colIndex - 1));
                    ws.Cell(4, colSubtotal).Value = $"TOTAL\n{familia.Key}";
                    ws.Cell(4, colSubtotal).Style.Font.Bold = true;
                    ws.Cell(4, colSubtotal).Style.Font.FontSize = 8;
                    ws.Cell(4, colSubtotal).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText(true);
                    ws.Cell(4, colSubtotal).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDBA74");
                    ws.Cell(4, colSubtotal).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    colIndex++;
                }

                var rangoNivel = ws.Range(3, colInicioNivel, 3, colIndex - 1);
                rangoNivel.Merge().Value = grupoNivel.Key;
                rangoNivel.Style.Font.Bold = true;
                rangoNivel.Style.Font.FontSize = 11;
                rangoNivel.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                rangoNivel.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }

            int ultimaColumna = colIndex - 1;
            int filaActual = 5;

            var filasTotalesGrupos = new List<int>();

            int RenderizarGrupoConTotales(string tituloGrupo, string textoTotalGrupo, int tipoUbicacionId, XLColor colorEtiqueta, XLColor colorFilaTotal)
            {
                var listaUbicacionesGrupo = ubicaciones.Where(u => u.TipoUbicacionId == tipoUbicacionId).ToList();
                if (!listaUbicacionesGrupo.Any()) return 0;

                int filaInicioGrupo = filaActual;

                foreach (var ub in listaUbicacionesGrupo)
                {
                    var celdaNombre = ws.Cell(filaActual, 2);
                    celdaNombre.Value = ub.Nombre;
                    celdaNombre.Style.Font.Bold = true;

                    // 🌟 HIPERVÍNCULO EN NEGRO PURO Y SIN LÍNEA DE SUBRAYADO
                    if (mapNombresHojas.TryGetValue(ub.UbicacionId, out string? nombreHojaDestino))
                    {
                        celdaNombre.CreateHyperlink().InternalAddress = $"'{nombreHojaDestino}'!A1";
                        celdaNombre.Style.Font.Underline = XLFontUnderlineValues.None; // 👈 Sin línea abajo
                        celdaNombre.Style.Font.FontColor = XLColor.Black;               // 👈 Texto negro
                    }

                    var saldosPorProd = ub.Movimientos
                        .GroupBy(m => m.ProductoId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Where(x => x.BloqueTipo == 2).Sum(x => x.Cantidad) - g.Where(x => x.BloqueTipo == 3).Sum(x => x.Cantidad)
                        );

                    foreach (var kvp in saldosPorProd)
                    {
                        if (mapColumnaProducto.TryGetValue(kvp.Key, out int cIdx))
                        {
                            ws.Cell(filaActual, cIdx).Value = kvp.Value;
                            ws.Cell(filaActual, cIdx).Style.NumberFormat.Format = "#,##0";
                            ws.Cell(filaActual, cIdx).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                        }
                    }

                    foreach (var sub in columnasSubtotales)
                    {
                        string colLetraIni = ws.Cell(filaActual, sub.ColInicio).WorksheetColumn().ColumnLetter();
                        string colLetraFin = ws.Cell(filaActual, sub.ColFin).WorksheetColumn().ColumnLetter();
                        var cellSub = ws.Cell(filaActual, sub.ColSubtotal);
                        cellSub.FormulaA1 = $"SUM({colLetraIni}{filaActual}:{colLetraFin}{filaActual})";
                        cellSub.Style.NumberFormat.Format = "#,##0";
                        cellSub.Style.Font.Bold = true;
                        cellSub.Style.Font.FontColor = XLColor.FromHtml("#991B1B");
                        cellSub.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEDD5");
                        cellSub.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    }

                    ws.Range(filaActual, 2, filaActual, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Range(filaActual, 2, filaActual, ultimaColumna).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                    filaActual++;
                }

                int filaFinGrupo = filaActual - 1;

                var rangoEtiqueta = ws.Range(filaInicioGrupo, 1, filaFinGrupo, 1);
                rangoEtiqueta.Merge().Value = tituloGrupo;
                rangoEtiqueta.Style.Font.Bold = true;
                rangoEtiqueta.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                                             .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                                             .Alignment.SetTextRotation(90);
                rangoEtiqueta.Style.Fill.BackgroundColor = colorEtiqueta;
                rangoEtiqueta.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                int filaSubtotalGrupo = filaActual;
                ws.Range(filaSubtotalGrupo, 1, filaSubtotalGrupo, 2).Merge().Value = textoTotalGrupo;
                ws.Range(filaSubtotalGrupo, 1, filaSubtotalGrupo, 2).Style.Font.Bold = true;
                ws.Range(filaSubtotalGrupo, 1, filaSubtotalGrupo, 2).Style.Fill.BackgroundColor = colorFilaTotal;
                ws.Range(filaSubtotalGrupo, 1, filaSubtotalGrupo, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                for (int c = 3; c <= ultimaColumna; c++)
                {
                    string colLetra = ws.Cell(1, c).WorksheetColumn().ColumnLetter();
                    var cellTot = ws.Cell(filaSubtotalGrupo, c);
                    cellTot.FormulaA1 = $"SUM({colLetra}{filaInicioGrupo}:{colLetra}{filaFinGrupo})";
                    cellTot.Style.Font.Bold = true;
                    cellTot.Style.NumberFormat.Format = "#,##0";
                    cellTot.Style.Fill.BackgroundColor = colorFilaTotal;
                    cellTot.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }
                ws.Range(filaSubtotalGrupo, 1, filaSubtotalGrupo, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                filasTotalesGrupos.Add(filaSubtotalGrupo);
                filaActual += 2;

                return filaSubtotalGrupo;
            }

            // 1. PROMOTORES
            RenderizarGrupoConTotales("PROMOTORES", "TOTAL PROMOTORES", 3, XLColor.FromHtml("#FDE047"), XLColor.FromHtml("#FEF08A"));

            // 2. DISTRIBUIDORES
            RenderizarGrupoConTotales("DISTRIBUIDORES", "TOTAL DISTRIBUIDORES", 4, XLColor.FromHtml("#93C5FD"), XLColor.FromHtml("#BFDBFE"));

            // 3. ALMACENES
            RenderizarGrupoConTotales("ALMACENES", "TOTAL ALMACENES", 1, XLColor.FromHtml("#E2E8F0"), XLColor.FromHtml("#F1F5F9"));

            // 4. TOTAL CONSOLIDADO GENERAL
            int filaGranTotal = filaActual;
            ws.Range(filaGranTotal, 1, filaGranTotal, 2).Merge().Value = "TOTAL CONSOLIDADO GENERAL";
            ws.Range(filaGranTotal, 1, filaGranTotal, 2).Style.Font.Bold = true;
            ws.Range(filaGranTotal, 1, filaGranTotal, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FACC15");
            ws.Range(filaGranTotal, 1, filaGranTotal, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            for (int c = 3; c <= ultimaColumna; c++)
            {
                string colLetra = ws.Cell(1, c).WorksheetColumn().ColumnLetter();
                var cellTot = ws.Cell(filaGranTotal, c);

                if (filasTotalesGrupos.Any())
                {
                    cellTot.FormulaA1 = string.Join("+", filasTotalesGrupos.Select(f => $"{colLetra}{f}"));
                }
                else
                {
                    cellTot.FormulaA1 = $"SUM({colLetra}5:{colLetra}{filaGranTotal - 1})";
                }

                cellTot.Style.Font.Bold = true;
                cellTot.Style.NumberFormat.Format = "#,##0";
                cellTot.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE047");
                cellTot.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            ws.Range(filaGranTotal, 1, filaGranTotal, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 3.5;
            ws.Column(2).Width = 24;

            // 🌟 Ancho dinámico para que "BOLSOS", "CATÁLOGOS", etc. no se corten
            for (int c = 3; c <= ultimaColumna; c++)
            {
                var textoHeader = ws.Cell(4, c).GetString();
                if (textoHeader.Length > 8)
                {
                    ws.Column(c).Width = Math.Max(textoHeader.Length + 2, 11);
                }
                else
                {
                    ws.Column(c).Width = 7;
                }
            }
        }

        // =========================================================================
        // 🌟 2. HOJA INDIVIDUAL CON BOTÓN VOLVER EN LA ESQUINA (A1)
        // =========================================================================
        private void ConstruirHojaMatrizIndividual(
            IXLWorksheet ws,
            string tituloUbicacion,
            string campanaTexto,
            List<MatrizKardexItemDTO> movimientos,
            List<ProductoColumnaDTO> catalogoProductos,
            int tipoUbicacionId = 3,
            bool incluirBotonRetorno = true)
        {
            ws.ShowGridLines = true;

            // 🌟 BOTÓN DE RETORNO DISCRETO UBICADO EN LA ESQUINA A1
            if (incluirBotonRetorno)
            {
                var celdaRetorno = ws.Cell("A1");
                celdaRetorno.Value = "🏠 RESUMEN";
                celdaRetorno.CreateHyperlink().InternalAddress = "'RESUMEN GENERAL'!A1";
                celdaRetorno.Style.Font.Bold = true;
                celdaRetorno.Style.Font.FontSize = 9;
                celdaRetorno.Style.Font.FontColor = XLColor.White;
                celdaRetorno.Style.Font.Underline = XLFontUnderlineValues.None;
                celdaRetorno.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B"); // Fondo oscuro discreto
                celdaRetorno.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                celdaRetorno.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // Título Principal
            ws.Cell("D1").Value = $"LIBROS GUIAS Y VENTAS CAMPANA {campanaTexto.ToUpper()} - {tituloUbicacion.ToUpper()}";
            ws.Cell("D1").Style.Font.Bold = true;
            ws.Cell("D1").Style.Font.FontSize = 13;
            ws.Cell("D1").Style.Font.Underline = XLFontUnderlineValues.Single;

            ws.Cell(3, 2).Value = "ORDEN";
            ws.Range(3, 2, 4, 2).Merge().Style.Font.Bold = true;
            ws.Range(3, 2, 4, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Range(3, 2, 4, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            ws.Cell(3, 3).Value = "FECHA";
            ws.Range(3, 3, 4, 3).Merge().Style.Font.Bold = true;
            ws.Range(3, 3, 4, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Range(3, 3, 4, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            int colIndex = 4;
            var mapColumnaProducto = new Dictionary<int, int>();
            var gruposNivel = catalogoProductos.GroupBy(p => p.NivelNombre).ToList();
            var columnasSubtotales = new List<(int ColSubtotal, int ColInicio, int ColFin)>();

            foreach (var grupoNivel in gruposNivel)
            {
                int colInicioNivel = colIndex;
                var gruposFamilia = grupoNivel.GroupBy(p => p.FamiliaNombre).ToList();

                foreach (var familia in gruposFamilia)
                {
                    int colInicioFamilia = colIndex;
                    foreach (var prod in familia)
                    {
                        mapColumnaProducto[prod.ProductoId] = colIndex;
                        string codigoLimpio = prod.Codigo.Replace("-V-V-", " V ").Replace("-G-G-", " G ").Replace("-G-G", " G").Replace("-V-V", " V").Trim();
                        ws.Cell(4, colIndex).Value = codigoLimpio;
                        ws.Cell(4, colIndex).Style.Font.Bold = true;
                        ws.Cell(4, colIndex).Style.Font.FontSize = 8.5;
                        ws.Cell(4, colIndex).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText(true);
                        ws.Cell(4, colIndex).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        colIndex++;
                    }

                    int colSubtotal = colIndex;
                    columnasSubtotales.Add((colSubtotal, colInicioFamilia, colIndex - 1));
                    ws.Cell(4, colSubtotal).Value = $"TOTAL\n{familia.Key}";
                    ws.Cell(4, colSubtotal).Style.Font.Bold = true;
                    ws.Cell(4, colSubtotal).Style.Font.FontSize = 8;
                    ws.Cell(4, colSubtotal).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText(true);
                    ws.Cell(4, colSubtotal).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6E0B4");
                    ws.Cell(4, colSubtotal).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    colIndex++;
                }

                var rangoNivel = ws.Range(3, colInicioNivel, 3, colIndex - 1);
                rangoNivel.Merge().Value = grupoNivel.Key;
                rangoNivel.Style.Font.Bold = true;
                rangoNivel.Style.Font.FontSize = 11;
                rangoNivel.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                rangoNivel.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }

            int ultimaColumna = colIndex - 1;
            int filaActual = 5;

            int RenderizarBloque(string etiquetaVertical, string tituloTotal, XLColor colorEtiqueta, XLColor colorFilaTotal, int tipoBloqueId, XLColor? colorFondoOrden = null)
            {
                int filaInicio = filaActual;
                var grupoFilas = movimientos
                    .Where(m => m.BloqueTipo == tipoBloqueId)
                    .GroupBy(m => new { m.MovimientoId, m.OrdenDocumento, m.Fecha })
                    .OrderBy(g => g.Key.Fecha)
                    .ToList();

                int totalFilasPintar = Math.Max(grupoFilas.Count, 4);

                for (int i = 0; i < totalFilasPintar; i++)
                {
                    if (i < grupoFilas.Count)
                    {
                        var grupo = grupoFilas[i];
                        string ordenLimpia = grupo.Key.OrdenDocumento;
                        if (ordenLimpia.Contains("-"))
                        {
                            var partes = ordenLimpia.Split('-');
                            if (partes.Length > 1 && int.TryParse(partes[1], out int num))
                                ordenLimpia = num.ToString();
                        }
                        else if (int.TryParse(ordenLimpia, out int numPuro))
                        {
                            ordenLimpia = numPuro.ToString();
                        }

                        ws.Cell(filaActual, 2).Value = ordenLimpia;
                        ws.Cell(filaActual, 2).Style.Font.Bold = true;
                        if (colorFondoOrden != null) ws.Cell(filaActual, 2).Style.Fill.BackgroundColor = colorFondoOrden;
                        ws.Cell(filaActual, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        ws.Cell(filaActual, 3).Value = grupo.Key.Fecha.ToString("dd/MM/yyyy");
                        ws.Cell(filaActual, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        foreach (var item in grupo)
                        {
                            if (mapColumnaProducto.TryGetValue(item.ProductoId, out int cIdx))
                            {
                                ws.Cell(filaActual, cIdx).Value = item.Cantidad;
                                ws.Cell(filaActual, cIdx).Style.NumberFormat.Format = "#,##0";
                                ws.Cell(filaActual, cIdx).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                            }
                        }
                    }
                    else
                    {
                        if (colorFondoOrden != null) ws.Cell(filaActual, 2).Style.Fill.BackgroundColor = colorFondoOrden;
                    }

                    foreach (var sub in columnasSubtotales)
                    {
                        string colLetraIni = ws.Cell(filaActual, sub.ColInicio).WorksheetColumn().ColumnLetter();
                        string colLetraFin = ws.Cell(filaActual, sub.ColFin).WorksheetColumn().ColumnLetter();
                        var cellSub = ws.Cell(filaActual, sub.ColSubtotal);
                        cellSub.FormulaA1 = $"SUM({colLetraIni}{filaActual}:{colLetraFin}{filaActual})";
                        cellSub.Style.NumberFormat.Format = "#,##0";
                        cellSub.Style.Font.Bold = true;
                        cellSub.Style.Font.FontColor = XLColor.FromHtml("#166534");
                        cellSub.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCFCE7");
                        cellSub.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    }

                    ws.Range(filaActual, 2, filaActual, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Range(filaActual, 2, filaActual, ultimaColumna).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                    filaActual++;
                }

                int filaFin = filaActual - 1;

                var rangoEtiqueta = ws.Range(filaInicio, 1, filaFin, 1);
                rangoEtiqueta.Merge().Value = etiquetaVertical;
                rangoEtiqueta.Style.Font.Bold = true;
                rangoEtiqueta.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                                             .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                                             .Alignment.SetTextRotation(90);
                rangoEtiqueta.Style.Fill.BackgroundColor = colorEtiqueta;
                rangoEtiqueta.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                int filaTotal = filaActual;
                ws.Range(filaTotal, 1, filaTotal, 3).Merge().Value = tituloTotal;
                ws.Range(filaTotal, 1, filaTotal, 3).Style.Font.Bold = true;
                ws.Range(filaTotal, 1, filaTotal, 3).Style.Fill.BackgroundColor = colorFilaTotal;
                ws.Range(filaTotal, 1, filaTotal, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                for (int c = 4; c <= ultimaColumna; c++)
                {
                    string colLetra = ws.Cell(1, c).WorksheetColumn().ColumnLetter();
                    var cellTot = ws.Cell(filaTotal, c);
                    cellTot.FormulaA1 = $"SUM({colLetra}{filaInicio}:{colLetra}{filaFin})";
                    cellTot.Style.Font.Bold = true;
                    cellTot.Style.NumberFormat.Format = "#,##0";
                    cellTot.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }
                ws.Range(filaTotal, 1, filaTotal, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                filaActual += 2;

                return filaTotal;
            }

            int filaTotIngresos = 0;
            if (tipoUbicacionId == 1)
            {
                filaTotIngresos = RenderizarBloque("INGRESOS", "TOTAL INGRESOS", XLColor.FromHtml("#93C5FD"), XLColor.FromHtml("#BFDBFE"), 1);
            }

            int filaTotSalidas = RenderizarBloque("SALIDAS", "TOTAL SALIDAS", XLColor.FromHtml("#FDE047"), XLColor.FromHtml("#FACC15"), 2, XLColor.FromHtml("#FEF08A"));
            int filaTotDevoluciones = RenderizarBloque("DEVOLUCIONES", "TOTAL DEVOLUCIONES", XLColor.FromHtml("#FCA5A5"), XLColor.FromHtml("#FECACA"), 3);

            // SALDO NETO EN PODER
            int filaSaldoFinal = filaActual;
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, 3).Merge().Value = $"TOTAL {campanaTexto.ToUpper()} (SALDO EN PODER)";
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, 3).Style.Font.Bold = true;
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, 3).Style.Font.Italic = true;
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDBA74");
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            for (int c = 4; c <= ultimaColumna; c++)
            {
                string colLetra = ws.Cell(1, c).WorksheetColumn().ColumnLetter();
                var cellSaldo = ws.Cell(filaSaldoFinal, c);
                cellSaldo.FormulaA1 = $"{colLetra}{filaTotSalidas}-{colLetra}{filaTotDevoluciones}";
                cellSaldo.Style.Font.Bold = true;
                cellSaldo.Style.NumberFormat.Format = "#,##0";
                cellSaldo.Style.Fill.BackgroundColor = XLColor.FromHtml("#FED7AA");
                cellSaldo.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                if (columnasSubtotales.Any(x => x.ColSubtotal == c))
                {
                    cellSaldo.Style.Font.FontColor = XLColor.Red;
                }
            }
            ws.Range(filaSaldoFinal, 1, filaSaldoFinal, ultimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 14; // Botón "🏠 RESUMEN" en A1
            ws.Column(2).Width = 9;  // ORDEN
            ws.Column(3).Width = 12; // FECHA

            // 🌟 Ancho dinámico para columnas de productos
            for (int c = 4; c <= ultimaColumna; c++)
            {
                var textoHeader = ws.Cell(4, c).GetString();
                if (textoHeader.Length > 8)
                {
                    ws.Column(c).Width = Math.Max(textoHeader.Length + 2, 11);
                }
                else
                {
                    ws.Column(c).Width = 7;
                }
            }
        }


    }
}