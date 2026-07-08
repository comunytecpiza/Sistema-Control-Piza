using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Views
{
    public partial class ProductosUserControl : UserControl
    {
        private readonly ProductoService _productoService;
        private ObservableCollection<Producto> productos = new ObservableCollection<Producto>();
        private Producto? productoActual;

        public ProductosUserControl()
        {
            EventBus.OnProductosChanged += () => Application.Current.Dispatcher.InvokeAsync(CargarProductosAsync);
            InitializeComponent();
            _productoService = new ProductoService();
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
                MessageBox.Show("Error al cargar los productos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarCampos()) return;

            try
            {
                var p = new Producto
                {
                    Id = productoActual?.Id ?? 0,
                    Descripcion = TxtDescripcion.Text,
                    Abreviatura = string.IsNullOrEmpty(TxtAbreviatura.Text) ? null : TxtAbreviatura.Text,
                    UnidadMedidaId = (int?)CmbUnidadMedida.SelectedValue,
                    TipoProductoId = (int?)CmbTipoProducto.SelectedValue,
                    PrecioUnitario = string.IsNullOrWhiteSpace(TxtPrecioUnitario.Text) ? 0.00m : decimal.Parse(TxtPrecioUnitario.Text),
                    Porcentaje = string.IsNullOrWhiteSpace(TxtPorcentaje.Text) ? 0.00m : decimal.Parse(TxtPorcentaje.Text),
                    NivelId = (CmbNivel.SelectedValue is int nId && nId > 0) ? nId : null,
                    GradoId = (CmbGrado.SelectedValue is int gId && gId > 0) ? gId : null,
                    CursoId = (CmbCurso.IsEnabled && CmbCurso.SelectedValue is int cId && cId > 0) ? cId : null,
                    TituloCursoId = ChkTitulo.IsChecked == true && CmbTitulo.IsEnabled ? (int?)CmbTitulo.SelectedValue : null,

                    // 🌟 NUEVA LÓGICA: Guardar Stock Mínimo
                    StockMinimo = string.IsNullOrWhiteSpace(TxtStockMinimo.Text) ? 0 : int.Parse(TxtStockMinimo.Text),

                    AfectacionIgvId = (int?)CmbAfectacionIgv.SelectedValue,
                    EstadoId = 1
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
                EventBus.NotificarProductosChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el producto: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        EventBus.NotificarProductosChanged();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error al eliminar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                EventBus.NotificarProductosChanged();
                ProductoModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para editar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancelarProducto_Click(object sender, RoutedEventArgs e)
        {
            ProductoModal.Visibility = Visibility.Collapsed;
        }

        private async Task CargarDatosProductoEnUI(Producto producto)
        {
            try
            {
                CmbNivel.SelectionChanged -= CmbNivel_SelectionChanged;

                TxtDescripcion.Text = producto.Descripcion ?? "";
                TxtAbreviatura.Text = producto.Abreviatura ?? "";
                TxtPrecioUnitario.Text = Convert.ToDecimal(producto.PrecioUnitario).ToString("N2");
                TxtPorcentaje.Text = Convert.ToDecimal(producto.Porcentaje).ToString("N2");

                // 🌟 NUEVO: Llenar el campo Stock Mínimo
                TxtStockMinimo.Text = producto.StockMinimo.ToString();

                CmbUnidadMedida.SelectedValue = producto.UnidadMedidaId;
                CmbTipoProducto.SelectedValue = producto.TipoProductoId;
                CmbAfectacionIgv.SelectedValue = producto.AfectacionIgvId;
                CmbEstado.SelectedValue = producto.EstadoId;

                if (producto.NivelId.HasValue)
                {
                    CmbNivel.SelectedValue = producto.NivelId.Value;
                    await CargarGradosAsync(producto.NivelId.Value);
                    await CargarCursosAsync(producto.NivelId.Value);
                    CmbGrado.SelectedValue = producto.GradoId;
                    CmbCurso.SelectedValue = producto.CursoId;
                }
                else
                {
                    CmbNivel.SelectedIndex = -1;
                }

                CmbNivel.SelectionChanged += CmbNivel_SelectionChanged;

                ChkTitulo.IsChecked = producto.TituloCursoId.HasValue;
                if (producto.TituloCursoId.HasValue)
                {
                    await CargarTitulosAsync();
                    CmbTitulo.SelectedValue = producto.TituloCursoId;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message);
            }
        }

        private void LimpiarCamposProducto()
        {
            TxtDescripcion.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            TxtPrecioUnitario.Text = string.Empty;
            TxtPorcentaje.Text = string.Empty;

            // 🌟 NUEVO: Reestablecer el stock a 0 por defecto
            TxtStockMinimo.Text = "0";

            CmbUnidadMedida.SelectedIndex = -1;
            CmbTipoProducto.SelectedIndex = -1;

            CmbNivel.SelectionChanged -= CmbNivel_SelectionChanged;
            CmbNivel.SelectedIndex = -1;
            CmbNivel.SelectionChanged += CmbNivel_SelectionChanged;

            CmbGrado.ItemsSource = null;
            CmbCurso.ItemsSource = null;
            CmbTitulo.ItemsSource = null;
            CmbAfectacionIgv.SelectedIndex = -1;
            CmbEstado.SelectedIndex = -1;

            ChkTitulo.Checked -= ChkTitulo_Checked;
            ChkTitulo.IsChecked = false;
            ChkTitulo.Checked += ChkTitulo_Checked;
            CmbTitulo.IsEnabled = false;
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();
            var resultados = productos.Where(p =>
                p.Id.ToString().Contains(busqueda) ||
                (p.Descripcion?.ToLower() ?? "").Contains(busqueda) ||
                (p.Abreviatura?.ToLower() ?? "").Contains(busqueda) ||
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
            try
            {
                var unidades = await _productoService.ObtenerUnidadesMedidaAsync();
                CmbUnidadMedida.ItemsSource = unidades;
                CmbUnidadMedida.DisplayMemberPath = "Descripcion";
                CmbUnidadMedida.SelectedValuePath = "Id";

                var tipos = await _productoService.ObtenerTiposProductoAsync();
                CmbTipoProducto.ItemsSource = tipos;
                CmbTipoProducto.DisplayMemberPath = "Nombre";
                CmbTipoProducto.SelectedValuePath = "Id";

                var niveles = await _productoService.ObtenerNivelesAsync();
                CmbNivel.ItemsSource = niveles;
                CmbNivel.DisplayMemberPath = "Nombre";
                CmbNivel.SelectedValuePath = "Id";

                CmbAfectacionIgv.ItemsSource = await _productoService.ObtenerAfectacionesIgvAsync();
                CmbAfectacionIgv.DisplayMemberPath = "Nombre";
                CmbAfectacionIgv.SelectedValuePath = "Id";

                CmbEstado.ItemsSource = await _productoService.ObtenerEstadosAsync();
                CmbEstado.DisplayMemberPath = "Nombre";
                CmbEstado.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar catálogos: " + ex.Message);
            }
        }

        private void CmbTipoProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTipoProducto.SelectedItem is TipoProducto tipo)
            {
                if (tipo.Nombre.ToLower().Contains("otros"))
                {
                    CmbCurso.SelectedIndex = -1;
                    CmbCurso.IsEnabled = false;

                    ChkTitulo.IsChecked = false;
                    ChkTitulo.IsEnabled = false;
                    CmbTitulo.SelectedIndex = -1;
                    CmbTitulo.IsEnabled = false;
                }
                else
                {
                    CmbCurso.IsEnabled = true;
                    ChkTitulo.IsEnabled = true;
                }
            }
            else
            {
                CmbCurso.IsEnabled = true;
                ChkTitulo.IsEnabled = true;
            }
        }

        private async void CmbNivel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (CmbNivel.SelectedValue is int nivelId)
                {
                    await CargarGradosAsync(nivelId);
                    await CargarCursosAsync(nivelId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cambiar de nivel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbCurso_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private async void ChkTitulo_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                CmbTitulo.IsEnabled = true;
                await CargarTitulosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar títulos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChkTitulo_Unchecked(object sender, RoutedEventArgs e)
        {
            CmbTitulo.IsEnabled = false;
            CmbTitulo.SelectedIndex = -1;
        }

        private async Task CargarGradosAsync(int nivelId)
        {
            var grados = await _productoService.ObtenerGradosAsync(nivelId);
            grados.Insert(0, new Grado { Id = 0, Nombre = "-- Seleccione un Grado --" });
            CmbGrado.ItemsSource = grados;
            CmbGrado.DisplayMemberPath = "Nombre";
            CmbGrado.SelectedValuePath = "Id";
        }

        private async Task CargarCursosAsync(int nivelId)
        {
            var cursos = await _productoService.ObtenerCursosAsync(nivelId);
            cursos.Insert(0, new Curso { Id = 0, Nombre = "-- Seleccione un Curso --" });
            CmbCurso.ItemsSource = cursos;
            CmbCurso.DisplayMemberPath = "Nombre";
            CmbCurso.SelectedValuePath = "Id";
        }

        private async Task CargarTitulosAsync()
        {
            CmbTitulo.ItemsSource = await _productoService.ObtenerTitulosAsync();
            CmbTitulo.DisplayMemberPath = "Nombre";
            CmbTitulo.SelectedValuePath = "Id";
        }

        private bool ValidarCampos()
        {
            if (string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("La descripción del producto es obligatoria.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtDescripcion.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtAbreviatura.Text))
            {
                MessageBox.Show("La abreviatura es obligatoria para poder generar los códigos en el sistema.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAbreviatura.Focus();
                return false;
            }

            if (CmbUnidadMedida.SelectedValue == null)
            {
                MessageBox.Show("Debe seleccionar una Unidad de Medida (Ej. Unidad, Docena, Paquete).", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbUnidadMedida.Focus();
                return false;
            }

            if (CmbTipoProducto.SelectedValue == null)
            {
                MessageBox.Show("Debe clasificar el producto seleccionando un Tipo de Producto.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbTipoProducto.Focus();
                return false;
            }

            if (CmbAfectacionIgv.SelectedValue == null)
            {
                MessageBox.Show("Debe seleccionar el tipo de Afectación IGV para la facturación.", "Validación Contable", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbAfectacionIgv.Focus();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TxtPrecioUnitario.Text) && !decimal.TryParse(TxtPrecioUnitario.Text, out _))
            {
                MessageBox.Show("El Precio Unitario ingresado no tiene un formato numérico válido.", "Validación Numérica", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPrecioUnitario.Focus();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TxtPorcentaje.Text) && !decimal.TryParse(TxtPorcentaje.Text, out _))
            {
                MessageBox.Show("El Porcentaje ingresado no tiene un formato numérico válido.", "Validación Numérica", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPorcentaje.Focus();
                return false;
            }

            // 🌟 NUEVA VALIDACIÓN: Verifica que el Stock Mínimo sea un número válido 🌟
            if (!string.IsNullOrWhiteSpace(TxtStockMinimo.Text) && !int.TryParse(TxtStockMinimo.Text, out _))
            {
                MessageBox.Show("El Stock Mínimo debe ser un número entero válido (Ej. 100).", "Validación Numérica", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStockMinimo.Focus();
                return false;
            }

            if (CmbNivel.SelectedValue != null && CmbNivel.SelectedValue is int nivelId && nivelId > 0)
            {
                if (CmbGrado.SelectedValue == null || (CmbGrado.SelectedValue is int gradoId && gradoId == 0))
                {
                    MessageBox.Show("Si ha seleccionado un Nivel Académico, debe especificar obligatoriamente el Grado.", "Validación Académica", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CmbGrado.Focus();
                    return false;
                }
            }

            return true;
        }
    }
}