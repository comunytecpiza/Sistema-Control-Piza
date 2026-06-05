#nullable enable
using System;
using System.Windows;
using System.Windows.Threading;
using System.Globalization;

namespace AplicativoDeAlmacen.Views
{
    public partial class AdminPanel : Window
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

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            new MainWindow().Show();
            Close();
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            new UserWindow().Show();
        }

        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e)
        {
            new LocalidadesWindow().Show();
        }

        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e)
        {
            ZonasPromotoriaWindow zonasWindow = new ZonasPromotoriaWindow();
            zonasWindow.Show();
        }

        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e)
        {
            UbicacionesWindow ubicacionesWindow = new UbicacionesWindow();
            ubicacionesWindow.Show();
        }

        private void MenuItemProductos_Click(object sender, RoutedEventArgs e)
        {
            ProductosUserControl productosWindow = new ProductosUserControl();
            // new ProductosWindow().Show();
        }

        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e)
        {
            //new UnidadesMedidaWindow().Show();
        }

        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e)
        {
            PersonasComercialesWindow personasComercialesWindow = new PersonasComercialesWindow();
            personasComercialesWindow.Show();
        }

        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e)
        {
           // ColeccionesWindow coleccionesWindow = new ColeccionesWindow();
            //coleccionesWindow.Show();
        }

        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e)
        {
            //TitulosWindow titulosWindow = new TitulosWindow();
            //titulosWindow.Show();
        }

        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e)
        {
            RegistroCodigosWindow registroCodigosWindow = new RegistroCodigosWindow();
            registroCodigosWindow.Show();
        }

        private void MenuItemIngresoProductos_Click(object obj, RoutedEventArgs e)
        {
            IngresoProductosWindow ingresoProductosWindow = new IngresoProductosWindow();
            ingresoProductosWindow.Show();
        }
    }
}