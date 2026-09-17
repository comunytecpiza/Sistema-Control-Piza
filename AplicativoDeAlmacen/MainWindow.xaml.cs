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
using System.Collections.Generic;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Sistemas;
using AplicativoDeAlmacen.Services.Sistemas;

namespace AplicativoDeAlmacen
{
    public partial class MainWindow : Window
    {
        private readonly MediaPlayer _mediaPlayer = new MediaPlayer();
        private readonly AuditoriaService _auditoriaService = new AuditoriaService();
        private bool _isMuted = false;

        // Bloqueo atómico contra doble Enter o clics rápidos concurrentes
        private bool _isAuthenticating = false;

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
                _isMuted = Properties.Settings.Default.AudioMuted;
                ActualizarBotonAudioUI();

                if (_isMuted) return;

                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "UI", "bienvenida.mp3");

                if (File.Exists(audioPath))
                {
                    _mediaPlayer.Open(new Uri(audioPath, UriKind.Absolute));
                    _mediaPlayer.Volume = 0.8;
                    _mediaPlayer.Play();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al reproducir audio de bienvenida: " + ex.Message);
            }
        }

        private void BtnAudioControl_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;

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
        // ATAJO SECRETO (Ctrl + Shift + Click Derecho)
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
        // LÓGICA DE LOGIN AUDITADA & ANTI-DOBLE SUBMIT
        // ==============================================================
        private async void IngresarButton_Click(object sender, RoutedEventArgs e)
        {
            await ValidateUserAndRedirectAsync();
        }

        private async Task ValidateUserAndRedirectAsync()
        {
            if (_isAuthenticating) return;

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

            _isAuthenticating = true;
            IngresarButton.IsEnabled = false;

            try
            {
                // 1. Capturar Telemetría del equipo
                TelemetriaEquipo telemetria = await NetworkHelper.CapturarTelemetriaAsync();

                // 2. Verificar bloqueo previo de la cuenta en esta PC
                var estadoBloqueo = await _auditoriaService.VerificarBloqueoAsync(username, telemetria.NombrePc);
                if (estadoBloqueo.EstaBloqueado)
                {
                    MessageBox.Show(estadoBloqueo.Mensaje, "Acceso Bloqueado por TI", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                Usuario? usuarioLogueado = null;

                await Task.Run(() =>
                {
                    using (IDbConnection conn = new DataConnection.DatabaseConnection().GetConnection())
                    {
                        conn.Open();

                        using (IDbCommand cmd = conn.CreateCommand())
                        {
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
                                        usuarioLogueado = new Usuario { Id = -1 }; // Contraseña incorrecta
                                    }
                                }
                            }
                        }
                    }
                });

                // 3. Manejo unificado de intentos fallidos (usuario inexistente o clave errónea)
                if (usuarioLogueado == null || usuarioLogueado.Id == -1)
                {
                    var resFallo = await _auditoriaService.RegistrarIntentoFallidoAsync(username, telemetria);

                    MessageBox.Show(resFallo.Mensaje, "Error de Autenticación", MessageBoxButton.OK, MessageBoxImage.Warning);

                    PasswordBox.Clear();
                    PasswordBox.Focus();
                    return;
                }

                if (!usuarioLogueado.Estado)
                {
                    MessageBox.Show("Su cuenta se encuentra INACTIVA. Comuníquese con el Administrador.", "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                // ==============================================================
                // CREDENCIALES CORRECTAS: ACTIVAR OVERLAY DE CARGA
                // ==============================================================
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = "Iniciando sesión en el sistema...";
                LoadingSubText.Text = "Cargando almacenes y permisos";
                LoadingSubText.Visibility = Visibility.Visible;

                _mediaPlayer.Stop();

                // 4. Registro de sesión activa y auditoría
                string rolNombre = usuarioLogueado.RolUsuarioId == 1 ? "Administrador" : "Operador";
                string tokenSesion = await _auditoriaService.RegistrarLoginExitosoAsync(usuarioLogueado.Id, usuarioLogueado.Username, rolNombre, telemetria);

                SesionSistema.UsuarioActual = usuarioLogueado;
                SesionSistema.TokenSesionActual = tokenSesion;
                // Cargar permisos
                var service = new UsuarioService();
                SesionSistema.PermisosActuales = await service.ObtenerPermisosPorRolAsync(usuarioLogueado.RolUsuarioId);

                // Cargar almacenes asignados
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

                if (!almacenesPermitidos.Any())
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    MessageBox.Show("Acceso Denegado: Su usuario no tiene ningún almacén activo asignado en el sistema. Contacte al administrador.",
                                    "Sin Sede Asignada", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                SesionSistema.AlmacenesPermitidos = almacenesPermitidos;
                SesionSistema.AlmacenActual = almacenesPermitidos.FirstOrDefault(a => a.EsPredeterminado) ?? almacenesPermitidos.First();

                string nombre = usuarioLogueado.Nombres;
                bool esAdmin = usuarioLogueado.RolUsuarioId == 1;

                // 5. Iniciar MainShell y pasar el token generado para el latido/expulsión remota
                var mainShell = new Views.MainShell(nombre, esAdmin);

                // Si MainShell tiene la función o propiedad para el token, asígnala aquí:
                // mainShell.ConfigurarSesionToken(tokenSesion);

                mainShell.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"No se pudo conectar al servidor: {ex.Message}", "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isAuthenticating = false;
                IngresarButton.IsEnabled = true;
            }
        }

        // ==============================================================
        // EVENTOS DE ENTRADA Y CONTROLES
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
                                cmd.CommandText = "SELECT nombres FROM usuarios WHERE username = @username LIMIT 1";

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
                    // Falla silenciosa si no responde
                }
            }
            else
            {
                NameTextBox.Text = string.Empty;
            }
        }

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                PasswordBox.Focus();
            }
        }

        private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ValidateUserAndRedirectAsync();
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