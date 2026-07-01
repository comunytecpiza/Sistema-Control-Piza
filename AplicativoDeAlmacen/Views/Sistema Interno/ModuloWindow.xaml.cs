using AplicativoDeAlmacen.Models; // Asegúrate de que aquí esté tu ModuloSistema
using AplicativoDeAlmacen.Models.Models; // Por si tus modelos están aquí
using AplicativoDeAlmacen.Services;
using System;
using System.Windows;

namespace AplicativoDeAlmacen.Views.Sistema_Interno
{
    public partial class ModuloWindow : Window
    {
        private ModuloSistema _modulo;
        private readonly ConfiguracionService _service;

        // El '= null' permite usar esta ventana para "Nuevo Módulo" sin pasarle datos
        public ModuloWindow(ModuloSistema modulo = null)
        {
            InitializeComponent();
            _service = new ConfiguracionService();

            // Si viene null, creamos un objeto vacío. Si viene con datos, lo usamos.
            _modulo = modulo ?? new ModuloSistema();

            // Solo cargamos los textos si estamos en modo edición (ID > 0)
            if (_modulo.Id > 0)
            {
                TxtNombre.Text = _modulo.NombreModulo;
                TxtCodigo.Text = _modulo.CodigoModulo;
                TxtOrden.Text = _modulo.Orden.ToString();
            }
            else
            {
                // Valores por defecto para un módulo nuevo
                TxtOrden.Text = "99";
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var cats = await _service.ObtenerCategoriasActivasAsync();
                CboCategoria.ItemsSource = cats;

                // Si estamos editando, seleccionamos su categoría. Si es nuevo, seleccionamos el primero de la lista.
                if (_modulo.Id > 0)
                {
                    CboCategoria.SelectedValue = _modulo.CategoriaId;
                }
                else if (cats.Count > 0)
                {
                    CboCategoria.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar categorías: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validaciones básicas anti-errores
            if (string.IsNullOrWhiteSpace(TxtNombre.Text) || string.IsNullOrWhiteSpace(TxtCodigo.Text) || CboCategoria.SelectedValue == null)
            {
                MessageBox.Show("Por favor, complete todos los campos obligatorios.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Asignamos los valores de las cajas de texto al objeto
            _modulo.NombreModulo = TxtNombre.Text.Trim();
            _modulo.CodigoModulo = TxtCodigo.Text.Trim();
            _modulo.CategoriaId = (int)CboCategoria.SelectedValue;

            // TryParse evita que el programa "crashee" si el usuario escribe texto en lugar de un número
            _modulo.Orden = int.TryParse(TxtOrden.Text, out int orden) ? orden : 99;

            try
            {
                // 3. Decidimos si hacemos INSERT (Nuevo) o UPDATE (Editar)
                if (_modulo.Id == 0)
                {
                    await _service.RegistrarNuevoModuloAsync(_modulo);
                }
                else
                {
                    await _service.ActualizarModuloAsync(_modulo);
                }

                // 4. Avisamos que todo salió bien y cerramos el modal
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el módulo: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CboCategoria_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Solo calculamos el orden automático si es un MÓDULO NUEVO (Id == 0)
            if (_modulo.Id == 0 && CboCategoria.SelectedValue is int catId)
            {
                try
                {
                    // Llamamos a tu servicio que hace MAX(orden) + 1
                    int siguienteOrden = await _service.ObtenerSiguienteOrdenPorCategoriaAsync(catId);

                    // Actualizamos la caja de texto solitos
                    TxtOrden.Text = siguienteOrden.ToString();
                }
                catch
                {
                    // Si algo falla, ponemos 99 para que se vaya al final
                    TxtOrden.Text = "99";
                }
            }
        }
    }
}