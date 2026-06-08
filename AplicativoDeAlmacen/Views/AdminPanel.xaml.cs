#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AplicativoDeAlmacen.Views
{
    public partial class AdminPanel : Window, IMainWindow
    {
        private readonly bool isAdmin;

        public AdminPanel(string userNames, bool isAdmin)
        {
            InitializeComponent();

            this.isAdmin = isAdmin;

            SetupWelcomeMessage(userNames);
            StartClock();
            ConfigureUIForRole();
        }

        private void SetupWelcomeMessage(string userNames)
        {
            WelcomeMessage.Text = $"Bienvenido(a), {userNames}";
        }

        private void ConfigureUIForRole()
        {
            if (!isAdmin)
            {
                BtnAgregarUsuario.Visibility = Visibility.Collapsed;
            }
        }

        private void StartClock()
        {
            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            timer.Tick += Timer_Tick;
            timer.Start();

            Timer_Tick(this, EventArgs.Empty);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            CultureInfo culture = new("es-ES");

            string dayName =
                culture.TextInfo.ToTitleCase(
                    culture.DateTimeFormat.GetDayName(now.DayOfWeek));

            string monthName =
                culture.TextInfo.ToTitleCase(
                    culture.DateTimeFormat.GetMonthName(now.Month));

            string amPm =
                now.ToString("tt", CultureInfo.InvariantCulture);

            string formattedDate =
                $"{dayName} {now.Day:00} de {monthName} del {now.Year} - {now:hh:mm:ss} {amPm}";

            DateTimeTextBlock.Text = formattedDate;
        }

        // ==============================================================
        // MOTOR DE PESTAÑAS
        // ==============================================================

        public void AbrirPestaña(string titulo, UserControl contenido)
        {
            // 1. Evitar duplicados
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == titulo)
                {
                    MainTabControl.SelectedItem = tab;
                    return;
                }
            }

            // 2. Crear cabecera personalizada
            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Título
            headerPanel.Children.Add(new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold
            });

            // Botones (Solo los creamos aquí)
            Button btnDesvincular = CrearBotonPestaña("↗", "Separar en ventana");
            Button btnClose = CrearBotonPestaña("✕", "Cerrar pestaña");

            TabItem nuevaPestana = new TabItem
            {
                Header = headerPanel,
                Content = contenido,
                
            };

            // Lógica Cerrar
            btnClose.Click += (s, e) => { MainTabControl.Items.Remove(nuevaPestana); };

            // Lógica Desvincular
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

        private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            DependencyObject current = button;

            while (current != null && current is not TabItem)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is TabItem tabItem)
            {
                // No permitir cerrar la pestaña principal
                if (tabItem.Name == "TabInicio")
                    return;

                MainTabControl.Items.Remove(tabItem);
            }
        }

        // ==============================================================
        // BOTONES GENERALES
        // ==============================================================

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result =
                MessageBox.Show(
                    "¿Está seguro que desea cerrar sesión?",
                    "Confirmar",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                new MainWindow().Show();
                Close();
            }
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Módulo de usuarios en construcción.",
                "Aviso",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ==============================================================
        // CATÁLOGOS Y TABLAS
        // ==============================================================

        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("🌎 Localidades", new LocalidadesUserControl());
        }

        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📍 Zonas de Promotoría", new ZonasPromotoriaUserControl());
        }

        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("🏢 Ubicaciones", new UbicacionesUserControl());
        }

        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("👥 Personas Comerciales",new PersonasComercialesUserControl()
            );
        }

        private void MenuItemProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📦 Productos", new ProductosUserControl());
        }

        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📏 Unidades de Medida", new UnidadesMedidaUserControl());
        }

        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📚 Colecciones", new ColeccionesUserControl());
        }

        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("🏷️ Títulos", new TitulosUserControl());
        }

        // ==============================================================
        // MOVIMIENTOS
        // ==============================================================

        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl());
        }

        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📥 Ingreso de Productos", new MovimientosUserControl());
        }
    }
}