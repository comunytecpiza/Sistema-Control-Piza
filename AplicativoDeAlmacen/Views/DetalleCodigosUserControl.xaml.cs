using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class DetalleCodigosUserControl : UserControl
    {
        private readonly CodigoCreadoService _service;
        private readonly RegistroCodigo _lote;

        public DetalleCodigosUserControl(RegistroCodigo lote)
        {
            InitializeComponent();
            _service = new CodigoCreadoService();
            _lote = lote;

            // 🌟 LA MAGIA: Esperamos a que la ventana termine de cargar para inyectar los textos
            this.Loaded += DetalleCodigosUserControl_Loaded;
        }

        private void DetalleCodigosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TxtProducto.Text = !string.IsNullOrWhiteSpace(_lote.Producto?.Descripcion) ? _lote.Producto.Descripcion : "Sin Producto";
            TxtCategoria.Text = !string.IsNullOrWhiteSpace(_lote.CategoriaProducto?.Nombre) ? _lote.CategoriaProducto.Nombre : "Sin Categoría";
            TxtRango.Text = $"De {_lote.Desde} a {_lote.Hasta} (Total: {_lote.Cantidad} uds)";

            _ = CargarCodigosAsync();
        }

        private async Task CargarCodigosAsync()
        {
            try
            {
                var codigos = await _service.ObtenerPorRegistroIdAsync(_lote.Id);
                CodigosDataGrid.ItemsSource = codigos;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar códigos: " + ex.Message);
            }
        }

        private async void BtnRegistrarManual_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtNuevoCodigo.Text.Trim();

            if (!int.TryParse(input, out int numero))
            {
                MessageBox.Show("Por favor, ingrese solo el número del código (sin prefijo).");
                return;
            }

            string prefijo = _lote.Producto?.Abreviatura ?? "COD";
            string codigoCompleto = $"{prefijo}-{numero:D7}";

            try
            {
                await _service.RegistrarManualAsync(_lote.Id, codigoCompleto);

                TxtNuevoCodigo.Text = "";
                await CargarCodigosAsync();
                MessageBox.Show($"Código {codigoCompleto} registrado correctamente.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al registrar: " + ex.Message);
            }
        }

        private async void BtnEliminarCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext != null)
            {
                dynamic codigoItem = button.DataContext;

                int idCodigo = codigoItem.Id;
                string numeroCodigo = codigoItem.Codigo;

                MessageBoxResult resultado = MessageBox.Show(
                    $"¿Está seguro de que desea eliminar permanentemente el código '{numeroCodigo}'?\n\nEsta acción no se puede deshacer.",
                    "Confirmar Eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _service.EliminarAsync(idCodigo);

                        MessageBox.Show("El código ha sido eliminado del sistema.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                        await CargarCodigosAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("No se pudo eliminar el código. Es posible que ya tenga movimientos (kardex) asociados.\n\nDetalle: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}