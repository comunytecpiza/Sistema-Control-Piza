using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.facturaciòn;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Models; // Asegúrate de tener este using para ValidacionCodigoResult

namespace AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante
{
    public partial class AgregarItemWindow : Window
    {
        private readonly ProductoService _productoService;
        private readonly FacturacionService _facturacionService;

        private Producto _productoSeleccionado;
        private ObservableCollection<CodigoLeidoDTO> _codigosAgregados;
        private bool _isTyping = true;
        private bool _usaCodigos = false;
        private bool _modoEdicion = false;

        // 🌟 NUEVA VARIABLE PARA GUARDAR EL MOVIMIENTO
        private int _ultimoMovimientoId = 0;

        public ItemGridDTO NuevoItem { get; private set; }

        public AgregarItemWindow()
        {
            InitializeComponent();
            _productoService = new ProductoService();
            _facturacionService = new FacturacionService();
            _codigosAgregados = new ObservableCollection<CodigoLeidoDTO>();
            DgCodigos.ItemsSource = _codigosAgregados;
        }

        public AgregarItemWindow(ItemGridDTO itemExistente) : this()
        {
            _modoEdicion = true;
            Title = "Modificar Item";

            Loaded += async (s, e) => await CargarItemParaEdicionAsync(itemExistente);
        }

        private async void TxtProductoBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isTyping) return;
            string texto = TxtProductoBuscador.Text.Trim();
            if (texto.Length < 2) { PopProducto.IsOpen = false; return; }

            var resultados = await _productoService.BuscarProductosPorTextoAsync(texto);
            LstProductos.ItemsSource = resultados.Take(15).ToList();
            PopProducto.IsOpen = resultados.Any();
        }

        private void LstProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProductos.SelectedItem is Producto p)
            {
                _productoSeleccionado = p;

                _isTyping = false;
                TxtProductoBuscador.Text = p.Descripcion;
                TxtProductoId.Text = p.Id.ToString("D5");
                PopProducto.IsOpen = false;
                _isTyping = true;

                TxtUMedida.Text = p.UnidadMedida?.Descripcion ?? "UNIDAD";
                TxtAfectacion.Text = p.afectacion?.Nombre ?? "EXONERADO - OPERACION ONEROSA";
                TxtPreUnitario.Text = p.PrecioUnitario?.ToString("N0") ?? "0";

                ConfigurarSegunUnidadMedida();
            }
        }

        private void ConfigurarSegunUnidadMedida()
        {
            string um = TxtUMedida.Text.ToUpper();

            _codigosAgregados.Clear();
            TxtCantidad.Text = "0";
            TxtTotal.Text = "0";
            _ultimoMovimientoId = 0; // Reiniciamos el ID al cambiar producto

            if (um == "UNIDAD" || um == "UND")
            {
                _usaCodigos = false;
                TxtCantidad.IsReadOnly = false;
                TxtCantidad.Background = System.Windows.Media.Brushes.White;

                BtnAgregarCod.IsEnabled = false;
                BtnModificarCod.IsEnabled = false;
                BtnEliminarCod.IsEnabled = false;
                PanelCapturaCodigo.Visibility = Visibility.Collapsed;
            }
            else
            {
                _usaCodigos = true;
                TxtCantidad.IsReadOnly = true;
                TxtCantidad.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F1F5F9");

                BtnAgregarCod.IsEnabled = true;
                BtnEliminarCod.IsEnabled = true;
            }
            ConfigurarBotonesSegunUnidad();
            Calculo_TextChanged(null, null);
        }

        private void ConfigurarBotonesSegunUnidad()
        {
            string um = TxtUMedida.Text.ToUpper();

            if (um == "UNIDAD" || um == "UND")
            {
                _usaCodigos = false;
                TxtCantidad.IsReadOnly = false;
                TxtCantidad.Background = System.Windows.Media.Brushes.White;

                BtnAgregarCod.IsEnabled = false;
                BtnModificarCod.IsEnabled = false;
                BtnEliminarCod.IsEnabled = false;
                PanelCapturaCodigo.Visibility = Visibility.Collapsed;
            }
            else
            {
                _usaCodigos = true;
                TxtCantidad.IsReadOnly = true;
                TxtCantidad.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F1F5F9");

                BtnAgregarCod.IsEnabled = true;
                BtnEliminarCod.IsEnabled = true;
            }
        }

        private void Calculo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;

            decimal cantidad = 0, preUnitario = 0;
            decimal.TryParse(TxtCantidad.Text, out cantidad);
            decimal.TryParse(TxtPreUnitario.Text, out preUnitario);

            decimal total = cantidad * preUnitario;
            TxtTotal.Text = total.ToString("N0");

            if (BtnGrabarItem != null)
            {
                BtnGrabarItem.IsEnabled = (cantidad > 0);
            }
        }

        private void BtnAgregarCod_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null) return;
            PanelCapturaCodigo.Visibility = Visibility.Visible;
            TxtCapturaCodigo.Text = "";
            TxtCapturaCodigo.Focus();
        }

        private void TxtCapturaCodigo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmarCodigo_Click(null, null);
            }
        }

        private async void BtnConfirmarCodigo_Click(object sender, RoutedEventArgs e)
        {
            string codigoDigitado = TxtCapturaCodigo.Text.Trim();
            if (string.IsNullOrEmpty(codigoDigitado)) return;

            try
            {
                var resultado = await _facturacionService.ValidarCodigoParaVentaAsync(_productoSeleccionado.Id, codigoDigitado);

                int id = resultado.Id;
                string codigoReal = resultado.CodigoCompleto;

                // 🌟 GUARDAMOS EL MOVIMIENTO ID
                _ultimoMovimientoId = resultado.MovimientoId;

                if (_codigosAgregados.Any(c => c.CodigoCreadoId == id))
                {
                    MessageBox.Show("El código ya está en la lista.", "Aviso");
                    return;
                }

                _codigosAgregados.Add(new CodigoLeidoDTO
                {
                    CodigoCreadoId = id,
                    CodigoString = codigoReal,
                    Cantidad = 1,
                    Coleccion = "Kardex Validado"
                });

                ActualizarTotalYCantidad();
                TxtCapturaCodigo.Text = "";
                TxtCapturaCodigo.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminarCod_Click(object sender, RoutedEventArgs e)
        {
            if (DgCodigos.SelectedItem is CodigoLeidoDTO item)
            {
                _codigosAgregados.Remove(item);
                ActualizarTotalYCantidad();

                // Si eliminamos el último código, reseteamos el ID
                if (_codigosAgregados.Count == 0)
                {
                    _ultimoMovimientoId = 0;
                }
            }
        }

        private void ActualizarTotalYCantidad()
        {
            if (_usaCodigos)
            {
                TxtCantidad.Text = _codigosAgregados.Count.ToString();
            }

            Calculo_TextChanged(null, null);
        }

        private void BtnCancelarCodigo_Click(object sender, RoutedEventArgs e)
        {
            PanelCapturaCodigo.Visibility = Visibility.Collapsed;
        }

        private void BtnGrabarItem_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Debe seleccionar un producto.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!decimal.TryParse(TxtCantidad.Text, out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("La cantidad debe ser mayor a 0 para poder agregar este ítem.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCantidad.Focus();
                return;
            }

            if (_usaCodigos && _codigosAgregados.Count == 0)
            {
                MessageBox.Show("Este producto requiere al menos un código escaneado/digitado antes de grabar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_usaCodigos && _codigosAgregados.Count != cantidad)
            {
                MessageBox.Show("La cantidad no coincide con los códigos agregados. Vuelve a revisar.", "Error de integridad", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            decimal.TryParse(TxtTotal.Text, out decimal total);

            NuevoItem = new ItemGridDTO
            {
                ProductoId = _productoSeleccionado.Id,
                MovimientoId = _ultimoMovimientoId, // 🌟 AHORA SÍ TIENE EL ID DE LA SALIDA REAL
                DescripcionProducto = _productoSeleccionado.Descripcion,
                UnidadMedida = TxtUMedida.Text,
                CanProd = cantidad,
                PreUnit = decimal.Parse(TxtPreUnitario.Text),
                ImpTota = total,
                Codigos = _codigosAgregados.ToList()
            };

            this.DialogResult = true;
            this.Close();
        }

        private async Task CargarItemParaEdicionAsync(ItemGridDTO item)
        {
            _isTyping = false;

            var producto = await _productoService.ObtenerPorIdAsync(item.ProductoId);

            if (producto != null)
            {
                _productoSeleccionado = producto;
                TxtProductoBuscador.Text = producto.Descripcion;
                TxtProductoId.Text = producto.Id.ToString("D5");
                TxtUMedida.Text = producto.UnidadMedida?.Descripcion ?? item.UnidadMedida;
                TxtAfectacion.Text = producto.afectacion?.Nombre ?? "EXONERADO - OPERACION ONEROSA";
            }
            else
            {
                TxtProductoBuscador.Text = item.DescripcionProducto;
                TxtUMedida.Text = item.UnidadMedida;
            }

            TxtProductoBuscador.IsReadOnly = true;
            TxtProductoBuscador.Background = System.Windows.Media.Brushes.WhiteSmoke;

            _isTyping = true;
            ConfigurarBotonesSegunUnidad();

            _codigosAgregados.Clear();
            foreach (var cod in item.Codigos)
                _codigosAgregados.Add(cod);

            // 🌟 Restaurar el MovimientoId
            _ultimoMovimientoId = item.MovimientoId;

            TxtPreUnitario.Text = item.PreUnit.ToString("N0");
            TxtCantidad.Text = _usaCodigos ? _codigosAgregados.Count.ToString() : item.CanProd.ToString("N0");

            Calculo_TextChanged(null, null);

            BtnGrabarItem.Content = "💾 Actualizar";
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}