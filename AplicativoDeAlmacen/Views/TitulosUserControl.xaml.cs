using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Views
{
    public partial class TitulosUserControl : UserControl
    {
        private readonly TituloCursoService _tituloService;
        private ObservableCollection<TituloCurso> titulos = new ObservableCollection<TituloCurso>();
        private TituloCurso? tituloActual;

        public TitulosUserControl()
        {
            InitializeComponent();
            _tituloService = new TituloCursoService();
            _ = InicializarPantallaAsync();
        }

        private async Task InicializarPantallaAsync()
        {
            await CargarEstadosAsync();
            await CargarTitulosAsync();
        }

        private async Task CargarEstadosAsync()
        {
            try
            {
                EstadoComboBox.ItemsSource = await _tituloService.ObtenerEstadosAsync();
                EstadoComboBox.DisplayMemberPath = "Nombre";
                EstadoComboBox.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar estados: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarTitulosAsync()
        {
            try
            {
                titulos.Clear();
                var listaDb = await _tituloService.ObtenerTodosAsync();

                foreach (var item in listaDb)
                {
                    titulos.Add(item);
                }

                TitulosDataGrid.ItemsSource = titulos;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar los títulos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();
            var filtrados = titulos.Where(t =>
                (t.Nombre?.ToLower() ?? "").Contains(busqueda) ||
                (t.Estado?.Nombre?.ToLower() ?? "").Contains(busqueda) ||
                t.Id.ToString().Contains(busqueda)
            );
            TitulosDataGrid.ItemsSource = new ObservableCollection<TituloCurso>(filtrados);
        }

        private void AgregarTitulo_Click(object sender, RoutedEventArgs e)
        {
            tituloActual = null;
            ModalTitle.Text = "Agregar Nuevo Título";
            DescripcionTextBox.Text = string.Empty;
            EstadoComboBox.SelectedIndex = 0; // Seleccionar el primero por defecto
            ModalBackground.Visibility = Visibility.Visible;
        }

        private void EditarTitulo_Click(object sender, RoutedEventArgs e)
        {
            if (TitulosDataGrid.SelectedItem is TituloCurso seleccionado)
            {
                tituloActual = seleccionado;
                ModalTitle.Text = "Editar Título";
                DescripcionTextBox.Text = seleccionado.Nombre;
                EstadoComboBox.SelectedValue = seleccionado.EstadoId;

                ModalBackground.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un título para editar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void Guardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DescripcionTextBox.Text) || EstadoComboBox.SelectedValue == null)
            {
                MessageBox.Show("Por favor, complete todos los campos (Descripción y Estado).", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var tituloNuevo = new TituloCurso
                {
                    Id = tituloActual?.Id ?? 0,
                    Nombre = DescripcionTextBox.Text.Trim(),
                    EstadoId = (int?)EstadoComboBox.SelectedValue
                };

                if (tituloActual == null)
                {
                    await _tituloService.InsertarAsync(tituloNuevo);
                    MessageBox.Show("Título agregado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _tituloService.ActualizarAsync(tituloNuevo);
                    MessageBox.Show("Título actualizado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                ModalBackground.Visibility = Visibility.Collapsed;
                await CargarTitulosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }
    }
}