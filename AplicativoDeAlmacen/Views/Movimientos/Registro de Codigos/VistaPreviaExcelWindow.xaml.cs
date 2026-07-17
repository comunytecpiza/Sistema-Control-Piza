using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    // Clase temporal para la grilla
    public class CodigoExcelPreview
    {
        public bool Incluir { get; set; }
        public int Numero { get; set; }
        public string Codigo { get; set; }
        public string Estado { get; set; }
    }

    public partial class VistaPreviaExcelWindow : Window
    {
        // Esta es la lista que te marcaba error porque no existía
        public List<string> CodigosAprobados { get; private set; } = new List<string>();

        private List<CodigoExcelPreview> _listaVisual = new List<CodigoExcelPreview>();

        // Este es el constructor que te marcaba error porque faltaba
        public VistaPreviaExcelWindow(List<string> todosLosCodigos, List<string> duplicados)
        {
            InitializeComponent();
            CargarGrilla(todosLosCodigos, duplicados);
        }

        private void CargarGrilla(List<string> todos, List<string> duplicados)
        {
            int index = 1;
            int contadorNuevos = 0;
            int contadorDuplicados = 0;

            // 🌟 OPTIMIZACIÓN DE ALTO RENDIMIENTO: Convertir a HashSet para búsquedas instantáneas O(1)
            var setDuplicados = new HashSet<string>(duplicados, StringComparer.OrdinalIgnoreCase);

            foreach (var cod in todos)
            {
                // La búsqueda en el Hash es inmediata, no importa si son 60,000 registros
                bool esDuplicado = setDuplicados.Contains(cod);
                if (esDuplicado) contadorDuplicados++; else contadorNuevos++;

                _listaVisual.Add(new CodigoExcelPreview
                {
                    Incluir = !esDuplicado,
                    Numero = index++,
                    Codigo = cod,
                    Estado = esDuplicado ? "Duplicado" : "Nuevo"
                });
            }

            TxtResumen.Text = $"Total detectados: {todos.Count} | Nuevos: {contadorNuevos} | Duplicados: {contadorDuplicados}";
            DgCodigos.ItemsSource = _listaVisual;
        }

        private void ChkSeleccionarTodos_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = (sender as CheckBox).IsChecked == true;
            foreach (var item in _listaVisual)
            {
                // Solo permitimos marcar todos si no son duplicados (seguridad)
                if (item.Estado != "Duplicado" || !isChecked)
                {
                    item.Incluir = isChecked;
                }
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