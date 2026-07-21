using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private readonly PersonaComercialService _personaService;
        private readonly UbicacionService _ubicacionService;

        private int _productoSeleccionadoId;
        private bool _estaSeleccionando;
        private bool _isUpdatingFromSelection = false;
        private bool _isCargando = false;

        private List<ConsultaCodigoItem> _todosLosCodigos;
        private List<Producto> _todosLosProductos = new List<Producto>();
        private List<ConsultaMovimientoItem> _todosLosMovimientosRaw = new List<ConsultaMovimientoItem>();

        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _personaService = new PersonaComercialService();
            _ubicacionService = new UbicacionService();

            _todosLosCodigos = new List<ConsultaCodigoItem>();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;
        }

        private async void Control_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var dbProductos = await _productoService.ObtenerTodosAsync();
                _todosLosProductos = dbProductos.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar productos: " + ex.Message);
            }

            var txtProd = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (txtProd != null) txtProd.TextChanged += TxtProducto_TextChanged;

            ConfigurarMascaraFecha(DpDesde);
            ConfigurarMascaraFecha(DpHasta);

            CboProductos.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtRazonSocial.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtUbicacion.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        private void ChkFiltros_Click(object sender, RoutedEventArgs e)
        {
            TxtRazonSocial.IsEnabled = ChkRazonSocial.IsChecked == true;
            if (ChkRazonSocial.IsChecked == false) TxtRazonSocial.Text = string.Empty;

            TxtUbicacion.IsEnabled = ChkUbicacion.IsChecked == true;
            if (ChkUbicacion.IsChecked == false) TxtUbicacion.Text = string.Empty;

            AjustarModoPerspectivaUI();
        }

        private void ChkMostrarAnulados_Click(object sender, RoutedEventArgs e)
        {
            RefrescarVistaMovimientos();
        }

        private void AjustarModoPerspectivaUI()
        {
            bool hayFiltroEntidad = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            if (MovimientosDataGrid.Columns.Count >= 7)
            {
                var ingresoStyle = (Style)FindResource("IngresoStyle"); // Verde
                var salidaStyle = (Style)FindResource("SalidaStyle");   // Rojo

                var colSalida = MovimientosDataGrid.Columns[4] as DataGridTextColumn;
                var colIngreso = MovimientosDataGrid.Columns[5] as DataGridTextColumn;

                if (hayFiltroEntidad)
                {
                    // 🟢 MODO ESPECÍFICO (Promotora / Cliente)
                    if (colSalida != null)
                    {
                        colSalida.Header = "ENVIADO";
                        colSalida.CellStyle = salidaStyle; // Rojo para Salida/Entregado
                        colSalida.Binding = new System.Windows.Data.Binding("Salida") { StringFormat = "N2" };
                    }
                    if (colIngreso != null)
                    {
                        colIngreso.Header = "DEVUELTO";
                        colIngreso.CellStyle = ingresoStyle; // Verde para Devolución
                        colIngreso.Binding = new System.Windows.Data.Binding("Ingreso") { StringFormat = "N2" };
                    }

                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Visible; // Total en Poder

                    // Nombres de Tarjetas
                    LblCard1.Text = "Total Entregado (Salidas)";
                    LblCard2.Text = "Total Devoluciones";
                    LblCard3.Text = "Saldo Pendiente en Poder";

                    // Colores Tarjeta 1 (Entregado -> Rojo)
                    Card1.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF2F2"));
                    Card1.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                    LblCard1.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    TxtTotalIngreso.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B91C1C"));

                    // Colores Tarjeta 2 (Devolución -> Verde)
                    Card2.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
                    Card2.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                    LblCard2.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#065F46"));
                    TxtTotalSalida.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#047857"));

                    Card3.Visibility = Visibility.Visible;
                }
                else
                {
                    // 🔵 MODO GENERAL (Almacén Central)
                    if (colSalida != null)
                    {
                        colSalida.Header = "INGRESO";
                        colSalida.CellStyle = ingresoStyle; // Verde para Entrada
                        colSalida.Binding = new System.Windows.Data.Binding("Ingreso") { StringFormat = "N2" };
                    }
                    if (colIngreso != null)
                    {
                        colIngreso.Header = "SALIDA";
                        colIngreso.CellStyle = salidaStyle; // Rojo para Salida
                        colIngreso.Binding = new System.Windows.Data.Binding("Salida") { StringFormat = "N2" };
                    }

                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Collapsed;

                    // Nombres de Tarjetas
                    LblCard1.Text = "Total Entradas";
                    LblCard2.Text = "Total Salidas";
                    LblCard3.Text = string.Empty;

                    // Colores Tarjeta 1 (Entrada -> Verde)
                    Card1.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
                    Card1.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                    LblCard1.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#065F46"));
                    TxtTotalIngreso.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#047857"));

                    // Colores Tarjeta 2 (Salida -> Rojo)
                    Card2.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF2F2"));
                    Card2.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                    LblCard2.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    TxtTotalSalida.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B91C1C"));

                    Card3.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RefrescarVistaMovimientos()
        {
            var movimientos = _todosLosMovimientosRaw.AsEnumerable();

            // 1. Filtro Anulados
            bool mostrarAnulados = ChkMostrarAnulados.IsChecked == true;
            if (!mostrarAnulados)
            {
                movimientos = movimientos.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO"));
            }

            // 2. Filtros de Entidad (Razón Social o Ubicación)
            bool hayFiltroEntidad = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            var listaFinal = movimientos.ToList();

            // 3. Cálculo de Saldo Acumulado
            decimal saldoAcumulado = 0;
            foreach (var item in listaFinal)
            {
                if (!item.IsAnulado)
                {
                    if (hayFiltroEntidad)
                    {
                        saldoAcumulado += (item.Salida - item.Ingreso);
                    }
                }
                item.SaldoAcumulado = hayFiltroEntidad ? saldoAcumulado : 0;
            }

            MovimientosDataGrid.ItemsSource = null;
            MovimientosDataGrid.ItemsSource = listaFinal;

            // 4. Actualización de Valores en Tarjetas
            decimal totalIngresos = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Ingreso);
            decimal totalSalidas = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Salida);

            if (hayFiltroEntidad)
            {
                TxtTotalIngreso.Text = totalSalidas.ToString("N2");  // Total Entregado
                TxtTotalSalida.Text = totalIngresos.ToString("N2");  // Total Devoluciones
                TxtTotalVendidos.Text = (totalSalidas - totalIngresos).ToString("N2");
            }
            else
            {
                TxtTotalIngreso.Text = totalIngresos.ToString("N2"); // Total Entradas
                TxtTotalSalida.Text = totalSalidas.ToString("N2");   // Total Salidas
                TxtTotalVendidos.Text = "---";
            }

            // 5. Ajustar colores y perspectivas visuales
            AjustarModoPerspectivaUI();

            CodigosDataGrid.ItemsSource = null;
            TxtTotalCodigos.Text = "Seleccione un movimiento para auditar";
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (CboProductos.IsDropDownOpen || PopupRazonSocial.IsOpen || PopupUbicacion.IsOpen) return;
                e.Handled = true;
                BtnEjecutar_Click(BtnEjecutar, null);
            }
        }

        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string searchText = textBox.Text;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                CboProductos.IsDropDownOpen = false;
                CboProductos.ItemsSource = null;
                _productoSeleccionadoId = 0;
                return;
            }

            _estaSeleccionando = true;
            int cursorPosition = textBox.CaretIndex;

            var filtrados = _todosLosProductos
                .Where(p => p.Descripcion != null && p.Descripcion.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList();

            CboProductos.ItemsSource = filtrados;
            CboProductos.IsDropDownOpen = filtrados.Any();

            textBox.Text = searchText;
            textBox.CaretIndex = cursorPosition;
            _estaSeleccionando = false;
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;
                _productoSeleccionadoId = producto.Id;

                var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
                if (textBox != null)
                {
                    textBox.Text = producto.Descripcion;
                    textBox.CaretIndex = textBox.Text.Length;
                }
                CboProductos.IsDropDownOpen = false;
                _estaSeleccionando = false;
            }
        }

        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtRazonSocial.IsEnabled || _isUpdatingFromSelection) return;
            string textoBusqueda = TxtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    var sugerencias = await _personaService.BuscarPorRazonSocialAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstRazonSocial.ItemsSource = sugerencias;
                        PopupRazonSocial.IsOpen = true;
                    }
                    else PopupRazonSocial.IsOpen = false;
                }
                catch { PopupRazonSocial.IsOpen = false; }
            }
            else PopupRazonSocial.IsOpen = false;
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true;
                TxtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                                        ? personaSeleccionada.RazonSocial
                                        : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";
                PopupRazonSocial.IsOpen = false;
                LstRazonSocial.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
                AjustarModoPerspectivaUI();
            }
        }

        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtUbicacion.IsEnabled || _isUpdatingFromSelection) return;
            string textoBusqueda = TxtUbicacion.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    var sugerencias = await Task.Run(() => _ubicacionService.BuscarUbicaciones(textoBusqueda));
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstUbicacion.ItemsSource = sugerencias;
                        PopupUbicacion.IsOpen = true;
                    }
                    else PopupUbicacion.IsOpen = false;
                }
                catch { PopupUbicacion.IsOpen = false; }
            }
            else PopupUbicacion.IsOpen = false;
        }

        private void LstUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicacion.SelectedItem is Ubicacion ubiSeleccionada)
            {
                _isUpdatingFromSelection = true;
                TxtUbicacion.Text = ubiSeleccionada.Descripcion;
                PopupUbicacion.IsOpen = false;
                LstUbicacion.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
                AjustarModoPerspectivaUI();
            }
        }

        private void RbFiltro_Click(object sender, RoutedEventArgs e)
        {
            // 🌟 Permitimos que selecciones libremente la opción sin ejecutar nada aún.
            // La consulta se lanzará únicamente cuando presiones el botón "🚀 Ejecutar Consulta".
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_isCargando) return;
            if (_productoSeleccionadoId == 0) { MessageBox.Show("Seleccione un producto maestro para auditar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Button btn = sender as Button;
            string txtOriginal = btn?.Content?.ToString() ?? "Ejecutar";

            _isCargando = true;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ Consultando..."; }
            Mouse.OverrideCursor = Cursors.Wait;


            try
            {
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                string? filtroRazon = ChkRazonSocial.IsChecked == true ? TxtRazonSocial.Text : null;
                string? filtroUbicacion = ChkUbicacion.IsChecked == true ? TxtUbicacion.Text : null;

                // 🌟 EVALUAR EL ID DE CATEGORÍA SEGÚN EL RADIOBUTTON
                int? categoriaIdFiltro = null;
                if (RbGuia != null && RbGuia.IsChecked == true)
                {
                    categoriaIdFiltro = 1; // 1 = Libros Guía
                }
                else if (RbVenta != null && RbVenta.IsChecked == true)
                {
                    categoriaIdFiltro = 2; // 2 = Libros Venta
                }
                // 🌟 CONSULTA CON LA CATEGORÍA A NIVEL DE CÓDIGOS DE BASE DE DATOS
                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    _productoSeleccionadoId, desde, hasta, filtroRazon, filtroUbicacion, categoriaIdFiltro);

                _todosLosMovimientosRaw = reporte.Movimientos;
                _todosLosCodigos = reporte.Codigos;

                AjustarModoPerspectivaUI();
                RefrescarVistaMovimientos();
            }
            catch (Exception ex) { MessageBox.Show("Error al ejecutar consulta: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                _isCargando = false;
                Mouse.OverrideCursor = null;
                if (btn != null) { btn.IsEnabled = true; btn.Content = txtOriginal; }
            }
        }

        

        // 🌟 Muestra los códigos que le quedan actualmente en su poder al hacer clic en la tarjeta de saldo
        private void CardSaldoEnPoder_Click(object sender, MouseButtonEventArgs e)
        {
            if (_productoSeleccionadoId == 0) return;

            bool hayFiltroEntidad = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            string entidadSeleccionada = hayFiltroEntidad
                ? (!string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text : TxtRazonSocial.Text)
                : "ALMACÉN CENTRAL";

            // Lógica inteligente: 
            // Si es una entidad (Promotora), filtramos los códigos que salieron hacia ella 
            // y que no figuran en ningún movimiento de devolución posterior.
            // O de forma práctica para tu inventario físico con los datos que ya cargó el reporte:

            var codigosEnPoder = _todosLosCodigos
                .GroupBy(c => c.Codigo)
                .Where(g => g.Count() % 2 != 0) // Si un código tiene salidas impares sin contraparte de devolución equilibrada
                .Select(g => g.First())
                .ToList();

            CodigosDataGrid.ItemsSource = null;
            CodigosDataGrid.ItemsSource = codigosEnPoder;

            TxtTotalCodigos.Text = $"📦 Hay {codigosEnPoder.Count} códigos físicos pendientes en poder de: {entidadSeleccionada}";
        }
        
        private void MovimientosDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (MovimientosDataGrid.CurrentCell.Column != null)
            {
                int columnIndex = MovimientosDataGrid.Columns.IndexOf(MovimientosDataGrid.CurrentCell.Column);

                // Si la columna seleccionada es la 6 (TOTAL EN PODER)
                if (columnIndex == 6 && MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem itemSeleccionado)
                {
                    MostrarCodigosPendientesEnPoder();
                }
            }
        }

        private void MostrarCodigosPendientesEnPoder()
        {
            if (_productoSeleccionadoId == 0) return;

            // Obtenemos todos los códigos que salieron (Salidas/Entregados) menos los que ya volvieron (Devoluciones)
            // Agrupamos por código físico para ver su balance actual
            var balanceCodigos = _todosLosCodigos
                .GroupBy(c => c.Codigo)
                .Select(g => new {
                    CodigoItem = g.First(),
                    // Si el código tiene un número impar de movimientos o su última operación fue salida sin retorno
                    UltimoMovimiento = g.OrderByDescending(x => x.NumeroRegistro).FirstOrDefault()
                })
                .Where(x => x.UltimoMovimiento != null && x.UltimoMovimiento.TipoMovimiento == "SALIDA")
                .Select(x => x.CodigoItem)
                .ToList();

            CodigosDataGrid.ItemsSource = null;
            CodigosDataGrid.ItemsSource = balanceCodigos;
            TxtTotalCodigos.Text = $"📦 Hay {balanceCodigos.Count} códigos físicos pendientes en su poder.";
        }
        private void MovimientosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem movimiento)
            {
                string registroLimpio = movimiento.NumeroRegistro?
                    .Replace("❌ ANULADO - ", "")
                    .Trim() ?? string.Empty;

                bool esIngreso = movimiento.Ingreso > 0;
                string tipoBuscado = esIngreso ? "ENTRADA" : "SALIDA";

                var codigos = _todosLosCodigos
                    .Where(c => (c.NumeroRegistro.Equals(registroLimpio, StringComparison.OrdinalIgnoreCase) ||
                                c.NumeroRegistro.Equals(movimiento.NumeroRegistro, StringComparison.OrdinalIgnoreCase))
                                && c.TipoMovimiento == tipoBuscado)
                    .ToList();

                CodigosDataGrid.ItemsSource = null;
                CodigosDataGrid.ItemsSource = codigos;

                if (movimiento.IsAnulado || movimiento.NumeroRegistro.Contains("ANULADO"))
                {
                    TxtTotalCodigos.Text = $"⚠️ [ANULADO] {codigos.Count} códigos en esta operación.";
                }
                else
                {
                    TxtTotalCodigos.Text = $"Se auditaron {codigos.Count} Códigos Físicos";
                }
            }
        }

        private void MovimientosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem seleccionado)
            {
                string registroLimpio = seleccionado.NumeroRegistro?.Replace("❌ ANULADO - ", "").Trim() ?? string.Empty;
                var partes = registroLimpio.Split('-');

                if (partes.Length >= 2)
                {
                    string serie = partes[0];
                    string numero = partes[1];
                    bool esSalida = seleccionado.Salida > 0;

                    var mainShell = Application.Current.Windows.OfType<MainShell>().FirstOrDefault();
                    if (mainShell == null) return;

                    if (esSalida)
                    {
                        var salidasControl = new SalidasUserControl();
                        mainShell.AbrirPestaña($"Salida {serie}-{numero} (Consulta)", salidasControl);
                        salidasControl.CargarDocumentoParaConsulta(serie, numero); // 🌟 Carga automática
                    }
                    else
                    {
                        var ingresoControl = new IngresoUserControl();
                        mainShell.AbrirPestaña($"Ingreso {serie}-{numero} (Consulta)", ingresoControl);
                        ingresoControl.CargarDocumentoParaConsulta(serie, numero); // 🌟 Carga automática
                    }
                }
            }
        }
        private bool _isFormattingDate = false;
        private void ConfigurarMascaraFecha(DatePicker dp)
        {
            dp.ApplyTemplate();
            if (dp.Template.FindName("PART_TextBox", dp) is TextBox tb)
            {
                tb.MaxLength = 10;
                tb.TextChanged += (s, ev) => {
                    if (_isFormattingDate || ev.Changes.Any(c => c.RemovedLength > 0)) return;
                    _isFormattingDate = true;
                    string n = new string(tb.Text.Where(char.IsDigit).ToArray());
                    if (n.Length >= 2 && n.Length < 4) tb.Text = n.Insert(2, "/");
                    else if (n.Length >= 4) tb.Text = n.Insert(2, "/").Insert(5, "/");
                    tb.CaretIndex = tb.Text.Length;
                    _isFormattingDate = false;
                };
            }
        }
    }
}