#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Data.SqlClient;
using System.Linq;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Views
{
    public partial class LocalidadesWindow : Window
    {
        
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private ObservableCollection<Localidad> localidades = new ObservableCollection<Localidad>();
        private Localidad currentLocalidad;

        public LocalidadesWindow()
        {
            InitializeComponent();
            LoadLocalidades();
        }

        private void LoadLocalidades()
        {
            localidades.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"SELECT l.id, l.nombre, COALESCE(e.nombre, 'DESCONOCIDO') AS estado 
                         FROM localidades l 
                         LEFT JOIN estados e ON l.estado_id = e.id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            localidades.Add(new Localidad
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                               /* Estado = reader.GetString(2)*/
                            });
                        }
                    }
                }
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
            currentLocalidad = null;
            TxtNombreLocalidad.Text = "";
            CmbEstadoLocalidad.SelectedIndex = 0;
            AddEditLocalidadModal.Visibility = Visibility.Visible;
        }

        private void EditLocalidadButton_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadesGrid.SelectedItem is Localidad selectedLocalidad)
            {
                currentLocalidad = selectedLocalidad;
                TxtNombreLocalidad.Text = currentLocalidad.Nombre;
             //   var estadoItem = CmbEstadoLocalidad.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentLocalidad.Estado);
             //   CmbEstadoLocalidad.SelectedItem = estadoItem ?? CmbEstadoLocalidad.Items[0];
                AddEditLocalidadModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una localidad para editar.");
            }
        }

        private void GuardarLocalidad_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombreLocalidad.Text;
            string estado = (CmbEstadoLocalidad.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "ACTIVO";

            if (string.IsNullOrWhiteSpace(nombre))
            {
                MessageBox.Show("Por favor, ingrese un nombre para la localidad.");
                return;
            }

            if (currentLocalidad == null)
            {
                InsertLocalidad(nombre);
            }
            else
            {
                UpdateLocalidad(currentLocalidad.Id, nombre, estado);
            }

            LoadLocalidades();
            AddEditLocalidadModal.Visibility = Visibility.Collapsed;
        }

        private void InsertLocalidad(string nombre)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "INSERT INTO localidades (nombre, estado_id) VALUES (@Nombre, 1)"; // Asumiendo que 1 es el ID para ACTIVO
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Nombre", nombre);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void UpdateLocalidad(int id, string nombre, string estado)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "UPDATE localidades SET nombre = @Nombre, estado_id = (SELECT id FROM estados WHERE nombre = @Estado) WHERE id = @Id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@Nombre", nombre);
                    cmd.Parameters.AddWithValue("@Estado", estado);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void CancelarAddEditLocalidad_Click(object sender, RoutedEventArgs e)
        {
            AddEditLocalidadModal.Visibility = Visibility.Collapsed;
        }

        private void CloseLocalidadesWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
    /*
    public class Localidad
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }*/
}