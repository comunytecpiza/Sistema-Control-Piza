using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Data.SqlClient;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Media;
using AplicativoDeAlmacen.Data; // IMPORTANTE: Llama a tu ConfigManager

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
        // LÓGICA DE LOGIN (Silenciosa y Discreta)
        // ==============================================================
        private async void IngresarButton_Click(object sender, RoutedEventArgs e)
        {
            await ValidateUserAndRedirectAsync();
        }

        private async Task ValidateUserAndRedirectAsync()
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingrese un usuario y contraseña.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Validación Discreta: Si no hay archivo de configuración, nadie se entera de detalles técnicos.
            if (!ConfigManager.ExisteConfiguracion())
            {
                MessageBox.Show("Error de red. Consulte con el administrador del sistema.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Si hay configuración, mostramos la carga limpia
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Validando credenciales...";
            LoadingSubText.Visibility = Visibility.Collapsed;
            BtnReintentar.Visibility = Visibility.Collapsed;

            try
            {
                string connString = ConfigManager.ObtenerCadenaConexion();

                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(connString))
                    {
                        conn.Open();
                        var query = "SELECT password, nombres, rol_usuario_id FROM usuarios WHERE username = @username";
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
                                        // Login Exitoso
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            int rolId = Convert.ToInt32(reader["rol_usuario_id"]);
                                            string nombre = reader["nombres"]?.ToString() ?? "";

                                            if (rolId == 1) new Views.AdminPanel(nombre, true).Show();
                                            else if (rolId == 3) new Views.AlmacenPanel(nombre).Show();
                                            else MessageBox.Show("Rol sin panel asignado.");

                                            this.Close();
                                        });
                                    }
                                    else
                                    {
                                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Contraseña incorrecta.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning));
                                    }
                                }
                                else
                                {
                                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Usuario no encontrado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning));
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception)
            {
                // Si la DB falla (Internet caído, etc), mostramos mensaje corporativo
                MessageBox.Show("No se pudo conectar al servidor. Verifique su red o contacte a soporte TI.", "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Pase lo que pase, quitamos la pantalla de carga
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Solo busca el nombre si el archivo de config ya existe
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
                catch { /* Falla silenciosa mientras tipea para no congelar la UI */ }
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

        // Mantenemos este evento vacío en caso de que tu XAML aún tenga el Loaded puesto
        private void Window_Loaded(object sender, RoutedEventArgs e) { }

        // Mantenemos este evento por si tu XAML aún tiene el Click del botón
        private void BtnReintentar_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }
}