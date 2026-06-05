using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Services;      // Tu carpeta de servicios
using AplicativoDeAlmacen.Models.Models; // Tus modelos autogenerados

namespace AplicativoDeAlmacen.Views
{
    public partial class ProductosUserControl : UserControl
    {
        // 1. INYECTAMOS EL SERVICIO (Cero SQL en esta clase)
        private readonly ProductoService _productoService;
        private ObservableCollection<Producto> productos = new ObservableCollection<Producto>();
        private Producto? productoActual;

        public ProductosUserControl()
        {
            InitializeComponent();
            _productoService = new ProductoService();

            // Llamamos al método asíncrono desde el constructor
            _ = InicializarPantallaAsync();
        }

        private async Task InicializarPantallaAsync()
        {
            await CargarProductosAsync();
            await CargarCombosBaseAsync();
        }

        // ==========================================
        // MÉTODOS PRINCIPALES DEL CRUD
        // ==========================================

        private async Task CargarProductosAsync()
        {
            try
            {
                productos.Clear();
                var listaDb = await _productoService.ObtenerTodosAsync();

                foreach (var item in listaDb)
                {
                    productos.Add(item);
                }

                ProductosDataGrid.ItemsSource = productos;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar los productos: " + ex.Message);
            }
        }

        private async void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarCampos()) return;

            try
            {
                // Construimos el objeto Producto leyendo los controles visuales
                var p = new Producto
                {
                    Id = productoActual?.Id ?? 0,
                    Descripcion = TxtDescripcion.Text,
                    Abreviatura = string.IsNullOrEmpty(TxtAbreviatura.Text) ? null : TxtAbreviatura.Text,
                    UnidadMedidaId = (int?)CmbUnidadMedida.SelectedValue,
                    TipoProductoId = (int?)CmbTipoProducto.SelectedValue,
                    PrecioUnitario = string.IsNullOrWhiteSpace(TxtPrecioUnitario.Text) ? 0.00m : decimal.Parse(TxtPrecioUnitario.Text),
                    Porcentaje = string.IsNullOrWhiteSpace(TxtPorcentaje.Text) ? 0.00m : decimal.Parse(TxtPorcentaje.Text),
                    NivelId = (int?)CmbNivel.SelectedValue,
                    GradoId = (int?)CmbGrado.SelectedValue,
                    CursoId = (int?)CmbCurso.SelectedValue,
                    TituloCursoId = ChkTitulo.IsChecked == true ? (int?)CmbTitulo.SelectedValue : null,
                    AfectacionIgvId = (int?)CmbAfectacionIgv.SelectedValue,
                    EstadoId = (int?)CmbEstado.SelectedValue
                };

                if (productoActual == null)
                {
                    await _productoService.InsertarAsync(p);
                    MessageBox.Show("Producto registrado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _productoService.ActualizarAsync(p);
                    MessageBox.Show("Producto actualizado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                ProductoModal.Visibility = Visibility.Collapsed;
                await CargarProductosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el producto: " + ex.Message);
            }
        }

        private async void EliminarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductosDataGrid.SelectedItem is Producto productoAEliminar)
            {
                if (MessageBox.Show("¿Está seguro de que desea eliminar este producto?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _productoService.EliminarAsync(productoAEliminar.Id);
                        await CargarProductosAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error al eliminar: " + ex.Message);
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para eliminar.");
            }
        }

        // ==========================================
        // INTERACCIÓN CON LA UI (MODALES Y BOTONES)
        // ==========================================

        private void AgregarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            productoActual = null;
            ModalTitle.Text = "Nuevo Producto";
            LimpiarCamposProducto();
            ProductoModal.Visibility = Visibility.Visible;
        }

        private async void EditarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductosDataGrid.SelectedItem is Producto seleccionado)
            {
                productoActual = seleccionado;
                ModalTitle.Text = "Editar Producto";
                await CargarDatosProductoEnUI(productoActual);
                ProductoModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para editar.");
            }
        }

        private void BtnCancelarProducto_Click(object sender, RoutedEventArgs e)
        {
            ProductoModal.Visibility = Visibility.Collapsed;
        }

        private async Task CargarDatosProductoEnUI(Producto producto)
        {
            TxtDescripcion.Text = producto.Descripcion ?? "";
            TxtAbreviatura.Text = producto.Abreviatura ?? "";
            TxtPrecioUnitario.Text = Convert.ToDecimal(producto.PrecioUnitario).ToString("N2");
            TxtPorcentaje.Text = Convert.ToDecimal(producto.Porcentaje).ToString("N2");

            CmbUnidadMedida.SelectedValue = producto.UnidadMedidaId;
            CmbTipoProducto.SelectedValue = producto.TipoProductoId;
            CmbNivel.SelectedValue = producto.NivelId;
            CmbAfectacionIgv.SelectedValue = producto.AfectacionIgvId;
            CmbEstado.SelectedValue = producto.EstadoId;

            // Para los combos en cascada de Nivel -> Grado/Curso
            if (producto.NivelId.HasValue)
            {
                await CargarGradosAsync(producto.NivelId.Value);
                await CargarCursosAsync(producto.NivelId.Value);
                CmbGrado.SelectedValue = producto.GradoId;
                CmbCurso.SelectedValue = producto.CursoId;
            }

            // Los títulos ya son independientes, los cargamos directo si están activos
            ChkTitulo.IsChecked = producto.TituloCursoId.HasValue;
            if (producto.TituloCursoId.HasValue)
            {
                await CargarTitulosAsync();
                CmbTitulo.SelectedValue = producto.TituloCursoId;
            }
        }

        private void LimpiarCamposProducto()
        {
            TxtDescripcion.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtPrecioUnitario.Text = string.Empty;
            TxtPorcentaje.Text = string.Empty;

            CmbUnidadMedida.SelectedIndex = -1;
            CmbTipoProducto.SelectedIndex = -1;
            CmbNivel.SelectedIndex = -1;
            CmbGrado.ItemsSource = null;
            CmbCurso.ItemsSource = null;
            CmbTitulo.ItemsSource = null;
            CmbAfectacionIgv.SelectedIndex = -1;
            CmbEstado.SelectedIndex = -1;

            ChkTitulo.IsChecked = false;
            CmbTitulo.IsEnabled = false;
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();
            var resultados = productos.Where(p =>
                p.Id.ToString().Contains(busqueda) ||
                (p.Descripcion?.ToLower() ?? "").Contains(busqueda) ||
                (p.Abreviatura?.ToLower() ?? "").Contains(busqueda) ||
                // ⚠️ CORRECCIÓN: Navegamos dentro de los objetos para buscar sus textos
                (p.UnidadMedida?.Descripcion?.ToLower() ?? "").Contains(busqueda) ||
                p.PrecioUnitario.ToString().Contains(busqueda) ||
                (p.afectacion?.Nombre?.ToLower() ?? "").Contains(busqueda) ||
                (p.Estado?.Nombre?.ToLower() ?? "").Contains(busqueda)
            );
            ProductosDataGrid.ItemsSource = new ObservableCollection<Producto>(resultados);
        }

        private void TxtDecimal_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (decimal.TryParse(textBox.Text, out decimal value))
                    textBox.Text = value.ToString("N2");
                else
                    textBox.Text = "0.00";
            }
        }

        // ==========================================
        // CARGA Y EVENTOS DE COMBOBOX (EN CASCADA)
        // ==========================================

        private async Task CargarCombosBaseAsync()
        {
            CmbUnidadMedida.ItemsSource = await _productoService.ObtenerUnidadesMedidaAsync();
            CmbUnidadMedida.DisplayMemberPath = "Descripcion";
            CmbUnidadMedida.SelectedValuePath = "Id";

            CmbTipoProducto.ItemsSource = await _productoService.ObtenerTiposProductoAsync();
            CmbTipoProducto.DisplayMemberPath = "Nombre";
            CmbTipoProducto.SelectedValuePath = "Id";

            CmbNivel.ItemsSource = await _productoService.ObtenerNivelesAsync();
            CmbNivel.DisplayMemberPath = "Nombre";
            CmbNivel.SelectedValuePath = "Id";

            CmbAfectacionIgv.ItemsSource = await _productoService.ObtenerAfectacionesIgvAsync();
            CmbAfectacionIgv.DisplayMemberPath = "Nombre";
            CmbAfectacionIgv.SelectedValuePath = "Id";

            CmbEstado.ItemsSource = await _productoService.ObtenerEstadosAsync();
            CmbEstado.DisplayMemberPath = "Nombre";
            CmbEstado.SelectedValuePath = "Id";
        }

        private async void CmbNivel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbNivel.SelectedValue is int nivelId)
            {
                await CargarGradosAsync(nivelId);
                await CargarCursosAsync(nivelId);
            }
        }

        private void CmbCurso_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Como los títulos ya no dependen del curso, este evento queda vacío.
            // (Si quieres, puedes borrar el evento SelectionChanged="CmbCurso_SelectionChanged" en tu XAML)
        }

        private async void ChkTitulo_Checked(object sender, RoutedEventArgs e)
        {
            CmbTitulo.IsEnabled = true;
            // ⚠️ CORRECCIÓN: Llamamos a la versión asíncrona sin parámetros
            await CargarTitulosAsync();
        }

        private void ChkTitulo_Unchecked(object sender, RoutedEventArgs e)
        {
            CmbTitulo.IsEnabled = false;
            CmbTitulo.SelectedIndex = -1;
        }

        private async Task CargarGradosAsync(int nivelId)
        {
            CmbGrado.ItemsSource = await _productoService.ObtenerGradosAsync(nivelId);
            CmbGrado.DisplayMemberPath = "Nombre";
            CmbGrado.SelectedValuePath = "Id";
        }

        private async Task CargarCursosAsync(int nivelId)
        {
            CmbCurso.ItemsSource = await _productoService.ObtenerCursosAsync(nivelId);
            CmbCurso.DisplayMemberPath = "Nombre";
            CmbCurso.SelectedValuePath = "Id";
        }

        // ⚠️ CORRECCIÓN: Este método ya no pide cursoId, carga todos los títulos.
        private async Task CargarTitulosAsync()
        {
            // Asumiendo que actualizaste tu ObtenerTitulosAsync() en ProductoService 
            // para que no pida (int cursoId)
            CmbTitulo.ItemsSource = await _productoService.ObtenerTitulosAsync();
            CmbTitulo.DisplayMemberPath = "Nombre";
            CmbTitulo.SelectedValuePath = "Id";
        }

        private bool ValidarCampos()
        {
            if (string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("La descripción es obligatoria.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }
    }
}