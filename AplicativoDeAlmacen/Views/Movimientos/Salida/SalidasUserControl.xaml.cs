using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Necesario para colecciones reactivas en WPF
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class SalidasUserControl : UserControl
    {
        private readonly SalidaMovimientoService _salidaService;
        private List<PersonaComercial> _listaClientes;
        private List<Ubicacion> _listaUbicaciones;

        int idUsuarioLogueado = 1; // 💡 Cámbialo por tu variable global de sesión de usuario
        int estadoSalida = 1;      // 💡 Asegúrate que el estado 1 sea "Registrado" o el que corresponda
        // 💡 COLECCIONES VINCULADAS A TUS DATAGRIDS DEL XAML
        private ObservableCollection<VistaProductoGrid> _productosLista;
        private ObservableCollection<VistaCodigoGrid> _codigosLista;

        // 💡 CAPTURADORES DE IDS NUMÉRICOS PARA LAS LLAVES FORÁNEAS (Únicas versiones correctas)
        private int? _idClienteSeleccionado;
        private int? _idUbicacionSeleccionada;

        public SalidasUserControl()
        {
            InitializeComponent();
            _salidaService = new SalidaMovimientoService();

            // Inicialización de colecciones para evitar errores de referencia nula
            _productosLista = new ObservableCollection<VistaProductoGrid>();
            _codigosLista = new ObservableCollection<VistaCodigoGrid>();

            // Ejecuciones automáticas al cargar la vista
            EstadoInicialFormulario();
            CargarComboMotivosSalida();
        }

        // Configura el estado inicial bloqueado: El usuario debe presionar "Nuevo"
        private void EstadoInicialFormulario()
        {
            grdFormularioSalida.IsEnabled = false;

            // Bloqueo de botones de procesamiento inferior
            btnAgregarItem.IsEnabled = false;
            btnModificarItem.IsEnabled = false;
            btnEliminarItem.IsEnabled = false;
            btnImportarExcel.IsEnabled = false;
            btnGrabarSalida.IsEnabled = false;
            btnCancelar.IsEnabled = false;

            // Botones habilitados de control principal
            btnNuevo.IsEnabled = true;

            // Limpieza integral de campos
            txtSerieSalida.Clear();
            txtNumeroSalida.Clear();
            txtNumeroGuia.Clear();
            txtSerieGuia.Clear();
            dtpFechaDespacho.SelectedDate = null;
            cboMotivoSalida.SelectedIndex = -1;

            txtCliente.Clear();
            txtCodigoCliente.Clear();
            txtDireccionCliente.Clear();

            txtUbicacion.Clear();
            txtCodigoUbicacion.Clear();
            txtDireccionUbicacion.Clear();

            txtObservacionSalida.Clear();

            // Limpieza de variables de estado de datos
            _idClienteSeleccionado = null;
            _idUbicacionSeleccionada = null;
            _productosLista.Clear();
            _codigosLista.Clear();

            dgProductosSalida.ItemsSource = null; // Desvincula para asegurar refresco
            dgCodigosSalida.ItemsSource = null;   // LIMPIEZA EXPLÍCITA DEL GRID DE CÓDIGOS
        }

        private async void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            grdFormularioSalida.IsEnabled = true;

            // Habilitar acciones operativas
            btnAgregarItem.IsEnabled = true;
            btnImportarExcel.IsEnabled = true;
            btnGrabarSalida.IsEnabled = true;
            btnCancelar.IsEnabled = true;
            btnNuevo.IsEnabled = false;

            dtpFechaDespacho.SelectedDate = DateTime.Today;

            // Forzar el número de operación correlativo dinámico con una serie por defecto (Ej: "S001")
            try
            {
                string seriePorDefecto = "S001";
                var proxMov = await _salidaService.GenerarSiguienteCorrelativoAsync(seriePorDefecto);

                txtSerieSalida.Text = proxMov.SerieDocumento;
                txtNumeroSalida.Text = proxMov.NumeroDocumento;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el correlativo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Desea cancelar la operación actual? Perderá los datos no grabados.",
                "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                EstadoInicialFormulario();
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            // Restablece y limpia visualmente la interfaz antes de delegar el cierre del contenedor
            EstadoInicialFormulario();
        }

        private async void CargarComboMotivosSalida()
        {
            try
            {
                var motivosSalida = await _salidaService.ObtenerMotivosSalidaAsync();
                cboMotivoSalida.ItemsSource = motivosSalida;
                cboMotivoSalida.DisplayMemberPath = "Descripcion";
                cboMotivoSalida.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar motivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================================
        // EVENTOS COMPORTAMIENTO AUTOCOMPLETADO (Popups Inteligentes)
        // =========================================================================

        private async void TxtCliente_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtCliente.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaClientes = await _salidaService.BuscarClientesAsync(filtro);
                lstClientes.ItemsSource = _listaClientes;
                popupClientes.IsOpen = _listaClientes.Count > 0;
            }
            else
            {
                popupClientes.IsOpen = false;
            }
        }

        private void LstClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstClientes.SelectedItem is PersonaComercial cliente)
            {
                txtCliente.TextChanged -= TxtCliente_TextChanged;

                txtCliente.Text = cliente.RazonSocial;
                txtCodigoCliente.Text = cliente.Id.ToString("D6");
                txtDireccionCliente.Text = cliente.Direccion;
                _idClienteSeleccionado = cliente.Id; // Usamos la variable corregida con guion bajo

                popupClientes.IsOpen = false;
                txtCliente.TextChanged += TxtCliente_TextChanged;
            }
        }

        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filtro = txtUbicacion.Text.Trim();
            if (filtro.Length >= 2)
            {
                _listaUbicaciones = await _salidaService.BuscarUbicacionesAsync(filtro);
                lstUbicaciones.ItemsSource = _listaUbicaciones;
                popupUbicaciones.IsOpen = _listaUbicaciones.Count > 0;
            }
            else
            {
                popupUbicaciones.IsOpen = false;
            }
        }

        private void LstUbicaciones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstUbicaciones.SelectedItem is Ubicacion ub)
            {
                txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;

                txtUbicacion.Text = ub.Descripcion;
                txtCodigoUbicacion.Text = ub.Id.ToString();
                txtDireccionUbicacion.Text = ub.Direccion;
                _idUbicacionSeleccionada = ub.Id; // Usamos la variable corregida con guion bajo

                popupUbicaciones.IsOpen = false;
                txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
            }
        }

        private void LstClientes_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dependencyObject = (DependencyObject)e.OriginalSource;
            while (dependencyObject != null && !(dependencyObject is ListBoxItem))
            {
                dependencyObject = System.Windows.Media.VisualTreeHelper.GetParent(dependencyObject);
            }

            if (dependencyObject is ListBoxItem item && item.DataContext is PersonaComercial cliente)
            {
                txtCliente.TextChanged -= TxtCliente_TextChanged;

                txtCliente.Text = cliente.RazonSocial;
                txtCodigoCliente.Text = cliente.Id.ToString("D6");
                txtDireccionCliente.Text = cliente.Direccion;
                _idClienteSeleccionado = cliente.Id;

                popupClientes.IsOpen = false;
                lstClientes.SelectedIndex = -1;

                txtCliente.TextChanged += TxtCliente_TextChanged;
                e.Handled = true;
            }
        }

        private void LstUbicaciones_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dependencyObject = (DependencyObject)e.OriginalSource;
            while (dependencyObject != null && !(dependencyObject is ListBoxItem))
            {
                dependencyObject = System.Windows.Media.VisualTreeHelper.GetParent(dependencyObject);
            }

            if (dependencyObject is ListBoxItem item && item.DataContext is Ubicacion ub)
            {
                txtUbicacion.TextChanged -= TxtUbicacion_TextChanged;

                txtUbicacion.Text = ub.Descripcion;
                txtCodigoUbicacion.Text = ub.Id.ToString();
                txtDireccionUbicacion.Text = ub.Direccion;
                _idUbicacionSeleccionada = ub.Id;

                popupUbicaciones.IsOpen = false;
                lstUbicaciones.SelectedIndex = -1;

                txtUbicacion.TextChanged += TxtUbicacion_TextChanged;
                e.Handled = true;
            }
        }

        private void DgProductosSalida_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgProductosSalida.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                // El filtro aquí es crítico:
                var codigosFiltrados = _codigosLista
                    .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                    .ToList();

                dgCodigosSalida.ItemsSource = codigosFiltrados; // <-- Esto reemplaza la fuente, está bien.
            }
        }

        // =========================================================================
        // ACCIONES DE DETALLE DE ÍTEMS (Llamada al Modal - Única Versión)
        // =========================================================================
        // 1. Evento de cambio de motivo
        private void CboMotivoSalida_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActualizarVisibilidadCampos();
        }
        private void ActualizarVisibilidadCampos()
        {
            // Obtenemos el motivo seleccionado
            var motivo = cboMotivoSalida.SelectedItem as dynamic;
            string descripcion = motivo?.Descripcion?.ToString().ToLower() ?? "";

            // 1. Resetear estados: Bloqueamos ambos por defecto
            txtCliente.IsEnabled = false;
            txtUbicacion.IsEnabled = false;

            // 2. Lógica específica según el motivo
            switch (descripcion)
            {
                case "transferencia":
                case "transferencia entre almacenes":
                    txtUbicacion.IsEnabled = true;
                    break;

                case "devolucion entregada":
                case "donacion":
                case "feria":
                case "consignacion":
                case "venta":
                    txtCliente.IsEnabled = true;
                    break;

                case "otros":
                case "promocion/promotoria":
                    txtCliente.IsEnabled = true;
                    txtUbicacion.IsEnabled = true;
                    break;

                default:
                    // Si no hay motivo o es desconocido, mantener ambos bloqueados
                    break;
            }

            // 3. Limpieza de datos si el campo queda bloqueado
            if (!txtCliente.IsEnabled)
            {
                txtCliente.Clear();
                txtCodigoCliente.Clear();
                txtDireccionCliente.Clear();
                _idClienteSeleccionado = null;
            }

            if (!txtUbicacion.IsEnabled)
            {
                txtUbicacion.Clear();
                txtCodigoUbicacion.Clear();
                txtDireccionUbicacion.Clear();
                _idUbicacionSeleccionada = null;
            }
        }

        // 2. Método centralizado de control de UI

        private void btnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DetalleProductoModal modal = new DetalleProductoModal();
                modal.Owner = Window.GetWindow(this);

                bool? resultado = modal.ShowDialog();

                if (resultado == true)
                {
                    // 1. Añadir el producto principal a la lista de la izquierda
                    if (modal.ProductoGridGenerado != null)
                    {
                        _productosLista.Add(modal.ProductoGridGenerado);
                    }

                    // 2. Añadir el desglose de códigos a la lista MAESTRA (no al grid directamente)
                    if (modal.ListaCodigosGenerados != null)
                    {
                       
                        foreach (var cod in modal.ListaCodigosGenerados)
                        {
                            // ¡ASEGÚRATE DE QUE EL CODIGO TENGA EL PRODUCTOID ASIGNADO AQUÍ!
                   
                            cod.ProductoId = modal.ProductoGridGenerado.ProductoId;
                            _codigosLista.Add(cod);

                        }
                    }

                    // 3. Forzar refresco visual solo de la lista de productos
                    dgProductosSalida.ItemsSource = null;
                    dgProductosSalida.ItemsSource = _productosLista;

                    // 4. Seleccionamos el producto recién agregado para disparar el filtro
                    dgProductosSalida.SelectedItem = modal.ProductoGridGenerado;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al recibir los datos del modal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Aquí puedes colocar tu método `btnGrabarSalida_Click` cuando lo necesites implementar
        private async void btnGrabarSalida_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validaciones básicas de lista
            if (_productosLista.Count == 0 || _codigosLista.Count == 0)
            {
                MessageBox.Show("No hay productos o códigos en la lista para procesar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Validación de número de salida (siempre es obligatorio)
            if (string.IsNullOrEmpty(txtNumeroSalida.Text))
            {
                MessageBox.Show("El número de salida no puede estar vacío.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Validación DINÁMICA: Solo valida si el campo está habilitado según el motivo
            if (txtCliente.IsEnabled && _idClienteSeleccionado == null)
            {
                MessageBox.Show("Debe seleccionar un cliente para este motivo de salida.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (txtUbicacion.IsEnabled && _idUbicacionSeleccionada == null)
            {
                MessageBox.Show("Debe seleccionar una ubicación para este motivo de salida.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btnGrabarSalida.IsEnabled = false;

                // 4. Crear el objeto con manejo de nulos seguro
                var movimiento = new Movimiento
                {
                    SerieDocumento = txtSerieSalida.Text,
                    NumeroDocumento = txtNumeroSalida.Text,
                    FechaMovimiento = dtpFechaDespacho.SelectedDate.HasValue ? DateOnly.FromDateTime(dtpFechaDespacho.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Now),
                    // Si el campo estaba deshabilitado, enviamos null o un valor por defecto (0) según tu base de datos
                    UbicacionId = txtUbicacion.IsEnabled ? _idUbicacionSeleccionada.Value : (int?)null,
                    PersonaComercialId = txtCliente.IsEnabled ? _idClienteSeleccionado : (int?)null,
                    MotivoProductoId = (int)cboMotivoSalida.SelectedValue,
                    Observacion = txtObservacionSalida.Text,
                    CreatedAt = DateTime.Now
                };

                // 2. Preparar el detalle y los códigos desde tus colecciones reactivas
                // Transformamos lo que está en la grilla al formato que espera tu Service
                var detalles = _productosLista.Select(p => p.Detalle).ToList();
                var codigos = _codigosLista.Select(c => c.MovCodigo).ToList();

                // 3. Llamar al servicio transaccional
                bool resultado = await _salidaService.RegistrarSalidaCompletaAsync(
                         movimiento,
                         _productosLista.ToList(), // Convertimos la ObservableCollection a List
                         _codigosLista.ToList(),   // Convertimos la ObservableCollection a List
                         idUsuarioLogueado,
                         estadoSalida
                     );


                if (_codigosLista.Any(c => c.MovCodigo == null))
                {
                    MessageBox.Show("Error: Hay códigos en la lista que no tienen el objeto 'MovCodigo' inicializado.");
                    return;
                }
                    if (resultado)
                {
                    MessageBox.Show("Salida registrada correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    EstadoInicialFormulario(); // Limpia todo para una nueva operación
                    EventBus.NotificarMovimientosChanged();
                }
                else
                {
                    MessageBox.Show("Hubo un problema al guardar el movimiento en la base de datos.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al grabar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnGrabarSalida.IsEnabled = true;
            }
        }
    }
}