using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class ZonasPromotoriaWindow : Window
    {
      
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        public ZonasPromotoriaWindow()
        {
            InitializeComponent();
            CargarLocalidades();
        }

        private void CargarLocalidades()
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM localidades ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            LocalidadComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader["id"]
                            });
                        }
                    }
                }
            }
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem != null)
            {
                int localidadId = (int)((ComboBoxItem)LocalidadComboBox.SelectedItem).Tag;
                CargarZonas(localidadId);
            }
        }

        private void CargarZonas(int localidadId)
        {
            ZonasListBox.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, descripcion FROM zona_promotoria WHERE localidad_id = @LocalidadId";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@LocalidadId", localidadId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ZonasListBox.Items.Add(new ListBoxItem
                            {
                                Content = reader["descripcion"].ToString(),
                                Tag = reader["id"]
                            });
                        }
                    }
                }
            }
        }

        private void AgregarZona_Click(object sender, RoutedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem == null)
            {
                MessageBox.Show("Por favor, seleccione una localidad primero.");
                return;
            }

            string descripcion = Microsoft.VisualBasic.Interaction.InputBox("Ingrese la descripción de la nueva zona:", "Agregar Zona", "");
            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                int localidadId = (int)((ComboBoxItem)LocalidadComboBox.SelectedItem).Tag;
                AgregarZonaABaseDeDatos(descripcion, localidadId);
                CargarZonas(localidadId);
            }
        }

        private void AgregarZonaABaseDeDatos(string descripcion, int localidadId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "INSERT INTO zona_promotoria (descripcion, localidad_id) VALUES (@Descripcion, @LocalidadId)";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                    cmd.Parameters.AddWithValue("@LocalidadId", localidadId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void EliminarZona_Click(object sender, RoutedEventArgs e)
        {
            if (ZonasListBox.SelectedItem != null)
            {
                int zonaId = (int)((ListBoxItem)ZonasListBox.SelectedItem).Tag;
                if (MessageBox.Show("¿Está seguro de que desea eliminar esta zona?", "Confirmar eliminación", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    EliminarZonaDeBaseDeDatos(zonaId);
                    int localidadId = (int)((ComboBoxItem)LocalidadComboBox.SelectedItem).Tag;
                    CargarZonas(localidadId);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una zona para eliminar.");
            }
        }

        private void EliminarZonaDeBaseDeDatos(int zonaId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "DELETE FROM zona_promotoria WHERE id = @ZonaId";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ZonaId", zonaId);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}