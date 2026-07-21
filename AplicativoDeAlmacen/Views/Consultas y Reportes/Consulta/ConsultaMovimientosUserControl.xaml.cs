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
                if (hayFiltroEntidad)
                {
                    // Perspectiva de la Promotora / Cliente
                    MovimientosDataGrid.Columns[4].Header = "ENTREGADO"; // Columna Salida
                    MovimientosDataGrid.Columns[5].Header = "DEVOLUCIÓN"; // Columna Ingreso
                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Visible; // Total en Poder

                    LblCard1.Text = "Total Entregado (Salidas)";
                    LblCard2.Text = "Total Devoluciones";
                    LblCard3.Text = "Saldo Pendiente en Poder";
                }
                else
                {
                    // Perspectiva del Almacén Central
                    MovimientosDataGrid.Columns[4].Header = "SALEN";
                    MovimientosDataGrid.Columns[5].Header = "ENTRAN";
                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Collapsed;

                    LblCard1.Text = "Total Salidas";
                    LblCard2.Text = "Total Entradas";
                    LblCard3.Text = "Stock Actual en Almacén";
                }
            }
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

                string? filtroRazon = ChkRazonSocial.IsChecked == true ? TxtRazonSocial.Text : null;
                string? filtroUbicacion = ChkUbicacion.IsChecked == true ? TxtUbicacion.Text : null;

                // 🌟 BÚSQUEDA 100% FILTRADA EN SQL (No se mezclan Imprentas ni Almacenes extra)
                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    _productoSeleccionadoId, desde, hasta, filtroRazon, filtroUbicacion);

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

        private void RefrescarVistaMovimientos()
        {
            var movimientos = _todosLosMovimientosRaw.AsEnumerable();

            // 1. FILTRO ANULADOS (Ocultos por defecto)
            bool mostrarAnulados = ChkMostrarAnulados.IsChecked == true;
            if (!mostrarAnulados)
            {
                movimientos = movimientos.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO"));
            }

            // 🌟 2. FILTRO DE TIPO DE OPERACIÓN CORREGIDO
            if (RbGuia != null && RbGuia.IsChecked == true)
            {
                // Solo Guías: Considera documentos donde se registró una Guía de Remisión válida
                movimientos = movimientos.Where(m =>
                    !string.IsNullOrWhiteSpace(m.NumeroGuia) &&
                    !m.NumeroGuia.Equals("0000-0000000") &&
                    !m.NumeroGuia.Equals("000-0000000"));
            }
            else if (RbVenta != null && RbVenta.IsChecked == true)
            {
                // Solo Ventas / Documentos Directos: Sin guía de remisión asociada
                movimientos = movimientos.Where(m =>
                    string.IsNullOrWhiteSpace(m.NumeroGuia) ||
                    m.NumeroGuia.Equals("0000-0000000") ||
                    m.NumeroGuia.Equals("000-0000000"));
            }

            var listaFinal = movimientos.ToList();

            // 3. MATEMÁTICA LÓGICA Y POSITIVA PARA PROMOTORAS (SALIDAS - DEVOLUCIONES)
            decimal saldoAcumulado = 0;
            foreach (var item in listaFinal)
            {
                if (!item.IsAnulado)
                {
                    saldoAcumulado += (item.Salida - item.Ingreso);
                }
                item.SaldoAcumulado = saldoAcumulado;
            }

            MovimientosDataGrid.ItemsSource = null;
            MovimientosDataGrid.ItemsSource = listaFinal;

            // 4. RESUMEN DE TARJETAS
            decimal totalDevoluciones = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Ingreso);
            decimal totalEntregado = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Salida);

            bool hayFiltroEntidad = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            if (hayFiltroEntidad)
            {
                TxtTotalSalida.Text = totalEntregado.ToString("N2");    // Columna Roja: Lo que le diste
                TxtTotalIngreso.Text = totalDevoluciones.ToString("N2"); // Columna Verde: Lo que devolvió
                TxtTotalVendidos.Text = (totalEntregado - totalDevoluciones).ToString("N2"); // Columna Azul: En su poder (Positivo)
            }
            else
            {
                TxtTotalSalida.Text = totalEntregado.ToString("N2");    // Salidas del Almacén
                TxtTotalIngreso.Text = totalDevoluciones.ToString("N2"); // Entradas al Almacén
                TxtTotalVendidos.Text = (totalDevoluciones - totalEntregado).ToString("N2"); // Stock del Almacén
            }

            CodigosDataGrid.ItemsSource = null;
            TxtTotalCodigos.Text = "Seleccione un movimiento para auditar";
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

                    // Notificación o Apertura del movimiento
                    MessageBox.Show($"Abriendo documento de movimiento {serie}-{numero}...", "Abrir Movimiento", MessageBoxButton.OK, MessageBoxImage.Information);
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