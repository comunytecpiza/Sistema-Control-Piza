#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Data;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;

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

                // Abre el modal secreto para configurar la IP del servidor de BD
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
            // 🌟 1. LIMPIEZA DE ENTRADAS
            string username = UsernameTextBox.Text.Trim(); // El usuario sí se limpia
            string password = PasswordBox.Password;        // ⚠️ NUNCA le hagas .Trim() a la contraseña

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingrese un usuario y contraseña.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                Usuario? usuarioLogueado = null;

                // 🌟 2. CONSULTA BLINDADA (HILO SECUNDARIO)
                await Task.Run(() =>
                {
                    using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                    {
                        conn.Open();

                        using (IDbCommand cmd = conn.CreateCommand())
                        {
                            // Consulta exacta, solo traemos lo necesario
                            cmd.CommandText = "SELECT id, username, nombres, password, rol_usuario_id, estado FROM usuarios WHERE username = @username";

                            // Creación del parámetro Anti-Inyección SQL
                            var pUsername = cmd.CreateParameter();
                            pUsername.ParameterName = "@username";
                            pUsername.Value = username;
                            cmd.Parameters.Add(pUsername);

                            using (IDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    string? storedPassword = reader["password"]?.ToString();

                                    // 🌟 3. VERIFICACIÓN ESTRICTA DE CONTRASEÑA
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
                                        // Bandera de contraseña incorrecta
                                        usuarioLogueado = new Usuario { Id = -1 };
                                    }
                                }
                            }
                        }
                    }
                });

                // 🌟 4. VALIDACIONES FINALES EN LA UI
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
                    // Login Exitoso
                    var service = new UsuarioService();
                    SesionSistema.UsuarioActual = usuarioLogueado;
                    SesionSistema.PermisosActuales = await service.ObtenerPermisosPorRolAsync(usuarioLogueado.RolUsuarioId);

                    string nombre = usuarioLogueado.Nombres;
                    bool esAdmin = usuarioLogueado.RolUsuarioId == 1;

                    new Views.MainShell(nombre, esAdmin).Show();

                    // Usamos Close() para destruir la ventana de Login completamente y liberar memoria
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
                string user = UsernameTextBox.Text.Trim(); // Limpiamos espacios extra al buscar

                try
                {
                    await Task.Run(() =>
                    {
                        using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                        {
                            conn.Open();

                            using (IDbCommand cmd = conn.CreateCommand())
                            {
                                // Consulta blindada anti-inyección
                                cmd.CommandText = "SELECT nombres FROM usuarios WHERE username = @username";

                                var pUsername = cmd.CreateParameter();
                                pUsername.ParameterName = "@username";
                                pUsername.Value = user;
                                cmd.Parameters.Add(pUsername);

                                var result = cmd.ExecuteScalar();

                                // Actualizamos la UI de forma segura
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    NameTextBox.Text = result?.ToString() ?? "";
                                });
                            }
                        }
                    });
                }
                catch
                {
                    // Falla silenciosa: Si se cae la red mientras tipea, no queremos molestar al usuario con errores
                }
            }
            else
            {
                NameTextBox.Text = string.Empty;
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

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Está seguro que desea salir del sistema?", "Salir",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }
    }
    // ==============================================================
    // LÓGICA DE CIERRE DE APLICACIÓN
    // ==============================================================
    
}