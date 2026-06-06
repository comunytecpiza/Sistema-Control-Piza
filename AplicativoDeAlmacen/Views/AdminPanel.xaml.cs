#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;

namespace AplicativoDeAlmacen.Views
{
    // IMPORTANTE: Le agregamos la interfaz IMainWindow al lado de Window
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
            DispatcherTimer timer = new DispatcherTimer
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
            CultureInfo culture = new CultureInfo("es-ES");
            string dayName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(now.DayOfWeek));
            string monthName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(now.Month));
            string amPm = now.ToString("tt", CultureInfo.InvariantCulture);
            string formattedDate = $"{dayName} {now.Day:00} de {monthName} del {now.Year} - {now:hh:mm:ss} {amPm}";
            DateTimeTextBlock.Text = formattedDate;
        }

        // ==============================================================
        // MOTOR DE PESTAÑAS (IMPLEMENTACIÓN DE IMAINWINDOW)
        // ==============================================================
        public void AbrirPestaña(string titulo, UserControl contenido)
        {
            // 1. Evitar duplicados: Si la pestaña ya está abierta, la enfocamos
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Header != null && tab.Header.ToString() == titulo)
                {
                    MainTabControl.SelectedItem = tab;
                    return;
                }
            }

            // 2. Crear nueva pestaña
            var nuevoTab = new TabItem
            {
                Header = titulo,
                Content = contenido
            };

            MainTabControl.Items.Add(nuevoTab);
            MainTabControl.SelectedItem = nuevoTab;
        }

        private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
        {
            // Lógica para cerrar la pestaña cuando se hace clic en la "X"
            if (sender is Button btn && btn.TemplatedParent is TabItem tabItem)
            {
                // Protegemos la pestaña de Inicio para que no se pueda cerrar por accidente
                if (tabItem.Name != "TabInicio")
                {
                    MainTabControl.Items.Remove(tabItem);
                }
            }
        }

        // ==============================================================
        // EVENTOS DE BOTONES Y MENÚ
        // ==============================================================

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Está seguro que desea cerrar sesión?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                new MainWindow().Show();
                Close();
            }
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            // new UserWindow().Show();
            MessageBox.Show("Módulo de usuarios en construcción.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // --- Módulos Antiguos (Aún abren en ventanas flotantes) ---
        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e)
        {
            // new LocalidadesWindow().Show();
        }

        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e)
        {
            // new ZonasPromotoriaWindow().Show();
        }

        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e)
        {
            // new UbicacionesWindow().Show();
        }

        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e)
        {
             new PersonasComercialesWindow().Show();
        }

        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e)
        {
            // new IngresoProductosWindow().Show();
        }


        // --- Módulos Modernizados (Ahora abren como Pestañas Integradas) ---

        private void MenuItemProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📦 Productos", new ProductosUserControl());
        }

        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📏 Unidades", new UnidadesMedidaUserControl());
        }

        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📚 Colecciones", new ColeccionesUserControl());
        }

        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("🏷️ Títulos", new TitulosUserControl());
        }

        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl());
        }
    }
}