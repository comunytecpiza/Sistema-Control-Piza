
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Views
{
    public partial class LocalidadesUserControl : UserControl // Cambiado a UserControl
    {
        private ObservableCollection<Localidad> localidades = new ObservableCollection<Localidad>();
        
        private readonly LocalidadService _service;
        private Localidad? _currentLocalidad;
        public LocalidadesUserControl()
        {
            InitializeComponent();
            _service = new LocalidadService();

            LocalidadesGrid.ItemsSource = localidades;

            this.Loaded += async (s, e) => {
                await LoadEstados();
                await LoadLocalidades();
            };
        }

        private async Task LoadEstados()
        {
            CmbEstadoLocalidad.Items.Clear();
            var estados = await _service.ObtenerEstadosAsync();
            foreach (var estado in estados)
            {
                CmbEstadoLocalidad.Items.Add(new ComboBoxItem { Content = estado.Nombre, Tag = estado.Id });
            }
        }
        private async Task LoadLocalidades()
        {
            localidades.Clear();

            var lista = await _service.ObtenerTodosAsync();

            foreach (var item in lista)
            {
                localidades.Add(item);
            }

            LocalidadesGrid.ItemsSource = localidades;
        }

        private void LocalidadesSearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = LocalidadesSearchBox.Text.ToLower();
            var filteredLocalidades = localidades.Where(l =>
                l.Nombre.ToLower().Contains(searchTerm) ||
                l.Id.ToString().Contains(searchTerm));
            LocalidadesGrid.ItemsSource = filteredLocalidades;
        }

        private void AddLocalidadButton_Click(object sender, RoutedEventArgs e)
        {
            _currentLocalidad = null;
            TxtNombreLocalidad.Clear();
            if (CmbEstadoLocalidad.Items.Count > 0)
                CmbEstadoLocalidad.SelectedIndex = 0;
            AddEditLocalidadModal.Visibility = Visibility.Visible;
        }
        private void EditLocalidadButton_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadesGrid.SelectedItem is not Localidad localidad)
            {
                MessageBox.Show("Seleccione una localidad.");
                return;
            }

            _currentLocalidad = localidad;

            TxtNombreLocalidad.Text = localidad.Nombre;

            if (localidad.Estado != null)
            {
                var item = CmbEstadoLocalidad.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(x => (int)x.Tag == localidad.Estado.Id);
                CmbEstadoLocalidad.SelectedItem = item;
            }

            AddEditLocalidadModal.Visibility = Visibility.Visible;
        }

        private async void GuardarLocalidad_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombreLocalidad.Text))
            {
                MessageBox.Show("Ingrese el nombre.");
                return;
            }

            if (CmbEstadoLocalidad.SelectedItem == null)
            {
                MessageBox.Show("Seleccione un estado.");
                return;
            }

            var localidad = new Localidad
            {
                Id = _currentLocalidad?.Id ?? 0,
                Nombre = TxtNombreLocalidad.Text,

                Estado = new Estado
                {
                    Id = (int)((ComboBoxItem)CmbEstadoLocalidad.SelectedItem).Tag
                }
            };

            await _service.GuardarAsync(localidad);

            await LoadLocalidades();

            AddEditLocalidadModal.Visibility = Visibility.Collapsed;
        }

        private void CancelarAddEditLocalidad_Click(object sender, RoutedEventArgs e)
        {
            AddEditLocalidadModal.Visibility = Visibility.Collapsed;
        }

        
    }
  
}