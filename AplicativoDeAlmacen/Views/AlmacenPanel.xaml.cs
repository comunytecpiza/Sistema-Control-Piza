using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;
using System.Collections.ObjectModel;
using AplicativoDeAlmacen.Models;
using System.Windows.Data; // Importante para la Sesión

namespace AplicativoDeAlmacen.Views
{
    // IMPORTANTE: Agregamos IMainWindow aquí
    public class StockColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int stock = (int)value;
            if (stock == 0) return new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Rojo
            return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Naranja
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
    public partial class AlmacenPanel : Window, IMainWindow
    {
        private ObservableCollection<string> notasPendientes = new ObservableCollection<string>();
        private readonly ProductoService _productoService;
        public AlmacenPanel(string userNames)
        {
            InitializeComponent();
            // ¡Asegúrate de que esta línea esté aquí!
            _productoService = new ProductoService();

            SetupWelcomeMessage(userNames);
            StartClock();
            AplicarSeguridadDinamica();

            LbNotas.ItemsSource = notasPendientes;
            _ = CargarPanelPrincipal();
        }

        private async Task CargarPanelPrincipal()
        {
            try
            {
                // Validación de seguridad:
                if (_productoService == null) throw new Exception("_productoService no está inicializado.");
                if (DgStockCritico == null) throw new Exception("El DataGrid DgStockCritico no existe en el XAML.");

                var stockCritico = await _productoService.ObtenerStockCriticoAsync();

                // Si stockCritico es null, asignamos una lista vacía para evitar errores
                DgStockCritico.ItemsSource = stockCritico ?? new List<ProductoStock>();
            }
            catch (Exception ex)
            {
                // Aquí verás el mensaje real si el error es otro
                MessageBox.Show("Error en CargarPanelPrincipal: " + ex.Message);
            }
        }

        private void BtnAgregarNota_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtNuevaNota.Text))
            {
                notasPendientes.Add(TxtNuevaNota.Text);
                TxtNuevaNota.Clear();
            }
        }

        private void BtnEliminarNota_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Content is string nota)
            {
                notasPendientes.Remove(nota);
            }
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

        // ==============================================================
        // 🛡️ MOTOR DE SEGURIDAD (Solo evalúa lo que existe en este panel)
        // ==============================================================
        private void AplicarSeguridadDinamica()
        {
            // Si es SuperAdmin (1), ve todo
            if (SesionSistema.UsuarioActual?.RolUsuarioId == 1) return;

            // Catálogos
            OcultarSiNoTienePermiso(MenuLocalidades, "MOD_LOCALIDADES");
            OcultarSiNoTienePermiso(MenuZonas, "MOD_ZONAS");
            OcultarSiNoTienePermiso(MenuUbicaciones, "MOD_UBICACIONES");
            OcultarSiNoTienePermiso(MenuProductos, "MOD_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuUnidades, "MOD_UNIDADES");
            OcultarSiNoTienePermiso(MenuPersonas, "MOD_PERSONAS");
            OcultarSiNoTienePermiso(MenuColecciones, "MOD_COLECCIONES");
            OcultarSiNoTienePermiso(MenuTitulos, "MOD_TITULOS");

            // Movimientos
            OcultarSiNoTienePermiso(MenuRegCodigos, "MOD_REG_CODIGOS");
            OcultarSiNoTienePermiso(MenuIngProductos, "MOD_ING_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuSalProductos, "MOD_SAL_PRODUCTOS");

            // Consultas
            OcultarSiNoTienePermiso(MenuSaldosProd, "MOD_SALDOS_PROD");
            OcultarSiNoTienePermiso(MenuKardexFis, "MOD_KARDEX_FIS");
            OcultarSiNoTienePermiso(MenuMovProd, "MOD_MOV_PROD");
        }

        private void OcultarSiNoTienePermiso(MenuItem menu, string codigoModulo)
        {
            if (menu == null) return;
            var permiso = SesionSistema.ObtenerPermiso(codigoModulo);
            if (permiso == null || !permiso.PuedeVer)
            {
                menu.Visibility = Visibility.Collapsed;
            }
        }

        // =========================================================
        // MOTOR DE PESTAÑAS (CUMPLE CON IMAINWINDOW)
        // =========================================================
        public void AbrirPestaña(string titulo, UserControl contenido)
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
            AbrirPestaña("👥 Personas Comerciales", new PersonasComercialesUserControl());
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
        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl());
        }

        // Este es el método que conecta el menú con la vista del Kardex
        private void MenuItemKardexFisico_Click(object sender, RoutedEventArgs e)
        {
            // Llamamos al método que abre la pestaña
            AbrirPestaña("📊 Kardex Físico", new KardexUserControl());
        }

        // Método para abrir la pestaña de Saldos de Productos
        private void MenuItemSaldosProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📋 Saldos de Productos", new SaldosProductosUserControl());
        }
        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("📥 Ingreso de Productos", new MovimientosUserControl());
        }

        private void MovimientoProductos_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("🔄 Movimiento de Productos", new ConsultaMovimientosUserControl());
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}