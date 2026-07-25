using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class AcademicoMaestroUserControl : UserControl
    {
        private readonly AcademicoMaestroService _academicoService;
        private int _registroIdActual = 0;

        public AcademicoMaestroUserControl()
        {
            InitializeComponent();
            _academicoService = new AcademicoMaestroService();
        }

        private async void CboTablaAcademica_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboTablaAcademica.SelectedItem is ComboBoxItem item)
            {
                string tablaDestino = item.Tag.ToString();
                TxtTituloGrid.Text = $"Registros de: {item.Content}";
                LimpiarFormulario();

                // Lógica Dinámica de la Vista
                if (tablaDestino == "niveles")
                {
                    ColNivelPadre.Visibility = Visibility.Collapsed;
                    PanelNivelPadre.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ColNivelPadre.Visibility = Visibility.Visible;
                    PanelNivelPadre.Visibility = Visibility.Visible;
                    await CargarComboNivelesAsync(); // Llenamos el ComboBox Padre
                }

                await CargarGridAsync(tablaDestino);
            }
        }

        private async Task CargarComboNivelesAsync()
        {
            try
            {
                var niveles = await _academicoService.ObtenerNivelesAsync();
                CboNiveles.ItemsSource = niveles;
            }
            catch { /* Manejo silencioso */ }
        }

        private async Task CargarGridAsync(string tabla)
        {
            try
            {
                if (tabla == "niveles")
                {
                    var niveles = await _academicoService.ObtenerNivelesAsync();
                    // Aplanamos el modelo Nivel a VistaGradoCurso para que el DataGrid lo pinte igual
                    AcademicoDataGrid.ItemsSource = niveles.Select(n => new VistaGradoCurso { Id = n.Id, Nombre = n.Nombre }).ToList();
                }
                else
                {
                    var hijos = await _academicoService.ObtenerHijosAsync(tabla);
                    AcademicoDataGrid.ItemsSource = hijos;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la tabla: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (CboTablaAcademica.SelectedItem is null) return;

            string tablaActual = (CboTablaAcademica.SelectedItem as ComboBoxItem).Tag.ToString();

            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("El nombre no puede estar vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (tablaActual == "niveles")
                {
                    var nivel = new Nivel { Id = _registroIdActual, Nombre = TxtNombre.Text.Trim() };
                    await _academicoService.GuardarNivelAsync(nivel);
                }
                else
                {
                    if (CboNiveles.SelectedValue == null)
                    {
                        MessageBox.Show("Debe seleccionar un Nivel Padre.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var hijo = new VistaGradoCurso
                    {
                        Id = _registroIdActual,
                        Nombre = TxtNombre.Text.Trim(),
                        NivelId = (int)CboNiveles.SelectedValue
                    };
                    await _academicoService.GuardarHijoAsync(tablaActual, hijo);
                }

                MessageBox.Show("Operación exitosa.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                LimpiarFormulario();
                await CargarGridAsync(tablaActual);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is VistaGradoCurso item)
            {
                _registroIdActual = item.Id;
                TxtNombre.Text = item.Nombre;

                if (PanelNivelPadre.Visibility == Visibility.Visible && item.NivelId > 0)
                {
                    CboNiveles.SelectedValue = item.NivelId;
                }
            }
        }

        private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is VistaGradoCurso item)
            {
                var resp = MessageBox.Show($"¿Desea eliminar '{item.Nombre}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (resp == MessageBoxResult.Yes)
                {
                    string tablaActual = (CboTablaAcademica.SelectedItem as ComboBoxItem).Tag.ToString();
                    try
                    {
                        if (tablaActual == "niveles") await _academicoService.EliminarNivelAsync(item.Id);
                        else await _academicoService.EliminarHijoAsync(tablaActual, item.Id);

                        await CargarGridAsync(tablaActual);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("No se puede eliminar. Posiblemente esté siendo usado por un Libro/Producto.\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e) => LimpiarFormulario();

        private void LimpiarFormulario()
        {
            _registroIdActual = 0;
            TxtNombre.Text = string.Empty;
            CboNiveles.SelectedIndex = -1;
        }
    }
}