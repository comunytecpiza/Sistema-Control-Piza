#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AplicativoDeAlmacen.Core;

namespace AplicativoDeAlmacen.Views
{
    public partial class ContabilidadPanel : Window, IMainWindow
    {
        public ContabilidadPanel(string userNames)
        {
            InitializeComponent();

            SetupWelcomeMessage(userNames);
            StartClock();

            // 🔒 Validación automática de la matriz de permisos para Contabilidad
            AplicarSeguridadDinamica();
        }

        private void SetupWelcomeMessage(string userNames)
        {
            WelcomeMessage.Text = $"Contador(a): {userNames}";
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

        // ==============================================================
        // 🛡️ MOTOR DE EVALUACIÓN DE PERMISOS
        // ==============================================================
        private void AplicarSeguridadDinamica()
        {
            if (SesionSistema.UsuarioActual?.RolUsuarioId == 1) return; // SuperAdmin ve todo

            // Catálogos
            OcultarSiNoTienePermiso(MenuLocalidades, "MOD_LOCALIDADES");
            OcultarSiNoTienePermiso(MenuZonas, "MOD_ZONAS");
            OcultarSiNoTienePermiso(MenuUbicaciones, "MOD_UBICACIONES");
            OcultarSiNoTienePermiso(MenuProductos, "MOD_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuUnidades, "MOD_UNIDADES");
            OcultarSiNoTienePermiso(MenuPersonas, "MOD_PERSONAS");
            OcultarSiNoTienePermiso(MenuColecciones, "MOD_COLECCIONES");
            OcultarSiNoTienePermiso(MenuTitulos, "MOD_TITULOS");

            // Documentos / Movimientos Financieros
            OcultarSiNoTienePermiso(MenuProyeccion, "MOD_PROYECCION");
            OcultarSiNoTienePermiso(MenuRegFacturas, "MOD_REG_FACTURAS");
            OcultarSiNoTienePermiso(MenuRepClientes, "MOD_REP_CLIENTES");

            // Cierres
            OcultarSiNoTienePermiso(MenuValorizacion, "MOD_VALORIZACION");
            OcultarSiNoTienePermiso(MenuImpClientes, "MOD_IMP_CLIENTES");
            OcultarSiNoTienePermiso(MenuImpVentas, "MOD_IMP_VENTAS");

            // Consultas y Reportes
            OcultarSiNoTienePermiso(MenuSaldosProd, "MOD_SALDOS_PROD");
            OcultarSiNoTienePermiso(MenuKardexFis, "MOD_KARDEX_FIS");
            OcultarSiNoTienePermiso(MenuKardexVal, "MOD_KARDEX_VAL");
            OcultarSiNoTienePermiso(MenuMovProd, "MOD_MOV_PROD");
            OcultarSiNoTienePermiso(MenuHistCodigo, "MOD_HIST_CODIGO");
            OcultarSiNoTienePermiso(MenuRegVentas, "MOD_REG_VENTAS");
            OcultarSiNoTienePermiso(MenuVenCliente, "MOD_VEN_CLIENTE");
            OcultarSiNoTienePermiso(MenuVenUbicacion, "MOD_VEN_UBICACION");
            OcultarSiNoTienePermiso(MenuCliEmitidos, "MOD_CLI_EMITIDOS");
            OcultarSiNoTienePermiso(MenuResLibVendidos, "MOD_RES_LIB_VENDIDOS");
            OcultarSiNoTienePermiso(MenuResLibCliente, "MOD_RES_LIB_CLIENTE");
            OcultarSiNoTienePermiso(MenuKardexUbicacion, "MOD_KARDEX_UBICACION");
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

        // ==============================================================
        // MOTOR DE ADICIÓN DE PESTAÑAS (IMainWindow)
        // ==============================================================
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

            Button btnDesvincular = new Button { Content = "↗", Width = 22, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.Gray, Margin = new Thickness(2, 0, 0, 0) };
            Button btnClose = new Button { Content = "✕", Width = 22, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.Gray, Margin = new Thickness(2, 0, 0, 0) };

            TabItem nuevaPestana = new TabItem { Header = headerPanel, Content = contenido };

            btnClose.Click += (s, e) => { MainTabControl.Items.Remove(nuevaPestana); };
            btnDesvincular.Click += (s, e) => {
                MainTabControl.Items.Remove(nuevaPestana);
                Window ventanaFlotante = new Window { Title = titulo, Content = contenido, Width = 1000, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                ventanaFlotante.Closed += (s2, e2) => { nuevaPestana.Content = contenido; MainTabControl.Items.Add(nuevaPestana); };
                ventanaFlotante.Show();
            };

            headerPanel.Children.Add(btnDesvincular);
            headerPanel.Children.Add(btnClose);
            MainTabControl.Items.Add(nuevaPestana);
            MainTabControl.SelectedItem = nuevaPestana;
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("¿Está seguro que desea cerrar sesión?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                new MainWindow().Show();
                Close();
            }
        }

        // ==============================================================
        // DISPARADORES DE PESTAÑAS ACTIVAS
        // ==============================================================
        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🌎 Localidades", new LocalidadesUserControl());
        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📍 Zonas de Promotoría", new ZonasPromotoriaUserControl());
        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏢 Ubicaciones", new UbicacionesUserControl());
        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e) => AbrirPestaña("👥 Personas Comerciales", new PersonasComercialesUserControl());
        private void MenuItemProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📦 Productos", new ProductosUserControl());
        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📏 Unidades de Medida", new UnidadesMedidaUserControl());
        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📚 Colecciones", new ColeccionesUserControl());
        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏷️ Títulos", new TitulosUserControl());
        private void MenuItemKardexFisico_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📊 Kardex Físico", new KardexUserControl());
        private void MenuItemSaldosProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📋 Saldos de Productos", new SaldosProductosUserControl());
        private void MovimientoProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🔄 Movimiento de Productos", new ConsultaMovimientosUserControl());

        // ==============================================================
        // HITOS FUTUROS EN CONSTRUCCIÓN
        // ==============================================================
        private void MenuProyeccion_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuRegFacturas_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuRepClientes_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuValorizacion_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuImpClientes_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuImpVentas_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuKardexVal_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuHistCodigo_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuRegVentas_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuVenCliente_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuVenUbicacion_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuCliEmitidos_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuResLibVendidos_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuResLibCliente_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
        private void MenuKardexUbicacion_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Módulo en construcción...", "Aviso");
    }
}