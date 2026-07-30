using System.Windows;

namespace AplicativoDeAlmacen.Views
{
    public partial class FiltroImpresionKardexWindow : Window
    {
        public bool IncluirEnviados => ChkEnviados.IsChecked ?? false;
        public bool IncluirDevueltos => ChkDevueltos.IsChecked ?? false;
        public bool IncluirVendidos => ChkVendidos.IsChecked ?? false;

        public bool SeConfirmoImpresion { get; private set; } = false;

        // Modificamos el constructor para recibir los estados de los filtros
        public FiltroImpresionKardexWindow(bool tieneRazonSocial, bool tieneUbicacion)
        {
            InitializeComponent();

            // 🌟 Si hay Ubicación o Razón Social activa, habilitamos "Detalle Vendidos"
            if (tieneRazonSocial || tieneUbicacion)
            {
                ChkVendidos.IsEnabled = true;
                ChkVendidos.IsChecked = true; // Opcional: marcado por defecto si se cumple
            }
            else
            {
                // Si no hay ninguno, se mantiene bloqueado y desmarcado
                ChkVendidos.IsEnabled = false;
                ChkVendidos.IsChecked = false;
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            SeConfirmoImpresion = true;
            this.Close();
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            SeConfirmoImpresion = false;
            this.Close();
        }
    }
}