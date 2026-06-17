using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Data;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistroCodigosUserControl : UserControl
    {
        private readonly RegistroCodigoService _registroService;

        private ObservableCollection<RegistroCodigo> registrosGrid = new ObservableCollection<RegistroCodigo>();
        private ObservableCollection<Producto> productosTodos = new ObservableCollection<Producto>();

        private DispatcherTimer searchTimer;
        private string? productoAbreviaturaActual;
        private int ultimoCodigoActual = 0;
        private bool isTyping = false;

        public RegistroCodigosUserControl()
        {
            InitializeComponent();
            _registroService = new RegistroCodigoService();

            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += SearchTimer_Tick;

            _ = InicializarPantallaAsync();

            // 🌟 LA MAGIA: Escuchamos cuando la pestaña se vuelve a mostrar en pantalla
            this.IsVisibleChanged += RegistroCodigosUserControl_IsVisibleChanged;
        }

        // ==============================================================
        // ACTUALIZACIÓN AUTOMÁTICA AL CAMBIAR DE PESTAÑA
        // ==============================================================
        private async void RegistroCodigosUserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Si la pestaña acaba de hacerse visible...
            if (this.IsVisible && (bool)e.NewValue == true)
            {
                // Y ya hay una colección seleccionada, recargamos la tabla para traer datos frescos
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

                var productos = await _registroService.ObtenerProductosComboAsync();
                foreach (var p in productos) productosTodos.Add(p);
                CmbProducto.ItemsSource = productosTodos;
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

        private async Task CargarGridAsync(int coleccionId, int categoriaId)
        {
            try
            {
                registrosGrid.Clear();
                var data = await _registroService.ObtenerRegistrosAsync(coleccionId, categoriaId);
                foreach (var item in data) registrosGrid.Add(item);
                CodigosDataGrid.ItemsSource = registrosGrid;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la tabla: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            CmbProducto.SelectedIndex = -1;
            CmbProducto.Text = string.Empty;
            TxtCantidad.Text = string.Empty;
            TxtDesde.Text = string.Empty;
            TxtHasta.Text = string.Empty;
            productoAbreviaturaActual = null;
            ultimoCodigoActual = 0;

            var view = CollectionViewSource.GetDefaultView(productosTodos);
            view.Filter = null;
        }

        private void CmbProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CmbProducto.SelectedIndex != -1) return;

            isTyping = true;
            searchTimer.Stop();
            searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            searchTimer.Stop();

            if (!isTyping) return;

            var textBox = CmbProducto.Template.FindName("PART_EditableTextBox", CmbProducto) as TextBox;
            int cursorPosition = textBox?.CaretIndex ?? 0;
            string search = CmbProducto.Text ?? "";

            var view = CollectionViewSource.GetDefaultView(productosTodos);

            if (string.IsNullOrWhiteSpace(search))
            {
                view.Filter = null;
            }
            else
            {
                string lowerSearch = search.ToLower();
                view.Filter = item =>
                {
                    var p = (Producto)item;
                    return p.Descripcion != null && p.Descripcion.ToLower().Contains(lowerSearch);
                };
            }

            CmbProducto.IsDropDownOpen = true;

            if (textBox != null)
            {
                textBox.Text = search;
                textBox.CaretIndex = cursorPosition;
            }

            isTyping = false;
        }

        private async void CmbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProducto.SelectedItem is Producto prod && CmbModalCategoria.SelectedValue is int catId)
            {
                isTyping = false;
                productoAbreviaturaActual = prod.Abreviatura;
                try
                {
                    ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoAsync(prod.Id, prod.Abreviatura, catId);
                    CalcularRangos();
                }
                catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
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
            if (CmbModalColeccion.SelectedValue == null || CmbProducto.SelectedItem == null) return;

            try
            {
                PbProgreso.Visibility = Visibility.Visible;
                Mouse.OverrideCursor = Cursors.Wait;

                int coleccionId = (int)CmbModalColeccion.SelectedValue;
                int categoriaId = (int)CmbModalCategoria.SelectedValue;
                int productoId = ((Producto)CmbProducto.SelectedItem).Id;
                int cantidad = int.Parse(TxtCantidad.Text);

                await _registroService.GuardarCodigosTransactionAsync(coleccionId, productoId, cantidad, TxtDesde.Text, TxtHasta.Text, categoriaId);

                MessageBox.Show("Códigos generados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                ModalAgregar.Visibility = Visibility.Collapsed;
                await CargarGridAsync(coleccionId, categoriaId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                PbProgreso.Visibility = Visibility.Collapsed;
                Mouse.OverrideCursor = null;
            }
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