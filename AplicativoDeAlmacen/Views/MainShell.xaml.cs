#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Collections.Generic;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class MainShell : Window, IMainWindow
    {
        private readonly bool isAdmin;

        public MainShell(string userNames, bool isAdmin)
        {
            InitializeComponent();
            this.isAdmin = isAdmin;
            EventBus.OnRolesPermisosChanged += EventBus_OnRolesPermisosChanged;
            SetupWelcomeMessage(userNames);
            StartClock();

            // 1. Si es Admin maestro pero por alguna razón no tiene lista de permisos, cargamos un acceso temporal
            if (SesionSistema.UsuarioActual?.RolUsuarioId == 1 && (SesionSistema.PermisosActuales == null || !SesionSistema.PermisosActuales.Any()))
            {
                // Aquí podrías forzar la carga de todos los módulos para el super admin si tu servicio devuelve lista vacía
            }

            try
            {
                ConstruirMenuDinamico();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ERROR EN MENÚ:\n{ex.GetType().Name}\n{ex.Message}\n\nStackTrace:\n{ex.StackTrace}", "Debug Menú", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EventBus_OnRolesPermisosChanged()
        {
            // Usamos Dispatcher para asegurar que tocamos la UI en el hilo correcto
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await RefrescarMenuDinamicoAsync();
            });
        }

        public async Task RefrescarMenuDinamicoAsync()
        {
            var usuarioService = new AplicativoDeAlmacen.Services.UsuarioService();
            if (SesionSistema.UsuarioActual != null)
            {
                SesionSistema.PermisosActuales = await usuarioService.ObtenerPermisosPorRolAsync(SesionSistema.UsuarioActual.RolUsuarioId);
            }

            ConstruirMenuDinamico();
        }

        // ==============================================================
        // 🌟 EL RENDERIZADOR DEL MENÚ DINÁMICO (REFLECTION)
        // ==============================================================
        private void ConstruirMenuDinamico()
        {
            MenuPrincipal.Items.Clear();

            var permisos = SesionSistema.PermisosActuales?
                .Where(p => p.PuedeVer && p.EstadoModulo && p.EstadoCategoria)
                .ToList();

            if (permisos == null || permisos.Count == 0)
                return;

            var categorias = permisos
                .GroupBy(p => new
                {
                    p.CategoriaId,
                    p.CategoriaNombre,
                    p.IconoCategoria,
                    p.ColorCategoria,
                    p.OrdenCategoria
                })
                .OrderBy(g => g.Key.OrdenCategoria);

            foreach (var grupo in categorias)
            {
                StackPanel header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Brush colorIcono;

                try
                {
                    colorIcono = (Brush)new BrushConverter().ConvertFromString(grupo.Key.ColorCategoria);
                }
                catch
                {
                    colorIcono = Brushes.DodgerBlue;
                }

                TextBlock icono = new TextBlock
                {
                    Text = grupo.Key.IconoCategoria,
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 18,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = colorIcono
                };

                TextBlock titulo = new TextBlock
                {
                    Text = grupo.Key.CategoriaNombre,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                header.Children.Add(icono);
                header.Children.Add(titulo);

                MenuItem menuPadre = new MenuItem
                {
                    Header = header,
                    Style = (Style)FindResource("ModernMenuItemStyle")
                };

                foreach (var modulo in grupo.OrderBy(m => m.Orden))
                {
                    MenuItem menuHijo = new MenuItem
                    {
                        Header = modulo.NombreModulo,
                        Tag = modulo
                    };

                    menuHijo.Click += MenuModulo_Click;

                    menuPadre.Items.Add(menuHijo);
                }

                MenuPrincipal.Items.Add(menuPadre);
            }

            StackPanel salirHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            salirHeader.Children.Add(new TextBlock
            {
                Text = "🚪",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 18,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = Brushes.IndianRed
            });

            salirHeader.Children.Add(new TextBlock
            {
                Text = "Salir",
                FontWeight = FontWeights.SemiBold
            });

            MenuItem menuSalir = new MenuItem
            {
                Header = salirHeader,
                Style = (Style)FindResource("ModernMenuItemStyle")
            };

            menuSalir.Click += BtnSalir_Click;

            MenuPrincipal.Items.Add(menuSalir);

            BtnAgregarUsuario.Visibility =
                (isAdmin || SesionSistema.UsuarioActual?.RolUsuarioId == 1)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        // ==============================================================
        // 🌟 EL ENRUTADOR MÁGICO (REFLECTION)
        // ==============================================================
        private void MenuModulo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is Models.Models.RolPermiso modulo)
            {
                // Si aún no está programado en SQL
                if (string.IsNullOrWhiteSpace(modulo.ControlWpf))
                {
                    MessageBox.Show($"El módulo '{modulo.NombreModulo}' está en construcción.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    // 1. Buscamos la clase en el proyecto actual usando el nombre de la BD
                    Type? tipoVista = Assembly.GetExecutingAssembly().GetTypes()
                     .FirstOrDefault(t => t.Name == modulo.ControlWpf && t.IsSubclassOf(typeof(UserControl)));

                    if (tipoVista != null)
                    {
                        // 2. La instanciamos mágicamente (Equivale a 'new RegistroCodigosUserControl()')
                        UserControl vistaInstanciada = (UserControl)Activator.CreateInstance(tipoVista)!;

                        // 3. La abrimos en una pestaña
                        AbrirPestaña(modulo.NombreModulo, vistaInstanciada);
                    }
                    else
                    {
                        MessageBox.Show($"No se encontró la vista técnica: '{modulo.ControlWpf}'.", "Error 404", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al intentar abrir el módulo: {ex.Message}", "Error de Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        // ==============================================================
        // MÉTODOS BASE (Reloj, Bienvenida, Motor de Pestañas)
        // ==============================================================

        private void SetupWelcomeMessage(string userNames)
        {
            WelcomeMessage.Text = $"Bienvenido(a), {userNames}";
        }

        private void StartClock()
        {
            DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += Timer_Tick;
            timer.Start();
            Timer_Tick(this, EventArgs.Empty);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            CultureInfo culture = new("es-ES");
            string dayName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(now.DayOfWeek));
            string monthName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(now.Month));
            string amPm = now.ToString("tt", CultureInfo.InvariantCulture);
            DateTimeTextBlock.Text = $"{dayName} {now.Day:00} de {monthName} del {now.Year} - {now:hh:mm:ss} {amPm}";
        }

        public void AbrirPestaña(string titulo, UserControl contenido)
        {
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == titulo)
                {
                    MainTabControl.SelectedItem = tab;
                    return;
                }
            }

            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold
            });

            Button btnDesvincular = CrearBotonPestaña("↗", "Separar en ventana");
            Button btnClose = CrearBotonPestaña("✕", "Cerrar pestaña");

            TabItem nuevaPestana = new TabItem { Header = headerPanel, Content = contenido };

            btnClose.Click += (s, e) => { MainTabControl.Items.Remove(nuevaPestana); };

            btnDesvincular.Click += (s, e) => {
                MainTabControl.Items.Remove(nuevaPestana);
                Window ventanaFlotante = new Window
                {
                    Title = titulo,
                    Content = contenido,
                    Width = 1000,
                    Height = 650,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                ventanaFlotante.Closed += (s2, e2) => {
                    nuevaPestana.Content = contenido;
                    MainTabControl.Items.Add(nuevaPestana);
                };
                ventanaFlotante.Show();
            };

            headerPanel.Children.Add(btnDesvincular);
            headerPanel.Children.Add(btnClose);

            MainTabControl.Items.Add(nuevaPestana);
            MainTabControl.SelectedItem = nuevaPestana;
        }

        private Button CrearBotonPestaña(string texto, string tooltip)
        {
            return new Button
            {
                Content = texto,
                Width = 22,
                Height = 22,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.Gray,
                ToolTip = tooltip,
                Margin = new Thickness(2, 0, 0, 0)
            };
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Está seguro que desea cerrar sesión?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                new MainWindow().Show();
                SesionSistema.UsuarioActual = null;
                SesionSistema.PermisosActuales = null;
                Close();
            }
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("👥 Gestión de Usuarios", new UsuariosUserControl());
        }
    }
}