using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;
using AplicativoDeAlmacen.Services.Ubicaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class MovimientosUserControl : UserControl
    {
        private List<MovimientoDetalle> ListaProductosAgregados = new List<MovimientoDetalle>();
        private List<RangoCodigoItem> ListaTodosLosCodigosDelMovimiento = new List<RangoCodigoItem>();
        private readonly PersonaComercialService _service;
        private readonly IngresoMovimientoService _serviceMovimiento;
        private readonly UbicacionService _ubicacionService;

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
            _ubicacionService = new UbicacionService();

            InitializeComponent(); // ¡Primero se inicializa todo el XAML!

            ConfigurarEventosIniciales();
            EstablecerEstadoInicial();
        }
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Permitir solo dígitos
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void OnPasteNumeric(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!Regex.IsMatch(text, "^\\d+$"))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
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
            lstSugerenciasUbicacion.SelectionChanged += LstSugerenciasUbicacion_SelectionChanged;
            this.PreviewMouseDown += MovimientosUserControl_PreviewMouseDown;
            Loaded += MovimientosUserControl_Loaded;
            btnAgregar.Click += BtnAgregar_Click;
            btnAgregarProducto.Click += BtnAgregarItem_Click;
            btnModificar.Click -= BtnModificar_Click;
            btnEliminar.Click -= BtnEliminar_Click;
            btnModificar.Click += BtnModificar_Click;
            btnEliminar.Click += BtnEliminar_Click;
            btnCancelar.Click += BtnCancelar_Click;
            btnGrabar.Click += RegistrarMovimientoCompleto;
            // 🔥 COLÓCALO AQUÍ ABAJO (Para registrar el evento):
            dgProductos.SelectionChanged += DgProductos_SelectionChanged;
            dgProductos.MouseDoubleClick -= DgProductos_MouseDoubleClick;
            dgProductos.MouseDoubleClick += DgProductos_MouseDoubleClick;

            // Manejar rueda del ratón para que el DataGrid interno haga scroll cuando hay un ScrollViewer padre
            if (dgProductos != null)
            {
                dgProductos.PreviewMouseWheel -= DataGrid_PreviewMouseWheel;
                dgProductos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
            }

            if (dgCodigos != null)
            {
                dgCodigos.PreviewMouseWheel -= DataGrid_PreviewMouseWheel;
                dgCodigos.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
            }

        }

        private void RebuildCodigosGridList()
        {
            _codigosGridList.Clear();
            int contadorFila = 1;

            foreach (var producto in _productosGridList)
            {
                var rangosProducto = _rangosProcesadosGlobal.Where(r => r.productoId == producto.ProductoId).ToList();
                foreach (var rango in rangosProducto)
                {
                    for (int i = rango.DesdeNum; i <= rango.HastaNum; i++)
                    {
                        _codigosGridList.Add(new VistaCodigoGrid
                        {
                            MovCodigo = new MovimientoCodigo { MovimientoDetalleId = contadorFila++ },
                            CodigoUnique = $"{rango.AbreviaturaBase}-{i:D7}",
                            ColeccionTipo = rango.ColeccionTipo,
                            ProductoId = producto.ProductoId
                        });
                    }
                }
            }
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedProduct();
        }

        private void DgProductos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ignorar si doble click se produce fuera de una fila
            if (dgProductos.SelectedItem is VistaProductoGrid)
            {
                EditSelectedProduct();
            }
        }

        private void EditSelectedProduct()
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid seleccionado) return;

            var rangosExistentes = _rangosProcesadosGlobal.Where(r => r.productoId == seleccionado.ProductoId).ToList();

            var modal = new AgregarItemWindow { Owner = Window.GetWindow(this) };
            modal.ListaProductosExistentesEnPadre = _productosGridList;
            modal.InitializeForEdit(seleccionado, rangosExistentes);

            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                // Actualizar detalle del producto
                seleccionado.Detalle.CantidadIngreso = modal.CantidadProductoIngresada;
                seleccionado.Detalle.CostoUnitario = modal.CostoUnitarioIngresado;

                // Reemplazar rangos globales para este producto
                _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);
                foreach (var r in modal.ListaRangosAgregados)
                {
                    r.productoId = seleccionado.ProductoId;
                    _rangosProcesadosGlobal.Add(r);
                }

                // Reconstruir la lista de códigos y refrescar UI
                RebuildCodigosGridList();
                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
                dgProductos.SelectedItem = seleccionado;
                DgProductos_SelectionChanged(dgProductos, new SelectionChangedEventArgs(DataGrid.SelectionChangedEvent, new List<object>(), new List<object>()));
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgProductos.SelectedItem is not VistaProductoGrid seleccionado) return;

            var resp = MessageBox.Show($"¿Eliminar el producto '{seleccionado.Descripcion}' y sus códigos asociados?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            // Eliminar de productos
            _productosGridList.RemoveAll(p => p.ProductoId == seleccionado.ProductoId);

            // Eliminar rangos y códigos asociados
            _rangosProcesadosGlobal.RemoveAll(r => r.productoId == seleccionado.ProductoId);
            _codigosGridList.RemoveAll(c => c.ProductoId == seleccionado.ProductoId);

            // Reconstruir índices de códigos
            RebuildCodigosGridList();

            // Refrescar UI
            dgProductos.ItemsSource = null;
            dgProductos.ItemsSource = _productosGridList;
            dgCodigos.ItemsSource = null;
        }

        private void CboMotivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMotivo.SelectedItem == null)
                return;

            // Obtén la descripción del motivo
            dynamic motivo = cboMotivo.SelectedItem;
            string descripcion = motivo.Descripcion?.Trim().ToUpper() ?? "";

            // Primero deshabilitamos todo
            txtRazonSocial.IsEnabled = false;
            txtUbicacion.IsEnabled = false;

            // Limpiamos valores opcionalmente
            txtRazonSocial.Clear();
            txtCodigoRazonSocial.Clear();
            txtDireccion.Clear();

            txtUbicacion.Clear();
            txtCodigoUbicacion.Clear();
            txtDireccionUbicacion.Clear();

            switch (descripcion)
            {
                case "COMPRA":
                    txtRazonSocial.IsEnabled = true;
                    break;

                case "DEVOLUCION RECIBIDA":
                    txtRazonSocial.IsEnabled = true;
                    break;

                case "OTROS":
                    txtRazonSocial.IsEnabled = true;
                    txtUbicacion.IsEnabled = true;
                    break;

                case "PROMOCION/PROMOTORIA":
                    txtRazonSocial.IsEnabled = true;
                    txtUbicacion.IsEnabled = true;
                    break;

                case "TRANSFERENCIA ENTRE ALMACENES":
                    txtUbicacion.IsEnabled = true;
                    break;
            }
        }
        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtRazonSocial.IsEnabled)
                return;

            if (_isUpdatingFromSelection)
                return;


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

        private void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtUbicacion.IsEnabled)
                return;


            string busqueda = txtUbicacion.Text;

            if (string.IsNullOrWhiteSpace(busqueda))
            {
                popupUbicacion.IsOpen = false;
                return;
            }

            // Aquí llamas a tu servicio de base de datos
            // Supongamos que tienes un método 'BuscarUbicaciones(string criterio)'
            var resultados = _ubicacionService.BuscarUbicaciones(busqueda);

            if (resultados != null && resultados.Count > 0)
            {
                lstSugerenciasUbicacion.ItemsSource = resultados;
                popupUbicacion.IsOpen = true;
            }
            else
            {
                popupUbicacion.IsOpen = false;
            }
        }

        private void LstSugerenciasUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerenciasUbicacion.SelectedItem is Ubicacion itemSeleccionado)
            {
                txtUbicacion.Text = itemSeleccionado.Descripcion;
                txtCodigoUbicacion.Text = itemSeleccionado.Id.ToString(); // Autocompleta el código
                popupUbicacion.IsOpen = false;
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
            // Configurar virtualización y columnas adaptables antes de cargar datos
            ConfigurarDataGridsParaVirtualizacion();

            await CargarMotivosAsync();
        }

        private void ConfigurarDataGridsParaVirtualizacion()
        {
            // Habilitar virtualización y reciclaje para evitar instanciar todos los elementos visuales
            if (dgCodigos != null)
            {
                VirtualizingPanel.SetIsVirtualizing(dgCodigos, true);
                VirtualizingPanel.SetVirtualizationMode(dgCodigos, VirtualizationMode.Recycling);
                dgCodigos.EnableRowVirtualization = true;
                dgCodigos.EnableColumnVirtualization = false;
                ScrollViewer.SetCanContentScroll(dgCodigos, true);
            }

            if (dgProductos != null)
            {
                VirtualizingPanel.SetIsVirtualizing(dgProductos, true);
                VirtualizingPanel.SetVirtualizationMode(dgProductos, VirtualizationMode.Recycling);
                dgProductos.EnableRowVirtualization = true;
                dgProductos.EnableColumnVirtualization = false;
                ScrollViewer.SetCanContentScroll(dgProductos, true);
                // Si no existen columnas definidas en XAML, crear columnas por código.
                if (dgProductos.Columns == null || dgProductos.Columns.Count == 0)
                {
                    dgProductos.AutoGenerateColumns = false;

                    dgProductos.Columns.Add(new DataGridTextColumn
                    {
                        Header = "N°",
                        Binding = new System.Windows.Data.Binding("ProductoId"),
                        Width = new DataGridLength(60)
                    });

                    dgProductos.Columns.Add(new DataGridTextColumn
                    {
                        Header = "CÓDIGO",
                        Binding = new System.Windows.Data.Binding("CodigoProducto"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    });

                    dgProductos.Columns.Add(new DataGridTextColumn
                    {
                        Header = "DESCRIPCIÓN",
                        Binding = new System.Windows.Data.Binding("Descripcion"),
                        Width = new DataGridLength(2, DataGridLengthUnitType.Star)
                    });

                    dgProductos.Columns.Add(new DataGridTextColumn
                    {
                        Header = "CANT.",
                        Binding = new System.Windows.Data.Binding("Detalle.CantidadIngreso") { StringFormat = "N2" },
                        Width = new DataGridLength(0.7, DataGridLengthUnitType.Star)
                    });

                    dgProductos.Columns.Add(new DataGridTextColumn
                    {
                        Header = "COSTO UNIT.",
                        Binding = new System.Windows.Data.Binding("Detalle.CostoUnitario") { StringFormat = "N2" },
                        Width = new DataGridLength(0.9, DataGridLengthUnitType.Star)
                    });
                }
            }
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

                // No generamos correlativo en este punto para evitar condiciones de carrera.
                // Ocultamos los campos al usuario hasta que se confirme el registro.
                txtNumSerie.Text = string.Empty;
                txtNumDocumento.Text = string.Empty;
                txtNumSerie.Visibility = Visibility.Hidden;
                txtNumDocumento.Visibility = Visibility.Hidden;

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
                    SerieGuia=txtSerieGuia.Text.Trim(),
                    NumeroGuia=txtNumeroGuia.Text.Trim(),
                    Observacion = txtObservacion.Text.Trim()
                };

                // Ejecutar transacción
                bool exito = await _serviceMovimiento.RegistrarMovimientoCompletoAsync(nuevaCabecera, _productosGridList, _rangosProcesadosGlobal, UBICACION_ID_SELECCIONADA);

                if (exito)
                {
                    // El servicio ya asignó el correlativo definitivo en 'nuevaCabecera'
                    string correlativoFinal = $"{nuevaCabecera.SerieDocumento}-{nuevaCabecera.NumeroDocumento}";

                    // Limpiar formulario interno
                    LimpiarFormulario();

                    // Mostrar modal de confirmación con correlativo y botón para confirmar
                    var confirmWin = new Window
                    {
                        Owner = Window.GetWindow(this),
                        Title = "Registro creado",
                        Width = 420,
                        Height = 240,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ResizeMode = ResizeMode.NoResize,
                        Content = new System.Windows.Controls.Border
                        {
                            Padding = new Thickness(16),
                            Child = new System.Windows.Controls.StackPanel
                            {
                                Orientation = System.Windows.Controls.Orientation.Vertical,
                                Children =
                                {
                                    new System.Windows.Controls.TextBlock
                                    {
                                        Text = "Se ha registrado correctamente",
                                        FontSize = 18,
                                        FontWeight = FontWeights.SemiBold,
                                        Margin = new Thickness(0,0,0,8),
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        TextAlignment = TextAlignment.Center
                                    },
                                    new System.Windows.Controls.TextBlock
                                    {
                                        Text = correlativoFinal,
                                        FontSize = 28,
                                        FontWeight = FontWeights.Bold,
                                        Margin = new Thickness(0,0,0,12),
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        TextAlignment = TextAlignment.Center
                                    },
                                    new System.Windows.Controls.Button
                                    {
                                        Content = "Confirmar",
                                        Width = 120,
                                        Height = 36,
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        IsDefault = true
                                    }
                                }
                            }
                        }
                    };

                    // Asociar cierre del diálogo al botón
                    if (confirmWin.Content is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.StackPanel sp && sp.Children[2] is System.Windows.Controls.Button btn)
                    {
                        btn.Click += (s, ev) => { confirmWin.DialogResult = true; };
                    }

                    // Mostrar modal (el usuario confirma)
                    confirmWin.ShowDialog();

                    // Guardar correlativo en memoria pero mantener los campos ocultos por seguridad
                    txtNumSerie.Text = nuevaCabecera.SerieDocumento;
                    txtNumDocumento.Text = nuevaCabecera.NumeroDocumento;
                    txtNumSerie.Visibility = Visibility.Hidden;
                    txtNumDocumento.Visibility = Visibility.Hidden;

                    // Preparar UI para ingresar otro registro (campos editables, correlativos ocultos)
                    HabilitarCamposFormulario(true);
                    btnGrabar.IsEnabled = true;
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
            modal.ListaProductosExistentesEnPadre = _productosGridList;
            if (modal.ShowDialog() == true && modal.FueGrabado)
            {
                var productoSelected = modal._productoSeleccionado;
                var rangosDelModal = modal.ListaRangosAgregados;
                int idProducto = productoSelected.Id;

                // 🔥 VALIDACIÓN DE SOLAPAMIENTO DE RANGOS
                foreach (var nuevoRango in rangosDelModal)
                {
                    // Buscamos si existe algún rango ya registrado que se solape con este
                    // La lógica es: (NuevoInicio <= ExistenteFin) AND (NuevoFin >= ExistenteInicio)
                    var solapamiento = _rangosProcesadosGlobal.FirstOrDefault(r =>
                        r.productoId == idProducto &&
                        (nuevoRango.DesdeNum <= r.HastaNum && nuevoRango.HastaNum >= r.DesdeNum)
                    );

                    if (solapamiento != null)
                    {
                        MessageBox.Show($"Conflicto de rangos para el producto: {productoSelected.Descripcion}.\n" +
                                        $"El rango propuesto ({nuevoRango.DesdeNum}-{nuevoRango.HastaNum}) " +
                                        $"se solapa con el rango ya registrado ({solapamiento.DesdeNum}-{solapamiento.HastaNum}).",
                                        "Error de Rangos", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return; // Detenemos la operación
                    }
                }

                // --- SI PASA LA VALIDACIÓN, CONTINUAMOS ---

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
                    ProductoId = idProducto
                };
                _productosGridList.Add(nuevoProductoGrid);

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
                                ProductoId = idProducto
                            });
                        }
                    }
                }

                dgProductos.ItemsSource = null;
                dgProductos.ItemsSource = _productosGridList;
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

            // 1. Limpieza de datos en memoria (CRÍTICO)
            _productosGridList.Clear();
            _codigosGridList.Clear();
            _rangosProcesadosGlobal.Clear();

            // 2. Limpieza de controles visuales
            txtNumSerie.Clear();
            txtNumDocumento.Clear();
            dtpFechaRecepcion.SelectedDate = null;
            cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Clear();
            txtCodigoRazonSocial.Clear();
            txtDireccion.Clear();
            txtUbicacion.Clear();
            txtCodigoUbicacion.Clear();
            txtDireccionUbicacion.Clear();
            txtObservacion.Clear();
            txtSerieGuia.Clear();
            txtNumeroGuia.Clear();

            // 3. Limpieza de las tablas visuales
            dgProductos.ItemsSource = null;
            dgCodigos.ItemsSource = null;

            _personaComercialIdSeleccionada = null;
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
            txtSerieGuia.IsEnabled = habilitar;
            txtNumeroGuia.IsEnabled = habilitar;

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

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Cuando el control está dentro de un ScrollViewer padre, el evento rueda lo captura el padre.
            // Aquí forzamos que la grilla interna se desplace verticalmente.
            if (sender is DependencyObject dep)
            {
                var sv = FindVisualChild<ScrollViewer>(dep);
                if (sv != null)
                {
                    // Delta positivo -> hacia arriba
                    double newOffset = sv.VerticalOffset - e.Delta / 3.0; // ajuste de sensibilidad
                    if (newOffset < 0) newOffset = 0;
                    if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;
                    sv.ScrollToVerticalOffset(newOffset);
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

    }
}