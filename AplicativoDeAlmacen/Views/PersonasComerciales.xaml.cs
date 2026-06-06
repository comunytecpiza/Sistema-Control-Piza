using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading.Tasks;

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

        private readonly PersonaComercialService _service;

        public PersonasComercialesWindow()
        {
            InitializeComponent();

            _service = new PersonaComercialService();

            PersonasDataGrid.ItemsSource = personas;

            Loaded += async (s, e) =>
            {
                await LoadPersonas();
            };
            LoadData();


            PersonasDataGrid.ItemsSource = personas;
            currentPersona = new PersonaComercial();
        }

        private void LoadData()
        {
          
            LoadTipoPersonas();
            LoadDepartamentos();
           // LoadLocalidades();
            LoadEstados();
        }

        private async Task LoadPersonas()
        {
            personas.Clear();

            var lista = await _service.ObtenerTodosAsync();

            foreach (var item in lista)
            {
                personas.Add(item);
            }
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
        /*
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
        */
        /*
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
        */
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
            currentPersona = null;
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

                LoadPersonaToForm();

                ModalTitle.Text = "Editar Persona Comercial";
                ModalBackground.Visibility = Visibility.Visible;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var persona = new PersonaComercial
            {
                Id = currentPersona?.Id ?? 0,
                Nombres = NombresTextBox.Text,
                ApellidoPaterno = ApellidoPaternoTextBox.Text,
                ApellidoMaterno = ApellidoMaternoTextBox.Text,
                RazonSocial = RazonSocialTextBox.Text,
                NombreComercial = NombreComercialTextBox.Text,
                Ruc = RucTextBox.Text,
                Dni = DniTextBox.Text,
                Direccion = DireccionTextBox.Text,

                Localidad = (LocalidadComboBox.SelectedItem is ComboBoxItem loc)
                ? new Localidad { Id = (int)loc.Tag }
                : null,

                Departamento = (DepartamentoComboBox.SelectedItem is ComboBoxItem dep)
                ? new Departamento { Id = (int)dep.Tag }
                : null,

                Provincia = (ProvinciaComboBox.SelectedItem is ComboBoxItem prov)
                ? new Provincia { Id = (int)prov.Tag }
                : null,

                Distrito = (DistritoComboBox.SelectedItem is ComboBoxItem dist)
                ? new Distrito { Id = (int)dist.Tag }
                : null,

                Estado = (EstadoComboBox.SelectedItem is ComboBoxItem est)
                ? new Estado { Id = (int)est.Tag }
                : null,

                ZonaPromotoria = (ZonaPromotoriaComboBox.SelectedItem is ComboBoxItem zp)
                ? new ZonaPromotoria { Id = (int)zp.Tag }
                : null,

                TipoPersona = (TipoPersonaComboBox.SelectedItem is ComboBoxItem tp) ?
                new TipoPersona { Id = (int)tp.Tag } : null
            };

           

            if (ValidateForm())
            {
                await _service.GuardarAsync(persona);

                ModalBackground.Visibility = Visibility.Collapsed;

                await LoadPersonas(); // refrescar grid
            }

        }

        private void LoadPersonaToForm()
        {
            if (currentPersona == null)
                return;

            // Tipo Persona
            TipoPersonaComboBox.SelectedItem =
                TipoPersonaComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (int)x.Tag == currentPersona.TipoPersona?.Id);

            // Datos personales
            ApellidoPaternoTextBox.Text = currentPersona.ApellidoPaterno ?? "";
            ApellidoMaternoTextBox.Text = currentPersona.ApellidoMaterno ?? "";
            NombresTextBox.Text = currentPersona.Nombres ?? "";
            RazonSocialTextBox.Text = currentPersona.RazonSocial ?? "";
            NombreComercialTextBox.Text = currentPersona.NombreComercial ?? "";
            RucTextBox.Text = currentPersona.Ruc ?? "";
            DniTextBox.Text = currentPersona.Dni ?? "";
            DireccionTextBox.Text = currentPersona.Direccion ?? "";

            // Departamento
            DepartamentoComboBox.SelectedItem =
                DepartamentoComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (int)x.Tag == currentPersona.Departamento?.Id);


            // Cargar provincias del departamento
            if (currentPersona.Departamento != null)
            {
                LoadProvincias(currentPersona.Departamento.Id);

                ProvinciaComboBox.SelectedItem =
                    ProvinciaComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x =>
                        (int)x.Tag == currentPersona.Provincia?.Id);
            }

            // Cargar distritos de la provincia
            if (currentPersona.Provincia != null)
            {
                LoadDistritos(currentPersona.Provincia.Id);

                DistritoComboBox.SelectedItem =
                    DistritoComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x =>
                        (int)x.Tag == currentPersona.Distrito?.Id);
            }

            // Localidad
            LocalidadComboBox.SelectedItem =
                LocalidadComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (int)x.Tag == currentPersona.Localidad?.Id);

            // Zona Promotoria
            ZonaPromotoriaComboBox.SelectedItem =
                ZonaPromotoriaComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (int)x.Tag == currentPersona.ZonaPromotoria?.Id);

            // Estado
            EstadoComboBox.SelectedItem =
                EstadoComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (int)x.Tag == currentPersona.Estado?.Id);

            // Checkboxes
            DireccionFiscalCheckBox.IsChecked =
                !string.IsNullOrWhiteSpace(currentPersona.Direccion);

            InstitucionEducativaCheckBox.IsChecked =
                currentPersona.Localidad != null;

            // Actualizar habilitación de controles
            TipoPersonaComboBox_SelectionChanged(null, null);
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
           /* if (LocalidadComboBox.SelectedItem is ComboBoxItem selectedLocalidad)
            {
                int localidadId = (int)selectedLocalidad.Tag;
                LoadZonasPromotoria(localidadId);
                ZonaPromotoriaComboBox.IsEnabled = true;
            }*/
        }
    }

}