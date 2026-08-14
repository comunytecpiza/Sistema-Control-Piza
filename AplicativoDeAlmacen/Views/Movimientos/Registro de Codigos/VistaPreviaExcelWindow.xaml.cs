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
            _todosLosCodigos = todosLosCodigos ?? new List<string>();
            _duplicadosBD = duplicadosBD ?? new List<string>();
            _prefijoEsperado = prefijoEsperado?.Trim().ToUpperInvariant();

            Loaded += VistaPreviaExcelWindow_Loaded;
        }

        private void MostrarMensaje(string mensaje, string titulo, MessageBoxImage icono)
        {
            MessageBox.Show(mensaje, titulo, MessageBoxButton.OK, icono);
        }

        private static string LimpiarCodigoRapido(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // Limpieza eficiente en memoria
            return input.Trim().ToUpperInvariant().Replace("\t", "").Replace("\r", "").Replace("\n", "");
        }

        private async void VistaPreviaExcelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                _listaVisual.Clear();

                int contadorValidos = 0;
                int contadorErrores = 0;

                var loadingModal = new ProgressWindow("Auditoría de Lote Masivo", "Validando duplicados e integridad de 20,000+ registros...", async (progress) =>
                {
                    await Task.Run(() =>
                    {
                        int total = _todosLosCodigos.Count;
                        if (total == 0) return;

                        var arregloResultados = new CodigoExcelPreview[total];

                        // 🌟 1. HashSet O(1) con capacidad preasignada
                        var setDuplicadosBD = new HashSet<string>(_duplicadosBD.Count, StringComparer.OrdinalIgnoreCase);
                        foreach (var d in _duplicadosBD)
                        {
                            string dLimpio = LimpiarCodigoRapido(d);
                            if (!string.IsNullOrEmpty(dLimpio)) setDuplicadosBD.Add(dLimpio);
                        }

                        var codigosProcesadosEnExcel = new HashSet<string>(total, StringComparer.OrdinalIgnoreCase);

                        string prefijoLimpio = (_prefijoEsperado ?? "").Replace(" ", "").Replace("-", "").ToUpperInvariant();
                        bool tienePrefijoFiltro = !string.IsNullOrEmpty(prefijoLimpio);

                        int validosLocales = 0;
                        int erroresLocales = 0;

                        // 🌟 2. Punteros para colocar ERRORES al inicio y VÁLIDOS al final (O(N) sin ordenar)
                        int indexErrores = 0;
                        int indexValidos = total - 1;

                        int ultimoPorcentajeReportado = -1;

                        for (int i = 0; i < total; i++)
                        {
                            string cod = LimpiarCodigoRapido(_todosLosCodigos[i]);

                            CodigoExcelPreview item;

                            if (string.IsNullOrEmpty(cod))
                            {
                                item = new CodigoExcelPreview
                                {
                                    Numero = i + 1,
                                    Codigo = "VACÍO",
                                    Estado = "❌ Código Vacío",
                                    EsValido = false
                                };
                                erroresLocales++;
                                arregloResultados[indexErrores++] = item;
                            }
                            else
                            {
                                bool esDuplicadoBD = setDuplicadosBD.Contains(cod);
                                bool esDuplicadoInternoExcel = !codigosProcesadosEnExcel.Add(cod);

                                bool cumplePrefijo = true;
                                if (tienePrefijoFiltro)
                                {
                                    string codLimpioComparar = cod.Replace(" ", "").Replace("-", "");
                                    cumplePrefijo = codLimpioComparar.StartsWith(prefijoLimpio, StringComparison.OrdinalIgnoreCase);
                                }

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

                                item = new CodigoExcelPreview
                                {
                                    Numero = i + 1,
                                    Codigo = cod,
                                    Estado = mensajeEstado,
                                    EsValido = esValido
                                };

                                if (esValido)
                                {
                                    validosLocales++;
                                    arregloResultados[indexValidos--] = item;
                                }
                                else
                                {
                                    erroresLocales++;
                                    arregloResultados[indexErrores++] = item;
                                }
                            }

                            // Reportar progreso periódicamente
                            int pct = (i * 100) / total;
                            if (pct > ultimoPorcentajeReportado)
                            {
                                ultimoPorcentajeReportado = pct;
                                progress?.Report(pct);
                            }
                        }

                        // Reordenar elementos válidos preservando prioridad de errores al inicio
                        var listaFinal = new List<CodigoExcelPreview>(total);
                        for (int k = 0; k < indexErrores; k++) listaFinal.Add(arregloResultados[k]);
                        for (int k = total - 1; k > indexValidos; k--) listaFinal.Add(arregloResultados[k]);

                        _listaVisual = listaFinal;
                        contadorValidos = validosLocales;
                        contadorErrores = erroresLocales;
                    });
                });

                loadingModal.Owner = this;

                if (loadingModal.ShowDialog() == true)
                {
                    TxtResumen.Text = $"Total: {_listaVisual.Count:N0} | Aptos: {contadorValidos:N0} | Errores/Repetidos: {contadorErrores:N0}";

                    // Renderizado rápido en grilla con límite visual
                    DgCodigos.ItemsSource = null;
                    DgCodigos.ItemsSource = _listaVisual.Take(1000).ToList();

                    if (_listaVisual.Count > 1000)
                    {
                        TxtResumen.Text += " (Mostrando primeros 1,000 registros para fluidez visual)";
                    }

                    BtnConfirmar.IsEnabled = contadorValidos > 0;
                    BtnConfirmar.Content = contadorValidos > 0 ? $"Confirmar ({contadorValidos:N0} Aptos)" : "Lote Inválido";
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
                var resp = MessageBox.Show($"Se detectaron {invalidos:N0} códigos inválidos (repetidos en Excel, existentes en BD o con prefijo ajeno).\n\n¿Desea omitir los errores y guardar ÚNICAMENTE los válidos?", "Filtro de Seguridad", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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