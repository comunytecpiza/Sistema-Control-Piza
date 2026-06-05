using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models; // Fundamental para que reconozca RegistroCodigo y Producto

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistroCodigosUserControl : UserControl
    {
        private readonly RegistroCodigoService _registroService;

        // Usamos los modelos puros
        private ObservableCollection<RegistroCodigo> registrosGrid = new ObservableCollection<RegistroCodigo>();
        private ObservableCollection<Producto> productosTodos = new ObservableCollection<Producto>();

        private DispatcherTimer searchTimer;
        private string? productoAbreviaturaActual;
        private int ultimoCodigoActual = 0;

        public RegistroCodigosUserControl()
        {
            InitializeComponent();
            _registroService = new RegistroCodigoService();

            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += SearchTimer_Tick;

            _ = InicializarPantallaAsync();
        }

        private async Task InicializarPantallaAsync()
        {
            try
            {
                // Cargar Colecciones
                var colecciones = await _registroService.ObtenerColeccionesAsync();
                CmbFiltroColeccion.ItemsSource = colecciones;
                CmbFiltroColeccion.DisplayMemberPath = "Ano";
                CmbFiltroColeccion.SelectedValuePath = "Id";

                CmbModalColeccion.ItemsSource = colecciones;
                CmbModalColeccion.DisplayMemberPath = "Ano";
                CmbModalColeccion.SelectedValuePath = "Id";

                if (colecciones.Any()) CmbFiltroColeccion.SelectedIndex = 0;

                // Cargar Categorías
                var categorias = await _registroService.ObtenerCategoriasAsync();
                CmbModalCategoria.ItemsSource = categorias;
                CmbModalCategoria.DisplayMemberPath = "Nombre";
                CmbModalCategoria.SelectedValuePath = "Id";

                // Cargar Productos Puros
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
                        await _registroService.EliminarRegistroTransactionAsync(item.Id); // En tu modelo principal el ID de registro es "Id"
                        MessageBox.Show("Códigos eliminados correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        Filtros_Changed(null, null); // Recargar Grid
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

        // ==========================================
        // LÓGICA DEL MODAL (AGREGAR CÓDIGOS)
        // ==========================================

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
        }

        private void CmbProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            searchTimer.Stop();
            string search = CmbProducto.Text?.ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(search))
            {
                CmbProducto.ItemsSource = productosTodos;
            }
            else
            {
                var filtrados = productosTodos.Where(p => (p.Descripcion?.ToLower() ?? "").Contains(search));
                CmbProducto.ItemsSource = new ObservableCollection<Producto>(filtrados);
            }
            CmbProducto.IsDropDownOpen = true;
        }

        private async void CmbProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProducto.SelectedItem is Producto prod)
            {
                productoAbreviaturaActual = prod.Abreviatura;
                try
                {
                    ultimoCodigoActual = await _registroService.ObtenerUltimoCodigoAsync(prod.Id, prod.Abreviatura);
                    CalcularRangos();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo obtener el último código: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            if (CmbModalColeccion.SelectedValue == null || CmbModalCategoria.SelectedValue == null ||
                CmbProducto.SelectedItem == null || string.IsNullOrWhiteSpace(TxtDesde.Text))
            {
                MessageBox.Show("Complete todos los campos correctamente.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int coleccionId = (int)CmbModalColeccion.SelectedValue;
                int categoriaId = (int)CmbModalCategoria.SelectedValue;
                int productoId = ((Producto)CmbProducto.SelectedItem).Id;
                int cantidad = int.Parse(TxtCantidad.Text);

                await _registroService.GuardarCodigosTransactionAsync(coleccionId, productoId, cantidad, TxtDesde.Text, TxtHasta.Text, categoriaId);

                MessageBox.Show("Códigos generados y guardados con éxito en la base de datos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                ModalAgregar.Visibility = Visibility.Collapsed;

                Filtros_Changed(null, null); // Recargar Grid
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error Crítico en Base de Datos: " + ex.Message, "Rollback Ejecutado", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}