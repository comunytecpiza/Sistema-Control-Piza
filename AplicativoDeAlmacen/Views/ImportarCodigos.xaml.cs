using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class ImportarCodigos : Window
    {
        public int EstadoPermitido { get; set; } = 0;
        public int? ProductoIdEsperado { get; set; } = null;
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
            public bool EstadoValido { get; set; }
            public bool EsClonDuplicado { get; set; }
            public int? CodigoCreadoId { get; set; }
            public int? ProductoId { get; set; }
            public string ProductoDesc { get; set; }
            public int? EstadoId { get; set; }
            public string ObservacionAuditoria { get; set; }
            public string TipoGuiaBD { get; set; }
        }

        public ImportarCodigos()
        {
            InitializeComponent();
        }

        private void MostrarMensaje(string mensaje, string titulo, MessageBoxImage icono)
        {
            MessageBox.Show(mensaje, titulo, MessageBoxButton.OK, icono);
        }

        // 🌟 SANEAMIENTO PROFESIONAL DE TEXTO
        private string LimpiarCodigo(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // Elimina caracteres invisibles (espacios nulos, saltos de línea, tabs) que rompen los códigos
            string limpiado = Regex.Replace(input, @"[\u200B-\u200D\uFEFF\u00A0\t\r\n\0]", "");
            return limpiado.Trim().ToUpperInvariant();
        }

        // 🌟 LECTURA INTELIGENTE DE EXCEL
        private List<string> LeerExcelInteligente(string ruta)
        {
            var lista = new List<string>(60000);
            using var workbook = new XLWorkbook(ruta);
            var ws = workbook.Worksheet(1);
            var used = ws.RangeUsed();
            if (used == null) return lista;

            // Busca automáticamente la columna que tiene más filas con datos para no fallar si el código no está en la 'A'
            int colDestino = 1;
            int maxDatos = 0;
            for (int c = 1; c <= used.ColumnCount(); c++)
            {
                int datosEnCol = ws.Column(c).CellsUsed().Count();
                if (datosEnCol > maxDatos)
                {
                    maxDatos = datosEnCol;
                    colDestino = c;
                }
            }

            foreach (var row in used.Rows())
            {
                var cellStr = row.Cell(colDestino).GetString();
                string limpio = LimpiarCodigo(cellStr);
                if (!string.IsNullOrEmpty(limpio))
                {
                    lista.Add(limpio);
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
                string extension = Path.GetExtension(openFileDialog.FileName).ToLower();

                List<string> rawList = new List<string>();
                _masterList = new List<PreviewRow>();

                var loadingModal = new ProgressWindow("Auditoría de Archivo", "Validando reglas y saneando datos...", async (progress) =>
                {
                    if (extension == ".xlsx") rawList = LeerExcelInteligente(openFileDialog.FileName);
                    else rawList = File.ReadLines(openFileDialog.FileName).Select(LimpiarCodigo).Where(x => !string.IsNullOrEmpty(x)).ToList();

                    var contadorOcurrenciasExcel = rawList
                        .GroupBy(x => _serviceMovimiento.NormalizarCodigo(x), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

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

                    for (int i = 0; i < total; i++)
                    {
                        string raw = rawList[i];
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);

                        bool duplicadoEnArchivo = contadorOcurrenciasExcel.ContainsKey(norm) && contadorOcurrenciasExcel[norm] > 1;
                        var listaCoincidencias = lookup.Where(x => x.Key.Equals(norm, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (duplicadoEnArchivo)
                        {
                            _masterList.Add(new PreviewRow
                            {
                                CodigoRaw = raw,
                                CodigoNorm = norm,
                                EstadoValido = false,
                                EsClonDuplicado = true,
                                ProductoDesc = "COLISIÓN INTERNA EN EXCEL",
                                ObservacionAuditoria = "❌ DUPLICADO EN EXCEL",
                                TipoGuiaBD = "N/A"
                            });
                        }
                        else if (!listaCoincidencias.Any())
                        {
                            _masterList.Add(new PreviewRow
                            {
                                CodigoRaw = raw,
                                CodigoNorm = norm,
                                EstadoValido = false,
                                EsClonDuplicado = false,
                                ProductoDesc = "NO REGISTRADO",
                                ObservacionAuditoria = "❌ CÓDIGO INEXISTENTE",
                                TipoGuiaBD = "NINGUNO"
                            });
                        }
                        else
                        {
                            foreach (var coincidencia in listaCoincidencias)
                            {
                                var tup = coincidencia.Value;
                                bool isFound = tup.CodigoObj != null;
                                string prodDesc = "Desconocido";
                                if (tup.ProductoId.HasValue) prodMap.TryGetValue(tup.ProductoId.Value, out prodDesc);

                                string tipoLibro = isFound ? await ObtenerColeccionTipoBDFormatoLocalAsync(tup.CodigoObj.Id) : "N/A";
                                string motivoError = "";
                                bool estadoValido = true;

                                if (EstadoPermitido != 0 && tup.CodigoObj.EstadoId != EstadoPermitido)
                                {
                                    estadoValido = false; motivoError = $"❌ ESTADO INVÁLIDO ({tup.CodigoObj.EstadoId})";
                                }

                                if (ProductoIdEsperado.HasValue && tup.ProductoId != ProductoIdEsperado.Value)
                                {
                                    estadoValido = false; motivoError = "❌ PRODUCTO EQUIVOCADO";
                                }

                                string obsFinal = estadoValido ? "✅ OK - APTO" : motivoError;

                                _masterList.Add(new PreviewRow
                                {
                                    CodigoRaw = raw,
                                    CodigoNorm = norm,
                                    EstadoValido = estadoValido,
                                    EsClonDuplicado = false,
                                    CodigoCreadoId = isFound ? tup.CodigoObj.Id : (int?)null,
                                    ProductoId = isFound ? tup.ProductoId : (int?)null,
                                    ProductoDesc = prodDesc,
                                    EstadoId = isFound ? tup.CodigoObj.EstadoId : (int?)null,
                                    ObservacionAuditoria = obsFinal,
                                    TipoGuiaBD = tipoLibro
                                });
                            }
                        }

                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentajeReportado) { ultimoPorcentajeReportado = pct; progress?.Report(pct); }
                    }

                    _masterList = _masterList.OrderBy(x => x.EstadoValido ? 0 : 1).ThenBy(x => x.ProductoDesc).ToList();
                    int idx = 1; foreach (var row in _masterList) row.RowNumber = idx++;
                });

                loadingModal.Owner = this;
                if (loadingModal.ShowDialog() == true)
                {
                    txtTotalCodigos.Text = rawList.Count.ToString();
                    int numClones = _masterList.Count(x => x.EsClonDuplicado);
                    btnVerDuplicados.Visibility = numClones > 0 ? Visibility.Visible : Visibility.Collapsed;
                    txtContadorDuplicados.Text = numClones.ToString();

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

            if (!string.IsNullOrEmpty(textBusqueda))
                consulta = consulta.Where(x => x.CodigoRaw.ToLower().Contains(textBusqueda) || x.ProductoDesc.ToLower().Contains(textBusqueda));

            if (filtroComboIndex == 1) consulta = consulta.Where(x => x.TipoGuiaBD.Contains("GUÍA"));
            else if (filtroComboIndex == 2) consulta = consulta.Where(x => x.TipoGuiaBD.Contains("VENTA"));
            if (_filtrandoSoloDuplicados) consulta = consulta.Where(x => x.EsClonDuplicado);

            var ejecutado = consulta.ToList();
            dgDatos.ItemsSource = null;
            dgDatos.ItemsSource = ejecutado;
            UpdateTransferButtonState();
        }

        private void BtnVerDuplicados_Click(object sender, RoutedEventArgs e)
        {
            _filtrandoSoloDuplicados = !_filtrandoSoloDuplicados;
            btnVerDuplicados.Background = _filtrandoSoloDuplicados ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
            FiltrarYMostrarDatos();
        }

        // 🌟 IMPORTACIÓN PARCIAL (Solo aprueba los válidos y desecha los errores)
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            int invalidos = _masterList.Count(r => !r.EstadoValido);

            if (invalidos > 0)
            {
                var result = MessageBox.Show(
                    $"Se detectaron {invalidos} códigos con errores o advertencias.\n\nEl sistema ignorará estos registros automáticamente.\n\n¿Desea continuar y transferir ÚNICAMENTE los códigos válidos?",
                    "Importación Parcial Segura", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.No) return;
            }

            // Seleccionamos estrictamente solo los que pasaron las validaciones
            CodigosImportados = _masterList.Where(x => x.EstadoValido).Select(x => x.CodigoRaw).ToList();
            this.DialogResult = true;
            this.Close();
        }

        private void UpdateTransferButtonState()
        {
            int total = _masterList.Count;
            int validos = _masterList.Count(r => r.EstadoValido);
            int invalidos = total - validos;

            txtValidos.Text = validos.ToString();
            txtInvalidos.Text = invalidos.ToString();
            btnExportInvalid.Visibility = invalidos > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Health Score Visual
            if (invalidos == 0 && total > 0)
            {
                brdHealth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                txtHealth.Text = "🟢 100% Íntegro - Listo para importar";
                txtHealth.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
            }
            else if (validos > 0)
            {
                brdHealth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                txtHealth.Text = $"🟡 Parcial ({validos} aptos / {invalidos} fallidos)";
                txtHealth.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            }
            else
            {
                brdHealth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                txtHealth.Text = "🔴 Archivo Inválido - Revise los errores";
                txtHealth.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"));
            }

            // El botón se habilita si hay AL MENOS 1 válido
            btnTransferir.IsEnabled = validos > 0;
            btnTransferir.Content = validos > 0 ? $"Transferir ({validos} Válidos)" : "Bloqueado";
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
            var errores = _masterList.Where(r => !r.EstadoValido).ToList();
            if (!errores.Any()) return;

            var lines = new List<string> { "Fila;Codigo;ProductoAsociado;TipoLibro;Observaciones" };
            foreach (var i in errores) lines.Add($"{i.RowNumber};{i.CodigoRaw};{i.ProductoDesc};{i.TipoGuiaBD};{i.ObservacionAuditoria}");

            string ruta = Path.Combine(Path.GetTempPath(), $"errores_importacion_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(ruta, lines, Encoding.UTF8);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }
}