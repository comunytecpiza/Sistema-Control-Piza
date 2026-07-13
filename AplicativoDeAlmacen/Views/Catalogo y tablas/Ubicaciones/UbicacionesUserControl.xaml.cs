using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class UbicacionesUserControl : UserControl
    {
        private readonly UbicacionService _ubicacionService;
        private List<Ubicacion> _listadoCompletoUbicaciones = new List<Ubicacion>();
        private Ubicacion? _ubicacionActual;

        public UbicacionesUserControl()
        {
            InitializeComponent();
            _ubicacionService = new UbicacionService();

            // Usamos Loaded para asegurar que el control esté cargado
            this.Loaded += (s, e) => {
                ConfigurarFormatosCombos();
                CargarUbicaciones();
            };

            EventBus.OnUbicacionesChanged += () => Application.Current.Dispatcher.InvokeAsync(CargarUbicaciones);
        }

        private void ConfigurarFormatosCombos()
        {
            CmbTipoUbicacion.DisplayMemberPath = "Nombre";
            CmbLocalidad.DisplayMemberPath = "Nombre";
            CmbDepartamento.DisplayMemberPath = "Nombre";
            CmbProvincia.DisplayMemberPath = "Nombre";
            CmbDistrito.DisplayMemberPath = "Nombre";
            CmbEstado.DisplayMemberPath = "Nombre";
        }

        private void CargarUbicaciones()
        {
            try
            {
                _listadoCompletoUbicaciones = _ubicacionService.ObtenerTodas();
                UbicacionesDataGrid.ItemsSource = _listadoCompletoUbicaciones;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AsignarDatosAlFormulario(Ubicacion u)
        {
            TxtDescripcion.Text = u.Descripcion;
            TxtDireccion.Text = u.Direccion;

            // Buscamos e igualamos por ID el objeto de la lista del Combo para marcarlo como Seleccionado
            CmbTipoUbicacion.SelectedItem = CmbTipoUbicacion.Items.Cast<TipoUbicacion>().FirstOrDefault(x => x.Id == u.TipoUbicacion.Id);
            CmbLocalidad.SelectedItem = CmbLocalidad.Items.Cast<Localidad>().FirstOrDefault(x => x.Id == u.Localidad.Id);
            CmbEstado.SelectedItem = CmbEstado.Items.Cast<Estado>().FirstOrDefault(x => x.Id == u.Estado.Id);

            // Cargar cascada de ubigeo validando objetos existentes
            if (u.Departamento != null)
            {
                CmbDepartamento.SelectedItem = CmbDepartamento.Items.Cast<Departamento>().FirstOrDefault(x => x.Id == u.Departamento.Id);
                CmbProvincia.ItemsSource = _ubicacionService.ObtenerProvincias(u.Departamento.Id);

                if (u.Provincia != null)
                {
                    CmbProvincia.SelectedItem = CmbProvincia.Items.Cast<Provincia>().FirstOrDefault(x => x.Id == u.Provincia.Id);
                    CmbDistrito.ItemsSource = _ubicacionService.ObtenerDistritos(u.Provincia.Id);

                    if (u.Distrito != null)
                    {
                        CmbDistrito.SelectedItem = CmbDistrito.Items.Cast<Distrito>().FirstOrDefault(x => x.Id == u.Distrito.Id);
                    }
                }
            }
        }

        private void CmbDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDepartamento.SelectedItem is Departamento dep)
            {
                CmbProvincia.ItemsSource = _ubicacionService.ObtenerProvincias(dep.Id);
                CmbDistrito.ItemsSource = null;
            }
        }

        private void CmbProvincia_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProvincia.SelectedItem is Provincia prov)
            {
                CmbDistrito.ItemsSource = _ubicacionService.ObtenerDistritos(prov.Id);
            }
        }

        private void BtnGuardarUbicacion_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarCampos()) return;

            try
            {
                var u = _ubicacionActual ?? new Ubicacion();
                u.Descripcion = TxtDescripcion.Text;
                u.Direccion = TxtDireccion.Text;

                // Capturamos el objeto seleccionado completo y lo asignamos a la propiedad de la Ubicación
                u.TipoUbicacion = (TipoUbicacion)CmbTipoUbicacion.SelectedItem;
                u.Localidad = (Localidad)CmbLocalidad.SelectedItem;
                u.Estado = (Estado)CmbEstado.SelectedItem;

                u.Departamento = CmbDepartamento.SelectedItem as Departamento;
                u.Provincia = CmbProvincia.SelectedItem as Provincia;
                u.Distrito = CmbDistrito.SelectedItem as Distrito;

                if (_ubicacionActual == null)
                    _ubicacionService.Insertar(u);
                else
                    _ubicacionService.Actualizar(u);

                UbicacionModal.Visibility = Visibility.Collapsed;
                CargarUbicaciones();
                EventBus.NotificarUbicacionesChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidarCampos()
        {
            if (CmbTipoUbicacion.SelectedItem == null || CmbLocalidad.SelectedItem == null || CmbEstado.SelectedItem == null)
            {
                MessageBox.Show("Por favor, rellene los campos de objetos obligatorios.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void CargarCombosMaestros()
        {
            try
            {
                CmbTipoUbicacion.ItemsSource = _ubicacionService.ObtenerTiposUbicacion();
                CmbLocalidad.ItemsSource = _ubicacionService.ObtenerLocalidades();
                CmbDepartamento.ItemsSource = _ubicacionService.ObtenerDepartamentos();
                CmbEstado.ItemsSource = _ubicacionService.ObtenerEstados();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar combos: " + ex.Message);
            }
        }

        private void AgregarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            _ubicacionActual = null;
            ModalTitle.Text = "Nueva Ubicación";
            LimpiarCamposUbicacion();
            CargarCombosMaestros();
            UbicacionModal.Visibility = Visibility.Visible;
        }

        private void EditarUbicacionButton_Click(object sender, RoutedEventArgs e)
        {
            _ubicacionActual = UbicacionesDataGrid.SelectedItem as Ubicacion;
            if (_ubicacionActual != null)
            {
                ModalTitle.Text = "Editar Ubicación";
                CargarCombosMaestros();
                AsignarDatosAlFormulario(_ubicacionActual);
                UbicacionModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Seleccione una fila primero.");
            }
        }

        private void LimpiarCamposUbicacion()
        {
            TxtDescripcion.Text = string.Empty; TxtDireccion.Text = string.Empty;
            CmbTipoUbicacion.SelectedIndex = -1; CmbLocalidad.SelectedIndex = -1;
            CmbDepartamento.SelectedIndex = -1; CmbProvincia.ItemsSource = null;
            CmbDistrito.ItemsSource = null; CmbEstado.SelectedIndex = -1;
        }

        private void BtnCancelarUbicacion_Click(object sender, RoutedEventArgs e) => UbicacionModal.Visibility = Visibility.Collapsed;

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string b = BuscarTextBox.Text.ToLower().Trim();
            if (string.IsNullOrWhiteSpace(b)) UbicacionesDataGrid.ItemsSource = _listadoCompletoUbicaciones;
            else
            {
                UbicacionesDataGrid.ItemsSource = _listadoCompletoUbicaciones.Where(x =>
                    x.Id.ToString().Contains(b) ||
                    (x.Descripcion != null && x.Descripcion.ToLower().Contains(b)) ||
                    (x.Localidad?.Nombre != null && x.Localidad.Nombre.ToLower().Contains(b))
                ).ToList();
            }
        }

        private void BtnSeries_Click(object sender, RoutedEventArgs e)
        {
            if (UbicacionesDataGrid.SelectedItem is Ubicacion ubicacionSeleccionada)
            {
                // 🌟 VALIDACIÓN EXACTA A TU SISTEMA ANTERIOR 🌟
                if (ubicacionSeleccionada.TipoUbicacion == null ||
                   !ubicacionSeleccionada.TipoUbicacion.Nombre.ToUpper().Contains("PUNTO DE VENTA"))
                {
                    MessageBox.Show("Solamente manejan series las ubicaciones que son PUNTOS DE VENTA, verifique...!",
                                    "Mensaje al usuario", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Si pasa la validación, abrimos la ventana de series
                SeriesUbicacionWindow modalSeries = new SeriesUbicacionWindow(ubicacionSeleccionada);
                modalSeries.Owner = Window.GetWindow(this);
                modalSeries.ShowDialog();
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una ubicación de la lista para configurar sus series.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}