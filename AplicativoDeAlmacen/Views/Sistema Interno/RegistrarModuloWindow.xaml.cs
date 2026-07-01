using System;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistrarModuloWindow : Window
    {
        private readonly ConfiguracionService _configService;
        private readonly string _nombreVistaXaml;

        public RegistrarModuloWindow(string nombreVistaXaml)
        {
            InitializeComponent();
            _configService = new ConfiguracionService();
            _nombreVistaXaml = nombreVistaXaml;
            TxtVista.Text = _nombreVistaXaml;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cargamos categorías para el "Modo Nuevo"
                var categorias = await _configService.ObtenerCategoriasActivasAsync();
                CboCategoria.ItemsSource = categorias;
                if (categorias.Count > 0) CboCategoria.SelectedIndex = 0;

                // Cargamos módulos huérfanos para el "Modo Existente"
                var modulosSinVista = await _configService.ObtenerModulosSinVistaAsync();
                CboModulosHuerfanos.ItemsSource = modulosSinVista;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message);
            }
        }

        // Alterna entre mostrar el Panel de Nuevo o el Panel de Existente
        private void ChkModuloExistente_Click(object sender, RoutedEventArgs e)
        {
            if (ChkModuloExistente.IsChecked == true)
            {
                PanelNuevo.Visibility = Visibility.Collapsed;
                PanelExistente.Visibility = Visibility.Visible;
            }
            else
            {
                PanelNuevo.Visibility = Visibility.Visible;
                PanelExistente.Visibility = Visibility.Collapsed;
            }
        }

        private async void CboCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboCategoria.SelectedValue is int catId)
            {
                try
                {
                    int siguienteOrden = await _configService.ObtenerSiguienteOrdenPorCategoriaAsync(catId);
                    TxtOrden.Text = siguienteOrden.ToString();
                }
                catch { TxtOrden.Text = "99"; }
            }
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ESTRATEGIA 1: VINCULAR A MÓDULO EXISTENTE
                if (ChkModuloExistente.IsChecked == true)
                {
                    if (CboModulosHuerfanos.SelectedValue == null)
                    {
                        MessageBox.Show("Seleccione el módulo al que desea vincular la vista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    int moduloExistenteId = (int)CboModulosHuerfanos.SelectedValue;
                    await _configService.VincularVistaAModuloAsync(moduloExistenteId, _nombreVistaXaml);
                    MessageBox.Show("Vista vinculada correctamente al módulo existente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                // ESTRATEGIA 2: CREAR MÓDULO NUEVO
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtCodigo.Text) || string.IsNullOrWhiteSpace(TxtNombre.Text) || CboCategoria.SelectedValue == null)
                    {
                        MessageBox.Show("Complete todos los campos para el nuevo módulo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var nuevoModulo = new ModuloSistema
                    {
                        CodigoModulo = TxtCodigo.Text.Trim(),
                        NombreModulo = TxtNombre.Text.Trim(),
                        CategoriaId = (int)CboCategoria.SelectedValue,
                        ControlWpf = _nombreVistaXaml,
                        Orden = int.TryParse(TxtOrden.Text, out int ord) ? ord : 99
                    };

                    await _configService.RegistrarNuevoModuloAsync(nuevoModulo);
                    MessageBox.Show("Módulo registrado correctamente y permisos base asignados.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => Close();
    }
}