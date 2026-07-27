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

            InicializarComponentesNegocio();
            EventBus.OnUsuariosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => CargarUsuarios(""));
        }

        private async void InicializarComponentesNegocio()
        {
            try
            {
                // 1. Cargar el ComboBox de Roles[cite: 10]
                var roles = await _usuarioService.ObtenerRolesActivosAsync();
                CboRol.ItemsSource = roles;
                CboRol.DisplayMemberPath = "Nombre";
                CboRol.SelectedValuePath = "Id";

                // 2. Cargar el ComboBox de Almacenes a través del servicio
                var almacenes = await _usuarioService.ObtenerAlmacenesActivosAsync();
                CboAlmacen.ItemsSource = almacenes;
                CboAlmacen.DisplayMemberPath = "Nombre";
                CboAlmacen.SelectedValuePath = "Id";

                // 3. Poblar la lista principal de usuarios[cite: 10]
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

        private async void UsuariosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UsuariosDataGrid.SelectedItem is Usuario usuario)
            {
                _usuarioSeleccionadoId = usuario.Id;
                TxtCodigo.Text = usuario.Username;
                TxtNombres.Text = usuario.Nombres;
                TxtPassword.Password = usuario.Password;
                CboRol.SelectedValue = usuario.RolUsuarioId;
                CboEstado.SelectedIndex = usuario.Estado ? 0 : 1;

                // Leer y seleccionar el almacén perteneciente mediante el servicio
                int? almacenId = await _usuarioService.ObtenerAlmacenPorUsuarioAsync(usuario.Id);
                if (almacenId.HasValue)
                {
                    CboAlmacen.SelectedValue = almacenId.Value;
                }
                else
                {
                    CboAlmacen.SelectedIndex = -1;
                }
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _usuarioSeleccionadoId = 0;
            TxtCodigo.Text = "AUTO-GEN";
            TxtNombres.Text = string.Empty;
            TxtPassword.Password = string.Empty;
            CboRol.SelectedIndex = -1;
            CboAlmacen.SelectedIndex = -1;
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

            if (CboAlmacen.SelectedValue == null)
            {
                MessageBox.Show("Debe seleccionar el Almacén al que pertenece el usuario.", "Validación Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                bool estadoActivo = Convert.ToBoolean(((ComboBoxItem)CboEstado.SelectedItem).Tag);
                int almacenIdSel = Convert.ToInt32(CboAlmacen.SelectedValue);

                var usuario = new Usuario
                {
                    Id = _usuarioSeleccionadoId,
                    Nombres = TxtNombres.Text.Trim(),
                    Password = TxtPassword.Password,
                    RolUsuarioId = (int)CboRol.SelectedValue,
                    Estado = estadoActivo
                };

                int idUsuarioFinal;

                if (_usuarioSeleccionadoId == 0)
                {
                    idUsuarioFinal = await _usuarioService.InsertarAsync(usuario);
                    MessageBox.Show("Usuario incorporado con éxito al sistema corporativo.", "Confirmación", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _usuarioService.ActualizarAsync(usuario);
                    idUsuarioFinal = _usuarioSeleccionadoId;
                    MessageBox.Show("Registro de usuario actualizado correctamente.", "Confirmación", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Guardar la pertenencia en la tabla usuario_almacenes a través del servicio
                await _usuarioService.GuardarUsuarioAlmacenAsync(idUsuarioFinal, almacenIdSel);

                CargarUsuarios();
                BtnNuevo_Click(null, null);
                EventBus.NotificarUsuariosChanged();
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
            if (CboRol.SelectedItem == null)
            {
                MessageBox.Show("Debe seleccionar un Rol Principal antes de configurar los permisos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int rolId = (int)CboRol.SelectedValue;
            string rolNombre = CboRol.Text;

            PermisosRolWindow ventanaPermisos = new PermisosRolWindow(rolId, rolNombre);
            ventanaPermisos.ShowDialog();
        }

        private async void BtnNuevoRol_Click(object sender, RoutedEventArgs e)
        {
            var modal = new CrearRolWindow { Owner = Window.GetWindow(this) };
            if (modal.ShowDialog() == true && modal.RolCreadoId > 0)
            {
                // Recargar los roles y seleccionar el recién creado
                var roles = await _usuarioService.ObtenerRolesActivosAsync();
                CboRol.ItemsSource = roles;
                CboRol.SelectedValue = modal.RolCreadoId;
            }
        }
    }
}