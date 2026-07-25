using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class ZonasPromotoriaUserControl : UserControl
    {
        private readonly ZonaPromotoriaService _zonaService;
        private ZonaPromotoria? _zonaEnEdicion = null;

        public ZonasPromotoriaUserControl()
        {
            InitializeComponent();
            _zonaService = new ZonaPromotoriaService();

            LocalidadComboBox.DisplayMemberPath = "Nombre";

            this.Loaded += async (s, e) => await CargarLocalidadesAsync();
            EventBus.OnZonasChanged += () => Application.Current.Dispatcher.InvokeAsync(async () => {
                if (LocalidadComboBox.SelectedItem is Localidad loc) await CargarZonasAsync(loc.Id);
            });
        }

        private async Task CargarLocalidadesAsync()
        {
            try
            {
                var localidades = await _zonaService.ObtenerLocalidadesAsync();
                LocalidadComboBox.ItemsSource = localidades;
                if (localidades != null && localidades.Count > 0)
                {
                    LocalidadComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar localidades: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is Localidad localidadSeleccionada)
            {
                await CargarZonasAsync(localidadSeleccionada.Id);
            }
        }

        private async Task CargarZonasAsync(int localidadId)
        {
            try
            {
                ZonasListBox.ItemsSource = await _zonaService.ObtenerZonasPorLocalidadAsync(localidadId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar zonas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AgregarZona_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is not Localidad)
            {
                MessageBox.Show("Seleccione una localidad primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _zonaEnEdicion = null; // Modo Agregar
            ModalTitle.Text = "Nueva Zona de Promotoría";
            DescripcionTextBox.Text = string.Empty;
            ModalBackground.Visibility = Visibility.Visible;
            DescripcionTextBox.Focus();
        }

        private void EditarZona_Click(object sender, RoutedEventArgs e)
        {
            if (ZonasListBox.SelectedItem is ZonaPromotoria zona)
            {
                _zonaEnEdicion = zona; // Modo Edición
                ModalTitle.Text = "Editar Zona de Promotoría";
                DescripcionTextBox.Text = zona.Descripcion;
                ModalBackground.Visibility = Visibility.Visible;
                DescripcionTextBox.Focus();
                DescripcionTextBox.SelectAll();
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una zona de la lista para editar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void GuardarZona_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is not Localidad loc) return;

            string descripcion = DescripcionTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                MessageBox.Show("Ingrese una descripción para la zona.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_zonaEnEdicion == null)
                {
                    await _zonaService.RegistrarZonaAsync(descripcion, loc.Id);
                    MessageBox.Show("Zona de promotoría agregada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _zonaService.ActualizarZonaAsync(_zonaEnEdicion.Id, descripcion);
                    MessageBox.Show("Zona de promotoría actualizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                ModalBackground.Visibility = Visibility.Collapsed;
                await CargarZonasAsync(loc.Id);
                EventBus.NotificarZonasChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EliminarZona_Click(object sender, RoutedEventArgs e)
        {
            if (ZonasListBox.SelectedItem is ZonaPromotoria zona)
            {
                var confirm = MessageBox.Show($"¿Está seguro de eliminar la zona \"{zona.Descripcion}\"?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    await _zonaService.EliminarZonaAsync(zona.Id);
                    MessageBox.Show("Zona eliminada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (LocalidadComboBox.SelectedItem is Localidad loc)
                    {
                        await CargarZonasAsync(loc.Id);
                    }
                    EventBus.NotificarZonasChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Restricción de Kárdex", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una zona para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CancelarModal_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }
    }
}