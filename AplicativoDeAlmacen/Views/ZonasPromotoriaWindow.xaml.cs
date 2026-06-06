using System;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models; // Tus modelos
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ZonasPromotoriaWindow : Window
    {
        private readonly ZonaPromotoriaService _zonaService;

        public ZonasPromotoriaWindow()
        {
            InitializeComponent();
            _zonaService = new ZonaPromotoriaService();

            // Configuración clave: le decimos a WPF qué propiedad del objeto va a mostrar en el texto
            LocalidadComboBox.DisplayMemberPath = "Nombre";
            ZonasListBox.DisplayMemberPath = "Descripcion";

            CargarLocalidades();
        }

        private void CargarLocalidades()
        {
            try
            {
                // Pasamos la lista de objetos directos al ComboBox
                LocalidadComboBox.ItemsSource = _zonaService.ObtenerLocalidades();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Rescatamos el OBJETO completo de tipo Localidad que fue seleccionado
            if (LocalidadComboBox.SelectedItem is Localidad localidadSeleccionada)
            {
                CargarZonas(localidadSeleccionada.Id);
            }
        }

        private void CargarZonas(int localidadId)
        {
            try
            {
                // Pasamos la lista de objetos Zona al ListBox
                ZonasListBox.ItemsSource = _zonaService.ObtenerZonasPorLocalidad(localidadId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AgregarZona_Click(object sender, RoutedEventArgs e)
        {
            if (!(LocalidadComboBox.SelectedItem is Localidad localidadSeleccionada))
            {
                MessageBox.Show("Por favor, seleccione una localidad primero.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string descripcion = Microsoft.VisualBasic.Interaction.InputBox("Ingrese la descripción de la nueva zona:", "Agregar Zona", "");
            
            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                try
                {
                    _zonaService.RegistrarZona(descripcion, localidadSeleccionada.Id);
                    CargarZonas(localidadSeleccionada.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EliminarZona_Click(object sender, RoutedEventArgs e)
        {
            if (!(ZonasListBox.SelectedItem is ZonaPromotoria zonaSeleccionada))
            {
                MessageBox.Show("Por favor, seleccione una zona para eliminar.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"¿Está seguro de que desea eliminar la zona '{zonaSeleccionada.Descripcion}'?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    _zonaService.EliminarZona(zonaSeleccionada.Id);
                    
                    if (LocalidadComboBox.SelectedItem is Localidad localidadSeleccionada)
                    {
                        CargarZonas(localidadSeleccionada.Id);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}