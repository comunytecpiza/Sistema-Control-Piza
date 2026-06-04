using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AplicativoDeAlmacen.Views
{
    public partial class RegistroCodigosWindow : Window
    {
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private ObservableCollection<CodigoRegistro> codigos = new ObservableCollection<CodigoRegistro>();
        private List<ProductoItem> productos = new List<ProductoItem>();
        private string? productoAbreviatura;
        private int? ultimoCodigo;
        private bool esLibroGuia = true;
        private DispatcherTimer searchTimer;

        public RegistroCodigosWindow()
        {
            InitializeComponent();
            CargarColecciones();
            CargarProductos();
            CargarCategorias();
            CodigosDataGrid.ItemsSource = codigos;

            searchTimer = new DispatcherTimer();
            searchTimer.Interval = TimeSpan.FromMilliseconds(300);
            searchTimer.Tick += SearchTimer_Tick;
        }

        private void CargarColecciones()
        {
            ColeccionComboBox.Items.Clear();
            ModalColeccionComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, ano FROM colecciones ORDER BY ano DESC";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int id = reader.GetInt32(0);
                            int ano = reader.GetInt32(1);
                            var itemForColeccionComboBox = new ComboBoxItem { Content = ano.ToString(), Tag = id };
                            var itemForModalColeccionComboBox = new ComboBoxItem { Content = ano.ToString(), Tag = id };
                            ColeccionComboBox.Items.Add(itemForColeccionComboBox);
                            ModalColeccionComboBox.Items.Add(itemForModalColeccionComboBox);
                        }
                    }
                }
            }

            // Seleccionar la última colección (primera en la lista, ya que está ordenada DESC)
            if (ColeccionComboBox.Items.Count > 0)
            {
                ColeccionComboBox.SelectedIndex = 0;
            }
            if (ModalColeccionComboBox.Items.Count > 0)
            {
                ModalColeccionComboBox.SelectedIndex = 0;
            }
        }

        private void CargarProductos()
        {
            productos.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = @"SELECT p.id, p.descripcion, p.abreviatura, um.descripcion AS unidad_medida 
                 FROM productos p
                 JOIN unidad_medida um ON p.unidad_medida_id = um.id
                 WHERE p.descripcion IS NOT NULL AND p.abreviatura IS NOT NULL";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            productos.Add(new ProductoItem
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader.GetString(1),
                                Abreviatura = reader.GetString(2),
                                UnidadMedida = reader.GetString(3)
                            });
                        }
                    }
                }
            }
            ProductoComboBox.ItemsSource = new ObservableCollection<ProductoItem>(productos);
            ProductoComboBox.SelectedIndex = -1;
            ProductoComboBox.Text = string.Empty;
        }

        private void CargarCategorias()
        {
            CategoriaComboBox.Items.Clear();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT id, nombre FROM categoria_producto";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CategoriaComboBox.Items.Add(new ComboBoxItem
                            {
                                Content = reader.GetString(1),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
            // Seleccionar por defecto "Libro Guía"
            CategoriaComboBox.SelectedIndex = 0;
        }

        private void ColeccionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColeccionComboBox.SelectedItem != null)
            {
                CargarCodigos();
            }
        }

        private void TipoLibro_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                esLibroGuia = radioButton.Content.ToString() == "Libros Guía";
                CargarCodigos();
            }
        }

        private void AgregarCodigos_Click(object sender, RoutedEventArgs e)
        {
            CerrarYLimpiarModal();
            ModalAgregarCodigo.Visibility = Visibility.Visible;

            // Seleccionar la categoría correcta basada en el RadioButton seleccionado
            CategoriaComboBox.SelectedIndex = esLibroGuia ? 0 : 1;
        }

        private void EliminarCodigos_Click(object sender, RoutedEventArgs e)
        {
            if (CodigosDataGrid.SelectedItem is CodigoRegistro codigoSeleccionado)
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Eliminar de codigos_creados
                            string deleteCodigosCreados = "DELETE FROM codigos_creados WHERE registro_codigo_id = @registroCodigoId";
                            using (SqlCommand command = new SqlCommand(deleteCodigosCreados, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@registroCodigoId", codigoSeleccionado.RegistroCodigoId);
                                command.ExecuteNonQuery();
                            }

                            // Eliminar de registro_codigos
                            string deleteRegistroCodigos = "DELETE FROM registro_codigos WHERE id = @registroCodigoId";
                            using (SqlCommand command = new SqlCommand(deleteRegistroCodigos, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@registroCodigoId", codigoSeleccionado.RegistroCodigoId);
                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            MessageBox.Show("Código eliminado exitosamente.");
                            CargarCodigos();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            MessageBox.Show($"Error al eliminar el código: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un código para eliminar.");
            }
        }

        private void ObtenerUltimoCodigo(int productoId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = @"SELECT MAX(CAST(SUBSTRING(codigo, LEN(@abreviatura) + 2, LEN(codigo)) AS INT))
                                 FROM codigos_creados cc
                                 JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                                 WHERE rc.producto_id = @productoId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@abreviatura", productoAbreviatura ?? "");
                    command.Parameters.AddWithValue("@productoId", productoId);
                    object result = command.ExecuteScalar();
                    ultimoCodigo = result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
        }

        private void ActualizarCodigosDesdeHasta()
        {
            if (int.TryParse(CantidadTextBox.Text, out int cantidad) && !string.IsNullOrEmpty(productoAbreviatura) && ultimoCodigo.HasValue)
            {
                int desde = ultimoCodigo.Value + 1;
                int hasta = desde + cantidad - 1;
                string prefijo = $"{productoAbreviatura}-"; // Ajusta esto según tu lógica de negocio
                DesdeTextBox.Text = $"{prefijo}{desde:D7}";
                HastaTextBox.Text = $"{prefijo}{hasta:D7}";
            }
        }

        private void GuardarCodigos_Click(object sender, RoutedEventArgs e)
        {
            if (ValidarFormulario())
            {
                GuardarCodigos();
                CargarCodigos();
                ModalAgregarCodigo.Visibility = Visibility.Collapsed;
            }
        }

        private bool ValidarFormulario()
        {
            if (ProductoComboBox.SelectedItem == null ||
                ModalColeccionComboBox.SelectedItem == null ||
                CategoriaComboBox.SelectedItem == null ||
                string.IsNullOrWhiteSpace(CantidadTextBox.Text) ||
                string.IsNullOrWhiteSpace(DesdeTextBox.Text) ||
                string.IsNullOrWhiteSpace(HastaTextBox.Text) ||
                !int.TryParse(CantidadTextBox.Text, out _))
            {
                MessageBox.Show("Por favor, complete todos los campos correctamente antes de guardar.");
                return false;
            }
            return true;
        }

        private void GuardarCodigos()
        {
            if (!ValidarFormulario()) return;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Insertar en registro_codigos
                        string queryRegistro = @"INSERT INTO registro_codigos 
    (coleccion_id, producto_id, cantidad, desde, hasta, categoria_producto_id) 
    OUTPUT INSERTED.ID
    VALUES (@coleccionId, @productoId, @cantidad, @desde, @hasta, @categoriaId)";

                        int registroId;
                        using (SqlCommand command = new SqlCommand(queryRegistro, connection, transaction))
                        {
                            var selectedColeccion = (ComboBoxItem)ModalColeccionComboBox.SelectedItem;
                            command.Parameters.AddWithValue("@coleccionId", (int)selectedColeccion.Tag);
                            command.Parameters.AddWithValue("@productoId", ((ProductoItem)ProductoComboBox.SelectedItem).Id);
                            command.Parameters.AddWithValue("@cantidad", Convert.ToInt32(CantidadTextBox.Text));
                            command.Parameters.AddWithValue("@desde", DesdeTextBox.Text);
                            command.Parameters.AddWithValue("@hasta", HastaTextBox.Text);
                            command.Parameters.AddWithValue("@categoriaId", ((ComboBoxItem)CategoriaComboBox.SelectedItem).Tag);

                            registroId = (int)command.ExecuteScalar();
                        }

                        // Insertar en codigos_creados
                        string queryCodigosCreados = "INSERT INTO codigos_creados (registro_codigo_id, codigo) VALUES (@registroId, @codigo)";
                        using (SqlCommand command = new SqlCommand(queryCodigosCreados, connection, transaction))
                        {
                            string desde = DesdeTextBox.Text;
                            string hasta = HastaTextBox.Text;

                            // Extraer la parte numérica del código
                            string desdeNumerico = desde.Substring(desde.LastIndexOf('-') + 1);
                            string hastaNumerico = hasta.Substring(hasta.LastIndexOf('-') + 1);

                            int desdeInt = int.Parse(desdeNumerico);
                            int hastaInt = int.Parse(hastaNumerico);

                            string prefijo = desde.Substring(0, desde.LastIndexOf('-') + 1);

                            for (int i = desdeInt; i <= hastaInt; i++)
                            {
                                string codigo = $"{prefijo}{i:D7}";
                                command.Parameters.Clear();
                                command.Parameters.AddWithValue("@registroId", registroId);
                                command.Parameters.AddWithValue("@codigo", codigo);
                                command.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        MessageBox.Show("Códigos guardados exitosamente.");
                        CargarCodigos();
                        CerrarYLimpiarModal();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show($"Error al guardar los códigos: {ex.Message}");
                    }
                }
            }
        }

        private void CerrarModal_Click(object sender, RoutedEventArgs e)
        {
            CerrarYLimpiarModal();
        }

        private void CerrarYLimpiarModal()
        {
            ModalAgregarCodigo.Visibility = Visibility.Collapsed;
            LimpiarFormulario();
            ProductoComboBox.ItemsSource = null;
            ProductoComboBox.Items.Clear();
            ProductoComboBox.Text = string.Empty;
            ProductoComboBox.IsDropDownOpen = false;

            // Recargar los productos después de un breve retraso
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CargarProductos();
                ProductoComboBox.ItemsSource = new ObservableCollection<ProductoItem>(productos);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ProductoComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            searchTimer.Stop();
            string searchText = ProductoComboBox.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                ProductoComboBox.ItemsSource = new ObservableCollection<ProductoItem>(productos);
            }
            else
            {
                var filteredItems = new ObservableCollection<ProductoItem>(
                    productos.Where(p => p.Descripcion.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                );
                ProductoComboBox.ItemsSource = filteredItems;
            }

            ProductoComboBox.IsDropDownOpen = true;
        }

        private void ProductoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProductoComboBox.SelectedItem is ProductoItem selectedProduct)
            {
                productoAbreviatura = selectedProduct.Abreviatura;
                ObtenerUltimoCodigo(selectedProduct.Id);
                ActualizarCodigosDesdeHasta();
            }
        }

        private void CantidadTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(CantidadTextBox.Text, out int cantidad))
            {
                ActualizarCodigosDesdeHasta();
            }
        }

        private void LimpiarFormulario()
        {
            ProductoComboBox.SelectedIndex = -1;
            ProductoComboBox.Text = string.Empty;
            ModalColeccionComboBox.SelectedIndex = -1;
            CategoriaComboBox.SelectedIndex = -1;
            CantidadTextBox.Clear();
            DesdeTextBox.Clear();
            HastaTextBox.Clear();
        }

        private void CargarCodigos()
        {
            codigos.Clear();
            if (ColeccionComboBox.SelectedItem == null) return;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = @"SELECT rc.id AS registro_codigo_id, p.descripcion AS producto, um.descripcion AS unidad_medida,
                                rc.cantidad, rc.desde, rc.hasta, cp.nombre AS categoria
                         FROM registro_codigos rc
                         JOIN productos p ON rc.producto_id = p.id
                         JOIN unidad_medida um ON p.unidad_medida_id = um.id
                         JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
                         WHERE rc.coleccion_id = @coleccionId AND rc.categoria_producto_id = @categoriaId
                         ORDER BY rc.desde";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@coleccionId", ((ComboBoxItem)ColeccionComboBox.SelectedItem).Tag);
                    command.Parameters.AddWithValue("@categoriaId", esLibroGuia ? 1 : 2); // Asumiendo que 1 es Libro Guía y 2 es Libro Venta

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            codigos.Add(new CodigoRegistro
                            {
                                RegistroCodigoId = reader.GetInt32(0),
                                Producto = reader.GetString(1),
                                UnidadMedida = reader.GetString(2),
                                Cantidad = reader.GetInt32(3),
                                Desde = reader.GetString(4),
                                Hasta = reader.GetString(5),
                                Categoria = reader.GetString(6)
                            });
                        }
                    }
                }
            }

            if (codigos.Count == 0)
            {
                MessageBox.Show($"No hay códigos registrados para esta colección y tipo de libro ({(esLibroGuia ? "Libro Guía" : "Libro Venta")}).");
            }
        }

        public class CodigoRegistro
        {
            public int RegistroCodigoId { get; set; }
            public string? Producto { get; set; }
            public string? UnidadMedida { get; set; }
            public string? Categoria { get; set; }
            public int Cantidad { get; set; }
            public string? Desde { get; set; }
            public string? Hasta { get; set; }
        }

        public class ProductoItem : INotifyPropertyChanged
        {
            public int Id { get; set; }
            private string _descripcion = string.Empty;
            public string Descripcion
            {
                get => _descripcion;
                set
                {
                    if (_descripcion != value)
                    {
                        _descripcion = value;
                        OnPropertyChanged(nameof(Descripcion));
                    }
                }
            }
            public string Abreviatura { get; set; } = string.Empty;
            public string UnidadMedida { get; set; } = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
