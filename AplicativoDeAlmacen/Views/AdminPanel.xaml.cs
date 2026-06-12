#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using AplicativoDeAlmacen.Core; // Importante para leer la Sesión y Permisos

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

            // 🔒 Motor de Seguridad y Restricción de Vistas
            AplicarSeguridadDinamica();
        }

        private void SetupWelcomeMessage(string userNames)
        {
            WelcomeMessage.Text = $"Bienvenido(a), {userNames}";
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

            string dayName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(now.DayOfWeek));
            string monthName = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(now.Month));
            string amPm = now.ToString("tt", CultureInfo.InvariantCulture);

            string formattedDate = $"{dayName} {now.Day:00} de {monthName} del {now.Year} - {now:hh:mm:ss} {amPm}";
            DateTimeTextBlock.Text = formattedDate;
        }

        // ==============================================================
        // 🛡️ MOTOR DE SEGURIDAD Y RESTRICCIONES (RBAC)
        // ==============================================================

        private void AplicarSeguridadDinamica()
        {
            // 1. Si es el SuperAdmin (por parámetro o por ID = 1), tiene acceso a todo.
            if (isAdmin || SesionSistema.UsuarioActual?.RolUsuarioId == 1) return;

            // Ocultamos botones específicos de interfaz administrativa
            BtnAgregarUsuario.Visibility = Visibility.Collapsed;

            // 2. Evaluamos menú por menú leyendo la base de datos (SesionSistema)

            // --- CATÁLOGOS Y TABLAS ---
            OcultarSiNoTienePermiso(MenuUsuarios, "MOD_USUARIOS");
            OcultarSiNoTienePermiso(MenuLocalidades, "MOD_LOCALIDADES");
            OcultarSiNoTienePermiso(MenuZonas, "MOD_ZONAS");
            OcultarSiNoTienePermiso(MenuUbicaciones, "MOD_UBICACIONES");
            OcultarSiNoTienePermiso(MenuProductos, "MOD_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuUnidades, "MOD_UNIDADES");
            OcultarSiNoTienePermiso(MenuPersonas, "MOD_PERSONAS");
            OcultarSiNoTienePermiso(MenuColecciones, "MOD_COLECCIONES");
            OcultarSiNoTienePermiso(MenuTitulos, "MOD_TITULOS");

            // --- MOVIMIENTOS ---
            OcultarSiNoTienePermiso(MenuRegCodigos, "MOD_REG_CODIGOS");
            OcultarSiNoTienePermiso(MenuIngProductos, "MOD_ING_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuSalProductos, "MOD_SAL_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuProyeccion, "MOD_PROYECCION");
            OcultarSiNoTienePermiso(MenuRegFacturas, "MOD_REG_FACTURAS");
            OcultarSiNoTienePermiso(MenuRepClientes, "MOD_REP_CLIENTES");

            // --- PROCESOS INTERNOS ---
            OcultarSiNoTienePermiso(MenuValorizacion, "MOD_VALORIZACION");
            OcultarSiNoTienePermiso(MenuImpClientes, "MOD_IMP_CLIENTES");
            OcultarSiNoTienePermiso(MenuImpVentas, "MOD_IMP_VENTAS");

            // --- CONSULTAS Y REPORTES ---
            OcultarSiNoTienePermiso(MenuSaldosProd, "MOD_SALDOS_PROD");
            OcultarSiNoTienePermiso(MenuKardexFis, "MOD_KARDEX_FIS");
            OcultarSiNoTienePermiso(MenuKardexVal, "MOD_KARDEX_VAL");
            OcultarSiNoTienePermiso(MenuMovProd, "MOD_MOV_PROD");
            OcultarSiNoTienePermiso(MenuHistCodigo, "MOD_HIST_CODIGO");
            OcultarSiNoTienePermiso(MenuProyeccionRep, "MOD_PROYECCION");
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
            // Si olvidaste ponerle x:Name al menú en el XAML, evitamos que el sistema colapse
            if (menu == null) return;

            var permiso = SesionSistema.ObtenerPermiso(codigoModulo);

            // Si no hay configuración o el check "Ver" está apagado, desaparecemos el botón
            if (permiso == null || !permiso.PuedeVer)
            {
                menu.Visibility = Visibility.Collapsed;
            }
        }

        // ==============================================================
        // MOTOR DE PESTAÑAS
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

            Button btnDesvincular = CrearBotonPestaña("↗", "Separar en ventana");
            Button btnClose = CrearBotonPestaña("✕", "Cerrar pestaña");

            TabItem nuevaPestana = new TabItem
            {
                Header = headerPanel,
                Content = contenido
            };

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

        // ==============================================================
        // BOTONES GENERALES
        // ==============================================================

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("¿Está seguro que desea cerrar sesión?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                new MainWindow().Show();
                Close();
            }
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            AbrirPestaña("👥 Gestión de Usuarios", new UsuariosUserControl());
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e) { /* Genérico vacío */ }

        // ==============================================================
        // EVENTOS YA DESARROLLADOS
        // ==============================================================

        private void GestionUsuarios_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🌎 Usuarios", new UsuariosUserControl());
        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🌎 Localidades", new LocalidadesUserControl());
        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📍 Zonas de Promotoría", new ZonasPromotoriaUserControl());
        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏢 Ubicaciones", new UbicacionesUserControl());
        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e) => AbrirPestaña("👥 Personas Comerciales", new PersonasComercialesUserControl());
        private void MenuItemProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📦 Productos", new ProductosUserControl());
        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📏 Unidades de Medida", new UnidadesMedidaUserControl());
        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📚 Colecciones", new ColeccionesUserControl());
        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏷️ Títulos", new TitulosUserControl());
        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl());
        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📥 Ingreso de Productos", new MovimientosUserControl());
        private void MenuItemKardexFisico_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📊 Kardex Físico", new KardexUserControl());
        private void MenuItemSaldosProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📋 Saldos de Productos", new SaldosProductosUserControl());
        private void MovimientoProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🔄 Movimiento de Productos", new ConsultaMovimientosUserControl());

        // ==============================================================
        // MÓDULOS EN CONSTRUCCIÓN (Comentados y Listos)
        // ==============================================================

        private void MenuSalProductos_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📤 Salida de Productos", new SalidaProductosUserControl());
        }

        private void MenuProyeccion_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📈 % Proyección", new ProyeccionUserControl());
        }

        private void MenuRegFacturas_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("🧾 Registro de Facturas", new RegistroFacturasUserControl());
        }

        private void MenuRepClientes_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("👥 Reporte de Clientes", new ReporteClientesUserControl());
        }

        private void MenuValorizacion_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("💰 Valorización de Productos", new ValorizacionUserControl());
        }

        private void MenuImpClientes_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("⬇️ Importación de Clientes", new ImportacionClientesUserControl());
        }

        private void MenuImpVentas_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("⬇️ Importación de Ventas", new ImportacionVentasUserControl());
        }

        private void MenuKardexVal_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📊 Kardex Valorizado", new KardexValorizadoUserControl());
        }

        private void MenuHistCodigo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("⏳ Historial x Código", new HistorialCodigoUserControl());
        }

        private void MenuRegVentas_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("💳 Registro de Ventas", new RegistroVentasUserControl());
        }

        private void MenuVenCliente_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📈 Ventas x Cliente", new VentasClienteUserControl());
        }

        private void MenuVenUbicacion_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📍 Ventas x Ubicación", new VentasUbicacionUserControl());
        }

        private void MenuCliEmitidos_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📄 Reporte Clientes Emitidos", new ClientesEmitidosUserControl());
        }

        private void MenuResLibVendidos_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("📚 Resumen Libros Vendidos", new ResumenLibrosVendidosUserControl());
        }

        private void MenuResLibCliente_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("👤 Resumen Libros x Cliente", new ResumenLibrosClienteUserControl());
        }
      
        private void MenuKardexUbicacion_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Módulo en construcción...", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            // AbrirPestaña("🏢 Kardex x Ubicación", new KardexUbicacionUserControl());ll
        }
        
        private void MenuItemAcademico_Click(object sender, RoutedEventArgs e)
        {
            
            AbrirPestaña("🏢 Módulo Academico", new AcademicoMaestroUserControl());
        }

        private void MenuItemCatalogosMaestros_Click(object sender, RoutedEventArgs e)
        {

            AbrirPestaña("🏢 Mòdulo de Catalogos", new CatalogoMaestroUserControl());
        }

        
            
        private void MenuItemSalidasProductos_Click(object sender, RoutedEventArgs e)
        {
           AbrirPestaña("📥 Salidas de Productos", new SalidasUserControl());
        }

    }
}