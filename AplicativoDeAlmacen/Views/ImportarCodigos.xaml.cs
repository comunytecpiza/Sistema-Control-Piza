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
using Microsoft.Win32; // <--- Esta es la librería correcta para WPF
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Data.Common;
using AplicativoDeAlmacen.Models.Models;
using System.Diagnostics;

namespace AplicativoDeAlmacen.Views
{
    /// <summary>
    /// Lógica de interacción para ImportarCodigos.xaml
    /// </summary>
    public partial class ImportarCodigos : Window
    {
        // Estado permitido según motivo (1=COMPRA -> estado 1, otro -> estado 4). Si 0, no filtrar por estado.
        public int EstadoPermitido { get; set; } = 0;

        // Si se marca, al transferir incluir códigos aunque su estado no sea el permitido
        private bool GetIncluirInvalidos()
        {
            try
            {
                var field = this.FindName("chkIncluirInvalidos") as System.Windows.Controls.CheckBox;
                return field != null && field.IsChecked == true;
            }
            catch { return false; }
        }
        // Propiedad pública para que la ventana principal pueda leer los datos
        public List<string> CodigosImportados { get; set; } = new List<string>();
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
        public ImportarCodigos()
        {
            InitializeComponent();
            // Inicialmente no permitimos transferir hasta que haya al menos un código encontrado
            try
            {
                btnTransferir.IsEnabled = false;
            }
            catch { }
            // Actualizar estado del botón cuando se editen checkboxes en la grilla
            try
            {
                dgDatos.CellEditEnding += (s, e) => {
                    // Postpone the check to after the edit is committed
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new System.Action(() => {
                        UpdateTransferButtonState();
                    }));
                };
            }
            catch { }
        }

        private DataTable LeerExcel(string ruta)
        {
            // Leer todo el rango usado y devolver una DataTable con UNA COLUMNA llamada "Codigo".
            // Esto asegura que se recojan códigos distribuidos en varias columnas (A,B,C...) como suele venir en tus excels.
            DataTable dt = new DataTable();
            dt.Columns.Add("Codigo");

            using (XLWorkbook workbook = new XLWorkbook(ruta))
            {
                var worksheet = workbook.Worksheet(1);
                var usedRange = worksheet.RangeUsed();
                if (usedRange == null) return dt;

                // Recorrer todas las celdas usadas y añadir cada valor no vacío como fila independiente
                foreach (var row in usedRange.Rows())
                {
                    foreach (var cell in row.Cells())
                    {
                        var val = cell.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            var dr = dt.NewRow();
                            dr[0] = val.Trim();
                            dt.Rows.Add(dr);
                        }
                    }
                }
            }
            return dt;
        }

        private DataTable LeerTXT(string ruta)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Codigo"); // Nombre de la columna que verás en el DataGrid

            string[] lineas = System.IO.File.ReadAllLines(ruta);
            foreach (string linea in lineas)
            {
                if (!string.IsNullOrWhiteSpace(linea))
                    dt.Rows.Add(linea.Trim());
            }
            return dt;
        }
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            // Filtro para mostrar ambos tipos de archivos
            openFileDialog.Filter = "Archivos Permitidos (*.xlsx; *.txt)|*.xlsx;*.txt";

            if (openFileDialog.ShowDialog() == true)
            {
                // 1. Ponemos la ruta en el TextBox (el que dice 373 en tu imagen)
                txtRutaArchivo.Text = openFileDialog.FileName;

                // 2. Detectamos la extensión
                string extension = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();
                DataTable dt = new DataTable();

                try
                {
                    if (extension == ".xlsx")
                    {
                        dt = LeerExcel(openFileDialog.FileName);
                    }
                    else if (extension == ".txt")
                    {
                        dt = LeerTXT(openFileDialog.FileName);
                    }

                    // 3. Prevalidar contra la BD y mostrar previsualización
                    var rawList = dt.Rows.Cast<DataRow>().Select(r => r[0].ToString().Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    txtTotalCodigos.Text = rawList.Count.ToString();

                    // Llamar al servicio para obtener mapping de códigos encontrados
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    // Obtener producto descripciones para los productIds encontrados (parametrizado)
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

                    // Preparar mapping de estados (nombre) para mostrar en la previsualización
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

                    // Mostrar indicador de carga
                    try { pnlLoading.Visibility = Visibility.Visible; } catch { }
                    await Task.Delay(50);

                    // Construir filas de previsualización
                    var preview = new List<PreviewRow>();
                    foreach (var raw in rawList)
                    {
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);
                        lookup.TryGetValue(norm, out var tup);
                        bool isFound = tup.CodigoObj != null;
                        bool estadoValido = true;
                        if (EstadoPermitido != 0 && tup.CodigoObj != null) estadoValido = tup.CodigoObj.EstadoId == EstadoPermitido;

                        var pr = new PreviewRow
                        {
                            CodigoRaw = raw,
                            CodigoNorm = norm,
                            // Mostrar como encontrado solo si existe en BD y cumple la condición de estado o si el usuario eligió incluir inválidos
                            // Mostrar como encontrado solo si existe en BD y cumple la condición de estado o si el usuario eligió incluir inválidos
                            Encontrado = isFound && (estadoValido || GetIncluirInvalidos()),
                            CodigoCreadoId = tup.CodigoObj?.Id,
                            ProductoId = tup.ProductoId,
                            ProductoDesc = tup.ProductoId.HasValue && prodMap.ContainsKey(tup.ProductoId.Value) ? prodMap[tup.ProductoId.Value] : string.Empty,
                            EstadoId = tup.CodigoObj?.EstadoId,
                            EstadoNombre = (tup.CodigoObj != null && tup.CodigoObj.EstadoId != 0 && estadoMap.ContainsKey(tup.CodigoObj.EstadoId)) ? estadoMap[tup.CodigoObj.EstadoId] : string.Empty,
                            EstadoValido = estadoValido
                        };
                        preview.Add(pr);
                    }

                    // Ordenar por producto (nombre) y luego por código
                    preview = preview.OrderBy(p => p.ProductoDesc).ThenBy(p => p.CodigoRaw).ToList();
                    // Reasignar números de fila
                    int idx2 = 1;
                    foreach (var p in preview) p.RowNumber = idx2++;

                    dgDatos.ItemsSource = preview;

                    // Actualizar conteos y estado del botón Transferir según la previsualización
                    UpdateTransferButtonState(preview);
                    try
                    {
                        int valid = preview.Count(p => p.EstadoValido);
                        int invalid = preview.Count - valid;
                        txtValidos.Text = valid.ToString();
                        txtInvalidos.Text = invalid.ToString();
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al leer el archivo: " + ex.Message);
                }
                finally
                {
                    try { pnlLoading.Visibility = Visibility.Collapsed; } catch { }
                }
            }
        }


        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            // Llenamos nuestra lista pública con los códigos marcados como Encontrado en la previsualización
            CodigosImportados.Clear();
            if (dgDatos.ItemsSource is IEnumerable<PreviewRow> rows)
            {
                foreach (var r in rows.Where(x => x.Encontrado))
                {
                    CodigosImportados.Add(r.CodigoRaw);
                }
            }
            else if (dgDatos.ItemsSource is System.Collections.IEnumerable anyRows)
            {
                foreach (var item in anyRows)
                {
                    // intentar convertir dinámicamente
                    try
                    {
                        var prop = item.GetType().GetProperty("CodigoRaw");
                        if (prop != null)
                        {
                            var val = prop.GetValue(item)?.ToString();
                            var encontrProp = item.GetType().GetProperty("Encontrado");
                            bool ok = true;
                            if (encontrProp != null) ok = Convert.ToBoolean(encontrProp.GetValue(item));
                            if (ok && !string.IsNullOrWhiteSpace(val)) CodigosImportados.Add(val);
                        }
                    }
                    catch { }
                }
            }

            // Confirmar acción al usuario
            int total = CodigosImportados.Count;
            if (total == 0)
            {
                MessageBox.Show("No hay códigos seleccionados para transferir.", "Transferir", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var resp = MessageBox.Show($"Se transferirán {total} códigos. ¿Desea continuar?","Confirmar transferencia", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            // Indicamos que la operación fue exitosa y cerramos
            this.DialogResult = true;
            this.Close();
        }

        private void UpdateTransferButtonState()
        {
            if (dgDatos.ItemsSource is IEnumerable<PreviewRow> rows)
            {
                int found = rows.Count(r => r.Encontrado);
                try
                {
                    btnTransferir.IsEnabled = found > 0;
                    btnTransferir.Content = found > 0 ? $"Transferir ({found})" : "Transferir";
                    try
                    {
                        int valid = rows.Count(r => r.EstadoValido);
                        int invalid = rows.Count() - valid;
                        txtValidos.Text = valid.ToString();
                        txtInvalidos.Text = invalid.ToString();
                    }
                    catch { }
                }
                catch { }
            }
            else if (dgDatos.ItemsSource is System.Collections.IEnumerable anyRows)
            {
                int count = 0;
                foreach (var item in anyRows)
                {
                    try
                    {
                        var prop = item.GetType().GetProperty("Encontrado");
                        if (prop != null && Convert.ToBoolean(prop.GetValue(item))) count++;
                    }
                    catch { }
                }
                try
                {
                    btnTransferir.IsEnabled = count > 0;
                    btnTransferir.Content = count > 0 ? $"Transferir ({count})" : "Transferir";
                    try
                    {
                        int valid = 0; int total = 0;
                        foreach (var item in anyRows)
                        {
                            try
                            {
                                var pv = item as PreviewRow;
                                if (pv != null) { total++; if (pv.EstadoValido) valid++; }
                            }
                            catch { }
                        }
                        txtValidos.Text = valid.ToString();
                        txtInvalidos.Text = (total - valid).ToString();
                    }
                    catch { }
                }
                catch { }
            }
        }

        private void UpdateTransferButtonState(IEnumerable<PreviewRow> preview)
        {
            int found = preview.Count(r => r.Encontrado);
            try
            {
                btnTransferir.IsEnabled = found > 0;
                btnTransferir.Content = found > 0 ? $"Transferir ({found})" : "Transferir";
                try
                {
                    int valid = preview.Count(r => r.EstadoValido);
                    int invalid = preview.Count() - valid;
                    txtValidos.Text = valid.ToString();
                    txtInvalidos.Text = invalid.ToString();
                }
                catch { }
            }
            catch { }
        }

        private void BtnExportInvalid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgDatos.ItemsSource is not IEnumerable<PreviewRow> rows) return;
                var invalids = rows.Where(r => r.CodigoCreadoId.HasValue && r.EstadoId.HasValue && EstadoPermitido != 0 && r.EstadoId.Value != EstadoPermitido).ToList();
                if (!invalids.Any()) { MessageBox.Show("No hay códigos inválidos para exportar.", "Exportar inválidos", MessageBoxButton.OK, MessageBoxImage.Information); return; }

                var lines = new List<string> { "Codigo;Normalizado;Estado" };
                foreach (var i in invalids) lines.Add($"{i.CodigoRaw};{i.CodigoNorm};{i.EstadoNombre}");

                string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codigos_invalidos_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                System.IO.File.WriteAllLines(ruta, lines, System.Text.Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exportando inválidos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // Cerrar la ventana sin efectuar la transferencia
            try
            {
                this.DialogResult = false;
            }
            catch { }
            this.Close();
        }


    }
}
