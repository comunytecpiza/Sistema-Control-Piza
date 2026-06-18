using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Core;

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

            this.Loaded += DetalleCodigosUserControl_Loaded;
        }

        private void DetalleCodigosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Llenamos la cabecera principal
            TxtProducto.Text = !string.IsNullOrWhiteSpace(_lote.Producto?.Descripcion) ? _lote.Producto.Descripcion : "Sin Producto";
            TxtCategoria.Text = !string.IsNullOrWhiteSpace(_lote.CategoriaProducto?.Nombre) ? _lote.CategoriaProducto.Nombre : "Sin Categoría";
            TxtRango.Text = $"De {_lote.Desde} a {_lote.Hasta}";
            TxtTotal.Text = $"{_lote.Cantidad} uds.";

            _ = CargarCodigosAsync();
        }

        private async Task CargarCodigosAsync()
        {
            try
            {
                var codigos = await _service.ObtenerPorRegistroIdAsync(_lote.Id);
                CodigosDataGrid.ItemsSource = codigos;

                // Calculamos las Excepciones (Manuales)
                int excepciones = codigos.Count(c => c.EsManual);
                TxtExcepciones.Text = $"{excepciones} manuales";

                // Calculamos el último código para sugerir el siguiente
                CalcularSiguienteCodigo(codigos);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar códigos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CalcularSiguienteCodigo(System.Collections.Generic.List<CodigoCreado> codigos)
        {
            if (codigos == null || codigos.Count == 0)
            {
                TxtNuevoCodigo.Text = "1";
                TxtUltimoAviso.Text = "Último: Ninguno";
                return;
            }

            int maxNumero = 0;
            string prefijo = _lote.Producto?.Abreviatura ?? "COD";

            foreach (var cod in codigos)
            {
                // Extraemos solo el número después del guion
                if (cod.Codigo != null && cod.Codigo.Contains("-"))
                {
                    string numeroStr = cod.Codigo.Substring(cod.Codigo.LastIndexOf('-') + 1);
                    if (int.TryParse(numeroStr, out int num))
                    {
                        if (num > maxNumero) maxNumero = num;
                    }
                }
            }

            TxtUltimoAviso.Text = $"Último registrado: {prefijo}-{maxNumero:D7}";
            TxtNuevoCodigo.Text = (maxNumero + 1).ToString(); // Sugerimos el siguiente
        }

        private async void BtnRegistrarManual_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtNuevoCodigo.Text.Trim();

            if (!int.TryParse(input, out int numero))
            {
                MessageBox.Show("Por favor, ingrese solo el número del código (sin prefijo).", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string prefijo = _lote.Producto?.Abreviatura ?? "COD";
            string codigoCompleto = $"{prefijo}-{numero:D7}";

            try
            {
                // 🛡️ REGLA: Verificar si el código ya existe antes de hacer nada
                bool existe = await _service.ExisteCodigoAsync(_lote.Id, codigoCompleto);
                if (existe)
                {
                    MessageBox.Show($"El código {codigoCompleto} YA EXISTE en el sistema. No se permiten duplicados.", "Colisión de Códigos", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                await _service.RegistrarManualAsync(_lote.Id, codigoCompleto);

                await CargarCodigosAsync();
                MessageBox.Show($"Excepción {codigoCompleto} registrada correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                EventBus.NotificarRegistroCodigosChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al registrar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _service.EliminarAsync(idCodigo);
                        await CargarCodigosAsync();
                        EventBus.NotificarRegistroCodigosChanged();
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