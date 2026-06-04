using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class TitulosWindow : Window
    {
        private ObservableCollection<Titulo> titulos = new ObservableCollection<Titulo>();
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private bool isEditing = false;
        private int editingId = 0;

        public TitulosWindow()
        {
            InitializeComponent();
            CargarTitulos();
            TitulosDataGrid.ItemsSource = titulos;
        }

        private void CargarTitulos()
        {
            titulos.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT t.id, t.nombre, e.nombre as estado FROM titulo_curso t INNER JOIN estados e ON t.estado_id = e.id";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            titulos.Add(new Titulo
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                                Estado = reader.GetString(2)
                            });
                        }
                    }
                }
            }
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = BuscarTextBox.Text.ToLower();
            var filteredItems = titulos.Where(t => t.Nombre.ToLower().Contains(searchText) || t.Estado.ToLower().Contains(searchText));
            TitulosDataGrid.ItemsSource = filteredItems;
        }

        private void AgregarTitulo_Click(object sender, RoutedEventArgs e)
        {
            isEditing = false;
            ModalTitle.Text = "Agregar Nuevo Título";
            DescripcionTextBox.Text = string.Empty;
            CargarEstados();
            ModalBackground.Visibility = Visibility.Visible;
        }

        private void EditarTitulo_Click(object sender, RoutedEventArgs e)
        {
            if (TitulosDataGrid.SelectedItem is Titulo selectedTitulo)
            {
                isEditing = true;
                editingId = selectedTitulo.Id;
                ModalTitle.Text = "Editar Título";
                DescripcionTextBox.Text = selectedTitulo.Nombre;
                CargarEstados(selectedTitulo.Estado);
                ModalBackground.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un título para editar.");
            }
        }

        private void CargarEstados(string selectedEstado = "Activo")
        {
            EstadoComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM estados ORDER BY CASE WHEN nombre = @selectedEstado THEN 0 ELSE 1 END, nombre";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@selectedEstado", selectedEstado);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ComboBoxItem item = new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            };
                            EstadoComboBox.Items.Add(item);
                            if (reader.GetString(1) == selectedEstado)
                            {
                                EstadoComboBox.SelectedItem = item;
                            }
                        }
                    }
                }
            }
            if (EstadoComboBox.SelectedItem == null && EstadoComboBox.Items.Count > 0)
            {
                EstadoComboBox.SelectedIndex = 0;
            }
        }

        private void Agregar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DescripcionTextBox.Text) || EstadoComboBox.SelectedItem == null)
            {
                MessageBox.Show("Por favor, complete todos los campos.");
                return;
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = isEditing ?
                    "UPDATE titulo_curso SET nombre = @nombre, estado_id = @estadoId WHERE id = @id" :
                    "INSERT INTO titulo_curso (nombre, estado_id) VALUES (@nombre, @estadoId)";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@nombre", DescripcionTextBox.Text);
                    command.Parameters.AddWithValue("@estadoId", ((ComboBoxItem)EstadoComboBox.SelectedItem).Tag);
                    if (isEditing)
                    {
                        command.Parameters.AddWithValue("@id", editingId);
                    }
                    command.ExecuteNonQuery();
                }
            }

            CargarTitulos();
            ModalBackground.Visibility = Visibility.Collapsed;
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }

        private void TitulosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Puedes agregar lógica adicional aquí si es necesario
        }
    }

    public class Titulo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Estado { get; set; }

        public Titulo()
        {
            Nombre = string.Empty;
            Estado = string.Empty;
        }
    }
}