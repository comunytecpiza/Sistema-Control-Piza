using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen
{
    public partial class ConfiguracionWindow : Window
    {
        public ConfiguracionWindow()
        {
            InitializeComponent();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtServer.Text))
            {
                MessageBox.Show("El servidor es obligatorio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string motor = (CmbMotor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "SQL Server";

            ConfigManager.GuardarConfiguracion(motor, TxtServer.Text.Trim(), TxtDatabase.Text.Trim(), TxtUser.Text.Trim(), TxtPassword.Password.Trim());

            MessageBox.Show("Configuración guardada correctamente en el sistema.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}