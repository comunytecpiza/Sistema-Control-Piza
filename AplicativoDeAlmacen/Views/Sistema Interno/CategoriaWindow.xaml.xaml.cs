using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Services;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views.Sistema_Interno
{
    public partial class CategoriaWindow : Window
    {
        private CategoriaModulo _categoria;

        public CategoriaWindow()
        {
            InitializeComponent();

            RegistrarEventos();

            CbIconos.SelectedIndex = 0;

            ColorPicker.SelectedColor =
                Colors.DodgerBlue;

            TxtOrden.Text = "1";

            ActualizarPreview();
        }

        public CategoriaWindow(CategoriaModulo categoria)
        {
            InitializeComponent();

            _categoria = categoria;

            RegistrarEventos();

            CargarCategoria();

            ActualizarPreview();
        }

        private void RegistrarEventos()
        {
            TxtNombre.TextChanged += (_, __)
                => ActualizarPreview();

            CbIconos.SelectionChanged += (_, __)
                => ActualizarPreview();

            ColorPicker.SelectedColorChanged += (_, __)
                => ActualizarPreview();
        }

        private void CargarCategoria()
        {
            TxtNombre.Text =
                _categoria.Nombre;

            TxtOrden.Text =
                _categoria.Orden.ToString();

            foreach (ComboBoxItem item in CbIconos.Items)
            {
                if (item.Content.ToString()
                    == _categoria.Icono)
                {
                    CbIconos.SelectedItem =
                        item;

                    break;
                }
            }

            try
            {
                ColorPicker.SelectedColor =
                    (Color)ColorConverter
                    .ConvertFromString(
                        _categoria.Color);
            }
            catch
            {
                ColorPicker.SelectedColor =
                    Colors.DodgerBlue;
            }
        }

        private void ActualizarPreview()
        {
            TxtPreviewTitulo.Text =
                TxtNombre.Text;

            if (CbIconos.SelectedItem
                is ComboBoxItem item)
            {
                TxtPreviewIcon.Text =
                    item.Content.ToString();
            }

            if (ColorPicker.SelectedColor
                .HasValue)
            {
                PreviewBox.Background =
                    new SolidColorBrush(
                        ColorPicker.SelectedColor.Value);
            }
        }

        private async void BtnGuardar_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                TxtNombre.Text))
            {
                MessageBox.Show(
                    "Ingrese un nombre.",
                    "Aviso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            bool esNuevo =
                _categoria == null;

            if (esNuevo)
            {
                _categoria =
                    new CategoriaModulo();
            }

            _categoria.Nombre =
                TxtNombre.Text;

            if (CbIconos.SelectedItem
                is ComboBoxItem icono)
            {
                _categoria.Icono =
                    icono.Content.ToString();
            }

            if (ColorPicker.SelectedColor
                .HasValue)
            {
                _categoria.Color =
                    ColorPicker.SelectedColor
                    .Value
                    .ToString();
            }

            if (!int.TryParse(
                    TxtOrden.Text,
                    out int orden))
            {
                orden = 1;
            }

            _categoria.Orden =
                orden;

            _categoria.Estado =
                true;

            var service =
                new ConfiguracionService();

            try
            {
                await service.GuardarCategoriaAsync(
                    _categoria,
                    esNuevo);

                DialogResult = true;

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}