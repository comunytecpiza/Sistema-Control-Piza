#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Almacen;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views.Catalogo_y_tablas
{
    public partial class AlmacenesUserControl : UserControl
    {
        private readonly DatabaseConnection _database;
        private List<Almacen> _listadoAlmacenes = new List<Almacen>();
        private Almacen? _almacenSeleccionado = null;

        public AlmacenesUserControl()
        {
            InitializeComponent();
            _database = new DatabaseConnection();
            Loaded += async (s, e) => await CargarAlmacenesAsync();
        }

        private static void AgregarParametro(IDbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private async Task CargarAlmacenesAsync()
        {
            try
            {
                _listadoAlmacenes.Clear();
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                string query = $"SELECT id, nombre, codigo, direccion, estado_id FROM almacenes {nolock} ORDER BY id ASC";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    _listadoAlmacenes.Add(new Almacen
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"].ToString() ?? "",
                        Codigo = reader["codigo"] == DBNull.Value ? "" : reader["codigo"].ToString()!,
                        Direccion = reader["direccion"] == DBNull.Value ? "" : reader["direccion"].ToString()!,
                        EstadoId = reader["estado_id"] == DBNull.Value ? 1 : Convert.ToInt32(reader["estado_id"])
                    });
                }

                AplicarFiltros();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar almacenes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AplicarFiltros()
        {
            // 🛡️ 1. Evitar ejecución antes de que el control termine de cargar
            if (!IsLoaded || DgAlmacenes == null || _listadoAlmacenes == null)
                return;

            string busqueda = TxtBuscar?.Text?.Trim().ToLower() ?? string.Empty;
            int filtroEstado = -1;

            if (CmbFiltroEstado?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int tagVal))
            {
                filtroEstado = tagVal;
            }

            // 🛡️ 2. Manejo seguro de nulos con operadores '?.' y '??'
            var filtrados = _listadoAlmacenes.Where(a =>
                (filtroEstado == -1 || a.EstadoId == filtroEstado) &&
                (string.IsNullOrWhiteSpace(busqueda) ||
                 a.Id.ToString().Contains(busqueda) ||
                 (!string.IsNullOrEmpty(a.Nombre) && a.Nombre.ToLower().Contains(busqueda)) ||
                 (!string.IsNullOrEmpty(a.Codigo) && a.Codigo.ToLower().Contains(busqueda)) ||
                 (!string.IsNullOrEmpty(a.Direccion) && a.Direccion.ToLower().Contains(busqueda)))
            ).ToList();

            DgAlmacenes.ItemsSource = filtrados;
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltros();

        private void CmbFiltroEstado_SelectionChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltros();

        private void BtnNuevoAlmacen_Click(object sender, RoutedEventArgs e)
        {
            _almacenSeleccionado = null;
            TxtModalTitulo.Text = "Nuevo Almacén";
            TxtNombreModal.Clear();
            TxtCodigoModal.Clear();
            TxtDireccionModal.Clear();
            ChkActivoModal.IsChecked = true;

            ModalAlmacen.Visibility = Visibility.Visible;
            TxtNombreModal.Focus();
        }

        private void BtnEditarAlmacen_Click(object sender, RoutedEventArgs e)
        {
            if (DgAlmacenes.SelectedItem is not Almacen a)
            {
                MessageBox.Show("Seleccione un almacén de la lista para editar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _almacenSeleccionado = a;
            TxtModalTitulo.Text = "Editar Almacén";
            TxtNombreModal.Text = a.Nombre;
            TxtCodigoModal.Text = a.Codigo;
            TxtDireccionModal.Text = a.Direccion;
            ChkActivoModal.IsChecked = (a.EstadoId == 1);

            ModalAlmacen.Visibility = Visibility.Visible;
            TxtNombreModal.Focus();
        }

        private async void BtnGuardarModal_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombreModal.Text.Trim();
            string codigo = TxtCodigoModal.Text.Trim();
            string direccion = TxtDireccionModal.Text.Trim();
            int estado = (ChkActivoModal.IsChecked == true) ? 1 : 0;

            if (string.IsNullOrWhiteSpace(nombre))
            {
                MessageBox.Show("El nombre del almacén es obligatorio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNombreModal.Focus();
                return;
            }

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                if (_almacenSeleccionado == null)
                {
                    string sqlInsert = @"INSERT INTO almacenes (nombre, codigo, direccion, estado_id, created_at) 
                                         VALUES (@nombre, @codigo, @direccion, @estado, " + (QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()") + ")";
                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(sqlInsert);
                    AgregarParametro(cmd, "@nombre", nombre);
                    AgregarParametro(cmd, "@codigo", string.IsNullOrWhiteSpace(codigo) ? (object)DBNull.Value : codigo);
                    AgregarParametro(cmd, "@direccion", string.IsNullOrWhiteSpace(direccion) ? (object)DBNull.Value : direccion);
                    AgregarParametro(cmd, "@estado", estado);

                    await cmd.ExecuteNonQueryAsync();
                    MessageBox.Show("Almacén registrado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string sqlUpdate = @"UPDATE almacenes 
                                         SET nombre = @nombre, codigo = @codigo, direccion = @direccion, estado_id = @estado 
                                         WHERE id = @id";
                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta(sqlUpdate);
                    AgregarParametro(cmd, "@id", _almacenSeleccionado.Id);
                    AgregarParametro(cmd, "@nombre", nombre);
                    AgregarParametro(cmd, "@codigo", string.IsNullOrWhiteSpace(codigo) ? (object)DBNull.Value : codigo);
                    AgregarParametro(cmd, "@direccion", string.IsNullOrWhiteSpace(direccion) ? (object)DBNull.Value : direccion);
                    AgregarParametro(cmd, "@estado", estado);

                    await cmd.ExecuteNonQueryAsync();
                    MessageBox.Show("Almacén actualizado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                ModalAlmacen.Visibility = Visibility.Collapsed;
                await CargarAlmacenesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar el almacén: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnToggleEstado_Click(object sender, RoutedEventArgs e)
        {
            if (DgAlmacenes.SelectedItem is not Almacen a)
            {
                MessageBox.Show("Seleccione un almacén para cambiar su estado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int nuevoEstado = a.EstadoId == 1 ? 0 : 1;
            string accionTexto = nuevoEstado == 1 ? "ACTIVAR" : "DESACTIVAR";

            if (MessageBox.Show($"¿Está seguro de {accionTexto} el almacén \"{a.Nombre}\"?", "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta("UPDATE almacenes SET estado_id = @estado WHERE id = @id");
                AgregarParametro(cmd, "@id", a.Id);
                AgregarParametro(cmd, "@estado", nuevoEstado);

                await cmd.ExecuteNonQueryAsync();
                await CargarAlmacenesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al actualizar estado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelarModal_Click(object sender, RoutedEventArgs e)
        {
            ModalAlmacen.Visibility = Visibility.Collapsed;
        }
    }
}