using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Users;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;
using static AplicativoDeAlmacen.Data.DataConnection;

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
        // 🌟 Helper agnóstico para manejo de conexiones (MySQL / SQL Server)
        private readonly DatabaseConnection _database = new DatabaseConnection();

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
                await LoadDataCombosAsync();
                await LoadPersonas();
            };
        }

        private async Task LoadDataCombosAsync()
        {
            await LoadTipoPersonasAsync();
            await LoadTiposDeNegocioAsync();
            await LoadDepartamentosAsync();
            await LoadLocalidadesAsync();
            await LoadEstadosAsync();
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

        // Helper local para agregar parámetros agnósticos a DbCommand
        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
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
            if (SearchTextBox != null && personas != null)
            {
                AplicarFiltro();
            }
        }

        private void AplicarFiltro()
        {
            if (SearchTextBox == null || personas == null) return;

            string texto = SearchTextBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(texto))
            {
                PersonasDataGrid.ItemsSource = personas;
                return;
            }

            IEnumerable<PersonaComercial> filtrados = personas;

            if (RbRazonSocial.IsChecked == true)
                filtrados = personas.Where(p => p.RazonSocial != null && p.RazonSocial.ToLower().Contains(texto));

            else if (RbNombreComercial.IsChecked == true)
                filtrados = personas.Where(p => p.NombreComercial != null && p.NombreComercial.ToLower().Contains(texto));

            else if (RbRuc.IsChecked == true)
                filtrados = personas.Where(p => p.Ruc != null && p.Ruc.Contains(texto));

            else if (RbDni.IsChecked == true)
                filtrados = personas.Where(p => p.Dni != null && p.Dni.Contains(texto));

            PersonasDataGrid.ItemsSource = filtrados.ToList();
        }

        private void LstBuscador_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstBuscador.SelectedItem is PersonaComercial clienteSeleccionado)
            {
                _isTyping = false;
                SearchTextBox.Text = clienteSeleccionado.RazonSocial ?? $"{clienteSeleccionado.Nombres} {clienteSeleccionado.ApellidoPaterno}";
                PopBuscador.IsOpen = false;
                _isTyping = true;

                PersonasDataGrid.ItemsSource = personas.Where(p => p.Id == clienteSeleccionado.Id).ToList();
            }
        }

        private void BtnLimpiarFiltro_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            RbRazonSocial.IsChecked = true;
            PersonasDataGrid.ItemsSource = personas;
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
                DefinicionPreciosWindow modal = new DefinicionPreciosWindow(p);
                modal.Owner = Window.GetWindow(this);
                modal.ShowDialog();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ModalBackground.Visibility = Visibility.Collapsed;
        }

        // ===============================================
        // CARGA DE COMBOS (Agnóstico Async: MySQL / SQL Server)
        // ===============================================
        private async Task LoadTipoPersonasAsync()
        {
            TipoPersonaComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM tipo_persona");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    TipoPersonaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar TipoPersonas: {ex.Message}");
            }
        }

        private async Task LoadTiposDeNegocioAsync()
        {
            CmbTipoNegocio.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM tipos_persona_comercial");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    CmbTipoNegocio.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar TiposDeNegocio: {ex.Message}");
            }
        }

        private async Task LoadDepartamentosAsync()
        {
            DepartamentoComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM departamentos");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    DepartamentoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar Departamentos: {ex.Message}");
            }
        }

        private async Task LoadProvinciasAsync(int departamentoId)
        {
            ProvinciaComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM provincias WHERE departamento_id = @id");
                AgregarParametro(cmd, "@id", departamentoId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    ProvinciaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar Provincias: {ex.Message}");
            }
        }

        private async Task LoadDistritosAsync(int provinciaId)
        {
            DistritoComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM distritos WHERE provincia_id = @id");
                AgregarParametro(cmd, "@id", provinciaId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    DistritoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar Distritos: {ex.Message}");
            }
        }

        private async Task LoadLocalidadesAsync()
        {
            LocalidadComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM localidades");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    LocalidadComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar Localidades: {ex.Message}");
            }
        }

        private async Task LoadZonasPromotoriaAsync(int localidadId)
        {
            ZonaPromotoriaComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion FROM zona_promotoria WHERE localidad_id = @id");
                AgregarParametro(cmd, "@id", localidadId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    ZonaPromotoriaComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar ZonasPromotoria: {ex.Message}");
            }
        }

        private async Task LoadEstadosAsync()
        {
            EstadoComboBox.Items.Clear();
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM estados");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    EstadoComboBox.Items.Add(new ComboBoxItem { Content = reader.GetString(1), Tag = reader.GetInt32(0) });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar Estados: {ex.Message}");
            }

            if (EstadoComboBox.Items.Count > 0) EstadoComboBox.SelectedIndex = 0;
        }

        // ===============================================
        // EVENTOS DE CASCADA Y LOGICA DE INTERFAZ
        // ===============================================
        private async void DepartamentoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepartamentoComboBox.SelectedItem is ComboBoxItem dep)
            {
                await LoadProvinciasAsync((int)dep.Tag);
                ProvinciaComboBox.IsEnabled = true;
            }
        }

        private async void ProvinciaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProvinciaComboBox.SelectedItem is ComboBoxItem prov)
            {
                await LoadDistritosAsync((int)prov.Tag);
                DistritoComboBox.IsEnabled = true;
            }
        }

        private async void LocalidadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalidadComboBox.SelectedItem is ComboBoxItem loc)
            {
                await LoadZonasPromotoriaAsync((int)loc.Tag);
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

            TipoPersonaComboBox.SelectedItem = TipoPersonaComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == currentPersona.TipoPersona?.Id);
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
            await LoadPersonas();
        }
    }
}