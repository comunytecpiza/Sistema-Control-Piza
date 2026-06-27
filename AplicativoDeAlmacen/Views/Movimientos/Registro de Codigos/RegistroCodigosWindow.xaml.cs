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

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistroCodigosUserControl : UserControl
    {
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

        // ====================================================================
        // 🌟 BÚSQUEDA TIPO AUTOCOMPLETADO (TEXTBOX + LISTBOX)
        // ====================================================================
        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtProducto.IsEnabled || _isUpdatingFromSelection) return;

            string textoBusqueda = TxtProducto.Text.Trim();

            if (!string.IsNullOrWhiteSpace(textoBusqueda))
            {
                // Busca coincidencias en RAM al vuelo
                var sugerencias = _todosLosProductos
                    .Where(p => p.Descripcion != null && p.Descripcion.Contains(textoBusqueda, StringComparison.OrdinalIgnoreCase))
                    .Take(10) // Limitamos a 10 resultados para no colapsar la vista
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
                // Bloqueamos el TextChanged para que no vuelva a buscar
                _isUpdatingFromSelection = true;

                _productoSeleccionadoId = prod.Id;
                TxtProducto.Text = prod.Descripcion;

                PopupProducto.IsOpen = false;
                LstProducto.SelectedIndex = -1;

                productoAbreviaturaActual = prod.Abreviatura;

                // Calculamos automáticamente el rango
                if (CmbModalCategoria.SelectedValue is int catId)
                {
                    try
                    {
                        ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoAsync(prod.Id, prod.Abreviatura, catId);
                        CalcularRangos();
                    }
                    catch (Exception ex) { MessageBox.Show("Error al obtener último código: " + ex.Message); }
                }

                _isUpdatingFromSelection = false;
            }
        }

        // ====================================================================

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

            if (CmbModalColeccion.SelectedValue == null || _productoSeleccionadoId == 0) return;

            _isGuardando = true;

            Button btnGuardar = (Button)sender;
            string textoOriginal = btnGuardar.Content?.ToString() ?? "Guardar";

            try
            {
                btnGuardar.IsEnabled = false;
                btnGuardar.Content = "⏳ Verificando...";

                PbProgreso.Visibility = Visibility.Visible;
                Mouse.OverrideCursor = Cursors.Wait;

                await Task.Delay(1000);
                btnGuardar.Content = "☁️ Subiendo códigos...";

                int coleccionId = (int)CmbModalColeccion.SelectedValue;
                int categoriaId = (int)CmbModalCategoria.SelectedValue;
                int productoId = _productoSeleccionadoId;
                int cantidad = int.Parse(TxtCantidad.Text);

                await _registroService.GuardarCodigosTransactionAsync(coleccionId, productoId, cantidad, TxtDesde.Text, TxtHasta.Text, categoriaId);

                MessageBox.Show("Códigos generados y sincronizados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                ModalAgregar.Visibility = Visibility.Collapsed;
                await CargarGridAsync(coleccionId, categoriaId);
                EventBus.NotificarRegistroCodigosChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Aviso del Sistema", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _isGuardando = false;
                btnGuardar.IsEnabled = true;
                btnGuardar.Content = textoOriginal;
                PbProgreso.Visibility = Visibility.Collapsed;
                Mouse.OverrideCursor = null;
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
    }
}