using System;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ZonasPromotoriaUserControl : UserControl // <--- Cambiado a UserControl
    {
        private readonly ZonaPromotoriaService _zonaService;

        public ZonasPromotoriaUserControl()
        {
            InitializeComponent();
            _zonaService = new ZonaPromotoriaService();

            LocalidadComboBox.DisplayMemberPath = "Nombre";
            

            this.Loaded += (s, e) => CargarLocalidades();
        }

        private void CargarLocalidades()
        {
            try
            {
                LocalidadComboBox.ItemsSource = _zonaService.ObtenerLocalidades();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is Localidad localidadSeleccionada)
            {
                CargarZonas(localidadSeleccionada.Id);
            }
        }

        private void CargarZonas(int localidadId)
        {
            try
            {
                ZonasListBox.ItemsSource = _zonaService.ObtenerZonasPorLocalidad(localidadId);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void AgregarZona_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is not Localidad loc)
            {
                MessageBox.Show("Seleccione una localidad.");
                return;
            }

            // Usamos un InputBox básico o un modal si prefieres
            string descripcion = Microsoft.VisualBasic.Interaction.InputBox("Descripción de la zona:", "Nueva Zona");

            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                _zonaService.RegistrarZona(descripcion, loc.Id);
                CargarZonas(loc.Id);
            }
        }

        private void EliminarZona_Click(object sender, RoutedEventArgs e)
        {
            if (ZonasListBox.SelectedItem is ZonaPromotoria zona)
            {
                _zonaService.EliminarZona(zona.Id);
                if (LocalidadComboBox.SelectedItem is Localidad loc) CargarZonas(loc.Id);
            }
        }
    }
}