using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class UbicacionesUserControl : UserControl
    {
        private readonly UbicacionService _ubicacionService;
        private List<Ubicacion> _listadoCompletoUbicaciones = new List<Ubicacion>();
        private Ubicacion? _ubicacionActual;

        public UbicacionesUserControl()
        {
            InitializeComponent();
            _ubicacionService = new UbicacionService();

            this.Loaded += async (s, e) => {
                ConfigurarFormatosCombos();
                await CargarCombosMaestrosAsync();
                await CargarUbicacionesAsync();
            };

            EventBus.OnUbicacionesChanged += () => Application.Current.Dispatcher.InvokeAsync(CargarUbicacionesAsync);
        }

        private void ConfigurarFormatosCombos()
        {
            CmbTipoUbicacion.DisplayMemberPath = "Nombre";
            CmbLocalidad.DisplayMemberPath = "Nombre";
            CmbDepartamento.DisplayMemberPath = "Nombre";
            CmbProvincia.DisplayMemberPath = "Nombre";
            CmbDistrito.DisplayMemberPath = "Nombre";
            CmbEstado.DisplayMemberPath = "Nombre";
            CmbFiltroTipoUbicacion.DisplayMemberPath = "Nombre";
        }

        private async Task CargarCombosMaestrosAsync()
        {
            try
            {
                var tipos = await _ubicacionService.ObtenerTiposUbicacionAsync();

                // Agregar opción "TODOS LOS TIPOS" al filtro
                var listaFiltro = new List<TipoUbicacion> { new TipoUbicacion { Id = 0, Nombre = "--- TODOS LOS TIPOS ---" } };
                listaFiltro.AddRange(tipos);

                CmbFiltroTipoUbicacion.ItemsSource = listaFiltro;
                CmbFiltroTipoUbicacion.SelectedIndex = 0;

                CmbTipoUbicacion.ItemsSource = tipos;
                CmbLocalidad.ItemsSource = await _ubicacionService.ObtenerLocalidadesAsync();
                CmbDepartamento.ItemsSource = await _ubicacionService.ObtenerDepartamentosAsync();
                CmbEstado.ItemsSource = await _ubicacionService.ObtenerEstadosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar maestros: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarUbicacionesAsync()
        {
            try
            {
                _listadoCompletoUbicaciones = await _ubicacionService.ObtenerTodasAsync();
                AplicarFiltros();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar las ubicaciones: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AplicarFiltros()
        {
            string busqueda = BuscarTextBox.Text.ToLower().Trim();
            int tipoIdSeleccionado = (CmbFiltroTipoUbicacion.SelectedItem is TipoUbicacion tu) ? tu.Id : 0;

            var resultado = _listadoCompletoUbicaciones.Where(u =>
                (tipoIdSeleccionado == 0 || (u.TipoUbicacion != null && u.TipoUbicacion.Id == tipoIdSeleccionado)) &&
                (string.IsNullOrWhiteSpace(busqueda) ||
                 u.Id.ToString().Contains(busqueda) ||
                 (u.Descripcion != null && u.Descripcion.ToLower().Contains(busqueda)) ||
                 (u.Direccion != null && u.Direccion.ToLower().Contains(busqueda)) ||
                 (u.Localidad?.Nombre != null && u.Localidad.Nombre.ToLower().Contains(busqueda)) ||
                 (u.Departamento?.Nombre != null && u.Departamento.Nombre.ToLower().Contains(busqueda))
                )
            ).ToList();

            UbicacionesDataGrid.ItemsSource = resultado;
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltros();

        private void CmbFiltroTipoUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltros();

        private async void CmbDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDepartamento.SelectedItem is Departamento dep)
            {
                CmbProvincia.ItemsSource = await _ubicacionService.ObtenerProvinciasAsync(dep.Id);
                CmbDistrito.ItemsSource = null;
            }
        }

        private async void CmbProvincia_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProvincia.SelectedItem is Provincia prov)
            {
                CmbDistrito.ItemsSource = await _ubicacionService.ObtenerDistritosAsync(prov.Id);
            }
        }

        private void AgregarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            _ubicacionActual = null;
            ModalTitle.Text = "Nueva Ubicación";
            LimpiarCamposModal();
            UbicacionModal.Visibility = Visibility.Visible;
        }

        private async void EditarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            if (UbicacionesDataGrid.SelectedItem is not Ubicacion seleccionada)
            {
                MessageBox.Show(
                    "Seleccione una ubicación para editar.",
                    "Aviso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _ubicacionActual = seleccionada;

            ModalTitle.Text = "Editar Ubicación";

            // Textos
            TxtDescripcion.Text = seleccionada.Descripcion ?? "";
            TxtDireccion.Text = seleccionada.Direccion ?? "";

            // Limpiar selección
            CmbProvincia.ItemsSource = null;
            CmbDistrito.ItemsSource = null;

            // Combos simples
            CmbTipoUbicacion.SelectedValue = seleccionada.TipoUbicacion?.Id;
            CmbLocalidad.SelectedValue = seleccionada.Localidad?.Id;
            CmbEstado.SelectedValue = seleccionada.Estado?.Id;

            // Departamento
            if (seleccionada.Departamento != null)
            {
                CmbDepartamento.SelectedValue = seleccionada.Departamento.Id;

                var provincias = await _ubicacionService.ObtenerProvinciasAsync(seleccionada.Departamento.Id);
                CmbProvincia.ItemsSource = provincias;

                if (seleccionada.Provincia != null)
                {
                    CmbProvincia.SelectedValue = seleccionada.Provincia.Id;

                    var distritos = await _ubicacionService.ObtenerDistritosAsync(seleccionada.Provincia.Id);
                    CmbDistrito.ItemsSource = distritos;

                    if (seleccionada.Distrito != null)
                    {
                        CmbDistrito.SelectedValue = seleccionada.Distrito.Id;
                    }
                }
            }

            UbicacionModal.Visibility = Visibility.Visible;
        }

        private async void EliminarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            if (UbicacionesDataGrid.SelectedItem is Ubicacion seleccionada)
            {
                var confirm = MessageBox.Show($"¿Está seguro de eliminar la ubicación \"{seleccionada.Descripcion}\"?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    await _ubicacionService.EliminarAsync(seleccionada.Id);
                    MessageBox.Show("Ubicación eliminada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    await CargarUbicacionesAsync();
                    EventBus.NotificarUbicacionesChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Restricción de Kárdex", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una ubicación para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnGuardarUbicacion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("Ingrese el nombre de la ubicación.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CmbTipoUbicacion.SelectedItem == null)
            {
                MessageBox.Show("Seleccione un Tipo de Ubicación.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var u = _ubicacionActual ?? new Ubicacion();
                u.Descripcion = TxtDescripcion.Text;
                u.Direccion = TxtDireccion.Text;

                u.TipoUbicacion = (TipoUbicacion)CmbTipoUbicacion.SelectedItem;
                u.Localidad = CmbLocalidad.SelectedItem as Localidad;
                u.Estado = CmbEstado.SelectedItem as Estado;

                u.Departamento = CmbDepartamento.SelectedItem as Departamento;
                u.Provincia = CmbProvincia.SelectedItem as Provincia;
                u.Distrito = CmbDistrito.SelectedItem as Distrito;

                if (_ubicacionActual == null)
                {
                    await _ubicacionService.InsertarAsync(u);
                    MessageBox.Show("Ubicación registrada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _ubicacionService.ActualizarAsync(u);
                    MessageBox.Show("Ubicación actualizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                UbicacionModal.Visibility = Visibility.Collapsed;
                await CargarUbicacionesAsync();
                EventBus.NotificarUbicacionesChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar la ubicación: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimpiarCamposModal()
        {
            TxtDescripcion.Clear();
            TxtDireccion.Clear();
            CmbTipoUbicacion.SelectedIndex = -1;
            CmbLocalidad.SelectedIndex = -1;
            CmbDepartamento.SelectedIndex = -1;
            CmbProvincia.ItemsSource = null;
            CmbDistrito.ItemsSource = null;
            CmbEstado.SelectedIndex = -1;
        }

        private void BtnCancelarUbicacion_Click(object sender, RoutedEventArgs e) => UbicacionModal.Visibility = Visibility.Collapsed;

        private void BtnSeries_Click(object sender, RoutedEventArgs e)
        {
            if (UbicacionesDataGrid.SelectedItem is Ubicacion ubicacionSeleccionada)
            {
                if (ubicacionSeleccionada.TipoUbicacion == null ||
                   !ubicacionSeleccionada.TipoUbicacion.Nombre.ToUpper().Contains("PUNTO DE VENTA"))
                {
                    MessageBox.Show("Solamente manejan series las ubicaciones que son PUNTOS DE VENTA.",
                                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SeriesUbicacionWindow modalSeries = new SeriesUbicacionWindow(ubicacionSeleccionada);
                modalSeries.Owner = Window.GetWindow(this);
                modalSeries.ShowDialog();
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una ubicación para configurar sus series.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}