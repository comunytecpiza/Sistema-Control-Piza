using AplicativoDeAlmacen.Models.Models;
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using ClosedXML.Excel;
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
                ws.Range(fila, 5, fila, 9).Style.NumberFormat.Format = "#,##0.00";

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
            ws.Range(fila, 5, fila, 9).Style.NumberFormat.Format = "#,##0.00";

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

    }
}