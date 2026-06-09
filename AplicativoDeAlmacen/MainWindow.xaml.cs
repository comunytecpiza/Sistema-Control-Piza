#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Data.SqlClient;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Media;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Core;      // Para acceder a SesionSistema
using AplicativoDeAlmacen.Services;  // Para acceder a UsuarioService
using AplicativoDeAlmacen.Models.Models; // Para acceder a la clase Usuario

namespace AplicativoDeAlmacen
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // El sistema arranca limpio, sin overlays molestos
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // ==============================================================
        // EL ATAJO SECRETO DE INGENIERÍA (Ctrl + Shift + Click Derecho)
        // ==============================================================
        private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true; // Evita el menú contextual normal de Windows

                // Abre el modal secreto para configurar la IP de SQL Server
                var configWindow = new ConfiguracionWindow();
                configWindow.ShowDialog();
            }
        }

        // ==============================================================
        // LÓGICA DE LOGIN (Conectada al RBAC y Matriz de Permisos)
        // ==============================================================
        private async void IngresarButton_Click(object sender, RoutedEventArgs e)
        {
            await ValidateUserAndRedirectAsync();
        }

        private async Task ValidateUserAndRedirectAsync()
        {
            string username = UsernameTextBox.Text.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingrese un usuario y contraseña.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Validación Discreta
            if (!ConfigManager.ExisteConfiguracion())
            {
                MessageBox.Show("Error de red. Consulte con el administrador del sistema.", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Validando credenciales y permisos...";
            LoadingSubText.Visibility = Visibility.Collapsed;
            BtnReintentar.Visibility = Visibility.Collapsed;

            try
            {
                string connString = ConfigManager.ObtenerCadenaConexion();
                Usuario? usuarioLogueado = null;

                // 2. Extraemos la información del usuario en un hilo secundario para no congelar la pantalla
                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(connString))
                    {
                        conn.Open();
                        // Nos aseguramos de traer el estado y el id para la sesión
                        var query = "SELECT id, username, nombres, password, rol_usuario_id, estado FROM usuarios WHERE username = @username";
                        using (var cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@username", username);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    string? storedPassword = reader["password"]?.ToString();

                                    if (storedPassword == password)
                                    {
                                        usuarioLogueado = new Usuario
                                        {
                                            Id = Convert.ToInt32(reader["id"]),
                                            Username = reader["username"]?.ToString() ?? "",
                                            Nombres = reader["nombres"]?.ToString() ?? "",
                                            RolUsuarioId = Convert.ToInt32(reader["rol_usuario_id"]),
                                            Estado = Convert.ToBoolean(reader["estado"])
                                        };
                                    }
                                    else
                                    {
                                        // Usamos Id -1 como bandera de contraseña incorrecta
                                        usuarioLogueado = new Usuario { Id = -1 };
                                    }
                                }
                            }
                        }
                    }
                });

                // 3. Validaciones finales en el hilo principal
                if (usuarioLogueado == null)
                {
                    MessageBox.Show("El usuario ingresado no existe en la base de datos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (usuarioLogueado.Id == -1)
                {
                    MessageBox.Show("La contraseña es incorrecta. Verifique sus credenciales.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (!usuarioLogueado.Estado)
                {
                    MessageBox.Show("Su cuenta se encuentra INACTIVA. Comuníquese con el Administrador.", "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Stop);
                }
                else
                {
                    // ================================================================
                    // 🌟 ¡LA LÍNEA MÁGICA! Carga de la Matriz de Seguridad a la RAM
                    // ================================================================
                    UsuarioService usuarioService = new UsuarioService();
                    SesionSistema.UsuarioActual = usuarioLogueado;
                    SesionSistema.PermisosActuales = await usuarioService.ObtenerPermisosPorRolAsync(usuarioLogueado.RolUsuarioId);

                    // ================================================================
                    // EL SEMÁFORO DE RUTEO POR ROLES
                    // ================================================================
                    int rolId = usuarioLogueado.RolUsuarioId;
                    string nombre = usuarioLogueado.Nombres;

                    if (rolId == 1) // Administrador (Acceso Total)
                    {
                        new Views.AdminPanel(nombre, true).Show();
                    }
                    else if (rolId == 2) // Contador / Auditor
                    {
                        new Views.ContabilidadPanel(nombre).Show();
                    }
                    else if (rolId == 3) // Almacén / Asistente
                    {
                        new Views.AlmacenPanel(nombre).Show();
                    }
                    else
                    {
                        MessageBox.Show("Su rol no tiene un panel de operaciones asignado.", "Error de Ruteo", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Cerramos la ventana de Login
                    this.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("No se pudo conectar al servidor. Verifique su red o contacte a soporte TI.", "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // ==============================================================
        // EFECTOS VISUALES Y EVENTOS SECUNDARIOS
        // ==============================================================

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ConfigManager.ExisteConfiguracion() && !string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                string user = UsernameTextBox.Text;
                string connString = "";
                try { connString = ConfigManager.ObtenerCadenaConexion(); } catch { return; }

                try
                {
                    await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(connString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SELECT nombres FROM usuarios WHERE username = @username", conn))
                            {
                                cmd.Parameters.AddWithValue("@username", user);
                                var result = cmd.ExecuteScalar();
                                Application.Current.Dispatcher.Invoke(() => NameTextBox.Text = result?.ToString() ?? "");
                            }
                        }
                    });
                }
                catch { /* Falla silenciosa mientras tipea */ }
            }
            else
            {
                NameTextBox.Text = "";
            }
        }

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) PasswordBox.Focus();
        }

        private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await ValidateUserAndRedirectAsync();
        }

        private void ShowPassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Parent is StackPanel stackPanel)
            {
                var passwordBox = stackPanel.Children.OfType<PasswordBox>().FirstOrDefault();
                var passwordTextBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault();

                if (passwordBox != null)
                {
                    passwordTextBox = new TextBox
                    {
                        Text = passwordBox.Password,
                        FontSize = passwordBox.FontSize,
                        Padding = passwordBox.Padding,
                        Width = passwordBox.Width,
                        Margin = passwordBox.Margin
                    };

                    ReplaceElement(stackPanel, passwordBox, passwordTextBox);
                    passwordTextBox.Focus();
                }
                else if (passwordTextBox != null)
                {
                    var newPasswordBox = new PasswordBox
                    {
                        Password = passwordTextBox.Text,
                        FontSize = passwordTextBox.FontSize,
                        Padding = passwordTextBox.Padding,
                        Width = passwordTextBox.Width,
                        Margin = passwordTextBox.Margin
                    };

                    ReplaceElement(stackPanel, passwordTextBox, newPasswordBox);
                    newPasswordBox.Focus();
                }
            }
        }

        private void ReplaceElement(Panel panel, UIElement oldElement, UIElement newElement)
        {
            int index = panel.Children.IndexOf(oldElement);
            panel.Children.Remove(oldElement);
            panel.Children.Insert(index, newElement);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) { }

        private void BtnReintentar_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }
}