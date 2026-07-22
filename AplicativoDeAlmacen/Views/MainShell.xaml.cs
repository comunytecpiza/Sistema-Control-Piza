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
// Alias explícito: usamos Growl directo desde HandyControl.Controls sin
// importar todo el namespace, porque HandyControl también tiene su propia
// clase "Window" que choca (CS0104) con System.Windows.Window de WPF,
// que es la que usa esta clase (MainShell : Window).
using Growl = HandyControl.Controls.Growl;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.UI;

namespace AplicativoDeAlmacen.Views
{

    public partial class MainShell : Window, IMainWindow
    {

        private ObservableCollection<NotaItem> _notasPendientes = new ObservableCollection<NotaItem>();
        private ObservableCollection<NotaItem> _notasCompletadas = new ObservableCollection<NotaItem>();
        private string _rutaArchivoNotas;
        private readonly bool isAdmin;

        public MainShell(string userNames, bool isAdmin)
        {
            InitializeComponent();
            this.isAdmin = isAdmin;
            EventBus.OnRolesPermisosChanged += EventBus_OnRolesPermisosChanged;

            // Llamada sin errores al mensaje de bienvenida
            SetupWelcomeMessage(userNames);
            StartClock();

            // Cargar los almacenes permitidos en el ComboBox de la barra inferior
            CboAlmacenBarra.ItemsSource = SesionSistema.AlmacenesPermitidos;

            CboAlmacenBarra.SelectionChanged -= CboAlmacenBarra_SelectionChanged;
            if (SesionSistema.AlmacenActual != null)
            {
                CboAlmacenBarra.SelectedValue = SesionSistema.AlmacenActual.Id;
            }
            CboAlmacenBarra.SelectionChanged += CboAlmacenBarra_SelectionChanged;

            if (SesionSistema.AlmacenesPermitidos.Count <= 1)
            {
                CboAlmacenBarra.IsEnabled = false;
            }

            CargarNotasLocales();
        }

        private void SetupWelcomeMessage(string userNames)
        {
            string nombreAlmacen = SesionSistema.AlmacenActual?.Nombre ?? "Almacén General";
            WelcomeMessage.Text = $"Bienvenido(a), {userNames} ({nombreAlmacen})";
        }


        private void CboAlmacenBarra_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboAlmacenBarra.SelectedItem is Almacen nuevoAlmacen)
            {
                // Si ya era el almacén actual, no hacemos nada
                if (SesionSistema.AlmacenActual?.Id == nuevoAlmacen.Id) return;

                // Actualizamos el almacén activo en la sesión global
                SesionSistema.AlmacenActual = nuevoAlmacen;

                // Lanzamos la notificación visual de cambio de sede
                Growl.Info($"🔄 Sede cambiada a: {nuevoAlmacen.Nombre}");

                // Opcional: Puedes refrescar datos de las vistas activas si fuera necesario
            }
        }
        // Notificación de bienvenida con el nombre de la persona y su rol.
        // Se dispara una sola vez, justo cuando MainShell termina de armarse,
        // y usa Dispatcher para asegurarse de que la ventana (y su Growl)
        // ya estén completamente visibles antes de mostrarla; si se llama
        // demasiado pronto, el Growl puede no tener dónde "anclarse" todavía.
        private void MostrarBienvenida(string userNames, bool esAdmin)
        {
            string rol = esAdmin ? "Administrador" : "Usuario";

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Growl.Success(
                    $"Ingresado con éxito\n{userNames} · {rol}");
            }, DispatcherPriority.Loaded);
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

        private void SetupWelcomeMessage(string userNames, string nombreAlmacen)
        {
            // Muestra en la cabecera superior derecha o en la barra inferior
            WelcomeMessage.Text = $"Bienvenido(a), {userNames} ({nombreAlmacen})";
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
            // Estilo en línea (sin necesidad de tocar App.xaml) que agranda
            // el ícono, le da más área de clic, y muestra un fondo gris
            // claro al pasar el mouse para que sea evidente que es un
            // botón y no solo texto decorativo.
            var estiloHover = new Style(typeof(Button));

            estiloHover.Setters.Add(new Setter(Button.TemplateProperty, CrearPlantillaBotonPestaña()));

            return new Button
            {
                Content = texto,
                Width = 28,
                Height = 28,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.DimGray,
                ToolTip = tooltip,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                Style = estiloHover
            };
        }

        // Plantilla compartida para los botones de cerrar/desacoplar.
        // Define el fondo redondeado que aparece SOLO cuando el mouse
        // está encima, para que el icono se vea limpio en reposo pero
        // sea obvio que se puede pulsar al pasar el cursor.
        private ControlTemplate CrearPlantillaBotonPestaña()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border), "BotonBorde");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(contentPresenter);
            template.VisualTree = border;

            var triggerHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            triggerHover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), "BotonBorde"));
            triggerHover.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.Black));

            template.Triggers.Add(triggerHover);

            return template;
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

        // =========================================================
        // 📝 SISTEMA DE NOTAS LOCALES (POR USUARIO)
        // =========================================================
        private void CargarNotasLocales()
        {
            // Asegúrate de que SesionSistema.UsuarioActual.Username coincida con la propiedad que usas en tu modelo
            string nombreUsuario = SesionSistema.UsuarioActual?.Username ?? "default";

            string carpetaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdicionesPiza", "Notas");
            Directory.CreateDirectory(carpetaAppData);

            _rutaArchivoNotas = Path.Combine(carpetaAppData, $"notas_{nombreUsuario}.json");

            // Limpiamos colecciones antes de cargar por si acaso
            _notasPendientes.Clear();
            _notasCompletadas.Clear();

            if (File.Exists(_rutaArchivoNotas))
            {
                try
                {
                    string json = File.ReadAllText(_rutaArchivoNotas);
                    var listaGuardada = JsonSerializer.Deserialize<List<NotaItem>>(json) ?? new List<NotaItem>();

                    foreach (var nota in listaGuardada)
                    {
                        if (nota.IsCompleted) _notasCompletadas.Add(nota);
                        else _notasPendientes.Add(nota);
                    }
                }
                catch { /* Ignorar errores de carga si el JSON está corrupto */ }
            }

            LbNotasPendientes.ItemsSource = _notasPendientes;
            LbNotasCompletadas.ItemsSource = _notasCompletadas;
        }

        private void GuardarNotasLocales()
        {
            // Si la ruta no está definida, no guardamos nada
            if (string.IsNullOrEmpty(_rutaArchivoNotas)) return;

            var todasLasNotas = new List<NotaItem>();
            todasLasNotas.AddRange(_notasPendientes);
            todasLasNotas.AddRange(_notasCompletadas);

            string json = JsonSerializer.Serialize(todasLasNotas);
            File.WriteAllText(_rutaArchivoNotas, json);
        }

        private void BtnAgregarNota_Click(object sender, RoutedEventArgs e)
        {
            // Validamos el texto del TextBox nuevo
            if (!string.IsNullOrWhiteSpace(TxtNuevaNota.Text))
            {
                _notasPendientes.Insert(0, new NotaItem { Texto = TxtNuevaNota.Text.Trim(), IsCompleted = false });
                GuardarNotasLocales();

                // Limpiamos el texto
                TxtNuevaNota.Text = string.Empty;
            }
        }

        private void BtnEliminarNota_Click(object sender, RoutedEventArgs e)
        {
            // Usamos el 'Tag' que pasamos desde el botón en el DataTemplate
            if (sender is Button boton && boton.Tag is NotaItem notaAEliminar)
            {
                _notasPendientes.Remove(notaAEliminar);
                _notasCompletadas.Remove(notaAEliminar);
                GuardarNotasLocales();
            }
        }

        private void CheckBox_CambioEstado(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is NotaItem nota)
            {
                // Movemos el ítem entre listas según su nuevo estado
                if (nota.IsCompleted)
                {
                    if (_notasPendientes.Contains(nota))
                    {
                        _notasPendientes.Remove(nota);
                        _notasCompletadas.Insert(0, nota);
                    }
                }
                else
                {
                    if (_notasCompletadas.Contains(nota))
                    {
                        _notasCompletadas.Remove(nota);
                        _notasPendientes.Add(nota);
                    }
                }
                GuardarNotasLocales();
            }
        }
    }
}