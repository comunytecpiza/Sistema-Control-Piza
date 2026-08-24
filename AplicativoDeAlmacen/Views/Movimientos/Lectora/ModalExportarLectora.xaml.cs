using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace AplicativoDeAlmacen.Views.Movimientos.Lectora
{
    public partial class ModalExportarLectora : Window
    {
        private readonly List<ItemLogLectora> _registros;

        public ModalExportarLectora(List<ItemLogLectora> registros)
        {
            InitializeComponent();
            _registros = registros ?? new List<ItemLogLectora>();
        }

        private void BtnGenerarExcel_Click(object sender, RoutedEventArgs e)
        {
            var filtrados = _registros.AsEnumerable();

            if (RbSoloBuenos.IsChecked == true)
                filtrados = filtrados.Where(x => x.EsAceptado);
            else if (RbSoloErrores.IsChecked == true)
                filtrados = filtrados.Where(x => !x.EsAceptado);
            else if (RbSoloVenta.IsChecked == true)
                filtrados = filtrados.Where(x => x.Categoria == "VENTA");
            else if (RbSoloGuia.IsChecked == true)
                filtrados = filtrados.Where(x => x.Categoria == "GUÍA");

            var listaFinal = filtrados.ToList();

            if (!listaFinal.Any())
            {
                MessageBox.Show("No hay registros que coincidan con el filtro seleccionado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Auditoria_Lectora");

                // Encabezados
                ws.Cell(1, 1).Value = "HORA";
                ws.Cell(1, 2).Value = "ESTADO";
                ws.Cell(1, 3).Value = "TIPO";
                ws.Cell(1, 4).Value = "CÓDIGO";
                ws.Cell(1, 5).Value = "PRODUCTO";
                ws.Cell(1, 6).Value = "DETALLE / MOTIVO";

                var headerRange = ws.Range(1, 1, 1, 6);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 41, 59);
                headerRange.Style.Font.FontColor = XLColor.White;

                int row = 2;
                foreach (var item in listaFinal)
                {
                    ws.Cell(row, 1).Value = item.Hora;
                    ws.Cell(row, 2).Value = item.EsAceptado ? "ACEPTADO" : "RECHAZADO";
                    ws.Cell(row, 3).Value = item.Categoria;
                    ws.Cell(row, 4).Value = item.Codigo;
                    ws.Cell(row, 5).Value = item.Producto;
                    ws.Cell(row, 6).Value = item.Detalle;

                    if (!item.EsAceptado)
                    {
                        ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromArgb(254, 242, 242);
                        ws.Cell(row, 2).Style.Font.FontColor = XLColor.Red;
                    }
                    row++;
                }

                ws.Columns().AdjustToContents();

                // 🌟 Guardar en archivo temporal y abrir de inmediato
                string tempPath = Path.Combine(Path.GetTempPath(), $"Reporte_Lectora_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                wb.SaveAs(tempPath);

                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar archivo temporal de Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}