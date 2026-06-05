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

                // Seleccionamos el primero por defecto (que gracias a tu SQL será "Activo")
                if (estados.Any())
                {
                    EstadoComboBox.SelectedIndex = 0;
                }
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

                foreach (var item in listaDb)
                {
                    colecciones.Add(item);
                }

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
            int siguienteAno;

            // Verificamos si hay colecciones que SÍ tengan un año asignado (no nulo)
            if (colecciones.Any(c => c.Ano.HasValue))
            {
                // Obtenemos el año mínimo asegurándonos de extraer el valor exacto (.Value)
                siguienteAno = colecciones.Where(c => c.Ano.HasValue).Min(c => c.Ano.Value);

                while (colecciones.Any(c => c.Ano == siguienteAno))
                {
                    siguienteAno++;
                }
            }
            else
            {
                // Si la lista está vacía o todas tienen el año nulo, usamos el año actual
                siguienteAno = DateTime.Now.Year;
            }

            AnoTextBox.Text = siguienteAno.ToString();

            if (EstadoComboBox.Items.Count > 0)
                EstadoComboBox.SelectedIndex = 0;

            ModalBackground.Visibility = Visibility.Visible;
        }
        private async void Agregar_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(AnoTextBox.Text, out int ano) && EstadoComboBox.SelectedValue is int estadoId)
            {
                if (colecciones.Any(c => c.Ano == ano))
                {
                    MessageBox.Show($"Ya existe una colección para el año {ano}.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var nuevaColeccion = new Coleccion
                    {
                        Ano = ano,
                        EstadoId = estadoId
                    };

                    await _coleccionService.InsertarAsync(nuevaColeccion);
                    MessageBox.Show("Colección agregada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

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