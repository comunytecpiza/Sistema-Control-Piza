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
    public partial class ColeccionesUserControl : UserControl
    {
        private readonly ColeccionService _coleccionService;
        private ObservableCollection<Coleccion> colecciones = new ObservableCollection<Coleccion>();
        private Coleccion? coleccionActual;

        public ColeccionesUserControl()
        {
            InitializeComponent();
            _coleccionService = new ColeccionService();
            _ = InicializarPantallaAsync();
        }

        private async Task InicializarPantallaAsync()
        {
            await CargarEstadosAsync();
            await CargarColeccionesAsync();
        }

        private async Task CargarEstadosAsync()
        {
            try
            {
                var estados = await _coleccionService.ObtenerEstadosAsync();
                EstadoComboBox.ItemsSource = estados;
                EstadoComboBox.DisplayMemberPath = "Nombre";
                EstadoComboBox.SelectedValuePath = "Id";

                if (estados.Any()) EstadoComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar estados: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarColeccionesAsync()
        {
            try
            {
                colecciones.Clear();
                var listaDb = await _coleccionService.ObtenerTodosAsync();

                foreach (var item in listaDb) colecciones.Add(item);

                ColeccionesDataGrid.ItemsSource = colecciones;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar las colecciones: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();
            var filtradas = colecciones.Where(c =>
                c.Ano.ToString().Contains(busqueda) ||
                (c.Estado?.Nombre?.ToLower() ?? "").Contains(busqueda) ||
                c.Id.ToString().Contains(busqueda)
            );
            ColeccionesDataGrid.ItemsSource = new ObservableCollection<Coleccion>(filtradas);
        }

        private void AgregarColeccion_Click(object sender, RoutedEventArgs e)
        {
            coleccionActual = null;
            ModalTitle.Text = "Nueva Colección";

            int siguienteAno = colecciones.Any(c => c.Ano.HasValue)
                ? colecciones.Where(c => c.Ano.HasValue).Max(c => c.Ano!.Value) + 1
                : DateTime.Now.Year;

            AnoTextBox.Text = siguienteAno.ToString();
            if (EstadoComboBox.Items.Count > 0) EstadoComboBox.SelectedIndex = 0;

            ModalBackground.Visibility = Visibility.Visible;
        }

        private void EditarColeccion_Click(object sender, RoutedEventArgs e)
        {
            if (ColeccionesDataGrid.SelectedItem is Coleccion seleccionada)
            {
                coleccionActual = seleccionada;
                ModalTitle.Text = "Editar Colección";
                AnoTextBox.Text = seleccionada.Ano?.ToString() ?? "";
                EstadoComboBox.SelectedValue = seleccionada.EstadoId;

                ModalBackground.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una colección para editar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void EliminarColeccion_Click(object sender, RoutedEventArgs e)
        {
            if (ColeccionesDataGrid.SelectedItem is Coleccion seleccionada)
            {
                var confirmacion = MessageBox.Show($"¿Está seguro de eliminar la colección del año {seleccionada.Ano}?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmacion != MessageBoxResult.Yes) return;

                try
                {
                    await _coleccionService.EliminarAsync(seleccionada.Id);
                    MessageBox.Show("Colección eliminada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    await CargarColeccionesAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Restricción de Kárdex", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una colección para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void Guardar_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(AnoTextBox.Text, out int ano) && EstadoComboBox.SelectedValue is int estadoId)
            {
                if ((coleccionActual == null || coleccionActual.Ano != ano) && colecciones.Any(c => c.Ano == ano))
                {
                    MessageBox.Show($"Ya existe una colección para el año {ano}.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var item = new Coleccion
                    {
                        Id = coleccionActual?.Id ?? 0,
                        Ano = ano,
                        EstadoId = estadoId
                    };

                    if (coleccionActual == null)
                    {
                        await _coleccionService.InsertarAsync(item);
                        MessageBox.Show("Colección registrada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        await _coleccionService.ActualizarAsync(item);
                        MessageBox.Show("Colección actualizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    ModalBackground.Visibility = Visibility.Collapsed;
                    await CargarColeccionesAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Por favor, ingrese un año válido y seleccione un estado.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }
    }
}