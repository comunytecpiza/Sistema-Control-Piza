#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Data.SqlClient;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AplicativoDeAlmacen.Views
{
    public partial class UnidadesMedidaWindow : Window
    {
       
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private ObservableCollection<Unidad> Unidad = new ObservableCollection<Unidad>();
        private Unidad? currentUnidad;

        public UnidadesMedidaWindow()
        {
            InitializeComponent();
            LoadUnidades();
        }

        private void LoadUnidades()
        {
            try
            {
                Unidad.Clear();
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"SELECT l.id, l.descripcion, l.abreviatura, 
                             COALESCE(e.nombre, 'DESCONOCIDO') AS estado 
                             FROM unidad_medida l 
                             LEFT JOIN estados e ON l.estado_id = e.id";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Unidad.Add(new Unidad
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                    Abreviatura = reader.GetString(reader.GetOrdinal("abreviatura")),
                                    Estado = reader.GetString(reader.GetOrdinal("estado"))
                                });
                            }
                        }
                    }
                }
                UnidadGrid.ItemsSource = Unidad;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar unidades: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }






        private void UnidadSearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = UnidadSearchBox.Text.ToLower();
            var filteredUnidad = Unidad.Where(l =>
                l.Descripcion.ToLower().Contains(searchTerm) ||
                l.Id.ToString().Contains(searchTerm) ||
                l.Estado.ToLower().Contains(searchTerm));
            UnidadGrid.ItemsSource = filteredUnidad;
        }

        private void AddUnidadButton_Click(object sender, RoutedEventArgs e)
        {
            currentUnidad = null;
            TxtDescripcionUnidad.Text = "";
            CmbEstadoUnidad.SelectedIndex = 0;
            AddEditUnidadModal.Visibility = Visibility.Visible;
        }

        private void EditUnidadButton_Click(object sender, RoutedEventArgs e)
        {
            if (UnidadGrid.SelectedItem is Unidad selectedUnidad)
            {
                currentUnidad = selectedUnidad; // Changed from unidadSeleccionada to currentUnidad
                TxtDescripcionUnidad.Text = currentUnidad.Descripcion; // Changed from unidadSeleccionada to currentUnidad
                TxtAbreviatura.Text = currentUnidad.Abreviatura; // Changed from unidadSeleccionada to currentUnidad
                CmbEstadoUnidad.SelectedItem = currentUnidad.Estado == "ACTIVO" ? CmbEstadoUnidad.Items[0] : CmbEstadoUnidad.Items[1];

                AddEditUnidadModal.Visibility = Visibility.Visible;
                MainContent.IsEnabled = false;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una unidad para editar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void GuardarUnidad_Click(object sender, RoutedEventArgs e)
        {
            string descripcion = TxtDescripcionUnidad.Text;
            string abreviatura = TxtAbreviatura.Text;
            string estado = (CmbEstadoUnidad.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "ACTIVO";

            if (string.IsNullOrWhiteSpace(descripcion))
            {
                MessageBox.Show("Por favor, ingrese un nombre para la Unidad.");
                return;
            }

            try
            {
                if (currentUnidad == null)
                {
                    InsertUnidad(descripcion, abreviatura);
                }
                else
                {
                    UpdateUnidad(currentUnidad.Id, descripcion, abreviatura, estado);
                }

                // Cierra el modal y habilita el contenido principal
                AddEditUnidadModal.Visibility = Visibility.Collapsed;
                MainContent.IsEnabled = true;

                // Actualiza la lista de unidades
                LoadUnidades();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar la unidad: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void InsertUnidad(string descripcion, string abreviatura)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Verifica si el estado 'ACTIVO' existe en la tabla estados
                string estadoQuery = "SELECT id FROM estados WHERE nombre = @Estado";
                int estadoId;

                using (SqlCommand estadoCmd = new SqlCommand(estadoQuery, conn))
                {
                    estadoCmd.Parameters.AddWithValue("@Estado", "ACTIVO");
                    var result = estadoCmd.ExecuteScalar();

                    if (result != null)
                    {
                        estadoId = Convert.ToInt32(result);
                    }
                    else
                    {
                        MessageBox.Show("El estado 'ACTIVO' no existe en la tabla de estados.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Inserta la nueva unidad
                string insertQuery = "INSERT INTO unidad_medida (descripcion, abreviatura, estado_id) VALUES (@Descripcion, @Abreviatura, @EstadoId)";

                using (SqlCommand insertCmd = new SqlCommand(insertQuery, conn))
                {
                    insertCmd.Parameters.AddWithValue("@Descripcion", descripcion);
                    insertCmd.Parameters.AddWithValue("@Abreviatura", abreviatura); // Asegúrate de pasar el valor correcto aquí
                    insertCmd.Parameters.AddWithValue("@EstadoId", estadoId);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }


        private void UpdateUnidad(int id, string descripcion, string abreviatura, string estado)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"
            UPDATE unidad_medida 
            SET 
                descripcion = @Descripcion, 
                abreviatura = @Abreviatura, 
                estado_id = (SELECT id FROM estados WHERE nombre = @Estado) 
            WHERE id = @Id";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                    cmd.Parameters.AddWithValue("@Abreviatura", abreviatura); // Actualiza abreviatura
                    cmd.Parameters.AddWithValue("@Estado", estado);
                    cmd.ExecuteNonQuery();
                }
            }
        }



        private void CancelarAddEditUnidad_Click(object sender, RoutedEventArgs e)
        {
            AddEditUnidadModal.Visibility = Visibility.Collapsed;
            MainContent.IsEnabled = true;
        }

        private void CloseUnidadWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class Unidad
    {
        public int Id { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string Abreviatura { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }
}

