using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Services.Importaciones;

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistroCodigosUserControl : UserControl
    {
        private bool _isModoExcel = false;
        private List<string> _codigosImportados = new List<string>();
        private readonly RegistroCodigoService _registroService;
        private List<RegistroCodigo> _registrosGrid = new List<RegistroCodigo>();

        private List<Producto> _todosLosProductos = new List<Producto>();

        private string? productoAbreviaturaActual;
        private int ultimoCodigoActual = 0;

        private int _productoSeleccionadoId = 0;
        private bool _isUpdatingFromSelection = false;
        private bool _isGuardando = false;

        public RegistroCodigosUserControl()
        {
            InitializeComponent();
            _registroService = new RegistroCodigoService();

            _ = InicializarPantallaAsync();

            AplicarPermisosRBAC();
            EventBus.OnProductosChanged += ActualizarComboProductosDesdeEvento;
            EventBus.OnRegistroCodigosChanged += () => Application.Current.Dispatcher.InvokeAsync(async () => {
                if (CmbFiltroColeccion.SelectedValue is int cId) await CargarGridAsync(cId, RbLibroGuia.IsChecked == true ? 1 : 2);
            });

            this.IsVisibleChanged += RegistroCodigosUserControl_IsVisibleChanged;
        }

        private async void RegistroCodigosUserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible && (bool)e.NewValue == true)
            {
                // Re-verificamos permisos cada vez que la pestaña toma el foco para evitar brechas de sesión
                AplicarPermisosRBAC();

                if (this.IsEnabled && CmbFiltroColeccion.SelectedValue is int coleccionId)
                {
                    int categoriaId = RbLibroGuia.IsChecked == true ? 1 : 2;
                    await CargarGridAsync(coleccionId, categoriaId);
                }
            }
        }

        private async Task InicializarPantallaAsync()
        {
            try
            {
                var colecciones = await _registroService.ObtenerColeccionesAsync();
                CmbFiltroColeccion.ItemsSource = colecciones;
                CmbFiltroColeccion.DisplayMemberPath = "Ano";
                CmbFiltroColeccion.SelectedValuePath = "Id";

                CmbModalColeccion.ItemsSource = colecciones;
                CmbModalColeccion.DisplayMemberPath = "Ano";
                CmbModalColeccion.SelectedValuePath = "Id";

                if (colecciones.Any()) CmbFiltroColeccion.SelectedIndex = 0;

                var categorias = await _registroService.ObtenerCategoriasAsync();
                CmbModalCategoria.ItemsSource = categorias;
                CmbModalCategoria.DisplayMemberPath = "Nombre";
                CmbModalCategoria.SelectedValuePath = "Id";

                var productos = await _registroService.ObtenerProductosComboAsync();
                _todosLosProductos = productos.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error de conexión inicial: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Filtros_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbFiltroColeccion.SelectedValue is int coleccionId)
            {
                int categoriaId = RbLibroGuia.IsChecked == true ? 1 : 2;
                await CargarGridAsync(coleccionId, categoriaId);
            }
        }

        private async void ActualizarComboProductosDesdeEvento()
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var productosFrescos = await _registroService.ObtenerProductosComboAsync();
                    _todosLosProductos = productosFrescos.ToList();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error recargando productos: " + ex.Message);
                }
            });
        }

        private async Task CargarGridAsync(int coleccionId, int categoriaId)
        {
            try
            {
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                // 🌟 REGLA CORREGIDA POR SESIÓN ACTIVA:
                // Solo la sesión en ALMACÉN CENTRAL TRUJILLO (Sede 1) consulta de forma global.
                // Si la sesión activa está en LIMA o cualquier sub-almacén, SE FILTRA POR ESA SEDE,
                // sin importar si el usuario es Administrador o Trabajador.
                int? filtroAlmacen = (miAlmacenId == ALMACEN_CENTRAL_ID) ? null : miAlmacenId;

                var data = await _registroService.ObtenerRegistrosAsync(coleccionId, categoriaId, filtroAlmacen);
                _registrosGrid = data.ToList();
                CodigosDataGrid.ItemsSource = _registrosGrid;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la tabla: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtProducto.IsEnabled || _isUpdatingFromSelection) return;

            string textoBusqueda = TxtProducto.Text.Trim();

            if (!string.IsNullOrWhiteSpace(textoBusqueda))
            {
                var sugerencias = _todosLosProductos
                    .Where(p => p.Descripcion != null &&
                                p.Descripcion.Contains(textoBusqueda, StringComparison.OrdinalIgnoreCase) &&
                                !(string.IsNullOrWhiteSpace(p.Abreviatura) &&
                                  (p.Descripcion.ToUpperInvariant().Contains("MOCHILA") ||
                                   p.Descripcion.ToUpperInvariant().Contains("CUADERNO"))))
                    .Take(10)
                    .ToList();

                if (sugerencias.Any())
                {
                    LstProducto.ItemsSource = sugerencias;
                    PopupProducto.IsOpen = true;
                }
                else
                {
                    PopupProducto.IsOpen = false;
                }
            }
            else
            {
                PopupProducto.IsOpen = false;
            }
        }

        private async void LstProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProducto.SelectedItem is Producto prod)
            {
                _isUpdatingFromSelection = true;

                _productoSeleccionadoId = prod.Id;
                TxtProducto.Text = prod.Descripcion;

                PopupProducto.IsOpen = false;
                LstProducto.SelectedIndex = -1;

                
                // 🌟 TOMAMOS LA ABREVIATURA COMPLETA DE LA BASE DE DATOS (Ej: "LMA5 C26-V" o "24015-V")
                if (!string.IsNullOrWhiteSpace(prod.Abreviatura))
                {
                    // Limpiamos espacios al inicio/final pero conservamos toda la abreviatura real
                    productoAbreviaturaActual = prod.Abreviatura.Trim().ToUpperInvariant();
                }
                else
                {
                    // Si la abreviatura es NULL (ej: Mochilas o Libro Alfa numérico GUIA)
                    productoAbreviaturaActual = null;
                }

                if (CmbModalCategoria.SelectedValue is int catId)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(prod.Abreviatura))
                        {
                            ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoAsync(prod.Id, productoAbreviaturaActual, catId);
                            TxtDesde.Text = "";
                            TxtHasta.Text = "";
                        }
                        else
                        {
                            if (_isModoExcel)
                            {
                                ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoAsync(prod.Id, productoAbreviaturaActual, catId);
                                TxtDesde.Text = "";
                                TxtHasta.Text = "";
                            }
                            else
                            {
                                ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoSecuencialAsync(prod.Id, productoAbreviaturaActual, catId);
                                CalcularRangos();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error al obtener último código: " + ex.Message);
                    }
                }

                _isUpdatingFromSelection = false;
            }
        }

        private void TxtCantidad_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalcularRangos();
        }

        private void CalcularRangos()
        {
            if (int.TryParse(TxtCantidad.Text, out int cantidad) && !string.IsNullOrEmpty(productoAbreviaturaActual))
            {
                int desde = ultimoCodigoActual + 1;
                int hasta = desde + cantidad - 1;
                TxtDesde.Text = $"{productoAbreviaturaActual}-{desde:D7}";
                TxtHasta.Text = $"{productoAbreviaturaActual}-{hasta:D7}";
            }
            else
            {
                TxtDesde.Text = "";
                TxtHasta.Text = "";
            }
        }

        
        private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (CodigosDataGrid.SelectedItem is RegistroCodigo item)
            {
                if (MessageBox.Show($"¿Está seguro de eliminar los códigos generados para {item.Producto?.Descripcion}?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _registroService.EliminarRegistroTransactionAsync(item.Id);
                        MessageBox.Show("Códigos eliminados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        Filtros_Changed(null, null);
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error al eliminar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Seleccione un registro de la tabla para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            LimpiarModal();
            if (CmbFiltroColeccion.SelectedValue != null) CmbModalColeccion.SelectedValue = CmbFiltroColeccion.SelectedValue;
            CmbModalCategoria.SelectedValue = RbLibroGuia.IsChecked == true ? 1 : 2;
            ModalAgregar.Visibility = Visibility.Visible;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            ModalAgregar.Visibility = Visibility.Collapsed;
        }

        private void LimpiarModal()
        {
            _isUpdatingFromSelection = true;
            TxtProducto.Text = string.Empty;
            _productoSeleccionadoId = 0;
            _isUpdatingFromSelection = false;

            TxtCantidad.Text = string.Empty;
            TxtDesde.Text = string.Empty;
            TxtHasta.Text = string.Empty;
            productoAbreviaturaActual = null;
            ultimoCodigoActual = 0;

            _codigosImportados.Clear();
            TxtCantidadExcel.Text = "Total detectados: 0";
            TxtRutaArchivo.Text = "Ningún archivo seleccionado";
            BtnVisualizarExcel.IsEnabled = false;
        }

        private void BtnVerDetalle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is RegistroCodigo lote)
            {
                AbrirPestanaDetalle(lote);
            }
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is RegistroCodigo lote)
            {
                AbrirPestanaDetalle(lote);
            }
        }

        private void AbrirPestanaDetalle(RegistroCodigo lote)
        {
            if (Window.GetWindow(this) is IMainWindow mainWindow)
            {
                string tituloPestana = $"Lote: {lote.Producto?.Abreviatura ?? "Cod"}";
                mainWindow.AbrirPestaña(tituloPestana, new DetalleCodigosUserControl(lote));
            }
        }

        private void ModoGeneracion_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSecuencial == null || PanelImportacion == null) return;

            if (RbModoExcel.IsChecked == true)
            {
                _isModoExcel = true;
                PanelSecuencial.Visibility = Visibility.Collapsed;
                PanelImportacion.Visibility = Visibility.Visible;
                btnGuardar.Content = "💾 Procesar Lote desde Excel";
            }
            else
            {
                _isModoExcel = false;
                PanelSecuencial.Visibility = Visibility.Visible;
                PanelImportacion.Visibility = Visibility.Collapsed;
                btnGuardar.Content = "💾 Subir y Generar Lote";
            }
        }

        // 🌟 1. AL SELECCIONAR EL EXCEL, SE ABRE AUTOMÁTICAMENTE LA AUDITORÍA OBLIGATORIA
        private async void BtnCargarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0 || string.IsNullOrWhiteSpace(productoAbreviaturaActual))
            {
                MessageBox.Show("Por favor, seleccione primero el producto maestro antes de cargar el archivo Excel.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos Excel (*.xlsx)|*.xlsx",
                Title = "Seleccione el archivo de códigos"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtRutaArchivo.Text = openFileDialog.FileName;
                TxtRutaArchivo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
                TxtRutaArchivo.FontStyle = FontStyles.Normal;

                try
                {
                    var importService = new ImportacionExcelService();
                    List<string> codigosBrutos = new List<string>();

                    // 1. Lectura rápida del Excel
                    Mouse.OverrideCursor = Cursors.Wait;
                    codigosBrutos = await importService.LeerCodigosDesdeExcelAsync(openFileDialog.FileName);
                    Mouse.OverrideCursor = null;

                    if (!codigosBrutos.Any())
                    {
                        MessageBox.Show("El archivo Excel seleccionado está vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 🌟 OPTIMIZACIÓN CLAVE: Filtrar previamente los códigos que coinciden con el prefijo del producto seleccionado
                    string prefijoLimpio = productoAbreviaturaActual.Replace(" ", "").Replace("-", "").ToUpperInvariant();

                    // Solo mandamos a consultar a la BD aquellos códigos que SÍ pertenecen a este producto
                    var codigosCandidatosProducto = codigosBrutos
                        .Where(c => !string.IsNullOrWhiteSpace(c) &&
                                    c.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant()
                                     .StartsWith(prefijoLimpio, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    List<string> duplicadosBD = new List<string>();

                    // Solo consultamos la BD si hay candidatos válidos para este producto
                    if (codigosCandidatosProducto.Any())
                    {
                        var loadingModal = new ProgressWindow("Verificando Base de Datos", $"Cruzando {codigosCandidatosProducto.Count:N0} candidatos con el inventario...", async (progress) =>
                        {
                            duplicadosBD = await importService.ObtenerCodigosDuplicadosAsync(codigosCandidatosProducto);
                        });

                        loadingModal.Owner = Window.GetWindow(this);
                        loadingModal.ShowDialog();
                    }

                    Mouse.OverrideCursor = null;

                    // 2. Abrir la vista previa con todos los códigos brutos para auditoría completa
                    var ventana = new VistaPreviaExcelWindow(codigosBrutos, duplicadosBD, productoAbreviaturaActual);
                    ventana.Owner = Window.GetWindow(this);

                    if (ventana.ShowDialog() == true)
                    {
                        _codigosImportados = ventana.CodigosAprobados;
                        TxtCantidadExcel.Text = $"Aprobados para guardar: {_codigosImportados.Count:N0}";
                        BtnVisualizarExcel.IsEnabled = true;
                        btnGuardar.IsEnabled = _codigosImportados.Count > 0;
                    }
                    else
                    {
                        _codigosImportados.Clear();
                        TxtCantidadExcel.Text = "Aprobados para guardar: 0 (Proceso Cancelado)";
                        btnGuardar.IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al procesar el archivo Excel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _codigosImportados.Clear();
                    btnGuardar.IsEnabled = false;
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        // Botón "Revisar/Visualizar" opcional por si quiere volver a abrir la ventana antes de guardar
        private async void BtnVisualizarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!_codigosImportados.Any()) return;

            var service = new ImportacionExcelService();
            List<string> duplicadosBD = new List<string>();

            var loadingModal = new ProgressWindow("Revisando Archivo", "Reverificando estado con el inventario...", async (progress) =>
            {
                duplicadosBD = await service.ObtenerCodigosDuplicadosAsync(_codigosImportados);
            });

            loadingModal.Owner = Window.GetWindow(this);

            if (loadingModal.ShowDialog() == true)
            {
                var ventana = new VistaPreviaExcelWindow(_codigosImportados, duplicadosBD, productoAbreviaturaActual);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    _codigosImportados = ventana.CodigosAprobados;
                    TxtCantidadExcel.Text = $"Aprobados para guardar: {_codigosImportados.Count}";
                    btnGuardar.IsEnabled = _codigosImportados.Count > 0;
                }
            }
        }

        // 🔒 CANDADO FINAL EN EL BOTÓN GUARDAR
        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_isGuardando) return;

            if (CmbModalColeccion.SelectedValue == null || _productoSeleccionadoId == 0)
            {
                MessageBox.Show("Por favor, seleccione una colección y un producto válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isModoExcel && (!_codigosImportados.Any()))
            {
                MessageBox.Show("No hay códigos válidos aprobados para guardar. Por favor, cargue un archivo Excel válido.", "Aviso de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isGuardando = true;
            Button btnGuardar = (Button)sender;
            string textoOriginal = btnGuardar.Content?.ToString() ?? "Guardar";

            try
            {
                btnGuardar.IsEnabled = false;

                int coleccionId = (int)CmbModalColeccion.SelectedValue;
                int categoriaId = (int)CmbModalCategoria.SelectedValue;
                int productoId = _productoSeleccionadoId;

                if (_isModoExcel)
                {
                    var importService = new ImportacionExcelService();
                    string nombreArchivoExcel = System.IO.Path.GetFileName(TxtRutaArchivo.Text);

                    int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
                    int almacenActualId = SesionSistema.AlmacenActual?.Id ?? 1;

                    Exception? errorTransaccion = null;

                    var progressModal = new ProgressWindow("Guardando Lote Limpio", "Insertando registros auditados...", async (progress) =>
                    {
                        try
                        {
                            await importService.GuardarCodigosImportadosTransactionAsync(
                                coleccionId,
                                productoId,
                                categoriaId,
                                _codigosImportados,
                                usuarioActivoId,
                                almacenActualId,
                                nombreArchivoExcel,
                                progress);
                        }
                        catch (Exception ex)
                        {
                            errorTransaccion = ex;
                            throw; // Permite que el modal detecte la falla
                        }
                    });

                    progressModal.Owner = Window.GetWindow(this);
                    bool? resultado = progressModal.ShowDialog();

                    if (resultado == true)
                    {
                        MessageBox.Show($"¡Éxito! Se registraron {_codigosImportados.Count} códigos correctamente.", "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);
                        ModalAgregar.Visibility = Visibility.Collapsed;
                        await CargarGridAsync(coleccionId, categoriaId);
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                    else if (errorTransaccion != null)
                    {
                        // 🚨 AQUÍ TE MOSTRARÁ EL MENSAJE DE ERROR EXACTO EN PANTALLA
                        MessageBox.Show($"Error en la base de datos al importar:\n\n{errorTransaccion.Message}", "Falla de Guardado", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtCantidad.Text)) return;
                    int cantidad = int.Parse(TxtCantidad.Text);
                    string desde = TxtDesde.Text;
                    string hasta = TxtHasta.Text;

                    int usuarioActivoId = SesionSistema.UsuarioActual?.Id ?? 1;
                    string modoOrigen = "SECUENCIAL"; // Como es secuencial, guardará explícitamente "SECUENCIAL"
                    int usuarioActualId = SesionSistema.UsuarioActual?.Id ?? 1;
                    int almacenActualId = SesionSistema.AlmacenActual?.Id > 0 ? SesionSistema.AlmacenActual.Id : 1;
                    var progressModal = new ProgressWindow("Generando Secuencia", "Creando e insertando lote secuencial...", async (progress) =>
                    {
                        await _registroService.GuardarCodigosTransactionAsync( // 👈 Cambiado a _registroService
                            coleccionId,
                            productoId,
                            cantidad,
                            desde,
                            hasta,
                            categoriaId,
                            usuarioActualId,  // 👈 ID de Usuario real de la sesión
                            almacenActualId,  // 👈 ID del Almacén activo en la barra inferior
                            "SECUENCIAL",
                            progress          // 👈 Cambiado a 'progress' (la variable de la lambda)
                        );
                    });

                    progressModal.Owner = Window.GetWindow(this);
                    if (progressModal.ShowDialog() == true)
                    {
                        MessageBox.Show("Códigos generados y sincronizados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        ModalAgregar.Visibility = Visibility.Collapsed;
                        await CargarGridAsync(coleccionId, categoriaId);
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un problema al procesar el lote:\n\n{ex.Message}", "Aviso del Sistema", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _isGuardando = false;
                btnGuardar.IsEnabled = true;
                btnGuardar.Content = textoOriginal;
            }
        }


        private void AplicarPermisosRBAC()
        {
            // 1. Buscamos los permisos asignados a este módulo evaluando tanto el nombre del control como posibles alias comunes
            var permisoModulo = SesionSistema.PermisosActuales?
                .FirstOrDefault(p => p.ControlWpf == nameof(RegistroCodigosUserControl) ||
                                     p.CodigoModulo.Equals("REGISTRO_CODIGOS", StringComparison.OrdinalIgnoreCase) ||
                                     p.NombreModulo.Contains("Códigos", StringComparison.OrdinalIgnoreCase));

            if (permisoModulo != null)
            {
                // 🛡️ SEGURIDAD TOTAL: Si el usuario NO tiene permiso de Ver, bloqueamos o cerramos la vista completa de inmediato
                if (!permisoModulo.PuedeVer)
                {
                    MessageBox.Show("No cuenta con autorización para acceder a este módulo de Registro de Códigos.", "Acceso Restringido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    this.IsEnabled = false;
                    this.Visibility = Visibility.Collapsed;
                    return;
                }

                // 2. Control granular de botones según la base de datos
                BtnNuevoLote.Visibility = permisoModulo.PuedeCrear ? Visibility.Visible : Visibility.Collapsed;
                BtnEliminarLote.Visibility = permisoModulo.PuedeEliminar ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // 🛡️ CANDADO ESTRICTO POR DEFECTO: Si no hay registro de permisos para este rol, se deniega el acceso total
                MessageBox.Show("No se encontraron privilegios configurados para este módulo. Acceso denegado por seguridad.", "Seguridad RBAC", MessageBoxButton.OK, MessageBoxImage.Stop);
                this.IsEnabled = false;
                this.Visibility = Visibility.Collapsed;
            }
        }

       
    }
}