using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Reporte
{
    public partial class HistorialPorCodigoUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private bool _necesitaRecargar = false;
        private readonly ReporteExcelService _reporteExcelService;

        private int _productoIdSeleccionado = 0;
        private int _categoriaProductoIdSeleccionada = 0; // 1 = Libro Guía, 2 = Libro Venta

        public HistorialPorCodigoUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _reporteExcelService = new ReporteExcelService();
            TxtCodigoEscaneado.IsEnabled = false;
            TxtProducto.Focus();

            DgHistorial.LoadingRow += DgHistorial_LoadingRow;

            Loaded += (s, e) => {
                Dispatcher.BeginInvoke(new Action(() => {
                    TxtProducto.Focus();
                    TxtProducto.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // 🌟 SUSCRIPCIÓN AL EVENTBUS
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(async () => {
                if (this.IsVisible && _productoIdSeleccionado > 0 && !string.IsNullOrWhiteSpace(TxtCodigoEscaneado.Text))
                {
                    await ProcesarLecturaAsync();
                }
                else
                {
                    _necesitaRecargar = true;
                }
            });

            this.IsVisibleChanged += async (s, e) => {
                if (this.IsVisible && _necesitaRecargar && _productoIdSeleccionado > 0 && !string.IsNullOrWhiteSpace(TxtCodigoEscaneado.Text))
                {
                    _necesitaRecargar = false;
                    await ProcesarLecturaAsync();
                }
            };
        }

        private void DgHistorial_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is KardexFisicoItem item)
            {
                if (item.IsAnulado || (item.Tipo != null && item.Tipo.ToUpperInvariant().Contains("ANULADO")))
                {
                    e.Row.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                    e.Row.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    e.Row.FontStyle = FontStyles.Italic;
                    e.Row.ToolTip = "Movimiento ANULADO. Sin efecto sobre el stock.";
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

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;
            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;

            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;
            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;

            TxtCodigoEscaneado.IsEnabled = false;

            string busqueda = TxtProducto.Text.Trim();

            if (busqueda.Length >= 1)
            {
                List<Producto> resultados;

                // 🔍 Si es numérico, busca por ID de producto (1 al 11...)
                if (int.TryParse(busqueda, out int idProd))
                {
                    var prodPorId = await _productoService.ObtenerPorIdAsync(idProd);
                    resultados = prodPorId != null
                        ? new List<Producto> { prodPorId }
                        : await _productoService.BuscarProductosPorTextoAsync(busqueda);
                }
                else if (busqueda.Length >= 2)
                {
                    resultados = await _productoService.BuscarProductosPorTextoAsync(busqueda);
                }
                else
                {
                    PopupResultados.IsOpen = false;
                    return;
                }

                LbProducto.ItemsSource = resultados;
                PopupResultados.IsOpen = resultados != null && resultados.Any();
            }
            else
            {
                PopupResultados.IsOpen = false;
            }
        }

        private void LbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LbProducto.SelectedItem is Producto p)
            {
                _productoIdSeleccionado = p.Id;

                TxtProducto.TextChanged -= TxtProducto_TextChanged;
                TxtProducto.Text = p.Descripcion;
                TxtProducto.TextChanged += TxtProducto_TextChanged;

                TxtIdProducto.Text = p.Id.ToString();
                TxtAbreviatura.Text = string.IsNullOrWhiteSpace(p.Abreviatura) ? "" : p.Abreviatura;
                TxtColeccion.Text = "";

                PopupResultados.IsOpen = false;

                // Restricción inteligente por nombre del producto
                string abrevUpper = p.Abreviatura?.Trim().ToUpperInvariant() ?? "";
                if (abrevUpper.EndsWith("-V") || abrevUpper.EndsWith(" V"))
                {
                    _categoriaProductoIdSeleccionada = 2;
                    RbLibroVenta.IsChecked = true;
                    RbLibroGuia.IsEnabled = false;
                    RbLibroVenta.IsEnabled = true;
                }
                else if (abrevUpper.EndsWith("-G") || abrevUpper.EndsWith(" G"))
                {
                    _categoriaProductoIdSeleccionada = 1;
                    RbLibroGuia.IsChecked = true;
                    RbLibroGuia.IsEnabled = true;
                    RbLibroVenta.IsEnabled = false;
                }
                else
                {
                    _categoriaProductoIdSeleccionada = 0;
                    RbLibroGuia.IsEnabled = true;
                    RbLibroVenta.IsEnabled = true;
                    RbLibroGuia.IsChecked = false;
                    RbLibroVenta.IsChecked = false;
                }

                HabilitarCampoCodigo();
            }
        }

        private void RbLibroGuia_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 1;
            HabilitarCampoCodigo();
        }

        private void RbLibroVenta_Checked(object sender, RoutedEventArgs e)
        {
            _categoriaProductoIdSeleccionada = 2;
            HabilitarCampoCodigo();
        }

        private void HabilitarCampoCodigo()
        {
            TxtCodigoEscaneado.IsEnabled = true;
            TxtCodigoEscaneado.Focus();
        }

        // 🌟 BOTÓN LECTOR: Abre la lectora modal para buscar 1 código por lectura
        private async void BtnLector_Click(object sender, RoutedEventArgs e)
        {
            if (_productoIdSeleccionado == 0)
            {
                MessageBox.Show("Seleccione un producto primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione si es Libro Guía o Libro Venta.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Creamos una colección temporal para la LectorWindow
            var itemsTemp = new ObservableCollection<ItemGridDTO>();
            var lectorModal = new LectorWindow(itemsTemp)
            {
                Owner = Window.GetWindow(this)
            };

            lectorModal.ShowDialog();

            // Al cerrar la lectora, recuperamos el código si se leyó alguno
            if (itemsTemp.Any() && itemsTemp.First().Codigos.Any())
            {
                string codigoLeido = itemsTemp.First().Codigos.First().CodigoString;
                TxtCodigoEscaneado.Text = codigoLeido;
                await ProcesarLecturaAsync();
            }
        }

        // 🌟 1. Evento para recargar la tabla al marcar/desmarcar el CheckBox
        private async void ChkMostrarAnulados_Click(object sender, RoutedEventArgs e)
        {
            if (_productoIdSeleccionado > 0 && !string.IsNullOrWhiteSpace(TxtCodigoEscaneado.Text))
            {
                await ProcesarLecturaAsync();
            }
        }

        // 🌟 2. Actualización de ProcesarLecturaAsync pasando el estado del CheckBox
        private async Task ProcesarLecturaAsync()
        {
            string codigo = TxtCodigoEscaneado.Text.Trim();

            if (_productoIdSeleccionado == 0)
            {
                MessageBox.Show("Primero debe seleccionar un producto.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaProductoIdSeleccionada == 0)
            {
                MessageBox.Show("Seleccione si es Libro Guía o Libro Venta.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(codigo)) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                int anoActual = DateTime.Now.Year;
                string tipoTexto = _categoriaProductoIdSeleccionada == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                bool incluirAnulados = ChkMostrarAnulados.IsChecked ?? false;

                // 🌟 1. CONSULTA DE LA COLECCIÓN REAL VINCULADA AL CÓDIGO
                string nombreColeccionReal = await _kardexService.ObtenerNombreColeccionCodigoAsync(
                    _productoIdSeleccionado,
                    codigo,
                    _categoriaProductoIdSeleccionada
                );

                // 🌟 2. ASIGNACIÓN EN LA CAJA DE TEXTO (Si no la encuentra por algún motivo, coloca un respaldo)
                TxtColeccion.Text = string.IsNullOrWhiteSpace(nombreColeccionReal)
                ? $"C{anoActual} / {tipoTexto}"
                : nombreColeccionReal;

                // 🌟 3. CONSULTA DEL HISTORIAL DE MOVIMIENTOS
                var historial = await _kardexService.ObtenerHistorialCompletoPorCodigoAsync(
                    _productoIdSeleccionado,
                    codigo,
                    _categoriaProductoIdSeleccionada,
                    miAlmacenId,
                    incluirAnulados
                );

                if (historial != null && historial.Any())
                {
                    DgHistorial.ItemsSource = historial;
                }
                else
                {
                    MessageBox.Show($"No se encontró ningún historial para el código '{codigo}' en este almacén.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    DgHistorial.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al consultar trazabilidad: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }


        // 🌟 3. DOBLE CLIC EN EL HISTORIAL DE TRAZABILIDAD
        private void DgHistorial_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgHistorial.SelectedItem is KardexFisicoItem filaSeleccionada)
            {
                if (string.IsNullOrWhiteSpace(filaSeleccionada.Registro)) return;

                // Limpiamos el prefijo '❌ ANULADO - ' en caso de que esté presente
                string registroLimpio = filaSeleccionada.Registro.Replace("❌ ANULADO - ", "").Trim();

                string[] partes = registroLimpio.Split('-');
                if (partes.Length < 2) return;

                string serie = partes[0].Trim();
                string numero = partes[1].Trim();

                if (Window.GetWindow(this) is IMainWindow mainShell)
                {
                    if (filaSeleccionada.IngresoNormal > 0)
                    {
                        // Abre el módulo de INGRESO en consulta
                        var vistaIngreso = new IngresoUserControl();
                        vistaIngreso.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📥 Ingreso: {serie}-{numero} (Vista Previa)", vistaIngreso);
                    }
                    else if (filaSeleccionada.SalidaNormal > 0)
                    {
                        // Abre el módulo de SALIDA en consulta
                        var vistaSalida = new SalidasUserControl();
                        vistaSalida.CargarDocumentoParaConsulta(serie, numero);
                        mainShell.AbrirPestaña($"📤 Salida: {serie}-{numero} (Vista Previa)", vistaSalida);
                    }
                }
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarLecturaAsync();
        }

        private async void TxtCodigoEscaneado_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcesarLecturaAsync();
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (DgHistorial.ItemsSource == null || !(DgHistorial.ItemsSource is List<KardexFisicoItem> historial) || !historial.Any())
            {
                MessageBox.Show("No hay datos de historial cargados para imprimir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string nombreAlmacen = SesionSistema.AlmacenActual?.Nombre ?? "ALMACÉN GENERAL";
                string abreviaturaProd = TxtAbreviatura.Text.Trim();
                string codigoEscaneado = TxtCodigoEscaneado.Text.Trim();

                // Formatea el código completo con la abreviatura si el usuario digitó solo el número
                string codigoCompleto = codigoEscaneado;
                if (int.TryParse(codigoEscaneado, out int numP) && !string.IsNullOrEmpty(abreviaturaProd))
                {
                    codigoCompleto = $"{abreviaturaProd}-{numP:D7}";
                }
                else if (!codigoCompleto.Contains("-") && !string.IsNullOrEmpty(abreviaturaProd))
                {
                    codigoCompleto = $"{abreviaturaProd}-{codigoCompleto}";
                }

                string descripcionProducto = TxtProducto.Text.Trim();

                _reporteExcelService.ExportarHistorialPorCodigo(
                    nombreAlmacen,
                    codigoCompleto,
                    descripcionProducto,
                    historial
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el reporte Excel: {ex.Message}", "Error de Exportación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            LimpiarPantalla();
        }

        private void LimpiarPantalla()
        {
            _productoIdSeleccionado = 0;
            _categoriaProductoIdSeleccionada = 0;

            TxtProducto.TextChanged -= TxtProducto_TextChanged;
            TxtProducto.Text = string.Empty;
            TxtProducto.TextChanged += TxtProducto_TextChanged;

            TxtIdProducto.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtColeccion.Text = string.Empty;
            TxtCodigoEscaneado.Text = string.Empty;
            TxtCodigoEscaneado.IsEnabled = false;

            RbLibroGuia.IsChecked = false;
            RbLibroVenta.IsChecked = false;
            RbLibroGuia.IsEnabled = false;
            RbLibroVenta.IsEnabled = false;

            DgHistorial.ItemsSource = null;
            TxtProducto.Focus();
        }
    }
}