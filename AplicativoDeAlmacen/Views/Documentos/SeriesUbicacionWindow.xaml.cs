using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Documentos;
using System;
using System.Windows;

namespace AplicativoDeAlmacen.Views
{
    public partial class SeriesUbicacionWindow : Window
    {
        private readonly SerieDocumentoService _serieService;
        private readonly Ubicacion _ubicacionActual;
        private SerieDocumento _serieEnEdicion; // Para saber si estamos creando o editando

        public SeriesUbicacionWindow(Ubicacion ubicacion)
        {
            InitializeComponent();
            _serieService = new SerieDocumentoService();
            _ubicacionActual = ubicacion;

            TxtTituloSede.Text = $"Series Punto de Venta : {_ubicacionActual.Descripcion}";

            Loaded += (s, e) => CargarSeries();
        }

        private async void CargarSeries()
        {
            try
            {
                var lista = await _serieService.ObtenerSeriesPorUbicacionAsync(_ubicacionActual.Id);
                DgSeries.ItemsSource = lista;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar series: {ex.Message}");
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _serieEnEdicion = null;
            TxtTituloModal.Text = "Agregar Nueva Serie";
            TxtSerie.Text = "";
            RbElectronica.IsChecked = true;
            TxtFactura.Text = "0";
            TxtBoleta.Text = "0";
            TxtRecibo.Text = "0";
            ModalFormulario.Visibility = Visibility.Visible;
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            if (DgSeries.SelectedItem is SerieDocumento serieSeleccionada)
            {
                _serieEnEdicion = serieSeleccionada;
                TxtTituloModal.Text = "Modificar Serie";

                TxtSerie.Text = serieSeleccionada.NumeroSerie;
                RbElectronica.IsChecked = serieSeleccionada.TipoSerie == "E";
                RbManual.IsChecked = serieSeleccionada.TipoSerie == "M";

                TxtFactura.Text = serieSeleccionada.CorrelativoFactura.ToString();
                TxtBoleta.Text = serieSeleccionada.CorrelativoBoleta.ToString();
                TxtRecibo.Text = serieSeleccionada.CorrelativoRecibo.ToString();

                ModalFormulario.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Seleccione una serie para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (DgSeries.SelectedItem is SerieDocumento serieSeleccionada)
            {
                var result = MessageBox.Show("¿Está seguro de eliminar esta serie?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await _serieService.EliminarSerieAsync(serieSeleccionada.Id);
                    CargarSeries();
                }
            }
            else
            {
                MessageBox.Show("Seleccione una serie para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnCerrarModal_Click(object sender, RoutedEventArgs e)
        {
            ModalFormulario.Visibility = Visibility.Collapsed;
        }

        private async void BtnGrabarSerie_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSerie.Text))
            {
                MessageBox.Show("Ingrese el número de serie.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int.TryParse(TxtFactura.Text, out int fact);
            int.TryParse(TxtBoleta.Text, out int bol);
            int.TryParse(TxtRecibo.Text, out int rec);

            bool esEdicion = _serieEnEdicion != null;
            var serieAGrabar = esEdicion ? _serieEnEdicion : new SerieDocumento();

            serieAGrabar.UbicacionId = _ubicacionActual.Id;
            serieAGrabar.NumeroSerie = TxtSerie.Text.Trim();
            serieAGrabar.TipoSerie = RbElectronica.IsChecked == true ? "E" : "M";
            serieAGrabar.CorrelativoFactura = fact;
            serieAGrabar.CorrelativoBoleta = bol;
            serieAGrabar.CorrelativoRecibo = rec;
            serieAGrabar.EstadoId = 1;
            serieAGrabar.CodigoUsuario = "ADMIN"; // Cambiar por tu usuario de sesión

            try
            {
                if (esEdicion)
                    await _serieService.ActualizarSerieAsync(serieAGrabar);
                else
                    await _serieService.InsertarSerieAsync(serieAGrabar);

                ModalFormulario.Visibility = Visibility.Collapsed;
                CargarSeries();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al grabar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}