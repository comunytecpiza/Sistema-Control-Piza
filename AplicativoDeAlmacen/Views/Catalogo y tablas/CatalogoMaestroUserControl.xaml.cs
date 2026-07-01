using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models; // Aquí viven MotivoProducto, CatalogoBasico y CatalogoViewItem
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class CatalogoMaestroUserControl : UserControl
    {
        private readonly CatalogoMaestroService _catalogoService;
        private int _registroIdActual = 0; // ID > 0 significa que estamos Editando

        public CatalogoMaestroUserControl()
        {
            InitializeComponent();
            _catalogoService = new CatalogoMaestroService();
        }

        // =========================================================
        // 1. CARGA DE TABLAS AL CAMBIAR EL COMBOBOX
        // =========================================================
        private async void CboCatalogo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboCatalogo.SelectedItem is ComboBoxItem item)
            {
                string tablaDestino = item.Tag.ToString();
                TxtTituloGrid.Text = $"Registros de: {item.Content}";
                LimpiarFormulario();

                // Adaptación de la Interfaz Visual según la tabla
                if (tablaDestino == "motivo_productos")
                {
                    ColInfoExtra.Visibility = Visibility.Visible;
                    PanelTipoMovimiento.Visibility = Visibility.Visible;
                    ColInfoExtra.Header = "Tipo Op.";
                }
                else if (tablaDestino == "unidad_medida")
                {
                    ColInfoExtra.Visibility = Visibility.Visible;
                    PanelTipoMovimiento.Visibility = Visibility.Collapsed;
                    ColInfoExtra.Header = "Abreviatura";
                }
                else
                {
                    ColInfoExtra.Visibility = Visibility.Collapsed;
                    PanelTipoMovimiento.Visibility = Visibility.Collapsed;
                }

                await CargarDatosGridAsync(tablaDestino);
            }
        }

        private async Task CargarDatosGridAsync(string tabla)
        {
            try
            {
                // Si es Motivos, usamos el método complejo
                if (tabla == "motivo_productos")
                {
                    var datosDb = await _catalogoService.ObtenerMotivosAsync();
                    // CORRECCIÓN: Usamos CatalogoViewItem para la vista
                    CatalogosDataGrid.ItemsSource = datosDb.Select(x => new CatalogoViewItem
                    {
                        Id = x.Id,
                        Descripcion = x.Descripcion,
                        InfoExtra = x.TipoMovimiento.ToUpper(),
                        TablaOrigen = tabla
                    }).ToList();
                }
                // Si es Unidades de Medida
                else if (tabla == "unidad_medida")
                {
                    var datosDb = await _catalogoService.ObtenerCatalogoAsync(tabla, "descripcion");
                    // CORRECCIÓN: Usamos CatalogoViewItem para la vista
                    CatalogosDataGrid.ItemsSource = datosDb.Select(x => new CatalogoViewItem
                    {
                        Id = x.Id,
                        Descripcion = x.Nombre,
                        InfoExtra = "UND",
                        TablaOrigen = tabla
                    }).ToList();
                }
                // Para todos los demás catálogos (Tipo Persona, Categorías, etc.)
                else
                {
                    // En tu BD, algunas tablas usan "nombre" y otras "descripcion". Validamos esto:
                    string campoTexto = (tabla == "tipos_libro" || tabla == "tipo_persona" || tabla == "categoria_producto" || tabla == "tipo_ubicacion" || tabla == "afectacion_igv") ? "nombre" : "descripcion";

                    var datosDb = await _catalogoService.ObtenerCatalogoAsync(tabla, campoTexto);
                    // CORRECCIÓN: Usamos CatalogoViewItem para la vista
                    CatalogosDataGrid.ItemsSource = datosDb.Select(x => new CatalogoViewItem
                    {
                        Id = x.Id,
                        Descripcion = x.Nombre,
                        TablaOrigen = tabla
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // 2. OPERACIONES CRUD (Guardar, Editar, Eliminar)
        // =========================================================
        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (CboCatalogo.SelectedItem is null)
            {
                MessageBox.Show("Seleccione un catálogo primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("La descripción no puede estar vacía.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tablaActual = (CboCatalogo.SelectedItem as ComboBoxItem).Tag.ToString();
            string campoTexto = (tablaActual == "motivo_productos" || tablaActual == "unidad_medida") ? "descripcion" : "nombre";

            try
            {
                if (tablaActual == "motivo_productos")
                {
                    // Usamos tu modelo preexistente
                    var motivo = new MotivoProducto
                    {
                        Id = _registroIdActual,
                        Descripcion = TxtDescripcion.Text.Trim(),
                        TipoMovimiento = RbEntrada.IsChecked == true ? "entrada" : "salida"
                    };
                    await _catalogoService.GuardarMotivoAsync(motivo);
                }
                else
                {
                    // Usamos el modelo básico genérico
                    var basico = new CatalogoBasico
                    {
                        Id = _registroIdActual,
                        Nombre = TxtDescripcion.Text.Trim()
                    };
                    await _catalogoService.GuardarCatalogoAsync(tablaActual, basico, campoTexto);
                }

                LimpiarFormulario();
                await CargarDatosGridAsync(tablaActual);
                MessageBox.Show("Registro guardado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditarFila_Click(object sender, RoutedEventArgs e)
        {
            // CORRECCIÓN: El DataContext de la fila es un CatalogoViewItem
            if ((sender as Button)?.DataContext is CatalogoViewItem item)
            {
                _registroIdActual = item.Id;
                TxtDescripcion.Text = item.Descripcion;

                if (item.TablaOrigen == "motivo_productos")
                {
                    if (item.InfoExtra == "ENTRADA") RbEntrada.IsChecked = true;
                    else RbSalida.IsChecked = true;
                }
            }
        }

        private async void BtnEliminarFila_Click(object sender, RoutedEventArgs e)
        {
            // CORRECCIÓN: El DataContext de la fila es un CatalogoViewItem
            if ((sender as Button)?.DataContext is CatalogoViewItem item)
            {
                var result = MessageBox.Show($"¿Seguro que desea eliminar: {item.Descripcion}?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (item.TablaOrigen == "motivo_productos")
                            await _catalogoService.EliminarMotivoAsync(item.Id);
                        else
                            await _catalogoService.EliminarCatalogoAsync(item.TablaOrigen, item.Id);

                        await CargarDatosGridAsync(item.TablaOrigen);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("No se puede eliminar porque está en uso en otra tabla.\n" + ex.Message, "Error de Dependencia", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e) => LimpiarFormulario();

        private void LimpiarFormulario()
        {
            _registroIdActual = 0;
            TxtDescripcion.Text = string.Empty;
            RbEntrada.IsChecked = true;
        }
    }
}