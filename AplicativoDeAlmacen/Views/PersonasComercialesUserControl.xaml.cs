using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;

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

    public partial class PersonasComercialesUserControl : UserControl
    {
        // Conexión dinámica centralizada (Ya no hardcodeada)
        private string connectionString => ConfigManager.ObtenerCadenaConexion();

        private ObservableCollection<PersonaComercial> personas = new ObservableCollection<PersonaComercial>();
        private PersonaComercial? currentPersona;
        private readonly PersonaComercialService _service;

        public PersonasComercialesUserControl()
        {
            InitializeComponent();
            _service = new PersonaComercialService();
            PersonasDataGrid.ItemsSource = personas;

            // Manejador Loaded 100% Asíncronico y seguro
            this.Loaded += async (s, e) =>
            {
                await LoadDataAsync();
                await LoadPersonasAsync();
            };
        }

        private async Task LoadDataAsync()
        {
            try
            {
                // Cargamos todos los diccionarios y combos en paralelo/secuencia asíncrona
                await LoadTipoPersonasAsync();
                await LoadDepartamentosAsync();
                await LoadEstadosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar catálogos iniciales: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task LoadPersonasAsync()
        {
            try
            {
                personas.Clear();
                var lista = await _service.ObtenerTodosAsync();
                foreach (var item in lista) personas.Add(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar personas comerciales: " + ex.Message);
            }
        }

        private async Task LoadTipoPersonasAsync()
        {
            TipoPersonaComboBox.Items.Clear();
            string query = "SELECT id, nombre FROM tipo_persona";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
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

        private async Task LoadDepartamentosAsync()
        {
            DepartamentoComboBox.Items.Clear();
            string query = "SELECT id, nombre FROM departamentos";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
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

        private async Task LoadProvinciasAsync(int departamentoId)
        {
            ProvinciaComboBox.Items.Clear();
            string query = "SELECT id, nombre FROM provincias WHERE departamento_id = @departamentoId";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@departamentoId", departamentoId);
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
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

        private async Task LoadDistritosAsync(int provinciaId)
        {
            DistritoComboBox.Items.Clear();
            string query = "SELECT id, nombre FROM distritos WHERE provincia_id = @provinciaId";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@provinciaId", provinciaId);
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
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

        private async Task LoadEstadosAsync()
        {
            EstadoComboBox.Items.Clear();
            string query = "SELECT id, nombre FROM estados";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        EstadoComboBox.Items.Add(new ComboBoxItem
                        {
                            Content = reader.GetString(1),
                            Tag = reader.GetInt32(0)
                        });
                    }
                }
            }

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
            currentPersona = null; // Nos aseguramos de limpiar el estado para un nuevo registro
            ClearForm();
            ModalTitle.Text = "Agregar Persona";
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

            // Restablecer estados visuales
            ApellidoPaternoTextBox.IsEnabled = false;
            ApellidoMaternoTextBox.IsEnabled = false;
            NombresTextBox.IsEnabled = false;
            RazonSocialTextBox.IsEnabled = false;
            NombreComercialTextBox.IsEnabled = false;
            DniTextBox.IsEnabled = false;
            RucTextBox.IsEnabled = false;
            DireccionFiscalCheckBox.IsEnabled = false;

            // Quitar eventos anteriores para evitar acumulaciones de memoria
            ApellidoPaternoTextBox.TextChanged -= UpdateRazonSocial;
            ApellidoMaternoTextBox.TextChanged -= UpdateRazonSocial;
            NombresTextBox.TextChanged -= UpdateRazonSocial;
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (PersonasDataGrid.SelectedItem is PersonaComercial p)
            {
                currentPersona = p;
                ModalTitle.Text = "Editar Persona";
                await LoadPersonaToFormAsync();
                ModalBackground.Visibility = Visibility.Visible;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm()) return;

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

                Localidad = (LocalidadComboBox.SelectedItem is ComboBoxItem loc) ? new Localidad { Id = (int)loc.Tag } : null,
                Departamento = (DepartamentoComboBox.SelectedItem is ComboBoxItem dep) ? new Departamento { Id = (int)dep.Tag } : null,
                Provincia = (ProvinciaComboBox.SelectedItem is ComboBoxItem prov) ? new Provincia { Id = (int)prov.Tag } : null,
                Distrito = (DistritoComboBox.SelectedItem is ComboBoxItem dist) ? new Distrito { Id = (int)dist.Tag } : null,
                Estado = (EstadoComboBox.SelectedItem is ComboBoxItem est) ? new Estado { Id = (int)est.Tag } : null,
                ZonaPromotoria = (ZonaPromotoriaComboBox.SelectedItem is ComboBoxItem zp) ? new ZonaPromotoria { Id = (int)zp.Tag } : null,
                TipoPersona = (TipoPersonaComboBox.SelectedItem is ComboBoxItem tp) ? new TipoPersona { Id = (int)tp.Tag } : null
            };

            try
            {
                await _service.GuardarAsync(persona);
                ModalBackground.Visibility = Visibility.Collapsed;
                await LoadPersonasAsync(); // Refrescar Grid
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message);
            }
        }

        private async Task LoadPersonaToFormAsync()
        {
            if (currentPersona == null) return;

            // Tipo Persona
            TipoPersonaComboBox.SelectedItem = TipoPersonaComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => (int)x.Tag == currentPersona.TipoPersona?.Id);

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
            DepartamentoComboBox.SelectedItem = DepartamentoComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => (int)x.Tag == currentPersona.Departamento?.Id);

            // Cargar provincias del departamento de forma asíncrona
            if (currentPersona.Departamento != null)
            {
                await LoadProvinciasAsync(currentPersona.Departamento.Id);

                ProvinciaComboBox.SelectedItem = ProvinciaComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => (int)x.Tag == currentPersona.Provincia?.Id);
            }

            // Cargar distritos de la provincia de forma asíncrona
            if (currentPersona.Provincia != null)
            {
                await LoadDistritosAsync(currentPersona.Provincia.Id);

                DistritoComboBox.SelectedItem = DistritoComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => (int)x.Tag == currentPersona.Distrito?.Id);
            }

            // Localidad
            LocalidadComboBox.SelectedItem = LocalidadComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => (int)x.Tag == currentPersona.Localidad?.Id);

            // Zona Promotoria
            ZonaPromotoriaComboBox.SelectedItem = ZonaPromotoriaComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => (int)x.Tag == currentPersona.ZonaPromotoria?.Id);

            // Estado
            EstadoComboBox.SelectedItem = EstadoComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => (int)x.Tag == currentPersona.Estado?.Id);

            // Checkboxes
            DireccionFiscalCheckBox.IsChecked = !string.IsNullOrWhiteSpace(currentPersona.Direccion);
            InstitucionEducativaCheckBox.IsChecked = currentPersona.Localidad != null;

            // Forzar actualización de campos habilitados
            TipoPersonaComboBox_SelectionChanged(null, null);
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

                // Desvincular manejadores temporales para evitar bucles visuales recurrentes
                ApellidoPaternoTextBox.TextChanged -= UpdateRazonSocial;
                ApellidoMaternoTextBox.TextChanged -= UpdateRazonSocial;
                NombresTextBox.TextChanged -= UpdateRazonSocial;

                if (tipoPersona == "Natural")
                {
                    ApellidoPaternoTextBox.IsEnabled = true;
                    ApellidoMaternoTextBox.IsEnabled = true;
                    NombresTextBox.IsEnabled = true;
                    RazonSocialTextBox.IsEnabled = false;
                    NombreComercialTextBox.IsEnabled = true;
                    DniTextBox.IsEnabled = true;
                    RucTextBox.IsEnabled = true;

                    // Enlazar el autocompletado reactivo de Razón Social
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
                }
                DireccionFiscalCheckBox.IsEnabled = true;
            }
        }

        private void UpdateRazonSocial(object sender, TextChangedEventArgs e)
        {
            string apellidoPaterno = ApellidoPaternoTextBox.Text.Trim();
            string apellidoMaterno = ApellidoMaternoTextBox.Text.Trim();
            string nombres = NombresTextBox.Text.Trim();

            RazonSocialTextBox.Text = $"{apellidoPaterno} {apellidoMaterno} {nombres}".Trim();
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

        private async void DepartamentoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepartamentoComboBox.SelectedItem is ComboBoxItem selectedDepartamento)
            {
                int departamentoId = (int)selectedDepartamento.Tag;
                await LoadProvinciasAsync(departamentoId);
                ProvinciaComboBox.IsEnabled = true;
            }
        }

        private async void ProvinciaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProvinciaComboBox.SelectedItem is ComboBoxItem selectedProvincia)
            {
                int provinciaId = (int)selectedProvincia.Tag;
                await LoadDistritosAsync(provinciaId);
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
              //  MessageBox.Show("Por favor, seleccione un tipo de persona.");
                return false;
            }

            string tipoPersona = (TipoPersonaComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

            if (tipoPersona == "Natural")
            {
                if (string.IsNullOrEmpty(ApellidoPaternoTextBox.Text) || string.IsNullOrEmpty(NombresTextBox.Text))
                {
                   // MessageBox.Show("Por favor, ingrese al menos el apellido paterno y nombres.");
                    return false;
                }
                if (string.IsNullOrEmpty(DniTextBox.Text) && string.IsNullOrEmpty(RucTextBox.Text))
                {
                   // MessageBox.Show("Por favor, ingrese el DNI o RUC.");
                    return false;
                }
            }
            else if (tipoPersona == "Jurídica")
            {
                if (string.IsNullOrEmpty(RazonSocialTextBox.Text))
                {
                  //  MessageBox.Show("Por favor, ingrese la razón social.");
                    return false;
                }
                if (string.IsNullOrEmpty(RucTextBox.Text))
                {
                   // MessageBox.Show("Por favor, ingrese el RUC.");
                    return false;
                }
            }

            if (string.IsNullOrEmpty(NombreComercialTextBox.Text))
            {
                //MessageBox.Show("Por favor, ingrese el nombre comercial.");
                return false;
            }

            return true;
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Espacio listo para la carga asíncrona de Zonas de Promotoría si se requiere en el futuro.
        }
    }
}