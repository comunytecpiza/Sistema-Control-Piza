#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Data;
using System.IO;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Almacen;

namespace AplicativoDeAlmacen
{
    public partial class MainWindow : Window
    {
        private readonly MediaPlayer _mediaPlayer = new MediaPlayer();
        private bool _isMuted = false;

        public MainWindow()
        {
            InitializeComponent();

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // ==============================================================
        // INICIALIZACIÓN DE AUDIO Y REPRODUCCIÓN AUTOMÁTICA
        // ==============================================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CargarYReproducirAudioBienvenida();
        }

        private void CargarYReproducirAudioBienvenida()
        {
            try
            {
                // 1. Cargar el estado guardado del usuario (Si no existe, por defecto NO está muteado)
                _isMuted = Properties.Settings.Default.AudioMuted;
                ActualizarBotonAudioUI();

                if (_isMuted) return; // Si el usuario eligió mutearlo antes, no reproducimos nada

                // 2. Ruta dinámica hacia la carpeta Audio/UI/bienvenida.mp3
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "UI", "bienvenida.mp3");

                if (File.Exists(audioPath))
                {
                    _mediaPlayer.Open(new Uri(audioPath, UriKind.Absolute));
                    _mediaPlayer.Volume = 0.8; // Volumen al 80%
                    _mediaPlayer.Play();
                }
            }
            catch (Exception ex)
            {
                // Falla silenciosa: si no hay tarjeta de sonido o falla el driver, la app abre normalmente
                Console.WriteLine("Error al reproducir audio de bienvenida: " + ex.Message);
            }
        }

        private void BtnAudioControl_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;

            // 🌟 Guardamos la preferencia de forma permanente en el equipo
            Properties.Settings.Default.AudioMuted = _isMuted;
            Properties.Settings.Default.Save();

            if (_isMuted)
            {
                _mediaPlayer.Stop();
            }
            else
            {
                CargarYReproducirAudioBienvenida();
            }

            ActualizarBotonAudioUI();
        }

        private void ActualizarBotonAudioUI()
        {
            if (BtnAudioControl.Template.FindName("TxtIconoAudio", BtnAudioControl) is TextBlock iconText)
            {
                iconText.Text = _isMuted ? "🔇" : "🔊";
            }
            BtnAudioControl.Opacity = _isMuted ? 0.5 : 1.0;
        }

        // ==============================================================
        // EL ATAJO SECRETO DE INGENIERÍA (Ctrl + Shift + Click Derecho)
        // ==============================================================
        private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;

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

                await Task.Run(() =>
                {
                    using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                    {
                        conn.Open();

                        using (IDbCommand cmd = conn.CreateCommand())
                        {
                            // 🌟 Aseguramos traer id, username, nombres, password, rol_usuario_id, estado
                            cmd.CommandText = "SELECT id, username, nombres, password, rol_usuario_id, estado FROM usuarios WHERE username = @username";

                            var pUsername = cmd.CreateParameter();
                            pUsername.ParameterName = "@username";
                            pUsername.Value = username;
                            cmd.Parameters.Add(pUsername);

                            using (IDataReader reader = cmd.ExecuteReader())
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
                                        usuarioLogueado = new Usuario { Id = -1 };
                                    }
                                }
                            }
                        }
                    }
                });

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
                    // Al ingresar con éxito detendremos el audio si seguía sonando
                    _mediaPlayer.Stop();

                    // 🌟 1. CARGAR PERMISOS DESDE LA BASE DE DATOS SEGÚN SU ROL
                    var service = new UsuarioService();
                    SesionSistema.UsuarioActual = usuarioLogueado;
                    SesionSistema.PermisosActuales = await service.ObtenerPermisosPorRolAsync(usuarioLogueado.RolUsuarioId);

                    // 🌟 2. CONSULTAR ESTRICTAMENTE LOS ALMACENES ASIGNADOS A ESTE USUARIO EN BD
                    var almacenesPermitidos = new List<Almacen>();
                    await Task.Run(() =>
                    {
                        using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                        {
                            conn.Open();
                            using (IDbCommand cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"
                    SELECT a.id, a.nombre, a.codigo, a.direccion, ua.es_predeterminado, a.estado_id 
                    FROM usuario_almacenes ua
                    INNER JOIN almacenes a ON ua.almacen_id = a.id
                    WHERE ua.usuario_id = @uId AND a.estado_id = 1";

                                var pUId = cmd.CreateParameter();
                                pUId.ParameterName = "@uId";
                                pUId.Value = usuarioLogueado.Id;
                                cmd.Parameters.Add(pUId);

                                using (IDataReader reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        almacenesPermitidos.Add(new Almacen
                                        {
                                            Id = Convert.ToInt32(reader["id"]),
                                            Nombre = reader["nombre"]?.ToString() ?? "",
                                            Codigo = reader["codigo"]?.ToString() ?? "",
                                            Direccion = reader["direccion"]?.ToString() ?? "",
                                            EstadoId = Convert.ToInt32(reader["estado_id"]),
                                            EsPredeterminado = Convert.ToBoolean(reader["es_predeterminado"])
                                        });
                                    }
                                }
                            }
                        }
                    });

                    // 🛡️ VALIDACIÓN ESTRICTA: Si no tiene almacenes en la BD, no se le inventa nada y se bloquea el acceso.
                    if (!almacenesPermitidos.Any())
                    {
                        MessageBox.Show("Acceso Denegado: Su usuario no tiene ningún almacén activo asignado en el sistema. Contacte al administrador.",
                                        "Sin Sede Asignada", MessageBoxButton.OK, MessageBoxImage.Stop);
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        return;
                    }

                    // 🌟 3. SELECCIÓN DINÁMICA DEL ALMACÉN (Busca el marcado como predeterminado en BD, sino toma el primero disponible)
                    SesionSistema.AlmacenesPermitidos = almacenesPermitidos;
                    SesionSistema.AlmacenActual = almacenesPermitidos.FirstOrDefault(a => a.EsPredeterminado) ?? almacenesPermitidos.First();

                    string nombre = usuarioLogueado.Nombres;
                    bool esAdmin = usuarioLogueado.RolUsuarioId == 1; // O la validación de rol que maneje tu sistema

                    // 🌟 4. ABRIR EL MAINSHELL CON SUS PERMISOS Y ALMACENES REALES
                    new Views.MainShell(nombre, esAdmin).Show();

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
                string user = UsernameTextBox.Text.Trim();

                try
                {
                    await Task.Run(() =>
                    {
                        using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                        {
                            conn.Open();

                            using (IDbCommand cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "SELECT nombres FROM usuarios WHERE username = @username";

                                var pUsername = cmd.CreateParameter();
                                pUsername.ParameterName = "@username";
                                pUsername.Value = user;
                                cmd.Parameters.Add(pUsername);

                                var result = cmd.ExecuteScalar();

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
                    // Falla silenciosa
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

        private void BtnReintentar_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Está seguro que desea salir del sistema?", "Salir",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _mediaPlayer.Stop();
                Application.Current.Shutdown();
            }
        }
    }
}