using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Core; // Asegúrate de tener este using para el EventBus

namespace AplicativoDeAlmacen.Views
{
    public partial class KardexUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;

        // Bandera para evitar bucles cuando manipulamos la caja de texto por código
        private bool _estaSeleccionando = false;

        // 🌟 Memoria RAM: Aquí guardaremos los productos para buscar a la velocidad de la luz
        private List<Producto> _todosLosProductos = new List<Producto>();

        public KardexUserControl()
        {
            InitializeComponent();

            _kardexService = new KardexService();
            _productoService = new ProductoService();

            // Fechas por defecto (Primer día del mes y Hoy)
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += KardexUserControl_Loaded;

            // ====================================================================
            // 🌟 ACTUALIZACIÓN EN TIEMPO REAL (REACTIVIDAD)
            // ====================================================================

            // 1. Si alguien guarda un ingreso/salida, actualizamos el Kardex automáticamente
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                // Solo recargamos si hay un producto seleccionado y la pantalla se está viendo
                if (_productoSeleccionadoId != 0 && this.IsVisible)
                {
                    BtnEjecutarKardex_Click(null, null);
                }
            });

            // 2. Si regresas a esta pestaña después de estar en otra, traemos datos frescos
            this.IsVisibleChanged += (s, e) => {
                if (this.IsVisible && (bool)e.NewValue == true && _productoSeleccionadoId != 0)
                {
                    BtnEjecutarKardex_Click(null, null);
                }
            };
        }

        private async void KardexUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Descargamos TODOS los productos a la memoria solo 1 vez al abrir la pantalla
                var dbProductos = await _productoService.ObtenerTodosAsync();
                _todosLosProductos = dbProductos.ToList();

                // 🌟 MÁSCARA: Activamos la máscara en los calendarios
                ConfigurarMascaraFecha(DpDesde);
                ConfigurarMascaraFecha(DpHasta);

                // 🌟 TECLADO: Escuchamos la tecla Enter en los filtros
                CboProductos.PreviewKeyDown += Filtros_PreviewKeyDown;
                DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
                DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la pantalla inicial: " + ex.Message);
            }
        }

        // ====================================================================
        // 🌟 ATAJO DE TECLADO (Presionar ENTER para buscar)
        // ====================================================================
        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Si la lista del combo está desplegada, dejamos que el Enter sirva para elegir el producto
                if (CboProductos.IsDropDownOpen) return;

                // Consumimos el evento y disparamos la búsqueda mágicamente
                e.Handled = true;
                BtnEjecutarKardex_Click(null, null);
            }
        }

        // ====================================================================
        // EVENTO 1: Cuando el usuario escribe (Filtro ultrarrápido en RAM)
        // ====================================================================
        private void CboProductos_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;

            // Buscamos la caja de texto real que está dentro del ComboBox
            var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (textBox == null) return;

            string searchText = textBox.Text;

            // Si el buscador está vacío, limpiamos la pantalla
            if (string.IsNullOrWhiteSpace(searchText))
            {
                CboProductos.IsDropDownOpen = false;
                CboProductos.ItemsSource = null;
                _productoSeleccionadoId = 0;
                return;
            }

            // Cerramos la puerta para evitar un ciclo infinito
            _estaSeleccionando = true;

            // EL TRUCO DE UX: Guardamos dónde está el cursor
            int cursorPosition = textBox.CaretIndex;

            // Filtramos en memoria RAM limitando a 3 resultados
            var filtrados = _todosLosProductos
                .Where(p => p.Descripcion != null &&
                            p.Descripcion.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            // Actualizamos la lista visible
            CboProductos.ItemsSource = filtrados;
            CboProductos.DisplayMemberPath = "Descripcion";
            CboProductos.IsDropDownOpen = filtrados.Any();

            // EL TRUCO DE UX: Restauramos el texto y la posición del cursor
            textBox.Text = searchText;
            textBox.CaretIndex = cursorPosition;

            // Abrimos la puerta
            _estaSeleccionando = false;
        }

        // ====================================================================
        // EVENTO 2: Cuando el usuario hace clic en un resultado de la lista
        // ====================================================================
        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;

                // Guardamos el ID real para el Kardex
                _productoSeleccionadoId = producto.Id;

                // Modificamos el texto interno visualmente
                var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
                if (textBox != null)
                {
                    textBox.Text = producto.Descripcion;
                    textBox.CaretIndex = textBox.Text.Length;
                }

                // Cerramos la lista desplegable
                CboProductos.IsDropDownOpen = false;

                _estaSeleccionando = false;
            }
        }

        private async void BtnEjecutarKardex_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Seleccione un producto válido de la lista antes de ejecutar la consulta.", "Kardex Físico", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Cambiamos temporalmente el cursor al reloj de arena
                Mouse.OverrideCursor = Cursors.Wait;

                // Ejecutamos el servicio del Kardex multimotor con rango de fechas
                var reporte = await _kardexService.GenerarKardexFisicoAsync(
                        _productoSeleccionadoId,
                        DpDesde.SelectedDate ?? DateTime.Today,
                        DpHasta.SelectedDate ?? DateTime.Today);

                // Llenamos la tabla del DataGrid de forma directa
                KardexDataGrid.ItemsSource = reporte.Detalles;

                // Actualizamos los cuadros de resumen (Asegurando formato de 2 decimales)
                TxtTotalIngresos.Text = reporte.TotalIngresos.ToString("N2");
                TxtTotalDevIngresos.Text = reporte.TotalDevIngresos.ToString("N2");
                TxtTotalSalidas.Text = reporte.TotalSalidas.ToString("N2");
                TxtTotalDevSalidas.Text = reporte.TotalDevSalidas.ToString("N2");
                TxtStockFinal.Text = reporte.StockFinal.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al generar Kardex", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null; // Restauramos el cursor
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

                    // Dejamos borrar tranquilamente
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