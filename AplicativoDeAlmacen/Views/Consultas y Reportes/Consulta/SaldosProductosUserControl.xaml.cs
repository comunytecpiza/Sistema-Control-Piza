using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views
{
    public partial class SaldosProductosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private List<SaldoProductoItem> _todosLosSaldos;
        private readonly ReporteExcelService _reporteExcel;
        // 🌟 Candado de UX para evitar "Doble Clic"
        private bool _isCargando = false;

        public SaldosProductosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _todosLosSaldos = new List<SaldoProductoItem>();
            _reporteExcel = new ReporteExcelService();
            // Reactividad: Si alguien mueve inventario, esta pantalla se actualiza sola
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                if (this.IsVisible) BtnEjecutar_Click(null, null);
            });

            // Fechas iniciales por defecto (Desde inicio de año hasta hoy)
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += SaldosProductosUserControl_Loaded;
        }

        private void SaldosProductosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 🌟 MÁSCARA: Activamos el formato inteligente en las fechas (XX/XX/XXXX)
            ConfigurarMascaraFecha(DpDesde);
            ConfigurarMascaraFecha(DpHasta);

            // 🌟 TECLADO: Escuchamos el ENTER en los filtros
            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtFiltro.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        // ====================================================================
        // 🌟 ATAJO DE TECLADO (Presionar ENTER para buscar)
        // ====================================================================
        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnEjecutar_Click(null, null); // Simula el clic en el botón Ejecutar
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_isCargando) return;

            Button btnEjecutar = sender as Button;
            string textoOriginal = btnEjecutar?.Content?.ToString() ?? "Ejecutar";

            try
            {
                _isCargando = true;
                Mouse.OverrideCursor = Cursors.Wait;

                if (btnEjecutar != null)
                {
                    btnEjecutar.IsEnabled = false;
                    btnEjecutar.Content = "⏳ Cargando...";
                }

                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                // 🌟 CORRECCIÓN CRÍTICA: Llamar a ObtenerSaldosYMovimientosAsync con el Almacén activo
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                _todosLosSaldos = await _kardexService.ObtenerSaldosYMovimientosAsync(desde, hasta, miAlmacenId);

                // Renderizar la tabla y aplicar filtros de texto si los hay
                FiltrarData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al obtener saldos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btnEjecutar != null)
                {
                    btnEjecutar.IsEnabled = true;
                    btnEjecutar.Content = textoOriginal;
                }

                Mouse.OverrideCursor = null;
                _isCargando = false;
            }
        }


        private async void BtnImprimir_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                if (_todosLosSaldos == null
                    || !_todosLosSaldos.Any())
                {
                    MessageBox.Show(

                        "No existen registros para exportar.",

                        "Aviso",

                        MessageBoxButton.OK,

                        MessageBoxImage.Information);

                    return;
                }

                await _reporteExcel
                    .ExportarSaldosProductosAsync(

                        _todosLosSaldos);

            }
            catch (Exception ex)
            {
                MessageBox.Show(

                    ex.Message,

                    "Error",

                    MessageBoxButton.OK,

                    MessageBoxImage.Error);
            }
        }

        private void TxtFiltro_TextChanged(object sender, TextChangedEventArgs e)
        {
            FiltrarData();
        }

        private void FiltrarData()
        {
            if (_todosLosSaldos == null || !_todosLosSaldos.Any()) return;

            string filtro = TxtFiltro.Text.ToLower().Trim();

            
            if (string.IsNullOrWhiteSpace(filtro))
            {
                SaldosDataGrid.ItemsSource = null;
                SaldosDataGrid.ItemsSource = _todosLosSaldos;
            }
            else
            {
                // Filtramos por descripción o código al vuelo en memoria RAM
                var filtrados = _todosLosSaldos.Where(p =>
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(filtro)) ||
                    (p.Codigo != null && p.Codigo.ToLower().Contains(filtro))
                ).ToList();

                SaldosDataGrid.ItemsSource = null;
                SaldosDataGrid.ItemsSource = filtrados;
            }
        }

        // ====================================================================
        // 🌟 MÁSCARA INTELIGENTE PARA FECHAS (Autocompleta las diagonales / )
        // ====================================================================
        private bool _isFormattingDate = false;

        private void ConfigurarMascaraFecha(DatePicker datePicker)
        {
            datePicker.ApplyTemplate();

            if (datePicker.Template.FindName("PART_TextBox", datePicker) is TextBox textBox)
            {
                textBox.MaxLength = 10;
                textBox.TextChanged += (s, ev) =>
                {
                    if (_isFormattingDate) return;

                    var tb = s as TextBox;
                    if (tb == null) return;

                    if (ev.Changes.Any(c => c.RemovedLength > 0 && c.AddedLength == 0)) return;

                    _isFormattingDate = true;

                    string numeros = new string(tb.Text.Where(char.IsDigit).ToArray());

                    if (numeros.Length >= 2 && numeros.Length < 4)
                    {
                        tb.Text = numeros.Insert(2, "/");
                        tb.CaretIndex = tb.Text.Length;
                    }
                    else if (numeros.Length >= 4 && numeros.Length <= 8)
                    {
                        tb.Text = numeros.Insert(2, "/").Insert(5, "/");
                        tb.CaretIndex = tb.Text.Length;
                    }

                    _isFormattingDate = false;
                };
            }
        }
    }
}