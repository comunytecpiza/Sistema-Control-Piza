using System;
using System.Windows;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class CrearRolWindow : Window
    {
        public int RolCreadoId { get; private set; } = 0;
        private readonly UsuarioService _usuarioService;

        public CrearRolWindow()
        {
            InitializeComponent();
            _usuarioService = new UsuarioService();
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombreRol.Text))
            {
                MessageBox.Show("Ingrese un nombre para el rol.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                RolCreadoId = await _usuarioService.CrearRolAsync(TxtNombreRol.Text.Trim(), TxtDescripcionRol.Text.Trim());
                MessageBox.Show("Rol registrado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al crear el rol: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}