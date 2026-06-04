using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views
{
    public partial class AlmacenPanel : Window
    {
        public AlmacenPanel(string userNames)
        {
            InitializeComponent();
            SetupWelcomeMessage(userNames);
            StartClock();
        }

        private void SetupWelcomeMessage(string userNames)
        {
            WelcomeMessage.Text = $"Operador(a) de Almacén: {userNames}";
        }

        private void StartClock()
        {
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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
            DateTimeTextBlock.Text = $"{dayName} {now.Day:00} de {monthName} del {now.Year} - {now:hh:mm:ss} {amPm}";
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            // Opcional: Confirmar si hay pestañas abiertas antes de salir
            new MainWindow().Show();
            Close();
        }

        // =========================================================
        // MOTOR DE PESTAÑAS (MDI NATIVO)
        // =========================================================
        // =========================================================
        // MOTOR DE PESTAÑAS (MDI NATIVO CON DESVINCULACIÓN)
        // =========================================================
        // =========================================================
        // MOTOR DE PESTAÑAS (DISEÑO PULIDO)
        // =========================================================
        private void AbrirPestaña(string titulo, UserControl contenido)
        {
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb && tb.Text == titulo)
                {
                    MainTabControl.SelectedItem = tab;
                    return;
                }
            }

            // 1. Contenedor principal de la pestaña
            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Título de la pestaña
            headerPanel.Children.Add(new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0), // Más espacio entre el texto y los botones
                FontWeight = FontWeights.SemiBold
            });

            // 2. Creación del Botón Desvincular (↗)
            Button btnDesvincular = CrearBotonPestaña("↗", "Separar en ventana independiente");

            // 3. Creación del Botón Cerrar (✕)
            Button btnClose = CrearBotonPestaña("✕", "Cerrar pestaña");

            TabItem nuevaPestaña = new TabItem
            {
                Header = headerPanel,
                Content = contenido
            };

            // Lógicas de los botones
            btnClose.Click += (s, e) => { MainTabControl.Items.Remove(nuevaPestaña); };

            btnDesvincular.Click += (s, e) =>
            {
                nuevaPestaña.Content = null;
                MainTabControl.Items.Remove(nuevaPestaña);

                Window ventanaFlotante = new Window
                {
                    Title = titulo + " - Módulo Desvinculado",
                    Content = contenido,
                    Width = 1000,
                    Height = 650,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = System.Windows.Media.Brushes.White
                };

                ventanaFlotante.Closing += (senderWindow, args) =>
                {
                    ventanaFlotante.Content = null;
                    nuevaPestaña.Content = contenido;
                    MainTabControl.Items.Add(nuevaPestaña);
                    MainTabControl.SelectedItem = nuevaPestaña;
                };

                ventanaFlotante.Show();
            };

            // Añadimos los botones al contenedor
            headerPanel.Children.Add(btnDesvincular);
            headerPanel.Children.Add(btnClose);

            MainTabControl.Items.Add(nuevaPestaña);
            MainTabControl.SelectedItem = nuevaPestaña;
        }

        // =========================================================
        // FUNCIÓN AUXILIAR PARA DIBUJAR BOTONES BONITOS
        // =========================================================
        private Button CrearBotonPestaña(string texto, string tooltip)
        {
            Button btn = new Button
            {
                Content = texto,
                Background = System.Windows.Media.Brushes.Transparent, // Fondo invisible
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White, // Letra blanca (porque la pestaña activa es roja)
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Width = 24, // Tamaño fijo para que se vea cuadrado y simétrico
                Height = 24,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = tooltip,
                FocusVisualStyle = null // Quita el recuadro punteado feo
            };

            // Efecto Hover: Se pone un poco negro transparente al pasar el ratón
            btn.MouseEnter += (s, e) => btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 0, 0));
            btn.MouseLeave += (s, e) => btn.Background = System.Windows.Media.Brushes.Transparent;

            return btn;
        }

        // =========================================================
        // EVENTOS DEL MENÚ (Abre Pestañas en lugar de Ventanas)
        // =========================================================
        private void MenuItemProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📦 Productos", new ProductosUserControl()); ;
        }

        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e)
        {
            // AbrirPestaña("📏 Unidades", new UnidadesMedidaUserControl()); 
        }

        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e)
        {
            // AbrirPestaña("📚 Colecciones", new ColeccionesUserControl()); 
        }

        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e)
        {
            // AbrirPestaña("🏷️ Títulos", new TitulosUserControl()); 
        }

        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e)
        {
            // AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl()); 
        }

        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e)
        {
            // AbrirPestaña("📥 Ingreso de Productos", new IngresoProductosUserControl()); 
        }
    }
}