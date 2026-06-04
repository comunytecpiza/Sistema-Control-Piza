using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class UbicacionesWindow : Window
    {
       
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private ObservableCollection<Ubicacion> ubicaciones = new ObservableCollection<Ubicacion>();
        private Ubicacion? ubicacionActual;

        public UbicacionesWindow()
        {
            InitializeComponent();
            CargarUbicaciones();
        }

        private void CargarUbicaciones()
        {
            ubicaciones.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"
    SELECT u.id, u.descripcion, 
           tu.nombre AS tipo_ubicacion, 
           l.nombre AS localidad, 
           u.direccion, 
           d.nombre AS departamento, 
           p.nombre AS provincia, 
           di.nombre AS distrito, 
           e.nombre AS estado
    FROM ubicaciones u
    LEFT JOIN tipo_ubicacion tu ON u.tipo_ubicacion_id = tu.id
    LEFT JOIN localidades l ON u.localidad_id = l.id
    LEFT JOIN departamentos d ON u.departamento_id = d.id
    LEFT JOIN provincias p ON u.provincia_id = p.id
    LEFT JOIN distritos di ON u.distrito_id = di.id
    LEFT JOIN estados e ON u.estado_id = e.id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ubicaciones.Add(new Ubicacion
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                TipoUbicacion = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Localidad = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                Direccion = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                Departamento = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                Provincia = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                                Distrito = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                                Estado = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                            });
                        }
                    }
                }
            }
            UbicacionesDataGrid.ItemsSource = ubicaciones;
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(busqueda))
            {
                // Si el buscador está vacío, muestra todas las ubicaciones
                UbicacionesDataGrid.ItemsSource = ubicaciones;
            }
            else
            {
                // Filtra las ubicaciones basándose en el texto de búsqueda
                var resultados = ubicaciones.Where(u =>
                    u.Id.ToString().Contains(busqueda) ||
                    u.Descripcion.ToLower().Contains(busqueda) ||
                    u.TipoUbicacion.ToLower().Contains(busqueda) ||
                    u.Localidad.ToLower().Contains(busqueda) ||
                    u.Direccion.ToLower().Contains(busqueda) ||
                    u.Departamento.ToLower().Contains(busqueda) ||
                    u.Provincia.ToLower().Contains(busqueda) ||
                    u.Distrito.ToLower().Contains(busqueda) ||
                    u.Estado.ToLower().Contains(busqueda)
                );
                UbicacionesDataGrid.ItemsSource = new ObservableCollection<Ubicacion>(resultados);
            }
        }

        private void AgregarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            ubicacionActual = null;
            ModalTitle.Text = "Nueva Ubicación";
            LimpiarCamposUbicacion();
            CargarCombos();
            UbicacionModal.Visibility = Visibility.Visible;
        }

        private void EditarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            ubicacionActual = UbicacionesDataGrid.SelectedItem as Ubicacion;
            if (ubicacionActual != null)
            {
                ModalTitle.Text = "Editar Ubicación";
                CargarDatosUbicacion(ubicacionActual);
                CargarCombos();
                UbicacionModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una ubicación para editar.");
            }
        }

        private void LimpiarCamposUbicacion()
        {
            TxtDescripcion.Text = string.Empty;
            TxtDireccion.Text = string.Empty;
            CmbTipoUbicacion.SelectedIndex = -1;
            CmbLocalidad.SelectedIndex = -1;
            CmbDepartamento.SelectedIndex = -1;
            CmbProvincia.SelectedIndex = -1;
            CmbDistrito.SelectedIndex = -1;
            CmbEstado.SelectedIndex = -1;
        }

        private void CargarCombos()
        {
            CargarTiposUbicacion();
            CargarLocalidades();
            CargarDepartamentos();
            CargarEstados();
        }

        private void CargarDatosUbicacion(Ubicacion ubicacion)
        {
            TxtDescripcion.Text = ubicacion.Descripcion;
            TxtDireccion.Text = ubicacion.Direccion;
            SeleccionarItemEnCombo(CmbTipoUbicacion, ubicacion.TipoUbicacionId);
            SeleccionarItemEnCombo(CmbLocalidad, ubicacion.LocalidadId);
            SeleccionarItemEnCombo(CmbDepartamento, ubicacion.DepartamentoId);
            SeleccionarItemEnCombo(CmbProvincia, ubicacion.ProvinciaId);
            SeleccionarItemEnCombo(CmbDistrito, ubicacion.DistritoId);
            SeleccionarItemEnCombo(CmbEstado, ubicacion.EstadoId);
        }

        private void SeleccionarItemEnCombo(ComboBox comboBox, int id)
        {
            var item = comboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == id);
            if (item != null)
            {
                comboBox.SelectedItem = item;
            }
        }

        private void BtnGuardarUbicacion_Click(object sender, RoutedEventArgs e)
        {
            if (ValidarCampos())
            {
                if (ubicacionActual == null)
                {
                    InsertarNuevaUbicacion();
                }
                else
                {
                    ActualizarUbicacion();
                }
                UbicacionModal.Visibility = Visibility.Collapsed;
                CargarUbicaciones();
            }
        }

        private bool ValidarCampos()
        {
            // Implementar validación de campos
            return true; // Cambiar según la lógica de validación
        }

        private void InsertarNuevaUbicacion()
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"INSERT INTO ubicaciones (descripcion, tipo_ubicacion_id, localidad_id, direccion, departamento_id, provincia_id, distrito_id, estado_id) 
                         VALUES (@Descripcion, @TipoUbicacionId, @LocalidadId, @Direccion, @DepartamentoId, @ProvinciaId, @DistritoId, @EstadoId)";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Descripcion", TxtDescripcion.Text);
                    cmd.Parameters.AddWithValue("@TipoUbicacionId", ((ComboBoxItem)CmbTipoUbicacion.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@LocalidadId", ((ComboBoxItem)CmbLocalidad.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@Direccion", TxtDireccion.Text);
                    cmd.Parameters.AddWithValue("@DepartamentoId", ((ComboBoxItem)CmbDepartamento.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@ProvinciaId", ((ComboBoxItem)CmbProvincia.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@DistritoId", ((ComboBoxItem)CmbDistrito.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@EstadoId", ((ComboBoxItem)CmbEstado.SelectedItem).Tag);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void ActualizarUbicacion()
        {
            if (ubicacionActual == null)
            {
                MessageBox.Show("Error: No se puede actualizar porque no hay una ubicación seleccionada.");
                return;
            }
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"UPDATE ubicaciones 
                         SET descripcion = @Descripcion, 
                             tipo_ubicacion_id = @TipoUbicacionId, 
                             localidad_id = @LocalidadId, 
                             direccion = @Direccion, 
                             departamento_id = @DepartamentoId, 
                             provincia_id = @ProvinciaId, 
                             distrito_id = @DistritoId, 
                             estado_id = @EstadoId
                         WHERE id = @Id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", ubicacionActual.Id);
                    cmd.Parameters.AddWithValue("@Descripcion", TxtDescripcion.Text);
                    cmd.Parameters.AddWithValue("@TipoUbicacionId", ((ComboBoxItem)CmbTipoUbicacion.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@LocalidadId", ((ComboBoxItem)CmbLocalidad.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@Direccion", TxtDireccion.Text);
                    cmd.Parameters.AddWithValue("@DepartamentoId", ((ComboBoxItem)CmbDepartamento.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@ProvinciaId", ((ComboBoxItem)CmbProvincia.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@DistritoId", ((ComboBoxItem)CmbDistrito.SelectedItem).Tag);
                    cmd.Parameters.AddWithValue("@EstadoId", ((ComboBoxItem)CmbEstado.SelectedItem).Tag);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void BtnCancelarUbicacion_Click(object sender, RoutedEventArgs e)
        {
            UbicacionModal.Visibility = Visibility.Collapsed;
        }

        private void CmbDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDepartamento.SelectedItem != null)
            {
                int departamentoId = (int)((ComboBoxItem)CmbDepartamento.SelectedItem).Tag;
                CargarProvincias(departamentoId);
                CmbDistrito.ItemsSource = null;
            }
        }

        private void CmbProvincia_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProvincia.SelectedItem != null)
            {
                int provinciaId = (int)((ComboBoxItem)CmbProvincia.SelectedItem).Tag;
                CargarDistritos(provinciaId);
            }
        }

        private void CargarTiposUbicacion()
        {
            CmbTipoUbicacion.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM tipo_ubicacion ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbTipoUbicacion.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarLocalidades()
        {
            CmbLocalidad.Items.Clear();
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
                            CmbLocalidad.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarDepartamentos()
        {
            CmbDepartamento.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM departamentos ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbDepartamento.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarProvincias(int departamentoId)
        {
            CmbProvincia.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM provincias WHERE departamento_id = @DepartamentoId ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@DepartamentoId", departamentoId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbProvincia.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarDistritos(int provinciaId)
        {
            CmbDistrito.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM distritos WHERE provincia_id = @ProvinciaId ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ProvinciaId", provinciaId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbDistrito.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarEstados()
        {
            CmbEstado.Items.Clear();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, nombre FROM estados ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbEstado.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }
    }

    public class Ubicacion
    {
        public int Id { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public int TipoUbicacionId { get; set; }
        public string TipoUbicacion { get; set; } = string.Empty;
        public int LocalidadId { get; set; }
        public string Localidad { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public int DepartamentoId { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int ProvinciaId { get; set; }
        public string Provincia { get; set; } = string.Empty;
        public int DistritoId { get; set; }
        public string Distrito { get; set; } = string.Empty;
        public int EstadoId { get; set; }
        public string Estado { get; set; } = string.Empty;
    }
}