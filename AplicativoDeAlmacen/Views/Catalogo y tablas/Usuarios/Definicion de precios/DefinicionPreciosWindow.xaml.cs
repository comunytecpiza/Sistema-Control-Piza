using AplicativoDeAlmacen.Models.Estados;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Users;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante
{
    public partial class DefinicionPreciosWindow : Window
    {
        private readonly ClientePrecioEspecialService _preciosService;
        private int _clienteId;
        private List<PrecioCatalogoDTO> _catalogoCompleto;
        private PrecioCatalogoDTO _itemSeleccionado;

        public DefinicionPreciosWindow(PersonaComercial cliente)
        {
            InitializeComponent();
            _preciosService = new ClientePrecioEspecialService();
            _clienteId = cliente.Id;

            // Ponemos el nombre del cliente en el título
            this.Title = $"Definición de Precios - {cliente.RazonSocial ?? cliente.NombreComercial}";

            Loaded += async (s, e) => await CargarCatalogo();
        }

        private async Task CargarCatalogo()
        {
            try
            {
                var data = await _preciosService.ObtenerCatalogoPreciosPorClienteAsync(_clienteId);
                _catalogoCompleto = data.Select(d => new PrecioCatalogoDTO
                {
                    ProductoId = (int)d.ProductoId,
                    Descripcion = (string)d.Descripcion,
                    UnidadMedida = (string)d.UnidadMedida,
                    PrecioBase = (decimal)d.PrecioBase,
                    PorcentajeBase = (decimal)d.PorcentajeBase, // 🌟 Nuevo mapeo
                    PrecioEspecialId = (int)d.PrecioEspecialId,
                    PrecioEspecial = (decimal)d.PrecioEspecial,
                    Porcentaje = (decimal)d.Porcentaje,
                    TienePrecioEspecial = (bool)d.TienePrecioEspecial
                }).ToList();

                DgProductos.ItemsSource = _catalogoCompleto;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar precios: " + ex.Message);
            }
        }

        // --- FILTRO DE PRODUCTOS EN TIEMPO REAL ---
        private void TxtBuscarProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_catalogoCompleto == null) return;
            string texto = TxtBuscarProducto.Text.Trim().ToLower();

            var filtrados = _catalogoCompleto.Where(p => p.Descripcion.ToLower().Contains(texto)).ToList();
            DgProductos.ItemsSource = filtrados;
        }

        // --- ACCIONES PRINCIPALES ---
        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _itemSeleccionado = DgProductos.SelectedItem as PrecioCatalogoDTO;
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            if (_itemSeleccionado == null)
            {
                MessageBox.Show("Seleccione un producto de la lista primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtModCodigo.Text = _itemSeleccionado.ProductoId.ToString("D5");
            TxtModDesc.Text = _itemSeleccionado.Descripcion;
            TxtModUMedida.Text = _itemSeleccionado.UnidadMedida;

            // Forzamos el chequeo según si tiene o no precio especial
            if (_itemSeleccionado.TienePrecioEspecial)
            {
                RbPrecioEspecial.IsChecked = true;
            }
            else
            {
                RbPrecioNormal.IsChecked = true;
            }

            ModalEdicion.Visibility = Visibility.Visible;
        }

        private void RbTipoPrecio_Checked(object sender, RoutedEventArgs e)
        {
            if (_itemSeleccionado == null || TxtModPrecio == null || TxtModPorcentaje == null) return;

            if (RbPrecioNormal.IsChecked == true)
            {
                // 🌟 MODO NORMAL: Todo bloqueado (Read-Only) y usando valores base del producto
                TxtModPrecio.IsReadOnly = true;
                TxtModPorcentaje.IsReadOnly = true;
                TxtModPrecio.Text = _itemSeleccionado.PrecioBase.ToString("N2");
                TxtModPorcentaje.Text = _itemSeleccionado.PorcentajeBase.ToString("N2");

                // Color grisáceo opaco para dar feedback visual
                TxtModPrecio.Background = System.Windows.Media.Brushes.WhiteSmoke;
                TxtModPorcentaje.Background = System.Windows.Media.Brushes.WhiteSmoke;
            }
            else if (RbPrecioEspecial.IsChecked == true)
            {
                // 🌟 MODO ESPECIAL: Campos libres para editar
                TxtModPrecio.IsReadOnly = false;
                TxtModPorcentaje.IsReadOnly = false;

                // Si ya tenía precio especial guardado, lo mostramos. Si no, precargamos el base para que parta de ahí.
                TxtModPrecio.Text = _itemSeleccionado.TienePrecioEspecial ? _itemSeleccionado.PrecioEspecial.ToString("N2") : _itemSeleccionado.PrecioBase.ToString("N2");
                TxtModPorcentaje.Text = _itemSeleccionado.TienePrecioEspecial ? _itemSeleccionado.Porcentaje.ToString("N2") : _itemSeleccionado.PorcentajeBase.ToString("N2");

                // Color blanco para indicar que se puede escribir
                TxtModPrecio.Background = System.Windows.Media.Brushes.White;
                TxtModPorcentaje.Background = System.Windows.Media.Brushes.White;
                TxtModPrecio.Focus();
                TxtModPrecio.SelectAll();
            }
        }

        private async void BtnGrabarPrecio_Click(object sender, RoutedEventArgs e)
        {
            decimal precioNuevo;
            decimal porcentajeNuevo;

            if (!decimal.TryParse(TxtModPrecio.Text, out precioNuevo)) precioNuevo = 0;
            if (!decimal.TryParse(TxtModPorcentaje.Text, out porcentajeNuevo)) porcentajeNuevo = 0;

            // 1: Activo (Especial) | 2: Inactivo (Vuelve a Normal)
            int estadoId = (RbPrecioEspecial.IsChecked == true) ? 1 : 2;

            var precioGuardar = new ClientePrecioEspecial
            {
                Id = _itemSeleccionado.PrecioEspecialId, // Será 0 si es nuevo, o >0 si se actualiza
                PersonaComercialId = _clienteId,
                ProductoId = _itemSeleccionado.ProductoId,
                PrecioUnitario = precioNuevo,
                PorcentajeBonificacion = porcentajeNuevo,
                EstadoId = estadoId,
                UsuarioId = 1 // Ajustar a tu sesión actual
            };

            try
            {
                await _preciosService.GuardarPrecioEspecialAsync(precioGuardar);
                ModalEdicion.Visibility = Visibility.Collapsed;

                // Recargar grilla
                await CargarCatalogo();

                // Aplicar el filtro que estaba en la caja de texto
                TxtBuscarProducto_TextChanged(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al grabar: " + ex.Message);
            }
        }

        private void BtnCerrarModal_Click(object sender, RoutedEventArgs e)
        {
            ModalEdicion.Visibility = Visibility.Collapsed;
        }
    }

    

}