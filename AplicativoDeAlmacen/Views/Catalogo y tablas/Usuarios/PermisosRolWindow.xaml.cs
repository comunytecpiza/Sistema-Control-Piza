using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class PermisosRolWindow : Window
    {
        private readonly UsuarioService _usuarioService;
        private readonly int _rolId;
        private List<RolPermiso> _matrizPermisos;

        public PermisosRolWindow(int rolId, string nombreRol)
        {
            InitializeComponent();
            _usuarioService = new UsuarioService();
            _rolId = rolId;
            TxtNombreRol.Text = nombreRol.ToUpper();

            Loaded += PermisosRolWindow_Loaded;
        }

        private async void PermisosRolWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Obtenemos los datos de la base de datos
                _matrizPermisos = await _usuarioService.ObtenerPermisosPorRolAsync(_rolId);

                // Conectamos la lista a la vista agrupada "PermisosAgrupados" que creamos en el XAML
                var cvs = (CollectionViewSource)this.Resources["PermisosAgrupados"];
                cvs.Source = _matrizPermisos;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la matriz: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // ==============================================================
        // LÓGICA DE SELECCIÓN MASIVA (Master Checkboxes)
        // ==============================================================

        private void AplicarMasivo(Action<RolPermiso, bool> accion, object sender)
        {
            if (_matrizPermisos == null || !_matrizPermisos.Any()) return;

            bool isChecked = (sender as CheckBox)?.IsChecked ?? false;

            foreach (var permiso in _matrizPermisos)
            {
                accion(permiso, isChecked);
            }

            // Forzamos a la tabla a refrescar lo visual
            PermisosDataGrid.Items.Refresh();
        }

        private void ChkAllVer_Click(object sender, RoutedEventArgs e) => AplicarMasivo((p, val) => p.PuedeVer = val, sender);
        private void ChkAllCrear_Click(object sender, RoutedEventArgs e) => AplicarMasivo((p, val) => p.PuedeCrear = val, sender);
        private void ChkAllEditar_Click(object sender, RoutedEventArgs e) => AplicarMasivo((p, val) => p.PuedeEditar = val, sender);
        private void ChkAllEliminar_Click(object sender, RoutedEventArgs e) => AplicarMasivo((p, val) => p.PuedeEliminar = val, sender);
        private void ChkAllImprimir_Click(object sender, RoutedEventArgs e) => AplicarMasivo((p, val) => p.PuedeImprimir = val, sender);

        // ==============================================================
        // GUARDADO DE DATOS
        // ==============================================================

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_matrizPermisos != null && _matrizPermisos.Any())
                {
                    await _usuarioService.GuardarPermisosMasivosAsync(_rolId, _matrizPermisos);
                    MessageBox.Show("Políticas de seguridad actualizadas correctamente para este rol.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar las políticas: " + ex.Message, "Error de Persistencia", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }
    }
}