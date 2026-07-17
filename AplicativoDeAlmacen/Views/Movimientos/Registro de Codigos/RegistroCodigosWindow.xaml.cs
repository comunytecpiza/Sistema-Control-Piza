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

        // 🌟 Memoria RAM rápida para búsqueda
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
                if (CmbFiltroColeccion.SelectedValue is int coleccionId)
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

                // Cargamos todos los productos a la memoria para búsqueda rápida
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
                var data = await _registroService.ObtenerRegistrosAsync(coleccionId, categoriaId);
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
                // 🌟 REGLA DE EXCLUSIÓN TOTAL:
                // Buscamos las coincidencias de texto ignorando mayúsculas/minúsculas.
                // Solo descartamos el producto si cumple AMBAS condiciones de descarte: 
                // 1. Su descripción o datos contienen la palabra "MOCHILA" u "OTRO" (es un producto genérico por volumen).
                // 2. Y a la vez carece de prefijo (Abreviatura NULL o vacía).
                // De este modo, tu "LIBRO ALFANUMERICO" (Texto Escolar sin prefijo) pasará libre y se mostrará al instante.
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

                // 🌟 Asignación profesional de base: Si no hay abreviatura, usamos la descripción limpia
                productoAbreviaturaActual = string.IsNullOrWhiteSpace(prod.Abreviatura)
                    ? prod.Descripcion
                    : prod.Abreviatura;

                if (CmbModalCategoria.SelectedValue is int catId)
                {
                    try
                    {
                        // Si el producto no tiene abreviatura (es alfanumérico puro), forzamos Modo Excel automáticamente
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

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_isGuardando) return;

            if (CmbModalColeccion.SelectedValue == null || _productoSeleccionadoId == 0)
            {
                MessageBox.Show("Por favor, seleccione una colección y un producto válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    if (!_codigosImportados.Any())
                    {
                        MessageBox.Show("Debe importar códigos primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var importService = new ImportacionExcelService();

                    // 🌟 CORRECCIÓN ASÍNCRONA: Instanciamos el modal y le asignamos el Owner correcto de la App
                    var progressModal = new ProgressWindow("Guardando Lote de Códigos", "Insertando registros masivos en la base de datos...", async (progress) =>
                    {
                        await importService.GuardarCodigosImportadosTransactionAsync(coleccionId, productoId, categoriaId, _codigosImportados, progress);
                    });

                    progressModal.Owner = Window.GetWindow(this);

                    // Forzamos el ShowDialog de manera segura esperando el hilo gráfico
                    if (progressModal.ShowDialog() == true)
                    {
                        MessageBox.Show("Códigos importados y generados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        ModalAgregar.Visibility = Visibility.Collapsed;
                        await CargarGridAsync(coleccionId, categoriaId);
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                    else if (progressModal.ErrorResult != null)
                    {
                        throw progressModal.ErrorResult;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtCantidad.Text)) return;
                    int cantidad = int.Parse(TxtCantidad.Text);
                    string desde = TxtDesde.Text;
                    string hasta = TxtHasta.Text;

                    // 🌟 CORRECCIÓN ASÍNCRONA: Lote secuencial
                    var progressModal = new ProgressWindow("Generando Secuencia", "Creando e insertando lote secuencial...", async (progress) =>
                    {
                        await _registroService.GuardarCodigosTransactionAsync(coleccionId, productoId, cantidad, desde, hasta, categoriaId, progress);
                    });

                    progressModal.Owner = Window.GetWindow(this);

                    if (progressModal.ShowDialog() == true)
                    {
                        MessageBox.Show("Códigos generados y sincronizados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        ModalAgregar.Visibility = Visibility.Collapsed;
                        await CargarGridAsync(coleccionId, categoriaId);
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                    else if (progressModal.ErrorResult != null)
                    {
                        throw progressModal.ErrorResult;
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

        private async void BtnCargarExcel_Click(object sender, RoutedEventArgs e)
        {
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
                    Mouse.OverrideCursor = Cursors.Wait;
                    var importService = new AplicativoDeAlmacen.Services.Importaciones.ImportacionExcelService();

                    _codigosImportados = await importService.LeerCodigosDesdeExcelAsync(openFileDialog.FileName);

                    TxtCantidadExcel.Text = $"Total detectados: {_codigosImportados.Count}";
                    BtnVisualizarExcel.IsEnabled = _codigosImportados.Count > 0;
                    BtnVisualizarExcel.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al leer Excel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void BtnVisualizarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!_codigosImportados.Any()) return;

            var service = new ImportacionExcelService();
            List<string> duplicados = new List<string>();

            // 🌟 1. VENTANA DE PROGRESO REAL PARA LA BÚSQUEDA DE DUPLICADOS
            var loadingModal = new ProgressWindow("Verificando Duplicados", "Cruzando datos con el inventario...", async (progress) =>
            {
                // Gracias a tu índice SQL y la tabla temporal, esto volará, 
                // pero la ventana entretiene al usuario mientras la red responde.
                duplicados = await service.ObtenerCodigosDuplicadosAsync(_codigosImportados);
            });

            loadingModal.Owner = Window.GetWindow(this);

            if (loadingModal.ShowDialog() == true)
            {
                var ventana = new VistaPreviaExcelWindow(_codigosImportados, duplicados);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    _codigosImportados = ventana.CodigosAprobados;
                    TxtCantidadExcel.Text = $"Total seleccionados: {_codigosImportados.Count}";
                }
            }
        }

        
    }
}