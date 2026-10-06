#nullable enable

using AplicativoDeAlmacen.Models.Documentos;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services.Documentos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class SeriesUbicacionWindow : Window
    {
        private readonly SerieDocumentoService _serieService;
        private readonly DocumentoService _documentoService;
        private readonly Ubicacion _ubicacionActual;
        private SerieDocumento? _serieEnEdicion;
        private List<Documento> _documentosActivos = new List<Documento>();

        public SeriesUbicacionWindow(Ubicacion ubicacion)
        {
            InitializeComponent();
            _serieService = new SerieDocumentoService();
            _documentoService = new DocumentoService();
            _ubicacionActual = ubicacion;

            TxtTituloSede.Text = $"Series Punto de Venta : {_ubicacionActual.Descripcion}";

            Loaded += async (s, e) =>
            {
                await CargarDocumentosAsync();
                CargarSeries();
            };
        }

        private async System.Threading.Tasks.Task CargarDocumentosAsync()
        {
            try
            {
                _documentosActivos = await _documentoService.ObtenerActivosAsync();
                CboDocumentos.ItemsSource = _documentosActivos;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar tipos de documento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                MessageBox.Show($"Error al cargar series: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _serieEnEdicion = null;
            TxtTituloModal.Text = "Agregar Nueva Serie";
            TxtSerie.Text = "";
            TxtCorrelativo.Text = "0";

            if (_documentosActivos.Any())
            {
                CboDocumentos.SelectedIndex = 0;
            }

            ModalFormulario.Visibility = Visibility.Visible;
            TxtSerie.Focus();
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            if (DgSeries.SelectedItem is SerieDocumento serieSeleccionada)
            {
                _serieEnEdicion = serieSeleccionada;
                TxtTituloModal.Text = "Modificar Serie";
                TxtSerie.Text = serieSeleccionada.NumeroSerie;

                // Seleccionar el tipo de documento según tip_seri
                if (!string.IsNullOrWhiteSpace(serieSeleccionada.TipoSerie))
                {
                    CboDocumentos.SelectedValue = serieSeleccionada.TipoSerie;
                }

                // Cargar el correlativo según el tipo de documento
                ActualizarTextoCorrelativoSegunDocumento(serieSeleccionada);

                ModalFormulario.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Seleccione una serie para modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CboDocumentos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboDocumentos.SelectedItem is Documento doc)
            {
                LblCorrelativo.Text = $"Último Correlativo ({doc.Descripcion}):";
            }
        }

        private void ActualizarTextoCorrelativoSegunDocumento(SerieDocumento serie)
        {
            string codDoc = serie.TipoSerie ?? "";

            if (codDoc == "01")
                TxtCorrelativo.Text = serie.CorrelativoFactura.ToString();
            else if (codDoc == "02" || codDoc == "03")
                TxtCorrelativo.Text = serie.CorrelativoBoleta.ToString();
            else
                TxtCorrelativo.Text = serie.CorrelativoRecibo.ToString();
        }

        private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (DgSeries.SelectedItem is SerieDocumento serieSeleccionada)
            {
                var result = MessageBox.Show($"¿Está seguro de eliminar la serie '{serieSeleccionada.NumeroSerie}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            string serieTexto = TxtSerie.Text.Trim();
            if (string.IsNullOrWhiteSpace(serieTexto))
            {
                MessageBox.Show("Ingrese el número de serie.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSerie.Focus();
                return;
            }

            if (CboDocumentos.SelectedValue == null)
            {
                MessageBox.Show("Seleccione el tipo de comprobante asociado a esta serie.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string codDocumento = CboDocumentos.SelectedValue.ToString()!;
            int.TryParse(TxtCorrelativo.Text.Trim(), out int correlativo);

            bool esEdicion = _serieEnEdicion != null;
            var serieAGrabar = esEdicion ? _serieEnEdicion! : new SerieDocumento();

            serieAGrabar.UbicacionId = _ubicacionActual.Id;
            serieAGrabar.NumeroSerie = serieTexto.ToUpper();
            serieAGrabar.TipoSerie = codDocumento; // Se vincula el código de documento (01, 03, etc.)
            serieAGrabar.EstadoId = 1;
            serieAGrabar.CodigoUsuario = "ADMIN";

            // Asignación al campo correspondiente para compatibilidad
            if (codDocumento == "01")
            {
                serieAGrabar.CorrelativoFactura = correlativo;
            }
            else if (codDocumento == "02" || codDocumento == "03")
            {
                serieAGrabar.CorrelativoBoleta = correlativo;
            }
            else
            {
                serieAGrabar.CorrelativoRecibo = correlativo;
            }

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
                MessageBox.Show($"Error al guardar la serie: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}