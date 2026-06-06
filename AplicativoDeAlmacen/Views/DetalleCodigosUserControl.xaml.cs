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
    }
}