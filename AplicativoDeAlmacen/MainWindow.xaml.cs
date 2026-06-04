using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Data.SqlClient;
using System.Windows.Input;
using AplicativoDeAlmacen.Views;


namespace AplicativoDeAlmacen
{
    public partial class MainWindow : Window
    {

        private const string ConnectionString = @"Server=192.168.1.103;Database=EdicionesPizaControl;User Id=sa;Password=123456;TrustServerCertificate=True;";
        public MainWindow()
        {
            InitializeComponent();
            TestDatabaseConnection();
        }


        private void TestDatabaseConnection()
        {
            try
            {
                using (var conn = new SqlConnection(ConnectionString))
                {
                    conn.Open();
                    MessageBox.Show("Conexión a la base de datos exitosa.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al conectar con la base de datos: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
            }
        }

        private void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateNameFromUsername(UsernameTextBox.Text);
        }

        private void UpdateNameFromUsername(string username)
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                try
                {
                    conn.Open();
                    var query = "SELECT nombres FROM usuarios WHERE username = @username";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        var result = cmd.ExecuteScalar();
                        NameTextBox.Text = result?.ToString() ?? "Usuario no encontrado";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al buscar el nombre: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
                }
            }
        }

        private void IngresarButton_Click(object sender, RoutedEventArgs e)
        {
            ValidateUserAndRedirect();
        }

        private void ValidateUserAndRedirect()
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingrese un nombre de usuario y contraseña.");
                return;
            }

            using (var conn = new SqlConnection(ConnectionString))
            {
                try
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
                                string? userNames = reader["nombres"]?.ToString();
                                int rolUsuarioId = Convert.ToInt32(reader["rol_usuario_id"]);

                                if (storedPassword != null && storedPassword == password)
                                {
                                    if (!string.IsNullOrWhiteSpace(userNames))
                                    {
                                        // EL SEMÁFORO DE ROLES
                                        if (rolUsuarioId == 1) // Administrador
                                        {
                                            var adminPanel = new Views.AdminPanel(userNames, true);
                                            adminPanel.Show();
                                            this.Close();
                                        }
                                        else if (rolUsuarioId == 3) // Almacenero
                                        {
                                            var almacenPanel = new Views.AlmacenPanel(userNames);
                                            almacenPanel.Show();
                                            this.Close();
                                        }
                                        else
                                        {
                                            MessageBox.Show("Acceso denegado: Su rol no tiene un panel asignado en el sistema.");
                                        }
                                    }
                                    else
                                    {
                                        MessageBox.Show("Error: No se pudo obtener el nombre del usuario.");
                                    }
                                }
                                else
                                {
                                    MessageBox.Show("Contraseña incorrecta.");
                                }
                            }
                            else
                            {
                                MessageBox.Show($"Usuario '{username}' no encontrado.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al validar usuario: {ex.Message}");
                }
            }
        }

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                UpdateNameFromUsername(UsernameTextBox.Text);
                PasswordBox.Focus();
            }
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateUserAndRedirect();
            }
        }

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement)
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            }
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
    }
}