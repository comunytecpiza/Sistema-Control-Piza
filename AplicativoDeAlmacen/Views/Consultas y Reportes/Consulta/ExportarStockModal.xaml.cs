using System.Windows;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta
{
    public partial class ExportarStockModal : Window
    {
        public bool IncluirOperativos => ChkOperativos.IsChecked ?? false;
        public bool IncluirRestringidos => ChkRestringidos.IsChecked ?? false;
        public bool SeAcepto { get; private set; } = false;

        public ExportarStockModal()
        {
            InitializeComponent();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (!IncluirOperativos && !IncluirRestringidos)
            {
                MessageBox.Show("Debe seleccionar al menos una opción para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SeAcepto = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            SeAcepto = false;
            Close();
        }
    }
}