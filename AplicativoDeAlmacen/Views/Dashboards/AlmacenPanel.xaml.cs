using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Data;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models;

namespace AplicativoDeAlmacen.Views
{
    // Modelo para las notas
    public class NotaItem
    {
        public string Texto { get; set; }
        public bool IsCompleted { get; set; }
    }

    // Convertidor de colores (Lo dejamos por si lo usas en otro lado)
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
        // Variables globales para tus notas
        private ObservableCollection<NotaItem> _notasPendientes = new ObservableCollection<NotaItem>();
        private ObservableCollection<NotaItem> _notasCompletadas = new ObservableCollection<NotaItem>();
        private string _rutaArchivoNotas;

        private readonly ProductoService _productoService;

        public AlmacenPanel(string userNames)
        {
            InitializeComponent();
            _productoService = new ProductoService();

            SetupWelcomeMessage(userNames);
            StartClock();
            AplicarSeguridadDinamica();

            // Cargar notas desde el almacenamiento local
            CargarNotas();

            // Primera carga del panel
            _ = CargarPanelPrincipal();

            // ====================================================================
            // 🌟 REACTIVIDAD DEL DASHBOARD: Escuchar al EventBus
            // ====================================================================

            // Cuando haya un nuevo movimiento (Ingreso/Salida), recargamos el Stock Crítico
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(async () => {
                await CargarPanelPrincipal();
            });

            // Si se crea o edita un producto, también actualizamos el panel
            EventBus.OnProductosChanged += () => Application.Current.Dispatcher.InvokeAsync(async () => {
                await CargarPanelPrincipal();
            });

            // Opcional: Si quieres que también se actualice al cambiar de pestaña hacia el "Inicio"
            MainTabControl.SelectionChanged += async (s, e) =>
            {
                if (e.Source is TabControl && MainTabControl.SelectedIndex == 0) // El índice 0 es "🏠 Inicio"
                {
                    await CargarPanelPrincipal();
                }
            };
        }

        // =========================================================
        // 📌 MOTOR DE NOTAS LOCALES (MEJORADO)
        // =========================================================
        private void CargarNotas()
        {
            string carpetaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdicionesPiza");
            Directory.CreateDirectory(carpetaAppData);
            _rutaArchivoNotas = Path.Combine(carpetaAppData, "notas.json");

            if (File.Exists(_rutaArchivoNotas))
            {
                string json = File.ReadAllText(_rutaArchivoNotas);
                var listaGuardada = JsonSerializer.Deserialize<List<NotaItem>>(json) ?? new List<NotaItem>();

                // Repartimos las notas en sus listas correspondientes
                foreach (var nota in listaGuardada)
                {
                    if (nota.IsCompleted) _notasCompletadas.Add(nota);
                    else _notasPendientes.Add(nota);
                }
            }

            LbNotasPendientes.ItemsSource = _notasPendientes;
            LbNotasCompletadas.ItemsSource = _notasCompletadas;
        }

        private void GuardarNotas()
        {
            var todasLasNotas = new List<NotaItem>();
            todasLasNotas.AddRange(_notasPendientes);
            todasLasNotas.AddRange(_notasCompletadas);

            string json = JsonSerializer.Serialize(todasLasNotas);
            File.WriteAllText(_rutaArchivoNotas, json);
        }

        private void BtnAgregarNota_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtNuevaNota.Text))
            {
                _notasPendientes.Insert(0, new NotaItem { Texto = TxtNuevaNota.Text, IsCompleted = false });
                GuardarNotas();
                TxtNuevaNota.Clear();
            }
        }

        private void BtnEliminarNota_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            if (boton?.Tag is NotaItem notaAEliminar)
            {
                _notasPendientes.Remove(notaAEliminar);
                _notasCompletadas.Remove(notaAEliminar);
                GuardarNotas();
            }
        }

        private void CheckBox_CambioEstado(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb?.DataContext is NotaItem nota)
            {
                // Si la nota acaba de ser completada
                if (nota.IsCompleted && _notasPendientes.Contains(nota))
                {
                    _notasPendientes.Remove(nota);
                    _notasCompletadas.Insert(0, nota); // Se va a la lista de completadas
                }
                // Si la nota fue desmarcada
                else if (!nota.IsCompleted && _notasCompletadas.Contains(nota))
                {
                    _notasCompletadas.Remove(nota);
                    _notasPendientes.Add(nota); // Regresa a la lista de pendientes
                }

                GuardarNotas();
            }
        }

        // =========================================================
        // 📦 CARGA DE PANEL Y STOCK (CON FILTRO DE TIPO DE PRODUCTO)
        // =========================================================
        private async Task CargarPanelPrincipal()
        {
            try
            {
                var stockCriticoBD = await _productoService.ObtenerStockCriticoAsync();

                if (stockCriticoBD != null)
                {
                    // 1. FILTRAMOS: Stock <= 100 Y que sea de TipoProductoId 1 o 2 
                    // (⚠️ Cambia el 1 y 2 por los IDs reales de Plan Lector y Texto Escolar en tu BD)
                    var filtrado = stockCriticoBD.Where(p =>
                        p.StockActual <= 100 &&
                        (p.TipoProductoId == 1 || p.TipoProductoId == 2)
                    ).ToList();

                    // 2. AGRUPAMOS: Por el nombre del Grado
                    CollectionViewSource cvs = new CollectionViewSource { Source = filtrado };

                    // Ahora sí agrupará correctamente usando la nueva propiedad
                    cvs.GroupDescriptions.Add(new PropertyGroupDescription("GradoNombre"));

                    DgStockCritico.ItemsSource = cvs.View;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error en CargarPanelPrincipal: " + ex.Message);
            }
        }

        // =========================================================
        // 🕒 UTILIDADES (Reloj y Bienvenida)
        // =========================================================
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
            new MainWindow().Show();
            Close();
        }

        // ==============================================================
        // 🛡️ MOTOR DE SEGURIDAD
        // ==============================================================
        private void AplicarSeguridadDinamica()
        {
            if (SesionSistema.UsuarioActual?.RolUsuarioId == 1) return;

            OcultarSiNoTienePermiso(MenuLocalidades, "MOD_LOCALIDADES");
            OcultarSiNoTienePermiso(MenuZonas, "MOD_ZONAS");
            OcultarSiNoTienePermiso(MenuUbicaciones, "MOD_UBICACIONES");
            OcultarSiNoTienePermiso(MenuProductos, "MOD_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuUnidades, "MOD_UNIDADES");
            OcultarSiNoTienePermiso(MenuPersonas, "MOD_PERSONAS");
            OcultarSiNoTienePermiso(MenuColecciones, "MOD_COLECCIONES");
            OcultarSiNoTienePermiso(MenuTitulos, "MOD_TITULOS");

            OcultarSiNoTienePermiso(MenuRegCodigos, "MOD_REG_CODIGOS");
            OcultarSiNoTienePermiso(MenuIngProductos, "MOD_ING_PRODUCTOS");
            OcultarSiNoTienePermiso(MenuSalProductos, "MOD_SAL_PRODUCTOS");

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
        // 🗂️ MOTOR DE PESTAÑAS (CUMPLE CON IMAINWINDOW)
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

            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            headerPanel.Children.Add(new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0),
                FontWeight = FontWeights.SemiBold
            });

            Button btnDesvincular = CrearBotonPestaña("↗", "Separar en ventana independiente");
            Button btnClose = CrearBotonPestaña("✕", "Cerrar pestaña");

            TabItem nuevaPestaña = new TabItem
            {
                Header = headerPanel,
                Content = contenido
            };

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

            headerPanel.Children.Add(btnDesvincular);
            headerPanel.Children.Add(btnClose);

            MainTabControl.Items.Add(nuevaPestaña);
            MainTabControl.SelectedItem = nuevaPestaña;
        }

        private Button CrearBotonPestaña(string texto, string tooltip)
        {
            Button btn = new Button
            {
                Content = texto,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = tooltip,
                FocusVisualStyle = null
            };

            btn.MouseEnter += (s, e) => btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 0, 0));
            btn.MouseLeave += (s, e) => btn.Background = System.Windows.Media.Brushes.Transparent;

            return btn;
        }

        // =========================================================
        // 🖱️ EVENTOS DEL MENÚ
        // =========================================================
        private void MenuItemLocalidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🌎 Localidades", new LocalidadesUserControl());
        private void MenuItemZonasPromotoria_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📍 Zonas de Promotoría", new ZonasPromotoriaUserControl());
        private void MenuItemUbicaciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏢 Ubicaciones", new UbicacionesUserControl());
        private void MenuItemPersonasComerciales_Click(object sender, RoutedEventArgs e) => AbrirPestaña("👥 Personas Comerciales", new PersonasComercialesUserControl());
        private void MenuItemProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📦 Productos", new ProductosUserControl());
        private void MenuItemUnidades_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📏 Unidades de Medida", new UnidadesMedidaUserControl());
        private void MenuItemColecciones_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📚 Colecciones", new ColeccionesUserControl());
        private void MenuItemTitulos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🏷️ Títulos", new TitulosUserControl());
        private void MenuItemRegistroCodigos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📝 Registro de Códigos", new RegistroCodigosUserControl());
        private void MenuItemKardexFisico_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📊 Kardex Físico", new KardexUserControl());
        private void MenuItemSaldosProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📋 Saldos de Productos", new SaldosProductosUserControl());
        private void MenuItemIngresoProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("📥 Ingreso de Productos", new IngresoUserControl());
        private void MovimientoProductos_Click(object sender, RoutedEventArgs e) => AbrirPestaña("🔄 Movimiento de Productos", new ConsultaMovimientosUserControl());
        private void MenuItem_Click(object sender, RoutedEventArgs e) { }
    }
}