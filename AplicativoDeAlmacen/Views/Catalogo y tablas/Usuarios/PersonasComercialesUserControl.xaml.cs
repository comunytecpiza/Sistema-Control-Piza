using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Data;
using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Users;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;

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
        private string connectionString => ConfigManager.ObtenerCadenaConexion();

        private ObservableCollection<PersonaComercial> personas = new ObservableCollection<PersonaComercial>();
        private PersonaComercial? currentPersona;
        private readonly PersonaComercialService _service;

        // Bandera para evitar bucles de búsqueda
        private bool _isTyping = true;

        public PersonasComercialesUserControl()
        {
            InitializeComponent();
            _service = new PersonaComercialService();
            PersonasDataGrid.ItemsSource = personas;

            this.Loaded += async (s, e) =>
            {
                LoadDataCombos();
                await LoadPersonas();
            };
        }

        private void LoadDataCombos()
        {
            LoadTipoPersonas();
            LoadTiposDeNegocio(); // NUEVO
            LoadDepartamentos();
            LoadLocalidades();    // Corregido
            LoadEstados();
        }

        private async Task LoadPersonas()
        {
            try
            {
                personas.Clear();
                var lista = await _service.ObtenerTodosAsync();
                foreach (var item in lista) personas.Add(item);
            }
            catch (Exception ex) { MessageBox.Show("Error al cargar clientes: " + ex.Message); }
        }

        // ===============================================
        // BUSCADOR PREDICTIVO Y FILTROS (POPUP)
        // ===============================================
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AplicarFiltro();
        }

        private void FilterRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            // Validamos que los controles ya estén dibujados en pantalla
            if (SearchTextBox != null && personas != null)
            {
                AplicarFiltro();
            }
        }

        private void AplicarFiltro()
        {
            if (SearchTextBox == null || personas == null) return;

            string texto = SearchTextBox.Text.Trim().ToLower();

            // Si la caja está vacía, mostramos toda la lista
            if (string.IsNullOrEmpty(texto))
            {
                PersonasDataGrid.ItemsSource = personas;
                return;
            }

            // Aplicamos el filtro dependiendo de qué RadioButton esté marcado
            IEnumerable<PersonaComercial> filtrados = personas;

            if (RbRazonSocial.IsChecked == true)
                filtrados = personas.Where(p => p.RazonSocial != null && p.RazonSocial.ToLower().Contains(texto));

            else if (RbNombreComercial.IsChecked == true)
                filtrados = personas.Where(p => p.NombreComercial != null && p.NombreComercial.ToLower().Contains(texto));

            else if (RbRuc.IsChecked == true)
                filtrados = personas.Where(p => p.Ruc != null && p.Ruc.Contains(texto));

            else if (RbDni.IsChecked == true)
                filtrados = personas.Where(p => p.Dni != null && p.Dni.Contains(texto));

            // Actualizamos la grilla con los resultados
            PersonasDataGrid.ItemsSource = filtrados.ToList();
        }
        private void LstBuscador_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstBuscador.SelectedItem is PersonaComercial clienteSeleccionado)
            {
                _isTyping = false; // Pausamos el TextChanged
                SearchTextBox.Text = clienteSeleccionado.RazonSocial ?? $"{clienteSeleccionado.Nombres} {clienteSeleccionado.ApellidoPaterno}";
                PopBuscador.IsOpen = false;
                _isTyping = true;

                // Filtramos la grilla para mostrar SOLO al seleccionado
                PersonasDataGrid.ItemsSource = personas.Where(p => p.Id == clienteSeleccionado.Id).ToList();
            }
        }
        private void BtnLimpiarFiltro_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            RbRazonSocial.IsChecked = true; // Por defecto regresa a Razón Social
            PersonasDataGrid.ItemsSource = personas; // Restaura la grilla
        }

        // ===============================================
        // ACCIONES DE BOTONES PRINCIPALES
        // ===============================================
        private void PersonasDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PersonasDataGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            BtnDefinicionPrecios.IsEnabled = hasSelection;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            currentPersona = null;
            ClearForm();
            ModalTitle.Text = "Agregar Persona Comercial";
            ModalBackground.Visibility = Visibility.Visible;
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (PersonasDataGrid.SelectedItem is PersonaComercial p)
            {
                currentPersona = p;
                ClearForm();
                ModalTitle.Text = "Editar Persona Comercial";
                LoadPersonaToForm();
                ModalBackground.Visibility = Visibility.Visible;
            }
        }

        private void BtnDefinicionPrecios_Click(object sender, RoutedEventArgs e)
        {
            if (PersonasDataGrid.SelectedItem is PersonaComercial p)
            {
                // 1. Instanciamos tu nueva ventana pasándole el cliente completo
                DefinicionPreciosWindow modal = new DefinicionPreciosWindow(p);

                // 2. Asignamos el Owner para que el modal nazca centrado y no se pierda detrás de la ventana principal
                modal.Owner = Window.GetWindow(this);

                // 3. Abrimos la ventana en modo "Dialog" (bloquea el catálogo hasta que cierres los precios)
                modal.ShowDialog();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }

        // ===============================================
        // CARGA DE COMBOS (ADO.NET Directo como lo tenías)
        // ===============================================
        private void LoadTipoPersonas()
        {
            TipoPersonaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM tipo_persona";
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        TipoPersonaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                    }
                }
            }
        }

        private void LoadTiposDeNegocio()
        {
            CmbTipoNegocio.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM tipos_persona_comercial";
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CmbTipoNegocio.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
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
                using (SqlCommand command = new SqlCommand("SELECT id, nombre FROM departamentos", connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        DepartamentoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
        }

        private void LoadProvincias(int departamentoId)
        {
            ProvinciaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand("SELECT id, nombre FROM provincias WHERE departamento_id = @id", connection))
                {
                    command.Parameters.AddWithValue("@id", departamentoId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            ProvinciaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
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
                using (SqlCommand command = new SqlCommand("SELECT id, nombre FROM distritos WHERE provincia_id = @id", connection))
                {
                    command.Parameters.AddWithValue("@id", provinciaId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            DistritoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
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
                using (SqlCommand command = new SqlCommand("SELECT id, nombre FROM localidades", connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        LocalidadComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
        }

        private void LoadZonasPromotoria(int localidadId)
        {
            ZonaPromotoriaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand("SELECT id, descripcion FROM zona_promotoria WHERE localidad_id = @id", connection))
                {
                    command.Parameters.AddWithValue("@id", localidadId);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            ZonaPromotoriaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
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
                using (SqlCommand command = new SqlCommand("SELECT id, nombre FROM estados", connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        EstadoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            if (EstadoComboBox.Items.Count > 0) EstadoComboBox.SelectedIndex = 0;
        }

        // ===============================================
        // EVENTOS DE CASCADA Y LOGICA DE INTERFAZ
        // ===============================================
        private void DepartamentoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepartamentoComboBox.SelectedItem is ComboBoxItem dep)
            {
                LoadProvincias((int)dep.Tag);
                ProvinciaComboBox.IsEnabled = true;
            }
        }

        private void ProvinciaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProvinciaComboBox.SelectedItem is ComboBoxItem prov)
            {
                LoadDistritos((int)prov.Tag);
                DistritoComboBox.IsEnabled = true;
            }
        }

        private void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is ComboBoxItem loc)
            {
                LoadZonasPromotoria((int)loc.Tag);
                ZonaPromotoriaComboBox.IsEnabled = true;
            }
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

        private void InstitucionEducativaCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            LocalidadComboBox.IsEnabled = true;
        }

        private void InstitucionEducativaCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            LocalidadComboBox.IsEnabled = false;
            ZonaPromotoriaComboBox.IsEnabled = false;
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

                    ApellidoPaternoTextBox.TextChanged += UpdateRazonSocial;
                    ApellidoMaternoTextBox.TextChanged += UpdateRazonSocial;
                    NombresTextBox.TextChanged += UpdateRazonSocial;
                }
                else
                {
                    ApellidoPaternoTextBox.IsEnabled = false;
                    ApellidoMaternoTextBox.IsEnabled = false;
                    NombresTextBox.IsEnabled = false;
                    RazonSocialTextBox.IsEnabled = true;

                    ApellidoPaternoTextBox.TextChanged -= UpdateRazonSocial;
                    ApellidoMaternoTextBox.TextChanged -= UpdateRazonSocial;
                    NombresTextBox.TextChanged -= UpdateRazonSocial;
                }
            }
        }

        private void UpdateRazonSocial(object sender, TextChangedEventArgs e)
        {
            string apePat = ApellidoPaternoTextBox.Text.Trim();
            string apeMat = ApellidoMaternoTextBox.Text.Trim();
            string nom = NombresTextBox.Text.Trim();
            RazonSocialTextBox.Text = $"{apePat} {apeMat} {nom}".Trim();
        }

        // ===============================================
        // GUARDAR Y CARGAR DATOS
        // ===============================================
        private void ClearForm()
        {
            TipoPersonaComboBox.SelectedIndex = -1;
            CmbTipoNegocio.SelectedIndex = -1;
            ApellidoPaternoTextBox.Text = "";
            ApellidoMaternoTextBox.Text = "";
            NombresTextBox.Text = "";
            RazonSocialTextBox.Text = "";
            NombreComercialTextBox.Text = "";
            RucTextBox.Text = "";
            DniTextBox.Text = "";
            DireccionTextBox.Text = "";

            DepartamentoComboBox.SelectedIndex = -1;
            ProvinciaComboBox.SelectedIndex = -1;
            DistritoComboBox.SelectedIndex = -1;
            LocalidadComboBox.SelectedIndex = -1;
            ZonaPromotoriaComboBox.SelectedIndex = -1;

            DireccionFiscalCheckBox.IsChecked = false;
            InstitucionEducativaCheckBox.IsChecked = false;
            EstadoComboBox.SelectedIndex = 0;
        }

        private void LoadPersonaToForm()
        {
            if (currentPersona == null) return;

            // Tipo DNI/RUC
            TipoPersonaComboBox.SelectedItem = TipoPersonaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.TipoPersona?.Id);

            // Tipo de Negocio (Colegio, Empresa, etc)
            CmbTipoNegocio.SelectedItem = CmbTipoNegocio.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.TipoPersonaComercial?.Id);

            ApellidoPaternoTextBox.Text = currentPersona.ApellidoPaterno ?? "";
            ApellidoMaternoTextBox.Text = currentPersona.ApellidoMaterno ?? "";
            NombresTextBox.Text = currentPersona.Nombres ?? "";
            RazonSocialTextBox.Text = currentPersona.RazonSocial ?? "";
            NombreComercialTextBox.Text = currentPersona.NombreComercial ?? "";
            RucTextBox.Text = currentPersona.Ruc ?? "";
            DniTextBox.Text = currentPersona.Dni ?? "";
            DireccionTextBox.Text = currentPersona.Direccion ?? "";

            if (currentPersona.Departamento != null)
            {
                DireccionFiscalCheckBox.IsChecked = true;
                DepartamentoComboBox.SelectedItem = DepartamentoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.Departamento.Id);
                if (currentPersona.Provincia != null) ProvinciaComboBox.SelectedItem = ProvinciaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.Provincia.Id);
                if (currentPersona.Distrito != null) DistritoComboBox.SelectedItem = DistritoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.Distrito.Id);
            }

            if (currentPersona.Localidad != null)
            {
                InstitucionEducativaCheckBox.IsChecked = true;
                LocalidadComboBox.SelectedItem = LocalidadComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.Localidad.Id);
                if (currentPersona.ZonaPromotoria != null) ZonaPromotoriaComboBox.SelectedItem = ZonaPromotoriaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.ZonaPromotoria.Id);
            }

            EstadoComboBox.SelectedItem = EstadoComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.Estado?.Id);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validaciones básicas
            if (TipoPersonaComboBox.SelectedIndex == -1) { MessageBox.Show("Seleccione el Tipo Legal (DNI/RUC)."); return; }
            if (CmbTipoNegocio.SelectedIndex == -1) { MessageBox.Show("Seleccione el Tipo de Negocio (Colegio/Empresa)."); return; }
            if (string.IsNullOrWhiteSpace(RazonSocialTextBox.Text)) { MessageBox.Show("La Razón Social es obligatoria."); return; }

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

                TipoPersona = new TipoPersona { Id = (int)((ComboBoxItem)TipoPersonaComboBox.SelectedItem).Tag },
                TipoPersonaComercial = new TipoPersonaComercial { Id = (int)((ComboBoxItem)CmbTipoNegocio.SelectedItem).Tag },

                Localidad = LocalidadComboBox.SelectedItem is ComboBoxItem loc ? new Localidad { Id = (int)loc.Tag } : null,
                ZonaPromotoria = ZonaPromotoriaComboBox.SelectedItem is ComboBoxItem zp ? new ZonaPromotoria { Id = (int)zp.Tag } : null,
                Departamento = DepartamentoComboBox.SelectedItem is ComboBoxItem dep ? new Departamento { Id = (int)dep.Tag } : null,
                Provincia = ProvinciaComboBox.SelectedItem is ComboBoxItem prov ? new Provincia { Id = (int)prov.Tag } : null,
                Distrito = DistritoComboBox.SelectedItem is ComboBoxItem dist ? new Distrito { Id = (int)dist.Tag } : null,
                Estado = EstadoComboBox.SelectedItem is ComboBoxItem est ? new Estado { Id = (int)est.Tag } : null
            };

            await _service.GuardarAsync(persona);
            ModalBackground.Visibility = Visibility.Collapsed;
            await LoadPersonas(); // Refresca la grilla
        }
    }
}