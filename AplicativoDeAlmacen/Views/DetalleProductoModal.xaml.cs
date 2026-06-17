using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views
{
    public partial class DetalleProductoModal : Window
    {
        private readonly ProductoService _productoService;
        private CancellationTokenSource _debounceTimer;
        private Producto _productoSeleccionado = null;

        // Propiedad que leerá el UserControl principal al cerrarse el modal
        public VistaCodigoGrid ItemConfigurado { get; set; }

        // Colección reactiva para sincronizar la grilla visual de tramos
        public ObservableCollection<RangoCodigoItem> ListaRangosLocales { get; set; }

        public VistaProductoGrid ProductoGridGenerado { get; set; }
        public List<VistaCodigoGrid> ListaCodigosGenerados { get; set; } = new List<VistaCodigoGrid>();

        public DetalleProductoModal()
        {
            InitializeComponent();
            _productoService = new ProductoService();

            ListaRangosLocales = new ObservableCollection<RangoCodigoItem>();
            dgvCodigos.ItemsSource = ListaRangosLocales;

            txtProducto.TextChanged += TxtProducto_TextChanged;
        }

        // =======================================================
        // BÚSQUEDA ASÍNCRONA CON DEBOUNCE
        // =======================================================
        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtProducto.Text.Trim();

            _debounceTimer?.Cancel();
            _debounceTimer = new CancellationTokenSource();
            var token = _debounceTimer.Token;

            if (filtro.Length >= 2)
            {
                try
                {
                    await Task.Delay(300, token);
                    List<Producto> productos = await BuscarProductosOficialesAsync(filtro);

                    if (!token.IsCancellationRequested)
                    {
                        if (productos != null && productos.Count > 0)
                        {
                            lstProductos.ItemsSource = productos;
                            popupProductos.IsOpen = true;
                        }
                        else
                        {
                            popupProductos.IsOpen = false;
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al buscar productos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                popupProductos.IsOpen = false;
            }
        }

        private async Task<List<Producto>> BuscarProductosOficialesAsync(string filtro)
        {
            return await Task.Run(() => _productoService.BuscarProductosPorTexto(filtro));
        }

        private void LstProductos_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (lstProductos.SelectedItem is Producto producto)
            {
                txtProducto.TextChanged -= TxtProducto_TextChanged;

                _productoSeleccionado = producto;
                txtProducto.Text = producto.Descripcion;

                // Mapeo seguro de Unidad de Medida descriptiva
                txtUMedida.Text = producto.UnidadMedidaId == 1 ? "UNIDAD" : "MILLAR";

                popupProductos.IsOpen = false;
                txtProducto.TextChanged += TxtProducto_TextChanged;

                dgvCodigos.Focus();
            }
        }

        // =======================================================
        // GESTIÓN DE RANGOS / TRAMOS MASIVOS
        // =======================================================
        private void BtnOriginaRango_Click(object sender, RoutedEventArgs e)
        {
            // Nota: Cambiado a BtnOriginaRango_Click o BtnAgregarRango_Click según tu firma XAML
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto válido usando el buscador predictivo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtCantidadTotal.Text, out int cantidadTotalEsperada) || cantidadTotalEsperada <= 0)
            {
                MessageBox.Show("Por favor, asigne una cantidad total al ítem antes de armar los tramos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int totalCodigosYaAgregados = 0;
            foreach (var rango in ListaRangosLocales)
            {
                if (int.TryParse(rango.Cantidad, out int cantRango))
                    totalCodigosYaAgregados += cantRango;
            }

            if (totalCodigosYaAgregados >= cantidadTotalEsperada)
            {
                MessageBox.Show($"Ya completó los tramos para el total de {cantidadTotalEsperada} unidades indicadas.", "Lotes Listos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int saldoFaltantePorAsignar = cantidadTotalEsperada - totalCodigosYaAgregados;
            string abreviatura = _productoSeleccionado.Abreviatura ?? "COD";
            int prodId = _productoSeleccionado.Id;

            AgregarRangoModal modal = new AgregarRangoModal(ListaRangosLocales, abreviatura, prodId, saldoFaltantePorAsignar);
            modal.Owner = this;

            if (modal.ShowDialog() == true && modal.FueConfirmado)
            {
                RangoCodigoItem nuevoRango = modal.RangoProcesado;
                if (nuevoRango != null)
                {
                    ListaRangosLocales.Add(nuevoRango);

                    // Bloqueamos edición para asegurar consistencia con los tramos calculados
                    txtCantidadTotal.IsReadOnly = true;
                    txtProducto.IsEnabled = false;
                }
            }
        }

        // =======================================================
        // PROCESAMIENTO Y RETORNO DE ESTRUCTURAS DE DATOS
        // =======================================================
        // =======================================================
        // GESTIÓN DE RANGOS / TRAMOS MASIVOS
        // =======================================================
        private void BtnAbrirModalRango_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtCantidadTotal.Text, out int cantidadTotalEsperada) || cantidadTotalEsperada <= 0)
            {
                MessageBox.Show("Asigne una cantidad total válida.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Calcular cuánto falta
            int totalYaAgregado = ListaRangosLocales.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);
            int saldoFaltante = cantidadTotalEsperada - totalYaAgregado;

            if (saldoFaltante <= 0)
            {
                MessageBox.Show("Ya se completaron los tramos para la cantidad total indicada.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AgregarRangoModal modal = new AgregarRangoModal(ListaRangosLocales, _productoSeleccionado.Abreviatura ?? "COD", _productoSeleccionado.Id, saldoFaltante);
            modal.Owner = this;

            if (modal.ShowDialog() == true && modal.FueConfirmado)
            {
                if (modal.RangoProcesado != null)
                {
                    ListaRangosLocales.Add(modal.RangoProcesado);
                    txtCantidadTotal.IsReadOnly = true;
                    txtProducto.IsEnabled = false;
                }
            }
        }

        // =======================================================
        // ÚNICO PUNTO DE SALIDA Y PROCESAMIENTO
        // =======================================================
        private void BtnGrabar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validar que existan datos
            if (_productoSeleccionado == null || ListaRangosLocales.Count == 0)
            {
                MessageBox.Show("Debe configurar al menos un rango antes de aceptar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtCantidadTotal.Text, out int totalEsperado)) return;

            int totalAsignado = ListaRangosLocales.Sum(r => int.TryParse(r.Cantidad, out int c) ? c : 0);

            if (totalAsignado != totalEsperado)
            {
                MessageBox.Show($"La suma de los tramos ({totalAsignado}) no coincide con el total ({totalEsperado}).", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Generar ProductoGridGenerado
            ProductoGridGenerado = new VistaProductoGrid
            {
                Detalle = new MovimientoDetalle { ProductoId = _productoSeleccionado.Id, CantidadSalida = totalAsignado, CreatedAt = DateTime.Now },
                ProductoId = _productoSeleccionado.Id,
                CodigoProducto = _productoSeleccionado.Abreviatura ?? "COD",
                Descripcion = _productoSeleccionado.Descripcion,
                UnidadMedida = txtUMedida.Text
            };

            // 3. Generar Códigos (ListaCodigosGenerados)
            ListaCodigosGenerados.Clear();
            foreach (var rango in ListaRangosLocales)
            {
                for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                {
                    ListaCodigosGenerados.Add(new VistaCodigoGrid
                    {
                        MovCodigo = new MovimientoCodigo { CodigoCreadoId = 0, CantidadSalida = 1, CreatedAt = DateTime.Now },
                        CodigoUnique = $"{rango.AbreviaturaBase}-{i.ToString("D7")}",
                        ProductoId = _productoSeleccionado.Id
                    });
                }
            }

            // 4. Cerrar ventana con éxito
            this.DialogResult = true;
            this.Close();
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

       
    }
}