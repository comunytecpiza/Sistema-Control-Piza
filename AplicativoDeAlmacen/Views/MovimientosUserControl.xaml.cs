using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views
{
    public partial class MovimientosUserControl : UserControl
    {
        private List<MovimientoDetalle> ListaProductosAgregados = new List<MovimientoDetalle>();
        private List<RangoCodigoItem> ListaTodosLosCodigosDelMovimiento = new List<RangoCodigoItem>();
        private readonly PersonaComercialService _service;
        private readonly IngresoMovimientoService _serviceMovimiento;

        private List<VistaProductoGrid> _productosGridList;
        private List<VistaCodigoGrid> _codigosGridList;
        private List<RangoCodigoItem> _rangosProcesadosGlobal;

        private bool _isUpdatingFromSelection = false;
        private const string SERIE_POR_DEFECTO = "0001";
        private int? _personaComercialIdSeleccionada = null;
        private const int UBICACION_ID_SELECCIONADA = 1; // ID Fijo de Almacén

        public MovimientosUserControl()
        {
            _productosGridList = new List<VistaProductoGrid>();
            _codigosGridList = new List<VistaCodigoGrid>();
            _rangosProcesadosGlobal = new List<RangoCodigoItem>();
            _service = new PersonaComercialService();
            _serviceMovimiento = new IngresoMovimientoService();

            InitializeComponent(); // ¡Primero se inicializa todo el XAML!

            ConfigurarEventosIniciales();
            EstablecerEstadoInicial();
        }

        public void ConfigurarEventosIniciales()
        {
            // Primero limpiamos cualquier asignación previa por seguridad
            txtRazonSocial.TextChanged -= TxtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged -= LstSugerencias_SelectionChanged;
            this.PreviewMouseDown -= MovimientosUserControl_PreviewMouseDown;
            Loaded -= MovimientosUserControl_Loaded;
            btnAgregar.Click -= BtnAgregar_Click;
            btnAgregarProducto.Click -= BtnAgregarItem_Click;
            btnCancelar.Click -= BtnCancelar_Click;
            btnGrabar.Click -= RegistrarMovimientoCompleto;
            // 🔥 COLÓCALO AQUÍ ABAJO (Para des-registrar con seguridad):
            dgProductos.SelectionChanged -= DgProductos_SelectionChanged;

            // Ahora los asignamos con la certeza de que serán únicos
            txtRazonSocial.TextChanged += TxtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += LstSugerencias_SelectionChanged;
            this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
            Loaded += MovimientosUserControl_Loaded;
            btnAgregar.Click += BtnAgregar_Click;
            btnAgregarProducto.Click += BtnAgregarItem_Click;
            btnCancelar.Click += BtnCancelar_Click;
            btnGrabar.Click += RegistrarMovimientoCompleto;
            // 🔥 COLÓCALO AQUÍ ABAJO (Para registrar el evento):
            dgProductos.SelectionChanged += DgProductos_SelectionChanged;
        }
        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromSelection) return;
            string textoBusqueda = txtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    List<PersonaComercial> sugerencias = await _service.BuscarPorRazonSocialAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        lstSugerencias.ItemsSource = sugerencias;
                        popupSugerencias.IsOpen = true;
                    }
                    else
                    {
                        popupSugerencias.IsOpen = false;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al consultar sugerencias: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                popupSugerencias.IsOpen = false;
            }
        }

        private void LstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerencias.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true;

                _personaComercialIdSeleccionada = personaSeleccionada.Id;
                txtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                    ? personaSeleccionada.RazonSocial
                    : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";

                txtCodigoRazonSocial.Text = personaSeleccionada.Id.ToString("D6");
                txtDireccion.Text = personaSeleccionada.Direccion ?? "Sin dirección registrada";

                popupSugerencias.IsOpen = false;
                lstSugerencias.SelectedIndex = -1;

                _isUpdatingFromSelection = false;
            }
        }

        private void MovimientosUserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!txtRazonSocial.IsMouseOver && !popupSugerencias.IsMouseOver)
            {
                popupSugerencias.IsOpen = false;
            }
        }

        private async void MovimientosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarMotivosAsync();
        }

        private async Task CargarMotivosAsync()
        {
            try
            {
                this.Cursor = Cursors.Wait;
                cboMotivo.ItemsSource = await _serviceMovimiento.ObtenerMotivosProductosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los motivos de productos: {ex.Message}", "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private async void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;
                LimpiarFormulario();

                Movimiento nuevoMovimiento = await _serviceMovimiento.GenerarSiguienteCorrelativoAsync(SERIE_POR_DEFECTO);
                txtNumSerie.Text = nuevoMovimiento.SerieDocumento;
                txtNumDocumento.Text = nuevoMovimiento.NumeroDocumento;

                dtpFechaRecepcion.SelectedDate = DateTime.Today;
                HabilitarCamposFormulario(true);
                GestionarBotonesPrincipales(enEdicion: true);
                cboMotivo.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar el nuevo registro: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult resultado = MessageBox.Show("¿Está seguro que desea cancelar la operación actual? Se perderán los datos no guardados.",
                                                         "Confirmar Cancelación", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resultado == MessageBoxResult.Yes)
            {
                LimpiarFormulario();
                HabilitarCamposFormulario(false);
                GestionarBotonesPrincipales(enEdicion: false);
            }
        }

        private async void RegistrarMovimientoCompleto(object sender, RoutedEventArgs e)
        {
            // 1. Validaciones iniciales
            if (_productosGridList == null || _productosGridList.Count == 0)
            {
                MessageBox.Show("Debe agregar al menos un producto a la lista antes de guardar.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cboMotivo.SelectedValue == null)
            {
                MessageBox.Show("Por favor, seleccione el motivo del movimiento.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 2. APAGAR EL BOTÓN INMEDIATAMENTE para evitar el doble envío
                btnGrabar.IsEnabled = false;
                this.Cursor = Cursors.Wait;

                Movimiento nuevaCabecera = new Movimiento
                {
                    FechaMovimiento = dtpFechaRecepcion.SelectedDate != null ? DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate.Value) : DateOnly.FromDateTime(DateTime.Today),
                    SerieDocumento = txtNumSerie.Text.Trim(),
                    NumeroDocumento = txtNumDocumento.Text.Trim(),
                    MotivoProductoId = Convert.ToInt32(cboMotivo.SelectedValue),
                    UbicacionId = UBICACION_ID_SELECCIONADA,
                    UsuarioId = 1,
                    PersonaComercialId = _personaComercialIdSeleccionada,
                    Observacion = txtObservacion.Text.Trim()
                };

                // Ejecutar transacción
                bool exito = await _serviceMovimiento.RegistrarMovimientoCompletoAsync(nuevaCabecera, _productosGridList, _rangosProcesadosGlobal, UBICACION_ID_SELECCIONADA);

                if (exito)
                {
                    MessageBox.Show("El movimiento de inventario y sus códigos se registraron correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                    LimpiarFormulario();
                    HabilitarCamposFormulario(false);
                    GestionarBotonesPrincipales(enEdicion: false);

                    // Siguiente correlativo automático
                    Movimiento nuevoMovimiento = await _serviceMovimiento.GenerarSiguienteCorrelativoAsync(SERIE_POR_DEFECTO);
                    txtNumSerie.Text = nuevoMovimiento.SerieDocumento;
                    txtNumDocumento.Text = nuevoMovimiento.NumeroDocumento;

                    dtpFechaRecepcion.SelectedDate = DateTime.Today;
                    HabilitarCamposFormulario(true);
                    GestionarBotonesPrincipales(enEdicion: true);
                    cboMotivo.Focus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico en la transacción de inventario: {ex.Message}", "Error al Guardar", MessageBoxButton.OK, MessageBoxImage.Error);
                // Si falló, volvemos a encender el botón para que puedan intentar corregir/guardar de nuevo
                btnGrabar.IsEnabled = true;
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void BtnAgregarItem_Click(object sender, RoutedEventArgs e)
        {
            AgregarItemWindow modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados;

                int idProducto = productoSelected.Id;

                // 1. Creamos el nuevo ítem para la grilla izquierda
                var nuevoProductoGrid = new VistaProductoGrid
                {
                    Detalle = new MovimientoDetalle
                    {
                        ProductoId = idProducto,
                        CantidadIngreso = modal.CantidadProductoIngresada,
                        CostoUnitario = modal.CostoUnitarioIngresado
                    },
                    CodigoProducto = idProducto.ToString(),
                    Descripcion = productoSelected.Descripcion,
                    UnidadMedida = "UNIDAD",
                    ProductoId = idProducto // 🔥 CORRECCIÓN: Asignamos el ID entero aquí para que no sea 0
                };
                _productosGridList.Add(nuevoProductoGrid);

                // 2. Procesamos los códigos expansivos en la lista global en memoria
                if (rangosDelModal != null)
                {
                    int contadorFila = _codigosGridList.Count + 1;
                    foreach (var rango in rangosDelModal)
                    {
                        rango.productoId = idProducto;
                        _rangosProcesadosGlobal.Add(rango);

                        for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                        {
                            _codigosGridList.Add(new VistaCodigoGrid
                            {
                                MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                                CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                                ColeccionTipo = rango.ColeccionTipo,
                                ProductoId = idProducto // Vinculación clave en memoria
                            });
                        }
                    }
                }

                // 3. Forzamos el refresco completo de la grilla izquierda (Productos)
                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;

                // 4. Seleccionamos el producto recién agregado
                dgProductos.SelectedItem = nuevoProductoGrid;
            }
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 1. Verificamos si realmente se seleccionó algo en la grilla izquierda
            if (dgProductos.SelectedItem is VistaProductoGrid productoSeleccionado)
            {
                // 2. LIMPIEZA: Rompemos el origen de datos para limpiar la grilla de la derecha de forma segura
                dgCodigos.ItemsSource = null;

                // 3. FILTRADO: Buscamos en la lista global '_codigosGridList' los códigos que tengan el mismo ProductoId
                var codigosDelProducto = _codigosGridList
                                         .Where(c => c.ProductoId == productoSeleccionado.ProductoId)
                                         .ToList();

                // 4. CARGA: Asignamos la lista filtrada directamente al ItemsSource
                dgCodigos.ItemsSource = codigosDelProducto;
            }
            else
            {
                // Si no hay ningún producto seleccionado, la tabla de la derecha se queda vacía
                dgCodigos.ItemsSource = null;
            }
        }
        private void LimpiarFormulario()
        {
            _isUpdatingFromSelection = true;

            txtNumSerie.Clear();
            txtNumDocumento.Clear();
            dtpFechaRecepcion.SelectedDate = null;
            cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Clear();
            txtCodigoRazonSocial.Clear();
            txtDireccion.Clear();
            txtObservacion.Clear();

            _personaComercialIdSeleccionada = null;
            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            if (dgProductos != null) dgProductos.ItemsSource = null;
            if (dgCodigos != null) dgCodigos.ItemsSource = null;

            _isUpdatingFromSelection = false;
        }

        private void HabilitarCamposFormulario(bool habilitar)
        {
            txtNumSerie.IsEnabled = false;
            txtNumDocumento.IsEnabled = false;
            txtCodigoRazonSocial.IsEnabled = false;
            txtDireccion.IsEnabled = false;

            dtpFechaRecepcion.IsEnabled = habilitar;
            cboMotivo.IsEnabled = habilitar;
            txtRazonSocial.IsEnabled = habilitar;
            txtObservacion.IsEnabled = habilitar;

            if (btnModificar != null) btnModificar.IsEnabled = habilitar;
            if (btnEliminar != null) btnEliminar.IsEnabled = habilitar;
            if (btnImportar != null) btnImportar.IsEnabled = habilitar;
            if (btnAgregarProducto != null) btnAgregarProducto.IsEnabled = habilitar;
        }

        private void GestionarBotonesPrincipales(bool enEdicion)
        {
            btnAgregar.IsEnabled = !enEdicion;
            btnEditar.IsEnabled = !enEdicion;
            btnImprimir.IsEnabled = !enEdicion;
            btnAnular.IsEnabled = !enEdicion;
        }

        private void EstablecerEstadoInicial()
        {
            LimpiarFormulario();
            HabilitarCamposFormulario(false);
            GestionarBotonesPrincipales(enEdicion: false);
        }
    }
}