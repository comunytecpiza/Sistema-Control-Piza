#nullable enable

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services.Reportes;

namespace AplicativoDeAlmacen.Views
{
    public partial class KardexUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ReporteExcelService _reporteExcel;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;
        private KardexFisicoReporte? _ultimoReporte;

        private bool _estaSeleccionando = false;
        private List<Producto> _todosLosProductos = new List<Producto>();
        private bool _ejecutandoConsulta = false;

        public KardexUserControl()
        {
            this.Language = System.Windows.Markup.XmlLanguage.GetLanguage("es-ES");
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            InitializeComponent();

            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _reporteExcel = new ReporteExcelService();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += KardexUserControl_Loaded;

            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                if (_productoSeleccionadoId != 0 && this.IsVisible && !_ejecutandoConsulta)
                {
                    BtnEjecutarKardex_Click(null, null);
                }
            });

            this.IsVisibleChanged += (s, e) => {
                if (this.IsVisible && (bool)e.NewValue == true && _productoSeleccionadoId != 0 && !_ejecutandoConsulta)
                {
                    BtnEjecutarKardex_Click(null, null);
                }
            };
        }

        private async void KardexUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_todosLosProductos == null || !_todosLosProductos.Any())
                {
                    var dbProductos = await _productoService.ObtenerTodosAsync();
                    _todosLosProductos = dbProductos.ToList();
                }

                // Configuración de validación limpia y formato nativo
                ConfigurarDatePickerNativo(DpDesde);
                ConfigurarDatePickerNativo(DpHasta);

                CboProductos.PreviewKeyDown -= Filtros_PreviewKeyDown;
                DpDesde.PreviewKeyDown -= Filtros_PreviewKeyDown;
                DpHasta.PreviewKeyDown -= Filtros_PreviewKeyDown;

                CboProductos.PreviewKeyDown += Filtros_PreviewKeyDown;
                DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
                DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la pantalla inicial: " + ex.Message);
            }
        }

        private void ConfigurarDatePickerNativo(DatePicker dp)
        {
            if (dp == null) return;

            // Manejo de entrada manual permitiendo formatos con / o -
            dp.DateValidationError += (s, ev) =>
            {
                if (DateTime.TryParse(ev.Text, new System.Globalization.CultureInfo("es-ES"), System.Globalization.DateTimeStyles.None, out DateTime fechaParsed))
                {
                    dp.SelectedDate = fechaParsed;
                    ev.ThrowException = false;
                }
            };
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // Si se presionó enter sobre un DatePicker, asegurar parseo de texto manual
                if (sender is DatePicker dp)
                {
                    var textBox = dp.Template.FindName("PART_TextBox", dp) as TextBox;
                    if (textBox != null && DateTime.TryParse(textBox.Text, new System.Globalization.CultureInfo("es-ES"), System.Globalization.DateTimeStyles.None, out DateTime f))
                    {
                        dp.SelectedDate = f;
                    }
                }

                var txtProd = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
                string textoEscrito = txtProd?.Text?.Trim() ?? CboProductos.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(textoEscrito))
                {
                    if (int.TryParse(textoEscrito, out int idNum))
                    {
                        var prodPorId = _todosLosProductos.FirstOrDefault(p => p.Id == idNum);
                        if (prodPorId != null)
                        {
                            _estaSeleccionando = true;
                            _productoSeleccionadoId = prodPorId.Id;
                            if (txtProd != null)
                            {
                                txtProd.Text = prodPorId.Descripcion;
                                txtProd.CaretIndex = txtProd.Text.Length;
                            }
                            _estaSeleccionando = false;
                        }
                    }
                    else if (_productoSeleccionadoId == 0 && CboProductos.ItemsSource is IEnumerable<Producto> lista && lista.Any())
                    {
                        var primerProd = lista.First();
                        _estaSeleccionando = true;
                        _productoSeleccionadoId = primerProd.Id;
                        if (txtProd != null)
                        {
                            txtProd.Text = primerProd.Descripcion;
                            txtProd.CaretIndex = txtProd.Text.Length;
                        }
                        _estaSeleccionando = false;
                    }
                }

                CboProductos.IsDropDownOpen = false;
                BtnEjecutarKardex_Click(null, null);
            }
        }

        // ====================================================================
        // 🌟 BUSCADOR SIN CORTE DE ESPACIOS
        // ====================================================================
        private void CboProductos_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;

            var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (textBox == null) return;

            string rawText = textBox.Text ?? string.Empty;
            string searchText = rawText.Trim();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                CboProductos.IsDropDownOpen = false;
                CboProductos.ItemsSource = null;
                _productoSeleccionadoId = 0;
                return;
            }

            _estaSeleccionando = true;
            int cursorPosition = textBox.CaretIndex;

            bool esNumero = int.TryParse(searchText, out int idBuscado);

            var filtrados = _todosLosProductos
                .Where(p => (esNumero && p.Id == idBuscado) ||
                            p.Id.ToString().StartsWith(searchText) ||
                            (!string.IsNullOrEmpty(p.Descripcion) && p.Descripcion.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(p.Abreviatura) && p.Abreviatura.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => (esNumero && p.Id == idBuscado) ? 0 : 1)
                .ThenBy(p => p.Descripcion)
                .Take(15)
                .ToList();

            CboProductos.ItemsSource = filtrados;
            CboProductos.DisplayMemberPath = "Descripcion";
            CboProductos.IsDropDownOpen = filtrados.Any();

            if (esNumero)
            {
                var matchExacto = _todosLosProductos.FirstOrDefault(p => p.Id == idBuscado);
                if (matchExacto != null)
                {
                    _productoSeleccionadoId = matchExacto.Id;
                }
            }

            textBox.Text = rawText;
            textBox.CaretIndex = Math.Min(cursorPosition, rawText.Length);

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

        private async void BtnEjecutarKardex_Click(object? sender, RoutedEventArgs? e)
        {
            if (_ejecutandoConsulta) return;
            if (_productoSeleccionadoId == 0) return;

            try
            {
                _ejecutandoConsulta = true;
                Mouse.OverrideCursor = Cursors.Wait;

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                _ultimoReporte = await _kardexService.GenerarKardexFisicoAsync(
                    _productoSeleccionadoId,
                    DpDesde.SelectedDate ?? DateTime.Today,
                    DpHasta.SelectedDate ?? DateTime.Today,
                    miAlmacenId);

                var reporte = _ultimoReporte;

                KardexDataGrid.ItemsSource = reporte.Detalles;

                LblTituloIngresos.Text = (miAlmacenId == ALMACEN_CENTRAL_ID)
                    ? "Ingresos / Entradas"
                    : "Ingresos (Transferencias / Entradas)";

                TxtStockInicial.Text = reporte.StockInicial.ToString("N0");
                TxtTotalIngresos.Text = reporte.TotalIngresos.ToString("N0");
                TxtTotalDevoluciones.Text = reporte.TotalDevoluciones.ToString("N0");
                TxtTotalSalidas.Text = reporte.TotalSalidas.ToString("N0");
                TxtSalidasFijas.Text = reporte.SalidasFijas.ToString("N0");
                TxtStockFinal.Text = reporte.StockFinal.ToString("N0");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al generar Kardex", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _ejecutandoConsulta = false;
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (_ultimoReporte == null)
            {
                MessageBox.Show("Primero genere el Kardex.", "Sistema", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _reporteExcel.ExportarKardex(_ultimoReporte);
        }

        public async void CargarKardexDirecto(int productoId, string nombreProducto, DateTime desde, DateTime hasta)
        {
            try
            {
                DpDesde.SelectedDate = desde;
                DpHasta.SelectedDate = hasta;
                _productoSeleccionadoId = productoId;

                var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
                if (textBox != null)
                {
                    textBox.Text = nombreProducto;
                    textBox.CaretIndex = textBox.Text.Length;
                }

                BtnEjecutarKardex_Click(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar el Kárdex directo: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void KardexDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (KardexDataGrid.SelectedItem is KardexFisicoItem filaSeleccionada)
            {
                if (string.IsNullOrWhiteSpace(filaSeleccionada.Registro)) return;

                string[] partes = filaSeleccionada.Registro.Split('-');
                if (partes.Length < 2) return;

                string serie = partes[0].Trim();
                string numero = partes[1].Trim();

                if (Window.GetWindow(this) is IMainWindow mainShell)
                {
                    if (filaSeleccionada.Ingreso > 0)
                    {
                        var vistaIngreso = new IngresoUserControl();
                        vistaIngreso.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📥 Ingreso : {serie}-{numero}(Vista Previa)", vistaIngreso);
                    }
                    else if (filaSeleccionada.Salida > 0)
                    {
                        var vistaSalida = new SalidasUserControl();
                        vistaSalida.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📤 Salida : {serie}-{numero} (Vista Previa)", vistaSalida);
                    }
                }
            }
        }
    }
}