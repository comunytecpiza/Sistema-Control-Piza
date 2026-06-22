using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;
        private bool _estaSeleccionando;
        private bool _isCargando = false;

        private List<ConsultaCodigoItem> _todosLosCodigos;

        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _todosLosCodigos = new List<ConsultaCodigoItem>();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;
        }

        private void Control_Loaded(object sender, RoutedEventArgs e)
        {
            var txt = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (txt != null) txt.TextChanged += TxtProducto_TextChanged;

            ConfigurarMascaraFecha(DpDesde);
            ConfigurarMascaraFecha(DpHasta);

            // Vinculamos Enter a los filtros
            CboProductos.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        // --- MÉTODOS QUE FALTABAN PARA CORREGIR ERRORES CS1061 ---

        private void ChkFiltros_Click(object sender, RoutedEventArgs e)
        {
            // Lógica para habilitar/deshabilitar los campos de texto según el checkbox
            if (TxtRazonSocial != null) TxtRazonSocial.IsEnabled = ChkRazonSocial.IsChecked == true;
            if (TxtUbicacion != null) TxtUbicacion.IsEnabled = ChkUbicacion.IsChecked == true;
        }

        private void MovimientosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem movimiento)
            {
                // Filtramos los códigos de la memoria secreta
                var codigos = _todosLosCodigos.Where(c => c.NumeroRegistro == movimiento.NumeroRegistro).ToList();
                CodigosDataGrid.ItemsSource = codigos;
                TxtTotalCodigos.Text = $"Se auditaron {codigos.Count} Códigos Físicos en esta operación";
            }
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                // Llamamos a ejecutar pasando null, ya que nuestro BtnEjecutar_Click maneja el null con 'sender as Button'
                BtnEjecutar_Click(BtnEjecutar_Click, null);
            }
        }

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;
            try
            {
                string texto = ((TextBox)sender).Text;
                if (string.IsNullOrWhiteSpace(texto)) { CboProductos.ItemsSource = null; _productoSeleccionadoId = 0; return; }

                var productos = await _productoService.BuscarProductos(texto);
                CboProductos.ItemsSource = productos;
                CboProductos.IsDropDownOpen = productos != null && productos.Count > 0;
            }
            catch { }
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;
                _productoSeleccionadoId = producto.Id;
                CboProductos.Text = producto.Descripcion;
                CboProductos.IsDropDownOpen = false;
                _estaSeleccionando = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_isCargando) return;
            if (_productoSeleccionadoId == 0) { MessageBox.Show("Seleccione un producto."); return; }

            Button btn = sender as Button;
            string txtOriginal = btn?.Content?.ToString() ?? "Ejecutar";

            _isCargando = true;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ Cargando..."; }
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(_productoSeleccionadoId, desde, hasta);
                var movimientos = reporte.Movimientos.AsEnumerable();

                if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
                    movimientos = movimientos.Where(m => m.RazonSocialUbicacion.ToLower().Contains(TxtRazonSocial.Text.ToLower()));

                // RadioButtons (Asegúrate de que RbGuia y RbVenta existan en el XAML)
                if (RbGuia != null && RbGuia.IsChecked == true) movimientos = movimientos.Where(m => m.NumeroRegistro.Contains("-"));
                else if (RbVenta != null && RbVenta.IsChecked == true) movimientos = movimientos.Where(m => !m.NumeroRegistro.Contains("-"));

                MovimientosDataGrid.ItemsSource = movimientos.ToList();
                _todosLosCodigos = reporte.Codigos;
                CodigosDataGrid.ItemsSource = null;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally
            {
                _isCargando = false;
                Mouse.OverrideCursor = null;
                if (btn != null) { btn.IsEnabled = true; btn.Content = txtOriginal; }
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