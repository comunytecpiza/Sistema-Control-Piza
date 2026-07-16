using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ClosedXML.Excel;
using Microsoft.Win32;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Data.Common;
using AplicativoDeAlmacen.Models.Models;
using System.Diagnostics;
using System.IO;

namespace AplicativoDeAlmacen.Views
{
    public partial class ImportarCodigos : Window
    {
        public int EstadoPermitido { get; set; } = 0;
        public List<string> CodigosImportados { get; set; } = new List<string>();

        private readonly Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        private readonly IngresoMovimientoService _serviceMovimiento = new IngresoMovimientoService();
        private readonly DatabaseConnection _db = new DatabaseConnection();

        private class PreviewRow
        {
            public int RowNumber { get; set; }
            public string CodigoRaw { get; set; }
            public string CodigoNorm { get; set; }
            public bool Encontrado { get; set; }
            public bool EstadoValido { get; set; }
            public int? CodigoCreadoId { get; set; }
            public int? ProductoId { get; set; }
            public string ProductoDesc { get; set; }
            public int? EstadoId { get; set; }
            public string EstadoNombre { get; set; }
        }

        private bool GetIncluirInvalidos()
        {
            try
            {
                var field = this.FindName("chkIncluirInvalidos") as System.Windows.Controls.CheckBox;
                return field != null && field.IsChecked == true;
            }
            catch { return false; }
        }

        public ImportarCodigos()
        {
            InitializeComponent();
            try { btnTransferir.IsEnabled = false; } catch { }
        }

        private List<string> LeerExcel(string ruta)
        {
            var lista = new List<string>(50000);
            using var workbook = new XLWorkbook(ruta);
            var ws = workbook.Worksheet(1);
            var used = ws.RangeUsed();
            if (used == null) return lista;

            foreach (var row in used.Rows())
            {
                foreach (var cell in row.Cells())
                {
                    var codigo = cell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(codigo))
                        lista.Add(codigo);
                }
            }
            return lista;
        }

        private List<string> LeerTXT(string ruta)
        {
            return File.ReadLines(ruta)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
        }

        // =========================================================================
        // ACCIÓN 1: BUSCAR Y PREVISUALIZAR EXCEL CON BARRA DE PORCENTAJE REUTILIZABLE
        // =========================================================================
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            openFileDialog.Filter = "Archivos Permitidos (*.xlsx; *.txt)|*.xlsx;*.txt";

            if (openFileDialog.ShowDialog() == true)
            {
                txtRutaArchivo.Text = openFileDialog.FileName;
                string extension = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();

                List<string> rawList = new List<string>();
                var preview = new List<PreviewRow>();

                // 🌟 BARRA DE PROGRESO 1: Captura la lectura y el cruce masivo de la BD
                var loadingModal = new ProgressWindow("Leyendo Archivo Masivo", "Extrayendo y mapeando datos desde el almacén...", async (progress) =>
                {
                    if (extension == ".xlsx")
                        rawList = LeerExcel(openFileDialog.FileName);
                    else
                        rawList = LeerTXT(openFileDialog.FileName);

                    // Viaje rápido indexado a la base de datos
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    // Descripciones de productos en bloque
                    var prodIds = lookup.Values.Where(v => v.ProductoId.HasValue).Select(v => v.ProductoId.Value).Distinct().ToList();
                    var prodMap = new Dictionary<int, string>();
                    if (prodIds.Any())
                    {
                        using var conn = _db.GetConnection();
                        var dbConn = (DbConnection)conn;
                        await dbConn.OpenAsync();
                        var paramNames = new List<string>();
                        for (int i = 0; i < prodIds.Count; i++) paramNames.Add("@p" + i);
                        string q = $"SELECT id, descripcion FROM productos WHERE id IN ({string.Join(',', paramNames)})";
                        using var cmd = dbConn.CreateCommand();
                        cmd.CommandText = QueryAdapter.FormatearConsulta(q);
                        for (int i = 0; i < prodIds.Count; i++)
                        {
                            var p = cmd.CreateParameter(); p.ParameterName = "@p" + i; p.Value = prodIds[i]; cmd.Parameters.Add(p);
                        }
                        using var rdr = await cmd.ExecuteReaderAsync();
                        while (await rdr.ReadAsync())
                        {
                            int id = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
                            string desc = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                            if (id != 0) prodMap[id] = desc;
                        }
                    }

                    // Mapping de nombres de estados masivos
                    var estadoIds = lookup.Values.Where(v => v.CodigoObj != null).Select(v => v.CodigoObj.EstadoId).Where(id => id != 0).Distinct().ToList();
                    var estadoMap = new Dictionary<int, string>();
                    if (estadoIds.Any())
                    {
                        using var conn2 = _db.GetConnection();
                        var dbConn2 = (DbConnection)conn2;
                        await dbConn2.OpenAsync();
                        var paramNames2 = new List<string>();
                        for (int i = 0; i < estadoIds.Count; i++) paramNames2.Add("@e" + i);
                        string qest = $"SELECT id, nombre FROM estados WHERE id IN ({string.Join(',', paramNames2)})";
                        using var cmdEst = dbConn2.CreateCommand();
                        cmdEst.CommandText = QueryAdapter.FormatearConsulta(qest);
                        for (int i = 0; i < estadoIds.Count; i++) { var p = cmdEst.CreateParameter(); p.ParameterName = "@e" + i; p.Value = estadoIds[i]; cmdEst.Parameters.Add(p); }
                        using var rdrEst = await cmdEst.ExecuteReaderAsync();
                        while (await rdrEst.ReadAsync())
                        {
                            int id = rdrEst.IsDBNull(0) ? 0 : rdrEst.GetInt32(0);
                            string nombre = rdrEst.IsDBNull(1) ? string.Empty : rdrEst.GetString(1);
                            if (id != 0) estadoMap[id] = nombre;
                        }
                    }

                    // Construcción de filas con reporte de porcentaje iterativo
                    int total = rawList.Count;
                    for (int i = 0; i < total; i++)
                    {
                        string norm = _serviceMovimiento.NormalizarCodigo(rawList[i]);
                        lookup.TryGetValue(norm, out var tup);

                        bool isFound = tup.CodigoObj != null;
                        bool estadoValido = isFound && (EstadoPermitido == 0 || tup.CodigoObj.EstadoId == EstadoPermitido);

                        var pr = new PreviewRow
                        {
                            CodigoRaw = rawList[i],
                            CodigoNorm = norm,
                            EstadoValido = estadoValido,
                            CodigoCreadoId = isFound ? tup.CodigoObj.Id : (int?)null,
                            ProductoId = isFound ? tup.ProductoId : (int?)null,
                            ProductoDesc = (isFound && tup.ProductoId.HasValue && prodMap.ContainsKey(tup.ProductoId.Value)) ? prodMap[tup.ProductoId.Value] : (isFound ? "CODIGO PLANO REGISTRADO" : "NO EXISTE EN INVENTARIO"),
                            EstadoId = isFound ? tup.CodigoObj.EstadoId : (int?)null,
                            EstadoNombre = (isFound && tup.CodigoObj.EstadoId != 0 && estadoMap.ContainsKey(tup.CodigoObj.EstadoId)) ? estadoMap[tup.CodigoObj.EstadoId] : "INEXISTENTE"
                        };

                        pr.Encontrado = pr.EstadoValido || GetIncluirInvalidos();
                        preview.Add(pr);

                        // Reportamos el porcentaje cada 1,000 registros para no saturar el renderizador de la UI
                        if (i % 1000 == 0) progress.Report((i * 100) / total);
                    }

                    // Ordenar rápidamente en RAM antes de pintar
                    preview.Sort((a, b) => string.Compare(a.ProductoDesc, b.ProductoDesc, StringComparison.Ordinal));
                    int idx2 = 1;
                    foreach (var p in preview) p.RowNumber = idx2++;
                });

                loadingModal.Owner = this;
                if (loadingModal.ShowDialog() == true)
                {
                    txtTotalCodigos.Text = rawList.Count.ToString();
                    dgDatos.ItemsSource = null;
                    dgDatos.ItemsSource = preview;

                    // Forzar el redibujado instantáneo de la UI
                    dgDatos.UpdateLayout();
                    UpdateTransferButtonState(preview);
                }
                else if (loadingModal.ErrorResult != null)
                {
                    MessageBox.Show($"Error leyendo el archivo masivo:\n{loadingModal.ErrorResult.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // =========================================================================
        // ACCIÓN 2: BOTÓN TRANSFERIR CON BARRA DE PROGRESO DE PORCENTAJE (THREAD-SAFE)
        // =========================================================================
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            CodigosImportados.Clear();

            if (dgDatos.ItemsSource is not IEnumerable<PreviewRow> rows) return;
            var itemsSeleccionados = rows.Where(x => x.Encontrado).ToList();

            int total = itemsSeleccionados.Count;
            if (total == 0)
            {
                MessageBox.Show("No hay códigos seleccionados para transferir.", "Transferir", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var resp = MessageBox.Show($"Se transferirán {total} códigos. ¿Desea continuar?", "Confirmar transferencia", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            // 🌟 BARRA DE PROGRESO 2: Captura el empaquetado asíncrono hacia la memoria del Kárdex
            var transferModal = new ProgressWindow("Transfiriendo Lote", "Inyectando ítems al grid de movimientos...", async (progress) =>
            {
                for (int i = 0; i < total; i++)
                {
                    var r = itemsSeleccionados[i];
                    if (!string.IsNullOrWhiteSpace(r.CodigoRaw))
                    {
                        CodigosImportados.Add(r.CodigoRaw);
                    }

                    // Reporte dinámico de la inyección
                    if (i % 500 == 0) progress.Report((i * 100) / total);
                }
                await Task.CompletedTask;
            });

            transferModal.Owner = this;
            if (transferModal.ShowDialog() == true)
            {
                this.DialogResult = true;
                this.Close();
            }
        }

        private void UpdateTransferButtonState()
        {
            if (dgDatos.ItemsSource is IEnumerable<PreviewRow> rows)
            {
                UpdateTransferButtonState(rows);
            }
        }

        private void UpdateTransferButtonState(IEnumerable<PreviewRow> preview)
        {
            int found = preview.Count(r => r.Encontrado);
            try
            {
                btnTransferir.IsEnabled = found > 0;
                btnTransferir.Content = found > 0 ? $"Transferir ({found})" : "Transferir";
                int valid = preview.Count(r => r.EstadoValido);
                txtValidos.Text = valid.ToString();
                txtInvalidos.Text = (preview.Count() - valid).ToString();
            }
            catch { }
        }

        private void BtnExportInvalid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgDatos.ItemsSource is not IEnumerable<PreviewRow> rows) return;
                var invalids = rows.Where(r => r.CodigoCreadoId.HasValue && r.EstadoId.HasValue && EstadoPermitido != 0 && r.EstadoId.Value != EstadoPermitido).ToList();
                if (!invalids.Any()) { MessageBox.Show("No hay códigos inválidos para exportar.", "Exportar", MessageBoxButton.OK, MessageBoxImage.Information); return; }

                var lines = new List<string> { "Codigo;Normalizado;Estado" };
                foreach (var i in invalids) lines.Add($"{i.CodigoRaw};{i.CodigoNorm};{i.EstadoNombre}");

                string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codigos_invalidos_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                System.IO.File.WriteAllLines(ruta, lines, System.Text.Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show($"Error exportando: {ex.Message}"); }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = false; } catch { }
            this.Close();
        }
    }
}