using System;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class UsuariosUserControl : UserControl
    {
        private readonly UsuarioService _usuarioService;
        private int _usuarioSeleccionadoId = 0;

        public UsuariosUserControl()
        {
            InitializeComponent();
            _usuarioService = new UsuarioService();

            // Inicialización Asíncrona Controlada
            InicializarComponentesNegocio();
        }

        private async void InicializarComponentesNegocio()
        {
            try
            {
                // 1. Cargar el ComboBox de Roles directo de la Base de Datos de manera profesional
                var roles = await _usuarioService.ObtenerRolesActivosAsync();
                CboRol.ItemsSource = roles;
                CboRol.DisplayMemberPath = "Nombre";
                CboRol.SelectedValuePath = "Id";

                // 2. Poblar la lista principal de usuarios
                CargarUsuarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error de inicialización de catálogo: " + ex.Message, "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CargarUsuarios(string filtro = "")
        {
            try
            {
                var lista = await _usuarioService.ObtenerTodosAsync(filtro);
                UsuariosDataGrid.ItemsSource = lista;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la grilla de usuarios: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            CargarUsuarios(TxtBuscar.Text.Trim());
        }

        private void UsuariosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // CORREGIDO: Mapeo directo y legítimo utilizando la entidad Usuario
            if (UsuariosDataGrid.SelectedItem is Usuario usuario)
            {
                _usuarioSeleccionadoId = usuario.Id;
                TxtCodigo.Text = usuario.Username;
                TxtNombres.Text = usuario.Nombres;
                TxtPassword.Password = usuario.Password;
                CboRol.SelectedValue = usuario.RolUsuarioId;

                // Mapear estado booleano al ComboBoxItem correspondiente
                CboEstado.SelectedIndex = usuario.Estado ? 0 : 1;
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _usuarioSeleccionadoId = 0;
            TxtCodigo.Text = "AUTO-GEN";
            TxtNombres.Text = string.Empty;
            TxtPassword.Password = string.Empty;
            CboRol.SelectedIndex = -1;
            CboEstado.SelectedIndex = 0;
            TxtNombres.Focus();
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombres.Text) || string.IsNullOrWhiteSpace(TxtPassword.Password) || CboRol.SelectedValue == null)
            {
                MessageBox.Show("Los campos Nombres, Clave y Rol Principal son mandatorios.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Extraer el valor booleano del tag seleccionado para el estado
                bool estadoActivo = Convert.ToBoolean(((ComboBoxItem)CboEstado.SelectedItem).Tag);

                // CORREGIDO: Creación limpia del objeto con tipado robusto compatible con el Servicio
                var usuario = new Usuario
                {
                    Id = _usuarioSeleccionadoId,
                    Nombres = TxtNombres.Text.Trim(),
                    Password = TxtPassword.Password,
                    RolUsuarioId = (int)CboRol.SelectedValue,
                    Estado = estadoActivo
                };

                if (_usuarioSeleccionadoId == 0)
                {
                    await _usuarioService.InsertarAsync(usuario);
                    MessageBox.Show("Usuario incorporado con éxito al sistema corporativo.", "Confirmación", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _usuarioService.ActualizarAsync(usuario);
                    MessageBox.Show("Registro de usuario actualizado correctamente.", "Confirmación", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                CargarUsuarios();
                BtnNuevo_Click(null, null); // Limpieza de campos post-transacción
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falla en la transacción de persistencia: " + ex.Message, "Error de Datos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            BtnNuevo_Click(null, null);
        }

        private void BtnAccesos_Click(object sender, RoutedEventArgs e)
        {
            // Verificamos que el usuario haya seleccionado un rol válido
            if (CboRol.SelectedItem == null)
            {
                MessageBox.Show("Debe seleccionar un Rol Principal antes de configurar los permisos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Obtenemos el ID del Rol (gracias al SelectedValuePath = "Id")
            int rolId = (int)CboRol.SelectedValue;

            // 2. CORRECCIÓN: Como ahora está conectado a la base de datos, 
            // simplemente leemos el texto que está mostrando el ComboBox
            string rolNombre = CboRol.Text;

            // Instanciamos la nueva ventana flotante y le pasamos los datos
            PermisosRolWindow ventanaPermisos = new PermisosRolWindow(rolId, rolNombre);

            // Abrimos la ventana Modal
            ventanaPermisos.ShowDialog();
        }
    }
}