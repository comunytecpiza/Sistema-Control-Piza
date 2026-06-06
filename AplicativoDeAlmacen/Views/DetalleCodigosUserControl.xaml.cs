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

        // Modificamos el constructor para que reciba el Lote
        public DetalleCodigosUserControl(RegistroCodigo lote)
        {
            InitializeComponent();
            _service = new CodigoCreadoService();
            _lote = lote;

            // Llenamos la tarjeta superior
            TxtProducto.Text = _lote.Producto?.Descripcion ?? "Desconocido";
            TxtCategoria.Text = _lote.CategoriaProducto?.Nombre ?? "Desconocido";
            TxtRango.Text = $"De {_lote.Desde} a {_lote.Hasta} ({_lote.Cantidad} uds)";

            _ = CargarCodigosAsync();
        }

        private async Task CargarCodigosAsync()
        {
            try
            {
                var codigos = await _service.ObtenerPorRegistroIdAsync(_lote.Id);
                CodigosDataGrid.ItemsSource = codigos;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error al cargar códigos: " + ex.Message);
            }
        }

        private async void BtnRegistrarManual_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtNuevoCodigo.Text.Trim();

            // 1. Validar que solo sean números
            if (!int.TryParse(input, out int numero))
            {
                MessageBox.Show("Por favor, ingrese solo el número del código (sin prefijo).");
                return;
            }

            // 2. Obtener el prefijo del producto padre (Ej: SISTEMC34)
            string prefijo = _lote.Producto?.Abreviatura ?? "COD";

            // 3. Formatear: D7 significa 7 dígitos con ceros a la izquierda
            string codigoCompleto = $"{prefijo}-{numero:D7}";

            try
            {
                // 4. Registrar en BD
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
    }
}