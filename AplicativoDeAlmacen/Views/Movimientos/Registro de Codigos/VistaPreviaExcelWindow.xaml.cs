using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace AplicativoDeAlmacen.Views


{

    public class CodigoExcelPreview
    {
        public bool Incluir { get; set; }
        public int Numero { get; set; }
        public string Codigo { get; set; }
        public string Estado { get; set; }
    }
    public partial class VistaPreviaExcelWindow : Window
    {
        public List<string> CodigosAprobados { get; private set; } = new List<string>();
        private List<CodigoExcelPreview> _listaVisual = new List<CodigoExcelPreview>();

        // Guardamos las listas originales temporalmente para el procesamiento
        private readonly List<string> _todosLosCodigos;
        private readonly List<string> _duplicados;

        public VistaPreviaExcelWindow(List<string> todosLosCodigos, List<string> duplicados)
        {
            InitializeComponent();

            _todosLosCodigos = todosLosCodigos;
            _duplicados = duplicados;

            // Gatillamos la carga asíncrona inmediatamente después de que se inicialice el diseño
            Loaded += VistaPreviaExcelWindow_Loaded;
        }

        private async void VistaPreviaExcelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                _listaVisual.Clear();

                int contadorNuevos = 0;
                int contadorDuplicados = 0;

                // 🌟 INVOCACIÓN OFICIAL DE PROGRESSWINDOW PARA LA CARGA EN RAM
                var loadingModal = new ProgressWindow("Procesando Vista Previa", "Analizando consistencia de códigos en la memoria RAM...", async (progress) =>
                {
                    int total = _todosLosCodigos.Count;
                    int ultimoPorcentajeReportado = -1;

                    // Construcción acelerada O(1) mediante el Hash
                    var setDuplicados = new HashSet<string>(_duplicados, StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < total; i++)
                    {
                        string cod = _todosLosCodigos[i];
                        bool esDuplicado = setDuplicados.Contains(cod);

                        if (esDuplicado) contadorDuplicados++; else contadorNuevos++;

                        // Registramos en la lista temporal de RAM
                        _listaVisual.Add(new CodigoExcelPreview
                        {
                            Incluir = !esDuplicado,
                            Numero = i + 1,
                            Codigo = cod,
                            Estado = esDuplicado ? "Duplicado" : "Nuevo"
                        });

                        // 📈 REPORTAR PROGRESO A LA BARRA DE CARGA
                        int pct = (i * 100) / total;
                        if (pct > ultimoPorcentajeReportado)
                        {
                            ultimoPorcentajeReportado = pct;
                            progress?.Report(pct);
                        }
                    }

                    await Task.Delay(50); // Respiro técnico imperceptible para estabilizar el hilo gráfico
                });

                // Establecemos el Owner para que la barra se centre perfectamente encima
                loadingModal.Owner = this;

                if (loadingModal.ShowDialog() == true)
                {
                    // Volcamos los resultados procesados limpiamente a la UI principal
                    TxtResumen.Text = $"Total detectados: {_todosLosCodigos.Count} | Nuevos: {contadorNuevos} | Duplicados: {contadorDuplicados}";

                    DgCodigos.ItemsSource = null;
                    DgCodigos.ItemsSource = _listaVisual;

                    // Forzamos el redibujado instantáneo de las filas en la UI
                    DgCodigos.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al construir la vista previa masiva: {ex.Message}", "Falla de RAM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void ChkSeleccionarTodos_Click(object sender, RoutedEventArgs e)
        {
            if (DgCodigos == null) return;

            // Desactivamos estados de edición activos en las celdas para evitar crasheos
            DgCodigos.CancelEdit(DataGridEditingUnit.Cell);
            DgCodigos.CancelEdit(DataGridEditingUnit.Row);

            bool isChecked = (sender as CheckBox).IsChecked == true;

            foreach (var item in _listaVisual)
            {
                item.Incluir = isChecked;
            }

            DgCodigos.Items.Refresh();
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            CodigosAprobados = _listaVisual.Where(x => x.Incluir).Select(x => x.Codigo).ToList();
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