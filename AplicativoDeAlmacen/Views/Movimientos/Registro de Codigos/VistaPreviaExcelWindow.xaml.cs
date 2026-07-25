using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace AplicativoDeAlmacen.Views
{
    public class CodigoExcelPreview
    {
        public int Numero { get; set; }
        public string Codigo { get; set; }
        public string Estado { get; set; }
        public bool EsValido { get; set; }
    }

    public partial class VistaPreviaExcelWindow : Window
    {
        public List<string> CodigosAprobados { get; private set; } = new List<string>();
        private List<CodigoExcelPreview> _listaVisual = new List<CodigoExcelPreview>();

        private readonly List<string> _todosLosCodigos;
        private readonly List<string> _duplicadosBD;
        private readonly string _prefijoEsperado;

        public VistaPreviaExcelWindow(List<string> todosLosCodigos, List<string> duplicadosBD, string prefijoEsperado = null)
        {
            InitializeComponent();
            _todosLosCodigos = todosLosCodigos;
            _duplicadosBD = duplicadosBD ?? new List<string>();
            _prefijoEsperado = prefijoEsperado?.Trim().ToUpperInvariant();

            Loaded += VistaPreviaExcelWindow_Loaded;
        }

        private void MostrarMensaje(string mensaje, string titulo, MessageBoxImage icono)
        {
            MessageBox.Show(mensaje, titulo, MessageBoxButton.OK, icono);
        }

        private string LimpiarCodigo(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string limpiado = Regex.Replace(input, @"[\u200B-\u200D\uFEFF\u00A0\t\r\n\0]", "");
            return limpiado.Trim().ToUpperInvariant();
        }

        private async void VistaPreviaExcelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                _listaVisual.Clear();

                int contadorValidos = 0;
                int contadorErrores = 0;

                var loadingModal = new ProgressWindow("Auditoría de Lote", "Analizando colisiones internas, base de datos y prefijos...", async (progress) =>
                {
                    int total = _todosLosCodigos.Count;
                    int ultimoPorcentajeReportado = -1;

                    // 1. Set para detectar duplicados con la Base de Datos
                    var setDuplicadosBD = new HashSet<string>(_duplicadosBD.Select(LimpiarCodigo), StringComparer.OrdinalIgnoreCase);

                    // 🌟 2. Set para controlar duplicados INTERNOS dentro del mismo archivo Excel
                    var codigosProcesadosEnExcel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < total; i++)
                    {
                        string cod = LimpiarCodigo(_todosLosCodigos[i]);
                        if (string.IsNullOrEmpty(cod)) continue;

                        // A. ¿Ya existe en la Base de Datos?
                        bool esDuplicadoBD = setDuplicadosBD.Contains(cod);

                        // B. ¿Es una repetición interna dentro del mismo Excel?
                        bool esDuplicadoInternoExcel = codigosProcesadosEnExcel.Contains(cod);

                        // C. Validación de Prefijo
                        string codLimpioParaComparar = Regex.Replace(cod, @"[\s\-]", "").ToUpperInvariant();
                        string prefijoLimpioParaComparar = Regex.Replace(_prefijoEsperado ?? "", @"[\s\-]", "").ToUpperInvariant();

                        bool cumplePrefijo = string.IsNullOrEmpty(prefijoLimpioParaComparar) ||
                                             codLimpioParaComparar.StartsWith(prefijoLimpioParaComparar, StringComparison.OrdinalIgnoreCase);

                        // 🛑 Evaluación estricta de validez
                        bool esValido = !esDuplicadoBD && !esDuplicadoInternoExcel && cumplePrefijo;
                        string mensajeEstado = "✅ Listo para generar";

                        if (esDuplicadoBD)
                        {
                            mensajeEstado = "❌ Ya existe en Base de Datos";
                        }
                        else if (esDuplicadoInternoExcel)
                        {
                            mensajeEstado = "❌ Duplicado Repetido en Excel";
                        }
                        else if (!cumplePrefijo)
                        {
                            mensajeEstado = $"❌ Prefijo ajeno (Esperado: {_prefijoEsperado})";
                        }

                        // Registramos este código como procesado para atrapar futuras repeticiones en las filas de abajo
                        codigosProcesadosEnExcel.Add(cod);

                        if (esValido) contadorValidos++; else contadorErrores++;

                        _listaVisual.Add(new CodigoExcelPreview
                        {
                            Numero = i + 1,
                            Codigo = cod,
                            Estado = mensajeEstado,
                            EsValido = esValido
                        });

                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = pct;
                            progress?.Report(pct);
                        }
                    }

                    // Ordenamos: Los errores primero arriba para revisión rápida
                    _listaVisual = _listaVisual.OrderBy(x => x.EsValido ? 1 : 0).ToList();
                    await Task.Delay(30);
                });

                loadingModal.Owner = this;

                if (loadingModal.ShowDialog() == true)
                {
                    TxtResumen.Text = $"Total: {_listaVisual.Count} | Aptos: {contadorValidos} | Errores/Repetidos: {contadorErrores}";

                    // 🟢 OPTIMIZACIÓN VIRTUAL: Solo enviamos 1,000 elementos al DataGrid para carga instantánea
                    DgCodigos.ItemsSource = null;
                    DgCodigos.ItemsSource = _listaVisual.Take(1000).ToList();

                    if (_listaVisual.Count > 1000)
                    {
                        TxtResumen.Text += " (Mostrando primeros 1,000 registros para mayor fluidez)";
                    }

                    BtnConfirmar.IsEnabled = contadorValidos > 0;
                    BtnConfirmar.Content = contadorValidos > 0 ? $"Confirmar ({contadorValidos} Aptos)" : "Lote Inválido";
                    if (contadorValidos == 0) BtnConfirmar.Background = System.Windows.Media.Brushes.Gray;
                }
            }
            catch (Exception ex)
            {
                MostrarMensaje($"Error al procesar el archivo: {ex.Message}", "Falla del Sistema", MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            int invalidos = _listaVisual.Count(x => !x.EsValido);
            if (invalidos > 0)
            {
                var resp = MessageBox.Show($"Se detectaron {invalidos} códigos inválidos (repetidos en Excel, existentes en BD o con prefijo ajeno).\n\n¿Desea omitir los errores y guardar ÚNICAMENTE los válidos?", "Filtro de Seguridad", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (resp == MessageBoxResult.No) return;
            }

            CodigosAprobados = _listaVisual.Where(x => x.EsValido).Select(x => x.Codigo).ToList();
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}