using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClosedXML.Excel;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Data;
using System.Data.Common;
using System.IO;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Diagnostics;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views
{
    public partial class ImportarCodigos : Window
    {
        public int EstadoPermitido { get; set; } = 0;
        public List<string> CodigosImportados { get; set; } = new List<string>();

        private readonly Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        private readonly IngresoMovimientoService _serviceMovimiento = new IngresoMovimientoService();
        private readonly DatabaseConnection _db = new DatabaseConnection();
        private List<PreviewRow> _masterList = new List<PreviewRow>();
        private bool _filtrandoSoloDuplicados = false;

        private class PreviewRow
        {
            public int RowNumber { get; set; }
            public string CodigoRaw { get; set; }
            public string CodigoNorm { get; set; }
            public bool Encontrado { get; set; }
            public bool EstadoValido { get; set; }
            public bool EsClonDuplicado { get; set; }
            public int? CodigoCreadoId { get; set; }
            public int? ProductoId { get; set; }
            public string ProductoDesc { get; set; }
            public int? EstadoId { get; set; }
            public string EstadoNombre { get; set; }
            public string TipoGuiaBD { get; set; }
        }

        public ImportarCodigos()
        {
            InitializeComponent();
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
                    if (!string.IsNullOrEmpty(codigo)) lista.Add(codigo);
                }
            }
            return lista;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            openFileDialog.Filter = "Archivos Permitidos (*.xlsx; *.txt)|*.xlsx;*.txt";

            if (openFileDialog.ShowDialog() == true)
            {
                txtRutaArchivo.Text = openFileDialog.FileName;
                string extension = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();

                List<string> rawList = new List<string>();
                _masterList = new List<PreviewRow>();

                var loadingModal = new ProgressWindow("Procesando Archivo", "Mapeando colisiones...", async (progress) =>
                {
                    if (extension == ".xlsx") rawList = LeerExcel(openFileDialog.FileName);
                    else rawList = File.ReadLines(openFileDialog.FileName).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

                    // 🚀 OBTENER DICCIONARIO ATÓMICO MULTI-RESULTADO
                    // Nota: Asegúrate de que ObtenerCodigosPorListaAsync en tu Service use un JOIN sin 'TOP 1' o GroupBy drástico
                    // para permitir que un mismo String normalizado extraiga múltiples filas de 'codigos_creados'
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    // Catálogo indexado de productos
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
                            if (!rdr.IsDBNull(0)) prodMap[rdr.GetInt32(0)] = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                        }
                    }

                    int total = rawList.Count;
                    int ultimoPorcentajeReportado = -1;
                    int estadoPermitidoLocal = EstadoPermitido;

                    for (int i = 0; i < total; i++)
                    {
                        string raw = rawList[i];
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);

                        // 🌟 MULTI-MATCH: Buscamos todas las coincidencias que contengan esta cadena normalizada en el lookup
                        var listaCoincidencias = lookup.Where(x => x.Key.Equals(norm, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (!listaCoincidencias.Any())
                        {
                            _masterList.Add(new PreviewRow
                            {
                                CodigoRaw = raw,
                                CodigoNorm = norm,
                                Encontrado = false,
                                EstadoValido = false,
                                EsClonDuplicado = false,
                                ProductoDesc = "NO EXISTE EN INVENTARIO",
                                EstadoNombre = "INEXISTENTE",
                                TipoGuiaBD = "NINGUNO"
                            });
                        }
                        else
                        {
                            // Es un clon real si el mismo código normalizado está asignado a más de un ProductoId único
                            bool esClon = listaCoincidencias.Select(x => x.Value.ProductoId).Distinct().Count() > 1;

                            foreach (var coincidencia in listaCoincidencias)
                            {
                                var tup = coincidencia.Value;
                                bool isFound = tup.CodigoObj != null;
                                bool estadoValido = isFound && (estadoPermitidoLocal == 0 || tup.CodigoObj.EstadoId == estadoPermitidoLocal);

                                string prodDesc = "Desconocido";
                                if (tup.ProductoId.HasValue) prodMap.TryGetValue(tup.ProductoId.Value, out prodDesc);

                                string tipoLibro = await ObtenerColeccionTipoBDFormatoLocalAsync(tup.CodigoObj.Id);

                                // 🌟 TRADUCCIÓN INTELIGENTE DE ESTADOS REGLAMENTARIOS
                                string nombreEstadoReal = "OTRO";
                                if (isFound)
                                {
                                    nombreEstadoReal = tup.CodigoObj.EstadoId switch
                                    {
                                        1 => "DISPONIBLE",
                                        3 => "TIENE ENTRADA",
                                        4 => "TIENE SALIDA",
                                        _ => $"ESTADO {tup.CodigoObj.EstadoId}"
                                    };
                                }

                                _masterList.Add(new PreviewRow
                                {
                                    CodigoRaw = raw,
                                    CodigoNorm = norm,
                                    EstadoValido = estadoValido,
                                    EsClonDuplicado = esClon, // Activará la fila en amarillo
                                    CodigoCreadoId = isFound ? tup.CodigoObj.Id : (int?)null,
                                    ProductoId = isFound ? tup.ProductoId : (int?)null,
                                    ProductoDesc = prodDesc ?? "CÓDIGO PLANO",
                                    EstadoId = isFound ? tup.CodigoObj.EstadoId : (int?)null,
                                    EstadoNombre = nombreEstadoReal, // Refleja el estado dinámico correcto
                                    TipoGuiaBD = tipoLibro, // LIBRO GUÍA o LIBRO VENTA
                                    // Si es clon colisionado, por seguridad viene desmarcado (false) para que tú decidas cuál pasa
                                    Encontrado = estadoValido && !esClon
                                });
                            }
                        }

                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentajeReportado) { ultimoPorcentajeReportado = pct; progress?.Report(pct); }
                    }

                    // Priorizamos los duplicados en amarillo arriba para control directo de triaje
                    _masterList = _masterList.OrderByDescending(x => x.EsClonDuplicado).ThenBy(x => x.ProductoDesc).ToList();
                    int idx = 1; foreach (var row in _masterList) row.RowNumber = idx++;
                });

                loadingModal.Owner = this;
                if (loadingModal.ShowDialog() == true)
                {
                    txtTotalCodigos.Text = rawList.Count.ToString();

                    // Sincronizar el contador dinámico de colisiones
                    int numClones = _masterList.Count(x => x.EsClonDuplicado);
                    if (numClones > 0) { txtContadorDuplicados.Text = (numClones / 2).ToString(); btnVerDuplicados.Visibility = Visibility.Visible; }
                    else { btnVerDuplicados.Visibility = Visibility.Collapsed; }

                    _filtrandoSoloDuplicados = false;
                    FiltrarYMostrarDatos();
                }
            }
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e) { FiltrarYMostrarDatos(); }
        private void CboFiltroTipoLibro_SelectionChanged(object sender, SelectionChangedEventArgs e) { FiltrarYMostrarDatos(); }

        private void FiltrarYMostrarDatos()
        {
            if (_masterList == null || !_masterList.Any()) return;

            string textBusqueda = txtBuscarCodigo.Text.Trim().ToLower();
            int filtroComboIndex = cboFiltroTipoLibro.SelectedIndex;

            var consulta = _masterList.AsEnumerable();

            // 1. Buscador en tiempo real
            if (!string.IsNullOrEmpty(textBusqueda))
                consulta = consulta.Where(x => x.CodigoRaw.ToLower().Contains(textBusqueda) || x.ProductoDesc.ToLower().Contains(textBusqueda));

            // 2. Filtro Combo (Todos, Guía, Venta)
            if (filtroComboIndex == 1) consulta = consulta.Where(x => x.TipoGuiaBD.Contains("GUÍA"));
            else if (filtroComboIndex == 2) consulta = consulta.Where(x => x.TipoGuiaBD.Contains("VENTA"));

            // 3. Aislamiento estricto de Duplicados en Amarillo
            if (_filtrandoSoloDuplicados)
                consulta = consulta.Where(x => x.EsClonDuplicado);

            var ejecutado = consulta.ToList();
            dgDatos.ItemsSource = null;
            dgDatos.ItemsSource = ejecutado;
            UpdateTransferButtonState(ejecutado);
        }

        private void BtnVerDuplicados_Click(object sender, RoutedEventArgs e)
        {
            _filtrandoSoloDuplicados = !_filtrandoSoloDuplicados;
            btnVerDuplicados.Background = _filtrandoSoloDuplicados ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
            FiltrarYMostrarDatos();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (dgDatos.ItemsSource is List<PreviewRow> actual) UpdateTransferButtonState(actual);
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            CodigosImportados.Clear();
            var aprobados = _masterList.Where(x => x.Encontrado).ToList(); // Pasan todos los que dejaste con el Check activo

            var transferModal = new ProgressWindow("Inyectando Lote", "Transfiriendo...", async (progress) =>
            {
                int total = aprobados.Count;
                for (int i = 0; i < total; i++)
                {
                    CodigosImportados.Add(aprobados[i].CodigoRaw);
                    progress.Report((i * 100) / total);
                }
                await Task.Delay(20);
            });

            transferModal.Owner = this;
            if (transferModal.ShowDialog() == true) { this.DialogResult = true; this.Close(); }
        }

        private void UpdateTransferButtonState(IEnumerable<PreviewRow> visibleRows)
        {
            int approved = _masterList.Count(r => r.Encontrado);
            btnTransferir.IsEnabled = approved > 0;
            btnTransferir.Content = approved > 0 ? $"Transferir ({approved})" : "Transferir";

            int valid = _masterList.Count(r => r.EstadoValido);
            txtValidos.Text = valid.ToString();
            txtInvalidos.Text = (_masterList.Count - valid).ToString();
        }

        private async Task<string> ObtenerColeccionTipoBDFormatoLocalAsync(int codigoCreadoId)
        {
            using var conn = _db.GetConnection(); var dbConn = (DbConnection)conn; await dbConn.OpenAsync();
            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT rc.categoria_producto_id FROM codigos_creados cc JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id WHERE cc.id = @id");
            var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = codigoCreadoId; cmd.Parameters.Add(p);
            var res = await cmd.ExecuteScalarAsync();
            return (res != null && Convert.ToInt32(res) == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";
        }

        private void BtnExportInvalid_Click(object sender, RoutedEventArgs e)
        {
            // 🌟 REGLA DE EXPORTACIÓN EXPANDIDA: Exporta tanto los duplicados clonados como los inexistentes
            var alertas = _masterList.Where(r => r.EsClonDuplicado || !r.CodigoCreadoId.HasValue).ToList();
            if (!alertas.Any()) return;

            var lines = new List<string> { "Fila;Codigo;ProductoAsociado;TipoLibro;EstadoActual" };
            foreach (var i in alertas) lines.Add($"{i.RowNumber};{i.CodigoRaw};{i.ProductoDesc};{i.TipoGuiaBD};{i.EstadoNombre}");

            string ruta = Path.Combine(Path.GetTempPath(), $"alertas_inventario_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(ruta, lines, Encoding.UTF8);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }
}