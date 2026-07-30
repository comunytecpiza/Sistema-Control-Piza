using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AplicativoDeAlmacen.Models.Reportes;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta
{
    public partial class DetalleStockFinalWindow : Window
    {
        private readonly int _productoId;
        private readonly int _almacenId;
        private readonly DateTime _fechaCorte;
        private readonly string _nombreProducto;
        private readonly DatabaseConnection _db = new DatabaseConnection();
        private List<StockItemDetalleDTO> _listaCompleta = new List<StockItemDetalleDTO>();

        public DetalleStockFinalWindow(int productoId, string nombreProducto, int almacenId, DateTime fechaCorte)
        {
            InitializeComponent();
            _productoId = productoId;
            _nombreProducto = nombreProducto;
            _almacenId = almacenId;
            _fechaCorte = fechaCorte;

            TxtNombreProducto.Text = nombreProducto;
            TxtFechaCorte.Text = $"Estado actual en almacén al: {_fechaCorte:dd/MM/yyyy}";

            Loaded += async (s, e) => await CargarDatosStockAsync();
        }

        private async Task CargarDatosStockAsync()
        {
            try
            {
                _listaCompleta.Clear();
                var listaOperativos = new List<StockItemDetalleDTO>();
                var listaRestringidos = new List<StockItemDetalleDTO>();

                using var conn = _db.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();

                // 🚀 CONSULTA CORREGIDA: Usamos el INNER JOIN con registro_codigos para filtrar por producto_id
                string query = QueryAdapter.EsMySQL
                    ? @"SELECT cc.id, cc.codigo, COALESCE(cond.nombre, 'OK / Operativo'), COALESCE(cond.permitir_salida, 1)
                        FROM codigos_creados cc
                        INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                        LEFT JOIN condiciones_codigo cond ON cc.condicion_id = cond.id
                        WHERE rc.producto_id = @prodId AND cc.almacen_id = @almId AND cc.estado_id = 3;"
                    : @"SELECT cc.id, cc.codigo, ISNULL(cond.nombre, 'OK / Operativo'), ISNULL(cond.permitir_salida, 1)
                        FROM codigos_creados cc WITH (NOLOCK)
                        INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
                        LEFT JOIN condiciones_codigo cond WITH (NOLOCK) ON cc.condicion_id = cond.id
                        WHERE rc.producto_id = @prodId AND cc.almacen_id = @almId AND cc.estado_id = 3;";

                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var p1 = cmd.CreateParameter(); p1.ParameterName = "@prodId"; p1.Value = _productoId; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@almId"; p2.Value = _almacenId; cmd.Parameters.Add(p2);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    string codigo = rdr.GetString(1);
                    string condicion = rdr.GetString(2);
                    bool permiteSalida = Convert.ToBoolean(rdr.GetValue(3));

                    // Detección de Categoría limpia en memoria (Guía vs Venta)
                    string tipoCategoria = (codigo.Contains("-V-") || codigo.Contains("'V'") || codigo.Contains(" V ") || codigo.Contains("VENTA")) ? "VENTA" : "GUÍA";

                    var item = new StockItemDetalleDTO
                    {
                        Id = rdr.GetInt32(0),
                        CodigoUnico = codigo,
                        TipoCategoria = tipoCategoria,
                        NombreCondicion = condicion,
                        PermiteSalida = permiteSalida
                    };

                    _listaCompleta.Add(item);

                    if (permiteSalida)
                        listaOperativos.Add(item);
                    else
                        listaRestringidos.Add(item);
                }

                // Asignamos a las Grillas
                GridOperativos.ItemsSource = listaOperativos;
                GridRestringidos.ItemsSource = listaRestringidos;

                // Calculamos Totales
                int totalGuias = listaOperativos.Count(x => x.TipoCategoria == "GUÍA");
                int totalVentas = listaOperativos.Count(x => x.TipoCategoria == "VENTA");
                int totalRestringidos = listaRestringidos.Count;
                int stockTotal = _listaCompleta.Count;

                LblTotalGuias.Text = totalGuias.ToString("N0");
                LblTotalVentas.Text = totalVentas.ToString("N0");
                LblTotalRestringidos.Text = totalRestringidos.ToString("N0");
                LblStockTotal.Text = stockTotal.ToString("N0");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar el detalle del stock: " + ex.Message, "Error de Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_listaCompleta.Any())
                {
                    MessageBox.Show("No hay datos para generar la vista previa.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 1. ABRIR EL MODAL DE SELECCIÓN DE FILTROS
                var modal = new ExportarStockModal { Owner = this };
                modal.ShowDialog();

                if (!modal.SeAcepto) return;

                // 2. GENERAR EL EXCEL EN UNA RUTA TEMPORAL (VISTA PREVIA)
                string nombreArchivoTemp = $"StockFinal_{_nombreProducto.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                string rutaTemporal = Path.Combine(Path.GetTempPath(), nombreArchivoTemp);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Stock Detallado");

                    int currentRow = 1;

                    // 🏷️ TÍTULOS GENERALES
                    ws.Cell($"A{currentRow}").Value = $"Códigos en stock al {_fechaCorte:dd/MM/yyyy}";
                    ws.Cell($"A{currentRow}").Style.Font.Bold = true;
                    ws.Cell($"A{currentRow}").Style.Font.FontSize = 12;
                    ws.Cell($"A{currentRow}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Range($"A{currentRow}:G{currentRow}").Merge();
                    currentRow += 2;

                    ws.Cell($"A{currentRow}").Value = $"Producto: {_nombreProducto}";
                    ws.Cell($"A{currentRow}").Style.Font.Bold = true;
                    ws.Cell($"A{currentRow}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Range($"A{currentRow}:G{currentRow}").Merge();
                    currentRow += 3;

                    // 2. EXPORTAR OPERATIVOS (Si lo marcó)
                    if (modal.IncluirOperativos)
                    {
                        var operativos = _listaCompleta.Where(x => x.PermiteSalida).Select(x => x.CodigoUnico).ToList();

                        ws.Cell($"A{currentRow}").Value = $"Libros Guía y Venta Operativos: {operativos.Count} códigos";
                        ws.Cell($"A{currentRow}").Style.Font.Bold = true;
                        currentRow++;

                        int colIndex = 1;
                        foreach (var codigo in operativos)
                        {
                            ws.Cell(currentRow, colIndex).Value = codigo;
                            colIndex++;

                            if (colIndex > 7)
                            {
                                colIndex = 1;
                                currentRow++;
                            }
                        }

                        if (colIndex != 1) currentRow += 2;
                        else currentRow++;
                    }

                    // 3. EXPORTAR RESTRINGIDOS / MERMA (Si lo marcó)
                    if (modal.IncluirRestringidos)
                    {
                        var restringidos = _listaCompleta.Where(x => !x.PermiteSalida).Select(x => x.CodigoUnico).ToList();

                        ws.Cell($"A{currentRow}").Value = $"Códigos con Restricción (Merma / Perdidos): {restringidos.Count} códigos";
                        ws.Cell($"A{currentRow}").Style.Font.Bold = true;
                        currentRow++;

                        int colIndex = 1;
                        foreach (var codigo in restringidos)
                        {
                            ws.Cell(currentRow, colIndex).Value = codigo;
                            colIndex++;

                            if (colIndex > 7)
                            {
                                colIndex = 1;
                                currentRow++;
                            }
                        }
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(rutaTemporal);
                }

                // 3. ABRIR DIRECTAMENTE EL ARCHIVO TEMPORAL EN EXCEL (SIN MENSAJES MOLESTOS)
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = rutaTemporal,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hubo un problema al abrir Excel automáticamente: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error en la vista previa: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}