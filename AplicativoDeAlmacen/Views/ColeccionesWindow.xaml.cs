using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class ColeccionesWindow : Window
    {
        private ObservableCollection<Coleccion> colecciones = new ObservableCollection<Coleccion>();
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        public ColeccionesWindow()
        {
            InitializeComponent();
            CargarColecciones();
            ColeccionesDataGrid.ItemsSource = colecciones;
        }

        private void CargarColecciones()
        {
            colecciones.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT c.id, c.ano, e.nombre as estado FROM colecciones c INNER JOIN estados e ON c.estado_id = e.id ORDER BY c.ano DESC";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            colecciones.Add(new Coleccion
                            {
                                Id = reader.GetInt32(0),
                                Ano = reader.GetInt32(1),
                                Estado = reader.GetString(2)
                            });
                        }
                    }
                }
            }
        }

        private void AgregarColeccion_Click(object sender, RoutedEventArgs e)
        {
            int siguienteAno;
            if (colecciones.Any())
            {
                siguienteAno = colecciones.Min(c => c.Ano);
                while (colecciones.Any(c => c.Ano == siguienteAno))
                {
                    siguienteAno++;
                }
            }
            else
            {
                siguienteAno = DateTime.Now.Year;
            }
            AnoTextBox.Text = siguienteAno.ToString();
            CargarEstados();
            ModalBackground.Visibility = Visibility.Visible;
        }

        private void CargarEstados()
        {
            EstadoComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM estados ORDER BY CASE WHEN nombre = 'Activo' THEN 0 ELSE 1 END, nombre";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
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

                            // Si es el estado "Activo", seleccionarlo por defecto
                            if (reader.GetString(1) == "Activo")
                            {
                                EstadoComboBox.SelectedItem = item;
                            }
                        }
                    }
                }
            }

            // Si no se encontró el estado "Activo", seleccionar el primer elemento
            if (EstadoComboBox.SelectedItem == null && EstadoComboBox.Items.Count > 0)
            {
                EstadoComboBox.SelectedIndex = 0;
            }
        }
        private void Agregar_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(AnoTextBox.Text, out int ano) && EstadoComboBox.SelectedItem is ComboBoxItem selectedEstado)
            {
                if (colecciones.Any(c => c.Ano == ano))
                {
                    MessageBox.Show($"Ya existe una colección para el año {ano}.");
                    return;
                }

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = "INSERT INTO colecciones (ano, estado_id) VALUES (@ano, @estadoId)";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ano", ano);
                        command.Parameters.AddWithValue("@estadoId", (int)selectedEstado.Tag);
                        command.ExecuteNonQuery();
                    }
                }
                CargarColecciones();
                ModalBackground.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageBox.Show("Por favor, ingrese un año válido y seleccione un estado.");
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }
    }

    public class Coleccion
    {
        public int Id { get; set; }
        public int Ano { get; set; }
        public string? Estado { get; set; }
    }
}
