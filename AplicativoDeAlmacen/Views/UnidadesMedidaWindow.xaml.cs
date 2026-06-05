using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Services;      // Tu servicio
using AplicativoDeAlmacen.Models.Models; // Tus modelos de la BD

namespace AplicativoDeAlmacen.Views
{
    public partial class UnidadesMedidaUserControl : UserControl
    {
        private readonly UnidadMedidaService _unidadService;
        private ObservableCollection<UnidadMedida> unidades = new ObservableCollection<UnidadMedida>();
        private UnidadMedida? unidadActual;

        public UnidadesMedidaUserControl()
        {
            InitializeComponent();
            _unidadService = new UnidadMedidaService();
            _ = InicializarPantallaAsync();
        }

        private async Task InicializarPantallaAsync()
        {
            // Primero cargamos el ComboBox de Estados desde la BD
            await CargarCombosAsync();
            // Luego cargamos la tabla
            await CargarUnidadesAsync();
        }

        private async Task CargarCombosAsync()
        {
            try
            {
                CmbEstadoUnidad.ItemsSource = await _unidadService.ObtenerEstadosAsync();
                CmbEstadoUnidad.DisplayMemberPath = "Nombre";
                CmbEstadoUnidad.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar estados: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarUnidadesAsync()
        {
            try
            {
                unidades.Clear();
                var listaDb = await _unidadService.ObtenerTodosAsync();

                foreach (var item in listaDb)
                {
                    unidades.Add(item);
                }

                UnidadGrid.ItemsSource = unidades;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar unidades: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnidadSearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = UnidadSearchBox.Text.ToLower();
            var filtradas = unidades.Where(l =>
                (l.Descripcion?.ToLower() ?? "").Contains(searchTerm) ||
                (l.Abreviatura?.ToLower() ?? "").Contains(searchTerm) ||
                l.Id.ToString().Contains(searchTerm) ||
                (l.Estado?.Nombre?.ToLower() ?? "").Contains(searchTerm));

            UnidadGrid.ItemsSource = new ObservableCollection<UnidadMedida>(filtradas);
        }

        private void AddUnidadButton_Click(object sender, RoutedEventArgs e)
        {
            unidadActual = null;
            ModalTitle.Text = "Nueva Unidad";
            TxtDescripcionUnidad.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            CmbEstadoUnidad.SelectedIndex = -1; // Lo dejamos en blanco al empezar

            AddEditUnidadModal.Visibility = Visibility.Visible;
        }

        private void EditUnidadButton_Click(object sender, RoutedEventArgs e)
        {
            if (UnidadGrid.SelectedItem is UnidadMedida selectedUnidad)
            {
                unidadActual = selectedUnidad;
                ModalTitle.Text = "Editar Unidad";
                TxtDescripcionUnidad.Text = unidadActual.Descripcion;
                TxtAbreviatura.Text = unidadActual.Abreviatura;

                // Selecciona el estado correcto por ID usando la magia de WPF
                CmbEstadoUnidad.SelectedValue = unidadActual.EstadoId;

                AddEditUnidadModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una unidad para editar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void GuardarUnidad_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDescripcionUnidad.Text))
            {
                MessageBox.Show("Por favor, ingrese un nombre para la Unidad.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Construimos el objeto a guardar
                var u = new UnidadMedida
                {
                    Id = unidadActual?.Id ?? 0,
                    Descripcion = TxtDescripcionUnidad.Text,
                    Abreviatura = TxtAbreviatura.Text,
                    EstadoId = (int)(int?)CmbEstadoUnidad.SelectedValue
                };

                if (unidadActual == null)
                {
                    await _unidadService.InsertarAsync(u);
                    MessageBox.Show("Unidad registrada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _unidadService.ActualizarAsync(u);
                    MessageBox.Show("Unidad actualizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                AddEditUnidadModal.Visibility = Visibility.Collapsed;
                await CargarUnidadesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar la unidad: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelarAddEditUnidad_Click(object sender, RoutedEventArgs e)
        {
            AddEditUnidadModal.Visibility = Visibility.Collapsed;
        }
    }
}