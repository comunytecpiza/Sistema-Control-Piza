using System;
using System.Threading.Tasks;
using System.Windows;

namespace AplicativoDeAlmacen.Views
{
    public partial class ProgressWindow : Window
    {
        private readonly Func<IProgress<int>, Task> _worker;

        public Exception ErrorResult { get; private set; }

        public ProgressWindow(string titulo, string estadoInicial, Func<IProgress<int>, Task> worker)
        {
            InitializeComponent();
            txtTitulo.Text = titulo;
            txtEstado.Text = estadoInicial;
            _worker = worker;

            this.Loaded += ProgressWindow_Loaded;
        }

        private async void ProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Creamos el reportero de progreso conectado directamente a la UI
            var progressReporter = new Progress<int>(percent =>
            {
                pbProgreso.Value = percent;
                txtPorcentaje.Text = $"{percent}%";
            });

            try
            {
                // Ejecutamos la tarea masiva en un hilo de Background para liberar la UI por completo
                await Task.Run(() => _worker(progressReporter));
                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                ErrorResult = ex;
                this.DialogResult = false;
            }
            finally
            {
                this.Close();
            }
        }
    }
}