using System.Windows;

namespace AplicativoDeAlmacen.Views
{
    public partial class FiltroImpresionKardexWindow : Window
    {
        public bool IncluirEnviados => ChkEnviados.IsChecked ?? false;
        public bool IncluirDevueltos => ChkDevueltos.IsChecked ?? false;
        public bool IncluirVendidos => ChkVendidos.IsChecked ?? false;

        public bool SeConfirmoImpresion { get; private set; } = false;

        public FiltroImpresionKardexWindow(bool tieneRazonSocial, bool tieneUbicacion, bool tieneAlmacen = false)
        {
            InitializeComponent();

            if (tieneAlmacen)
            {
                // 🏢 MODO FILTRO POR ALMACÉN
                ChkEnviados.Content = "Detalle Enviados";
                ChkDevueltos.Content = "Detalle Devueltos / Recibidos";
                ChkVendidos.Content = "Detalle Transferidos en su poder";
                ChkVendidos.IsEnabled = true;
                ChkVendidos.IsChecked = false;
            }
            else if (tieneRazonSocial || tieneUbicacion)
            {
                // 👤 MODO PROMOTORA / TERCEROS / UBICACIÓN
                ChkEnviados.Content = "Detalle Enviados";
                ChkDevueltos.Content = "Detalle Devueltos";
                ChkVendidos.Content = "Detalle Vendidos / En Poder";
                ChkVendidos.IsEnabled = true;
                ChkVendidos.IsChecked = false;
            }
            else
            {
                // 🔵 MODO GENERAL SIN FILTRO DE ENTIDAD
                ChkVendidos.Content = "Detalle Vendidos";
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