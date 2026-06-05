using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Views
{
    public class TipoPersonaToIsReadOnlyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == "Natural";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class PersonasComercialesWindow : Window
    {
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        
        private ObservableCollection<PersonaComercial> personas = new ObservableCollection<PersonaComercial>();
        private PersonaComercial currentPersona;
        private bool isEditing = false;

        public PersonasComercialesWindow()
        {
            InitializeComponent();
            LoadData();
            PersonasDataGrid.ItemsSource = personas;
            currentPersona = new PersonaComercial();
        }

        private void LoadData()
        {
           // LoadPersonas();
            LoadTipoPersonas();
            LoadDepartamentos();
            LoadLocalidades();
            LoadEstados();
        }
        /*
        private void LoadPersonas()
        {
            personas.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = @"SELECT pc.*, tp.nombre AS tipo_persona, l.nombre AS localidad, zp.descripcion AS zona_promotoria, e.nombre AS estado,
                         d.nombre AS departamento, p.nombre AS provincia, di.nombre AS distrito
                         FROM personas_comerciales pc
                         LEFT JOIN tipo_persona tp ON pc.tipo_persona_id = tp.id
                         LEFT JOIN localidades l ON pc.localidad_id = l.id
                         LEFT JOIN zona_promotoria zp ON pc.zona_promotoria_id = zp.id
                         LEFT JOIN estados e ON pc.estado_id = e.id
                         LEFT JOIN departamentos d ON pc.departamento_id = d.id
                         LEFT JOIN provincias p ON pc.provincia_id = p.id
                         LEFT JOIN distritos di ON pc.distrito_id = di.id";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            personas.Add(new PersonaComercial
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                TipoPersona = reader.IsDBNull(reader.GetOrdinal("tipo_persona")) ? null : reader.GetString(reader.GetOrdinal("tipo_persona")),
                                Nombres = reader.IsDBNull(reader.GetOrdinal("nombres")) ? null : reader.GetString(reader.GetOrdinal("nombres")),
                                ApellidoPaterno = reader.IsDBNull(reader.GetOrdinal("apellido_paterno")) ? null : reader.GetString(reader.GetOrdinal("apellido_paterno")),
                                ApellidoMaterno = reader.IsDBNull(reader.GetOrdinal("apellido_materno")) ? null : reader.GetString(reader.GetOrdinal("apellido_materno")),
                                RazonSocial = reader.IsDBNull(reader.GetOrdinal("razon_social")) ? null : reader.GetString(reader.GetOrdinal("razon_social")),
                                NombreComercial = reader.IsDBNull(reader.GetOrdinal("nombre_comercial")) ? null : reader.GetString(reader.GetOrdinal("nombre_comercial")),
                                Ruc = reader.IsDBNull(reader.GetOrdinal("ruc")) ? null : reader.GetString(reader.GetOrdinal("ruc")),
                                Dni = reader.IsDBNull(reader.GetOrdinal("dni")) ? null : reader.GetString(reader.GetOrdinal("dni")),
                                Localidad = reader.IsDBNull(reader.GetOrdinal("localidad")) ? null : reader.GetString(reader.GetOrdinal("localidad")),
                                ZonaPromotoria = reader.IsDBNull(reader.GetOrdinal("zona_promotoria")) ? null : reader.GetString(reader.GetOrdinal("zona_promotoria")),
                                Estado = reader.IsDBNull(reader.GetOrdinal("estado")) ? null : reader.GetString(reader.GetOrdinal("estado")),
                                Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? null : reader.GetString(reader.GetOrdinal("direccion")),
                                Departamento = reader.IsDBNull(reader.GetOrdinal("departamento")) ? null : reader.GetString(reader.GetOrdinal("departamento")),
                                Provincia = reader.IsDBNull(reader.GetOrdinal("provincia")) ? null : reader.GetString(reader.GetOrdinal("provincia")),
                                Distrito = reader.IsDBNull(reader.GetOrdinal("distrito")) ? null : reader.GetString(reader.GetOrdinal("distrito"))
                            });
                        }
                    }
                }
            }
        }*/

        private void LoadTipoPersonas()
        {
            TipoPersonaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM tipo_persona";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            TipoPersonaComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadDepartamentos()
        {
            DepartamentoComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM departamentos";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DepartamentoComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadProvincias(int departamentoId)
        {
            ProvinciaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM provincias WHERE departamento_id = @departamentoId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@departamentoId", departamentoId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ProvinciaComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadDistritos(int provinciaId)
        {
            DistritoComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM distritos WHERE provincia_id = @provinciaId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@provinciaId", provinciaId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DistritoComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadLocalidades()
        {
            LocalidadComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM localidades WHERE estado_id = 1"; // Assuming 1 is the ID for active status
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            LocalidadComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadZonasPromotoria(int localidadId)
        {
            ZonaPromotoriaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, descripcion FROM zona_promotoria WHERE localidad_id = @localidadId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@localidadId", localidadId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ZonaPromotoriaComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void LoadEstados()
        {
            EstadoComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM estados";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            EstadoComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
            // Seleccionar el primer estado por defecto
            if (EstadoComboBox.Items.Count > 0)
            {
                EstadoComboBox.SelectedIndex = 0;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox != null)
              {
                string searchText = SearchTextBox.Text.ToLower();
                var filteredItems = personas.Where(p =>
                    (p.RazonSocial?.ToLower().Contains(searchText) ?? false) ||
                    (p.NombreComercial?.ToLower().Contains(searchText) ?? false) ||
                    (p.Ruc?.Contains(searchText) ?? false) ||
                    (p.Dni?.Contains(searchText) ?? false)
                );
                PersonasDataGrid.ItemsSource = filteredItems;
            }

        }
        private void FilterRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox != null)
            {
                SearchTextBox_TextChanged(SearchTextBox, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
            }
        }



        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            isEditing = false;
            currentPersona = new PersonaComercial();
            ClearForm();
            ModalTitle.Text = "Agregar Persona Comercial";
            ModalBackground.Visibility = Visibility.Visible;
        }


        private void ClearForm()
        {
            TipoPersonaComboBox.SelectedIndex = -1;
            ApellidoPaternoTextBox.Text = string.Empty;
            ApellidoMaternoTextBox.Text = string.Empty;
            NombresTextBox.Text = string.Empty;
            RazonSocialTextBox.Text = string.Empty;
            NombreComercialTextBox.Text = string.Empty;
            RucTextBox.Text = string.Empty;
            DniTextBox.Text = string.Empty;
            DireccionTextBox.Text = string.Empty;
            DepartamentoComboBox.SelectedIndex = -1;
            ProvinciaComboBox.SelectedIndex = -1;
            DistritoComboBox.SelectedIndex = -1;
            LocalidadComboBox.SelectedIndex = -1;
            ZonaPromotoriaComboBox.SelectedIndex = -1;
            DireccionFiscalCheckBox.IsChecked = false;
            InstitucionEducativaCheckBox.IsChecked = false;
            EstadoComboBox.SelectedIndex = 0;

            // Restablecer el estado habilitado/deshabilitado
            ApellidoPaternoTextBox.IsEnabled = false;
            ApellidoMaternoTextBox.IsEnabled = false;
            NombresTextBox.IsEnabled = false;
            RazonSocialTextBox.IsEnabled = false;
            NombreComercialTextBox.IsEnabled = false;
            DniTextBox.IsEnabled = false;
            RucTextBox.IsEnabled = false;
            DireccionFiscalCheckBox.IsEnabled = false;

            // Remover manejadores de eventos
            ApellidoPaternoTextBox.TextChanged -= UpdateRazonSocial;
            ApellidoMaternoTextBox.TextChanged -= UpdateRazonSocial;
            NombresTextBox.TextChanged -= UpdateRazonSocial;
        }





        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (PersonasDataGrid.SelectedItem is PersonaComercial selectedPersona)
            {
                isEditing = true;
                currentPersona = selectedPersona;
               // LoadPersonaToForm();
                ModalTitle.Text = "Editar Persona Comercial";
                ModalBackground.Visibility = Visibility.Visible;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateForm())
            {
                SavePersona();
                ModalBackground.Visibility = Visibility.Collapsed;
               // LoadPersonas();
            }
        }

/*
        private void LoadPersonaToForm()
        {
            TipoPersonaComboBox.SelectedItem = TipoPersonaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.TipoPersona);
            ApellidoPaternoTextBox.Text = currentPersona.ApellidoPaterno;
            ApellidoMaternoTextBox.Text = currentPersona.ApellidoMaterno;
            NombresTextBox.Text = currentPersona.Nombres;
            RazonSocialTextBox.Text = currentPersona.RazonSocial;
            NombreComercialTextBox.Text = string.IsNullOrEmpty(currentPersona.NombreComercial) ? currentPersona.RazonSocial : currentPersona.NombreComercial;
            RucTextBox.Text = currentPersona.Ruc;
            DniTextBox.Text = currentPersona.Dni;
            DireccionTextBox.Text = currentPersona.Direccion;
            DepartamentoComboBox.SelectedItem = DepartamentoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.Departamento);
            ProvinciaComboBox.SelectedItem = ProvinciaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.Provincia);
            DistritoComboBox.SelectedItem = DistritoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.Distrito);
            LocalidadComboBox.SelectedItem = LocalidadComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.Localidad);
            ZonaPromotoriaComboBox.SelectedItem = ZonaPromotoriaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.ZonaPromotoria);
            DireccionFiscalCheckBox.IsChecked = !string.IsNullOrEmpty(currentPersona.Direccion);
            InstitucionEducativaCheckBox.IsChecked = !string.IsNullOrEmpty(currentPersona.Localidad);
            EstadoComboBox.SelectedItem = EstadoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == currentPersona.Estado);
        }
*/
        private void SavePersona()
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = isEditing ?
                    @"UPDATE personas_comerciales 
              SET tipo_persona_id = @tipoPersona, nombres = @nombres, apellido_paterno = @apellidoPaterno, 
                  apellido_materno = @apellidoMaterno, razon_social = @razonSocial, nombre_comercial = @nombreComercial, 
                  ruc = @ruc, dni = @dni, direccion = @direccion, localidad_id = @localidad, 
                  zona_promotoria_id = @zonaPromotoria, estado_id = @estado, departamento_id = @departamento, 
                  provincia_id = @provincia, distrito_id = @distrito 
              WHERE id = @id"
                    :
                    @"INSERT INTO personas_comerciales 
              (tipo_persona_id, nombres, apellido_paterno, apellido_materno, razon_social, nombre_comercial, 
               ruc, dni, direccion, localidad_id, zona_promotoria_id, estado_id, departamento_id, provincia_id, distrito_id) 
              VALUES (@tipoPersona, @nombres, @apellidoPaterno, @apellidoMaterno, @razonSocial, @nombreComercial, 
                      @ruc, @dni, @direccion, @localidad, @zonaPromotoria, @estado, @departamento, @provincia, @distrito)";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    string tipoPersona = (TipoPersonaComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

                    AddParameterWithNullableValue(command, "@tipoPersona", (TipoPersonaComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@nombres", NombresTextBox.Text);
                    AddParameterWithNullableValue(command, "@apellidoPaterno", ApellidoPaternoTextBox.Text);
                    AddParameterWithNullableValue(command, "@apellidoMaterno", ApellidoMaternoTextBox.Text);
                    AddParameterWithNullableValue(command, "@razonSocial", RazonSocialTextBox.Text);
                    AddParameterWithNullableValue(command, "@nombreComercial", NombreComercialTextBox.Text);
                    AddParameterWithNullableValue(command, "@ruc", string.IsNullOrWhiteSpace(RucTextBox.Text) ? DBNull.Value : (object)RucTextBox.Text);
                    AddParameterWithNullableValue(command, "@dni", string.IsNullOrWhiteSpace(DniTextBox.Text) ? DBNull.Value : (object)DniTextBox.Text);
                    AddParameterWithNullableValue(command, "@direccion", DireccionTextBox.Text);
                    AddParameterWithNullableValue(command, "@localidad", (LocalidadComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@zonaPromotoria", (ZonaPromotoriaComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@estado", (EstadoComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@departamento", (DepartamentoComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@provincia", (ProvinciaComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);
                    AddParameterWithNullableValue(command, "@distrito", (DistritoComboBox.SelectedItem as ComboBoxItem)?.Tag ?? DBNull.Value);

                    if (isEditing)
                    {
                        command.Parameters.AddWithValue("@id", currentPersona.Id);
                    }

                    try
                    {
                        command.ExecuteNonQuery();
                        MessageBox.Show("Persona comercial guardada exitosamente.");
                    }
                    catch (SqlException ex)
                    {
                        MessageBox.Show($"Error al guardar la persona comercial: {ex.Message}");
                    }
                }
            }
        }


        private void AddParameterWithNullableValue(SqlCommand command, string parameterName, object value)
        {
            if (value == null || value == DBNull.Value || (value is string stringValue && string.IsNullOrWhiteSpace(stringValue)))
            {
                command.Parameters.AddWithValue(parameterName, DBNull.Value);
            }
            else
            {
                command.Parameters.AddWithValue(parameterName, value);
            }
        }






        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }

        private void PersonasDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditButton.IsEnabled = PersonasDataGrid.SelectedItem != null;
        }

        private void TipoPersonaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TipoPersonaComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string tipoPersona = selectedItem?.Content?.ToString() ?? string.Empty;
                if (tipoPersona == "Natural")
                {
                    ApellidoPaternoTextBox.IsEnabled = true;
                    ApellidoMaternoTextBox.IsEnabled = true;
                    NombresTextBox.IsEnabled = true;
                    RazonSocialTextBox.IsEnabled = false;
                    NombreComercialTextBox.IsEnabled = true;
                    DniTextBox.IsEnabled = true;
                    RucTextBox.IsEnabled = true;

                    // Agregar manejadores de eventos para actualizar la Razón Social
                    ApellidoPaternoTextBox.TextChanged += UpdateRazonSocial;
                    ApellidoMaternoTextBox.TextChanged += UpdateRazonSocial;
                    NombresTextBox.TextChanged += UpdateRazonSocial;
                }
                else if (tipoPersona == "Jurídica")
                {
                    ApellidoPaternoTextBox.IsEnabled = false;
                    ApellidoMaternoTextBox.IsEnabled = false;
                    NombresTextBox.IsEnabled = false;
                    RazonSocialTextBox.IsEnabled = true;
                    NombreComercialTextBox.IsEnabled = true;
                    DniTextBox.IsEnabled = false;
                    RucTextBox.IsEnabled = true;

                    // Remover manejadores de eventos
                    ApellidoPaternoTextBox.TextChanged -= UpdateRazonSocial;
                    ApellidoMaternoTextBox.TextChanged -= UpdateRazonSocial;
                    NombresTextBox.TextChanged -= UpdateRazonSocial;
                }
                DireccionFiscalCheckBox.IsEnabled = true;
            }
        }


        private void UpdateRazonSocial(object sender, TextChangedEventArgs e)
        {
            string apellidoPaterno = ApellidoPaternoTextBox.Text.Trim();
            string apellidoMaterno = ApellidoMaternoTextBox.Text.Trim();
            string nombres = NombresTextBox.Text.Trim();

            string razonSocial = $"{apellidoPaterno} {apellidoMaterno} {nombres}".Trim();
            RazonSocialTextBox.Text = razonSocial;
        }



       

        private void DireccionFiscalCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            DireccionTextBox.IsEnabled = true;
            DepartamentoComboBox.IsEnabled = true;
        }

        private void DireccionFiscalCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            DireccionTextBox.IsEnabled = false;
            DepartamentoComboBox.IsEnabled = false;
            ProvinciaComboBox.IsEnabled = false;
            DistritoComboBox.IsEnabled = false;
        }

        private void DepartamentoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepartamentoComboBox.SelectedItem is ComboBoxItem selectedDepartamento)
            {
                int departamentoId = (int)selectedDepartamento.Tag;
                LoadProvincias(departamentoId);
                ProvinciaComboBox.IsEnabled = true;
            }
        }

        private void ProvinciaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProvinciaComboBox.SelectedItem is ComboBoxItem selectedProvincia)
            {
                int provinciaId = (int)selectedProvincia.Tag;
                LoadDistritos(provinciaId);
                DistritoComboBox.IsEnabled = true;
            }
        }

        private void InstitucionEducativaCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            LocalidadComboBox.IsEnabled = true;
        }

        private void InstitucionEducativaCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            LocalidadComboBox.IsEnabled = false;
            ZonaPromotoriaComboBox.IsEnabled = false;
        }

        private bool ValidateForm()
        {
            if (TipoPersonaComboBox.SelectedIndex == -1)
            {
                MessageBox.Show("Por favor, seleccione un tipo de persona.");
                return false;
            }

            string tipoPersona = (TipoPersonaComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

            if (tipoPersona == "Natural")
            {
                if (string.IsNullOrEmpty(ApellidoPaternoTextBox.Text) || string.IsNullOrEmpty(NombresTextBox.Text))
                {
                    MessageBox.Show("Por favor, ingrese al menos el apellido paterno y nombres.");
                    return false;
                }
                if (string.IsNullOrEmpty(DniTextBox.Text) && string.IsNullOrEmpty(RucTextBox.Text))
                {
                    MessageBox.Show("Por favor, ingrese el DNI o RUC.");
                    return false;
                }
            }
            else if (tipoPersona == "Jurídica")
            {
                if (string.IsNullOrEmpty(RazonSocialTextBox.Text))
                {
                    MessageBox.Show("Por favor, ingrese la razón social.");
                    return false;
                }
                if (string.IsNullOrEmpty(RucTextBox.Text))
                {
                    MessageBox.Show("Por favor, ingrese el RUC.");
                    return false;
                }
            }

            if (string.IsNullOrEmpty(NombreComercialTextBox.Text))
            {
                MessageBox.Show("Por favor, ingrese el nombre comercial.");
                return false;
            }

            return true;
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is ComboBoxItem selectedLocalidad)
            {
                int localidadId = (int)selectedLocalidad.Tag;
                LoadZonasPromotoria(localidadId);
                ZonaPromotoriaComboBox.IsEnabled = true;
            }
        }
    }
    /*
    public class PersonaComercial
    {
        public int Id { get; set; }
        public string? TipoPersona { get; set; }
        public string? Nombres { get; set; }
        public string? ApellidoPaterno { get; set; }
        public string? ApellidoMaterno { get; set; }
        public string? RazonSocial { get; set; }
        public string? NombreComercial { get; set; }
        public string? Ruc { get; set; }
        public string? Dni { get; set; }
        public string? Localidad { get; set; }
        public string? ZonaPromotoria { get; set; }
        public string? Estado { get; set; }
        public string? Direccion { get; set; }
        public string? Departamento { get; set; }
        public string? Provincia { get; set; }
        public string? Distrito { get; set; }
    }*/
}