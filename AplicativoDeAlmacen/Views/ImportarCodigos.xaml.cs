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
            var lista = new List<string>(60000);
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
        // ACCIÓN 1: CARGAR EXCEL DE IMPRENTA CON VENTANA DE PROGRESO REUTILIZABLE
        // =========================================================================
        // =========================================================================
        // ACCIÓN 1: CARGAR EXCEL DE IMPRENTA CON VENTANA DE PROGRESO REUTILIZABLE
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

                // Lanzamos la ventana de progreso genérica de segundo plano
                var loadingModal = new ProgressWindow("Procesando Archivo de Imprenta", "Mapeando registros contra el inventario central...", async (progress) =>
                {
                    // 1. Lectura veloz en memoria
                    if (extension == ".xlsx")
                        rawList = LeerExcel(openFileDialog.FileName);
                    else
                        rawList = LeerTXT(openFileDialog.FileName);

                    // 2. Cruce atómico por el Join maestro indexado en Tabla Temporal
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    // 3. Obtener descripciones de productos relacionales
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

                    // 4. Obtener nombres de estados relacionales
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

                    int total = rawList.Count;
                    int ultimoPorcentajeReportado = -1;
                    int estadoPermitidoLocal = EstadoPermitido;
                    bool incluirInvalidosLocal = GetIncluirInvalidos();

                    for (int i = 0; i < total; i++)
                    {
                        string raw = rawList[i];
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);
                        lookup.TryGetValue(norm, out var tup);

                        bool isFound = tup.CodigoObj != null;
                        bool estadoValido = isFound && (estadoPermitidoLocal == 0 || tup.CodigoObj.EstadoId == estadoPermitidoLocal);

                        string productoDescFinal = "NO EXISTE EN INVENTARIO";
                        if (isFound && tup.ProductoId.HasValue)
                        {
                            prodMap.TryGetValue(tup.ProductoId.Value, out productoDescFinal);
                            if (string.IsNullOrEmpty(productoDescFinal)) productoDescFinal = "CODIGO PLANO REGISTRADO";
                        }

                        string estadoNombreFinal = "INEXISTENTE";
                        if (isFound && tup.CodigoObj.EstadoId != 0)
                        {
                            estadoMap.TryGetValue(tup.CodigoObj.EstadoId, out estadoNombreFinal);
                        }

                        var pr = new PreviewRow
                        {
                            CodigoRaw = raw,
                            CodigoNorm = norm,
                            EstadoValido = estadoValido,
                            CodigoCreadoId = isFound ? tup.CodigoObj.Id : (int?)null,
                            ProductoId = isFound ? tup.ProductoId : (int?)null,
                            ProductoDesc = productoDescFinal,
                            EstadoId = isFound ? tup.CodigoObj.EstadoId : (int?)null,
                            EstadoNombre = estadoNombreFinal
                        };

                        pr.Encontrado = pr.EstadoValido || incluirInvalidosLocal;
                        preview.Add(pr);

                        // 🌟 CORRECCIÓN TOTAL AQUÍ: El progreso reporta sobre el índice real 'i' del archivo importado
                        int porcentajeActual = (i * 100) / total;
                        if (porcentajeActual > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = porcentajeActual;
                            progress?.Report(porcentajeActual);
                        }
                    }

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
                    dgDatos.UpdateLayout();
                    UpdateTransferButtonState(preview);
                }
                else if (loadingModal.ErrorResult != null)
                {
                    MessageBox.Show($"Ocurrió un error al procesar el lote masivo:\n{loadingModal.ErrorResult.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // =========================================================================
        // ACCIÓN 2: TRANSFERIR CÓDIGOS AL MOVIMIENTO CON PROGRESO SUAVE
        // =========================================================================
        // =========================================================================
        // ACCIÓN 2: BOTÓN TRANSFERIR CON BARRA DE PROGRESO DE PORCENTAJE (CORREGIDO)
        // =========================================================================
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            CodigosImportados.Clear();

            if (dgDatos.ItemsSource is not IEnumerable<PreviewRow> rows) return;

            // Tomamos solo los que el usuario tiene marcados/aprobados en la grilla
            var itemsSeleccionados = rows.Where(x => x.Encontrado).ToList();
            int total = itemsSeleccionados.Count;

            if (total == 0)
            {
                MessageBox.Show("No hay códigos seleccionados o aprobados para transferir.", "Transferir", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var resp = MessageBox.Show($"Se inyectarán {total} códigos al registro kárdex. ¿Desea continuar?", "Confirmar Transferencia", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            // 🌟 INICIALIZAMOS Y PARAMETRIZAMOS LA VENTANA DE PROGRESO GENÉRICA
            var transferModal = new ProgressWindow("Transfiriendo Lote al Kárdex", "Sincronizando registros con el movimiento actual...", async (progress) =>
            {
                int ultimoPorcentaje = -1;

                for (int i = 0; i < total; i++)
                {
                    var r = itemsSeleccionados[i];
                    if (!string.IsNullOrWhiteSpace(r.CodigoRaw))
                    {
                        CodigosImportados.Add(r.CodigoRaw);
                    }

                    // Cálculo y suavizado matemático del porcentaje de 1% en 1%
                    int pct = (i * 100) / total;
                    if (pct > ultimoPorcentaje)
                    {
                        ultimoPorcentaje = pct;
                        progress.Report(pct);
                    }
                }
                // Pequeño respiro imperceptible para asegurar que la UI pinte el 100%
                await Task.Delay(50);
            });

            // 🌟 REGLA DE ORO EN WPF: Enlazamos el Owner para que se centre perfectamente sobre la ventana de importación
            transferModal.Owner = this;

            // Al llamar a .ShowDialog(), la ejecución se pausa y muestra la barra de progreso mientras corre el bucle de arriba
            if (transferModal.ShowDialog() == true)
            {
                // Si todo terminó sin errores, le devolvemos True al MovimientosUserControl padre para que refresque sus grillas
                this.DialogResult = true;
                this.Close();
            }
            else if (transferModal.ErrorResult != null)
            {
                MessageBox.Show($"Error durante la transferencia de memoria:\n{transferModal.ErrorResult.Message}", "Error Interno", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (!invalids.Any()) { MessageBox.Show("No hay registros inválidos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information); return; }

                var lines = new List<string> { "Codigo;Normalizado;Estado" };
                foreach (var i in invalids) lines.Add($"{i.CodigoRaw};{i.CodigoNorm};{i.EstadoNombre}");

                string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lote_errores_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                System.IO.File.WriteAllLines(ruta, lines, System.Text.Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = false; } catch { }
            this.Close();
        }
    }
}