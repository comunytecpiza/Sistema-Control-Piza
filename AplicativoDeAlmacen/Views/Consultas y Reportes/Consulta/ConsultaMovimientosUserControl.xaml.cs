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
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;

        // Servicios reales que me pasaste
        private readonly PersonaComercialService _personaService;
        private readonly UbicacionService _ubicacionService;

        private int _productoSeleccionadoId;
        private bool _estaSeleccionando;
        private bool _isUpdatingFromSelection = false;
        private bool _isCargando = false;

        private List<ConsultaCodigoItem> _todosLosCodigos;
        private List<Producto> _todosLosProductos = new List<Producto>();

        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _personaService = new PersonaComercialService();
            _ubicacionService = new UbicacionService();

            _todosLosCodigos = new List<ConsultaCodigoItem>();
            MovimientosDataGrid.LoadingRow += MovimientosDataGrid_LoadingRow;
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;
        }

        private void MovimientosDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is ConsultaMovimientoItem movimiento)
            {
                if (movimiento.IsAnulado || (movimiento.NumeroRegistro != null && movimiento.NumeroRegistro.Contains("ANULADO")))
                {
                    e.Row.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
                    e.Row.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                    e.Row.FontStyle = FontStyles.Italic;
                    e.Row.ToolTip = "Esta operación fue ANULADA y sus saldos fueron ignorados.";
                }
                else
                {
                    e.Row.Background = Brushes.White;
                    e.Row.Foreground = Brushes.Black;
                    e.Row.FontStyle = FontStyles.Normal;
                    e.Row.ToolTip = null;
                }
            }
        }
        private async void Control_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cargar RAM solo para Productos (es más rápido así para el maestro)
                var dbProductos = await _productoService.ObtenerTodosAsync();
                _todosLosProductos = dbProductos.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar los catálogos base: " + ex.Message);
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




        // ====================================================================
        // BÚSQUEDA DE PRODUCTOS (Se mantiene en RAM)
        // ====================================================================
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

        // ====================================================================
        // MAGIA DEL COMPAÑERO: RAZÓN SOCIAL (Con PersonaComercialService)
        // ====================================================================
        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtRazonSocial.IsEnabled || _isUpdatingFromSelection) return;

            string textoBusqueda = TxtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    // Usa el método async que tienes en tu PersonaComercialService
                    var sugerencias = await _personaService.BuscarPorRazonSocialAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstRazonSocial.ItemsSource = sugerencias;
                        PopupRazonSocial.IsOpen = true;
                    }
                    else
                    {
                        PopupRazonSocial.IsOpen = false;
                    }
                }
                catch { PopupRazonSocial.IsOpen = false; }
            }
            else
            {
                PopupRazonSocial.IsOpen = false;
            }
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true;

                // Asignamos el texto al TextBox y cerramos el popup
                TxtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                                        ? personaSeleccionada.RazonSocial
                                        : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";

                PopupRazonSocial.IsOpen = false;
                LstRazonSocial.SelectedIndex = -1;

                _isUpdatingFromSelection = false;
            }
        }

        // ====================================================================
        // MAGIA DEL COMPAÑERO: UBICACIÓN (Con UbicacionService)
        // ====================================================================
        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtUbicacion.IsEnabled || _isUpdatingFromSelection) return;

            string textoBusqueda = TxtUbicacion.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    // 🌟 SOLUCIÓN: Usamos el método síncrono que tienes, pero sin congelar la UI
                    var sugerencias = await Task.Run(() => _ubicacionService.BuscarUbicaciones(textoBusqueda));

                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstUbicacion.ItemsSource = sugerencias;
                        PopupUbicacion.IsOpen = true;
                    }
                    else
                    {
                        PopupUbicacion.IsOpen = false;
                    }
                }
                catch { PopupUbicacion.IsOpen = false; }
            }
            else
            {
                PopupUbicacion.IsOpen = false;
            }
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
            }
        }

        // ====================================================================
        // EJECUCIÓN DINÁMICA
        // ====================================================================
        private void RbFiltro_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId != 0 && this.IsLoaded) BtnEjecutar_Click(BtnEjecutar, null);
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

                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(_productoSeleccionadoId, desde, hasta);
                var movimientos = reporte.Movimientos.AsEnumerable();

                if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
                    movimientos = movimientos.Where(m => m.RazonSocialUbicacion.ToLower().Contains(TxtRazonSocial.Text.ToLower()));

                if (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text))
                    movimientos = movimientos.Where(m => m.RazonSocialUbicacion.ToLower().Contains(TxtUbicacion.Text.ToLower()));

                if (RbGuia != null && RbGuia.IsChecked == true) movimientos = movimientos.Where(m => m.NumeroRegistro.Contains("-") || string.IsNullOrWhiteSpace(m.NumeroRegistro.Replace("-", "")));
                else if (RbVenta != null && RbVenta.IsChecked == true)
                {
                    movimientos = movimientos.Where(m => !m.NumeroRegistro.Contains("-") && !string.IsNullOrWhiteSpace(m.NumeroRegistro.Replace("-", "")));
                }

                var listaFinal = movimientos.ToList();
                MovimientosDataGrid.ItemsSource = listaFinal;

                // 🌟 CALCULAMOS TOTALES IGNORANDO LAS FILAS ANULADAS (Como en Valorizado)
                decimal totalEntradas = listaFinal.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO")).Sum(m => m.Ingreso);
                decimal totalSalidas = listaFinal.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO")).Sum(m => m.Salida);

                TxtTotalIngreso.Text = totalEntradas.ToString("N2");
                TxtTotalSalida.Text = totalSalidas.ToString("N2");
                TxtTotalVendidos.Text = totalSalidas.ToString("N2"); // Ventas Netas

                _todosLosCodigos = reporte.Codigos;
                CodigosDataGrid.ItemsSource = null;
                TxtTotalCodigos.Text = "Seleccione un movimiento para auditar códigos";
            }
            catch (Exception ex) { MessageBox.Show("Error al ejecutar auditoría: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                _isCargando = false;
                Mouse.OverrideCursor = null;
                if (btn != null) { btn.IsEnabled = true; btn.Content = txtOriginal; }
            }
        }

        private void MovimientosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem movimiento)
            {
                string registroLimpio = movimiento.NumeroRegistro?
                    .Replace("❌ ANULADO - ", "")
                    .Trim() ?? string.Empty;

                // 🌟 Determinamos si la fila seleccionada es Entrada o Salida
                bool esIngreso = movimiento.Ingreso > 0;
                string tipoBuscado = esIngreso ? "ENTRADA" : "SALIDA";

                // 🛡️ FILTRADO EXACTO: Comprobante + Tipo de Movimiento
                var codigos = _todosLosCodigos
                    .Where(c => (c.NumeroRegistro.Equals(registroLimpio, StringComparison.OrdinalIgnoreCase) ||
                                c.NumeroRegistro.Equals(movimiento.NumeroRegistro, StringComparison.OrdinalIgnoreCase))
                                && c.TipoMovimiento == tipoBuscado)
                    .ToList();

                CodigosDataGrid.ItemsSource = null;
                CodigosDataGrid.ItemsSource = codigos;

                if (movimiento.IsAnulado || movimiento.NumeroRegistro.Contains("ANULADO"))
                {
                    TxtTotalCodigos.Text = $"⚠️ [ANULADO] Se registran {codigos.Count} códigos en el historial de esta operación.";
                }
                else
                {
                    TxtTotalCodigos.Text = $"Se auditaron {codigos.Count} Códigos Físicos en esta operación";
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