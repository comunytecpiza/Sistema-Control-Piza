#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class PermisosRolWindow : Window
    {
        private readonly UsuarioService _usuarioService;
        private readonly int _rolId;
        private List<RolPermiso> _todosLosPermisos = new();
        private List<RolPermiso> _permisosFiltrados = new();

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
                var lista = await _usuarioService.ObtenerPermisosPorRolAsync(_rolId);

                _todosLosPermisos = lista
                    .OrderBy(p => p.CategoriaNombre)
                    .ThenBy(p => p.NombreModulo)
                    .ToList();

                // Cargar lista de categorías únicas en el panel izquierdo
                var categorias = _todosLosPermisos
                    .Select(p => p.CategoriaNombre)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                LstCategorias.Items.Clear();
                LstCategorias.Items.Add("📂 TODAS LAS SECCIONES");
                foreach (var cat in categorias)
                {
                    LstCategorias.Items.Add(cat);
                }

                LstCategorias.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar la matriz de permisos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void RefrescarGrilla()
        {
            string categoriaSeleccionada = LstCategorias.SelectedItem?.ToString() ?? "📂 TODAS LAS SECCIONES";
            string filtroTexto = TxtFiltroModulo.Text.Trim().ToLower();

            var query = _todosLosPermisos.AsEnumerable();

            if (categoriaSeleccionada != "📂 TODAS LAS SECCIONES")
            {
                query = query.Where(p => p.CategoriaNombre.Equals(categoriaSeleccionada, StringComparison.OrdinalIgnoreCase));
                TxtTituloSeccion.Text = $"Sección: {categoriaSeleccionada}";
            }
            else
            {
                TxtTituloSeccion.Text = "Todos los módulos del sistema";
            }

            if (!string.IsNullOrEmpty(filtroTexto))
            {
                query = query.Where(p => p.NombreModulo.ToLower().Contains(filtroTexto));
            }

            _permisosFiltrados = query.ToList();
            PermisosDataGrid.ItemsSource = null;
            PermisosDataGrid.ItemsSource = _permisosFiltrados;
        }

        private void LstCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefrescarGrilla();
        }

        private void TxtFiltroModulo_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefrescarGrilla();
        }

        // ==============================================================
        // ACCIONES MASIVAS
        // ==============================================================

        private void AplicarColumna(Action<RolPermiso, bool> accion, object sender)
        {
            if (!_permisosFiltrados.Any()) return;
            bool isChecked = (sender as CheckBox)?.IsChecked ?? false;

            foreach (var item in _permisosFiltrados)
            {
                accion(item, isChecked);
            }
            PermisosDataGrid.Items.Refresh();
        }

        private void ChkAllVer_Click(object sender, RoutedEventArgs e) => AplicarColumna((p, v) => p.PuedeVer = v, sender);
        private void ChkAllCrear_Click(object sender, RoutedEventArgs e) => AplicarColumna((p, v) => p.PuedeCrear = v, sender);
        private void ChkAllEditar_Click(object sender, RoutedEventArgs e) => AplicarColumna((p, v) => p.PuedeEditar = v, sender);
        private void ChkAllEliminar_Click(object sender, RoutedEventArgs e) => AplicarColumna((p, v) => p.PuedeEliminar = v, sender);
        private void ChkAllImprimir_Click(object sender, RoutedEventArgs e) => AplicarColumna((p, v) => p.PuedeImprimir = v, sender);

        // Acciones por Categoría (Bloque activo en pantalla)
        private void BtnActivarCategoria_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _permisosFiltrados)
            {
                p.PuedeVer = true;
                p.PuedeCrear = true;
                p.PuedeEditar = true;
                p.PuedeEliminar = true;
                p.PuedeImprimir = true;
            }
            PermisosDataGrid.Items.Refresh();
        }

        private void BtnDesactivarCategoria_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _permisosFiltrados)
            {
                p.PuedeVer = false;
                p.PuedeCrear = false;
                p.PuedeEditar = false;
                p.PuedeEliminar = false;
                p.PuedeImprimir = false;
            }
            PermisosDataGrid.Items.Refresh();
        }

        // Acciones Globales
        private void BtnTodoGlobal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _todosLosPermisos)
            {
                p.PuedeVer = true;
                p.PuedeCrear = true;
                p.PuedeEditar = true;
                p.PuedeEliminar = true;
                p.PuedeImprimir = true;
            }
            PermisosDataGrid.Items.Refresh();
        }

        private void BtnLimpiarTodoGlobal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _todosLosPermisos)
            {
                p.PuedeVer = false;
                p.PuedeCrear = false;
                p.PuedeEditar = false;
                p.PuedeEliminar = false;
                p.PuedeImprimir = false;
            }
            PermisosDataGrid.Items.Refresh();
        }

        private void BtnSoloLecturaGlobal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _todosLosPermisos)
            {
                p.PuedeVer = true;
                p.PuedeCrear = false;
                p.PuedeEditar = false;
                p.PuedeEliminar = false;
                p.PuedeImprimir = true;
            }
            PermisosDataGrid.Items.Refresh();
        }

        // ==============================================================
        // GUARDAR / CANCELAR
        // ==============================================================

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_todosLosPermisos.Any())
                {
                    await _usuarioService.GuardarPermisosMasivosAsync(_rolId, _todosLosPermisos);
                    MessageBox.Show("Políticas de seguridad actualizadas con éxito.", "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }
    }
}