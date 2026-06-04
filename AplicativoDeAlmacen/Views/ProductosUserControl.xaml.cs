using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using AplicativoDeAlmacen.Models;
using static AplicativoDeAlmacen.Data.DataConnection; // IMPORTANTE: Agregamos la referencia a tu clase de conexión

namespace AplicativoDeAlmacen.Views
{
    public partial class ProductosUserControl : UserControl
    {
        // ⚠️ CAMBIO: Ya no usamos el string quemado, usamos tu clase centralizada
        private readonly DatabaseConnection _database;
        private ObservableCollection<Producto> productos = new ObservableCollection<Producto>();
        private Producto? productoActual;

        public ProductosUserControl()
        {
            InitializeComponent();
            _database = new DatabaseConnection(); // Instanciamos tu conexión central
            CargarProductos();
        }

        private void CargarProductos()
        {
            productos.Clear();
            using (SqlConnection conn = _database.GetConnection()) // Usamos GetConnection()
            {
                conn.Open();
                string query = @"
                SELECT p.id, p.descripcion, p.abreviatura, p.unidad_medida_id, um.descripcion AS unidad_medida, 
                p.tipo_producto_id, p.precio_unitario, p.porcentaje, p.nivel_id, p.grado_id, p.curso_id,
                p.titulo_curso_id, p.afectacion_igv_id, ai.nombre AS afectacion_igv, p.estado_id, e.nombre AS estado
                FROM productos p
                LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                LEFT JOIN afectacion_igv ai ON p.afectacion_igv_id = ai.id
                LEFT JOIN estados e ON p.estado_id = e.id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            productos.Add(new Producto
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.GetString(reader.GetOrdinal("descripcion")),
                                Abreviatura = reader.IsDBNull(reader.GetOrdinal("abreviatura")) ? null : reader.GetString(reader.GetOrdinal("abreviatura")),
                                UnidadMedidaId = reader.IsDBNull(reader.GetOrdinal("unidad_medida_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("unidad_medida_id")),
                                UnidadMedida = reader.IsDBNull(reader.GetOrdinal("unidad_medida")) ? string.Empty : reader.GetString(reader.GetOrdinal("unidad_medida")),
                                TipoProductoId = reader.IsDBNull(reader.GetOrdinal("tipo_producto_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("tipo_producto_id")),
                                PrecioUnitario = reader.IsDBNull(reader.GetOrdinal("precio_unitario")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("precio_unitario")),
                                Porcentaje = reader.IsDBNull(reader.GetOrdinal("porcentaje")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("porcentaje")),
                                NivelId = reader.IsDBNull(reader.GetOrdinal("nivel_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("nivel_id")),
                                GradoId = reader.IsDBNull(reader.GetOrdinal("grado_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("grado_id")),
                                CursoId = reader.IsDBNull(reader.GetOrdinal("curso_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("curso_id")),
                                TituloCursoId = reader.IsDBNull(reader.GetOrdinal("titulo_curso_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("titulo_curso_id")),
                                AfectacionIgvId = reader.IsDBNull(reader.GetOrdinal("afectacion_igv_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("afectacion_igv_id")),
                                AfectacionIgv = reader.IsDBNull(reader.GetOrdinal("afectacion_igv")) ? string.Empty : reader.GetString(reader.GetOrdinal("afectacion_igv")),
                                EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("estado_id")),
                                Estado = reader.IsDBNull(reader.GetOrdinal("estado")) ? string.Empty : reader.GetString(reader.GetOrdinal("estado"))
                            });
                        }
                    }
                }
            }
            ProductosDataGrid.ItemsSource = productos;
        }

        private void BuscarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = BuscarTextBox.Text.ToLower();
            var resultados = productos.Where(p =>
                p.Id.ToString().Contains(busqueda) ||
                p.Descripcion.ToLower().Contains(busqueda) ||
               (p.Abreviatura?.ToLower() ?? "").Contains(busqueda) ||
                p.UnidadMedida.ToLower().Contains(busqueda) ||
                p.PrecioUnitario.ToString().Contains(busqueda) ||
                p.Porcentaje.ToString().Contains(busqueda) ||
                p.AfectacionIgv.ToLower().Contains(busqueda) ||
                p.Estado.ToLower().Contains(busqueda)
            );
            ProductosDataGrid.ItemsSource = new ObservableCollection<Producto>(resultados);
        }

        private void AgregarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            productoActual = null;
            ModalTitle.Text = "Nuevo Producto";
            LimpiarCamposProducto();
            CargarCombos();
            ProductoModal.Visibility = Visibility.Visible;
        }

        private void EditarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            productoActual = ProductosDataGrid.SelectedItem as Producto;
            if (productoActual != null)
            {
                ModalTitle.Text = "Editar Producto";
                CargarCombos();
                CargarDatosProducto(productoActual);
                ProductoModal.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para editar.");
            }
        }

        private void EliminarProductoButton_Click(object sender, RoutedEventArgs e)
        {
            var productoAEliminar = ProductosDataGrid.SelectedItem as Producto;
            if (productoAEliminar != null)
            {
                if (MessageBox.Show("¿Está seguro de que desea eliminar este producto?", "Confirmar eliminación", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    EliminarProducto(productoAEliminar.Id);
                    CargarProductos();
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un producto para eliminar.");
            }
        }

        private void LimpiarCamposProducto()
        {
            TxtDescripcion.Text = string.Empty;
            CmbUnidadMedida.SelectedIndex = -1;
            CmbTipoProducto.SelectedIndex = -1;
            CmbNivel.SelectedIndex = -1;
            CmbGrado.SelectedIndex = -1;
            CmbCurso.SelectedIndex = -1;
            ChkTitulo.IsChecked = false;
            CmbTitulo.SelectedIndex = -1;
            CmbTitulo.IsEnabled = false;
            TxtPrecioUnitario.Text = string.Empty;
            TxtPorcentaje.Text = string.Empty;
            TxtAbreviatura.Text = string.Empty;
            CmbAfectacionIgv.SelectedIndex = -1;
            CmbEstado.SelectedIndex = -1;
        }

        private void CargarCombos()
        {
            CargarUnidadesMedida();
            CargarTiposProducto();
            CargarNiveles();
            CargarAfectacionesIgv();
            CargarEstados();
        }

        private void CargarDatosProducto(Producto producto)
        {
            TxtDescripcion.Text = producto.Descripcion ?? "";
            TxtAbreviatura.Text = producto.Abreviatura ?? "";
            TxtPrecioUnitario.Text = producto.PrecioUnitario.ToString("N2");
            TxtPorcentaje.Text = producto.Porcentaje.ToString("N2");

            CargarCombos();

            SeleccionarItemEnCombo(CmbUnidadMedida, producto.UnidadMedidaId);
            SeleccionarItemEnCombo(CmbTipoProducto, producto.TipoProductoId);
            SeleccionarItemEnCombo(CmbNivel, producto.NivelId);
            SeleccionarItemEnCombo(CmbGrado, producto.GradoId);
            SeleccionarItemEnCombo(CmbCurso, producto.CursoId);
            SeleccionarItemEnCombo(CmbAfectacionIgv, producto.AfectacionIgvId);
            SeleccionarItemEnCombo(CmbEstado, producto.EstadoId);

            ChkTitulo.IsChecked = producto.TituloCursoId.HasValue;
            CmbTitulo.IsEnabled = ChkTitulo.IsChecked == true;
            if (ChkTitulo.IsChecked == true)
            {
                CargarTitulos(producto.CursoId ?? 0);
                SeleccionarItemEnCombo(CmbTitulo, producto.TituloCursoId);
            }
            else
            {
                CmbTitulo.Items.Clear();
            }
        }

        private void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (ValidarCampos())
            {
                if (productoActual == null)
                {
                    InsertarNuevoProducto();
                }
                else
                {
                    ActualizarProducto();
                }
                ProductoModal.Visibility = Visibility.Collapsed;
                CargarProductos();
            }
        }

        private bool ValidarCampos()
        {
            if (string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("La descripción es obligatoria.");
                return false;
            }
            return true;
        }

        private void BtnCancelarProducto_Click(object sender, RoutedEventArgs e)
        {
            ProductoModal.Visibility = Visibility.Collapsed;
        }

        private void CmbNivel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbNivel.SelectedItem != null)
            {
                int nivelId = (int)((ComboBoxItem)CmbNivel.SelectedItem).Tag;
                CargarGrados(nivelId);
                CargarCursos(nivelId);
            }
        }

        private void CmbCurso_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCurso.SelectedItem != null && ChkTitulo.IsChecked == true)
            {
                int cursoId = (int)((ComboBoxItem)CmbCurso.SelectedItem).Tag;
                CargarTitulos(cursoId);
            }
        }

        private void ChkTitulo_Checked(object sender, RoutedEventArgs e)
        {
            CmbTitulo.IsEnabled = true;
            if (CmbCurso.SelectedItem != null)
            {
                int cursoId = (int)((ComboBoxItem)CmbCurso.SelectedItem).Tag;
                CargarTitulos(cursoId);
            }
        }

        private void ChkTitulo_Unchecked(object sender, RoutedEventArgs e)
        {
            CmbTitulo.IsEnabled = false;
            CmbTitulo.SelectedIndex = -1;
        }

        private void InsertarNuevoProducto()
        {
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = @"INSERT INTO productos (descripcion, abreviatura, unidad_medida_id, tipo_producto_id, 
                 precio_unitario, porcentaje, nivel_id, grado_id, curso_id, titulo_curso_id, 
                 afectacion_igv_id, estado_id, created_at, updated_at) 
                 VALUES (@Descripcion, @Abreviatura, @UnidadMedidaId, @TipoProductoId, @PrecioUnitario, 
                 @Porcentaje, @NivelId, @GradoId, @CursoId, @TituloCursoId, @AfectacionIgvId, @EstadoId, 
                 GETDATE(), GETDATE())";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Descripcion", TxtDescripcion.Text);
                    cmd.Parameters.AddWithValue("@Abreviatura", string.IsNullOrEmpty(TxtAbreviatura.Text) ? DBNull.Value : (object)TxtAbreviatura.Text);
                    cmd.Parameters.AddWithValue("@UnidadMedidaId", CmbUnidadMedida.SelectedItem != null ? ((ComboBoxItem)CmbUnidadMedida.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@TipoProductoId", CmbTipoProducto.SelectedItem != null ? ((ComboBoxItem)CmbTipoProducto.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@PrecioUnitario", string.IsNullOrWhiteSpace(TxtPrecioUnitario.Text) ? 0.00m : decimal.Parse(TxtPrecioUnitario.Text));
                    cmd.Parameters.AddWithValue("@Porcentaje", string.IsNullOrWhiteSpace(TxtPorcentaje.Text) ? 0.00m : decimal.Parse(TxtPorcentaje.Text));
                    cmd.Parameters.AddWithValue("@NivelId", CmbNivel.SelectedItem != null ? ((ComboBoxItem)CmbNivel.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@GradoId", CmbGrado.SelectedItem != null ? ((ComboBoxItem)CmbGrado.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@CursoId", CmbCurso.SelectedItem != null ? ((ComboBoxItem)CmbCurso.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@TituloCursoId", ChkTitulo.IsChecked == true && CmbTitulo.SelectedItem != null ? ((ComboBoxItem)CmbTitulo.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@AfectacionIgvId", CmbAfectacionIgv.SelectedItem != null ? ((ComboBoxItem)CmbAfectacionIgv.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@EstadoId", CmbEstado.SelectedItem != null ? ((ComboBoxItem)CmbEstado.SelectedItem).Tag : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void ActualizarProducto()
        {
            if (productoActual == null)
            {
                MessageBox.Show("No hay producto seleccionado para actualizar.");
                return;
            }

            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = @"UPDATE productos SET 
                         descripcion = @Descripcion, 
                         abreviatura = @Abreviatura, 
                         unidad_medida_id = @UnidadMedidaId, 
                         tipo_producto_id = @TipoProductoId, 
                         precio_unitario = @PrecioUnitario, 
                         porcentaje = @Porcentaje, 
                         nivel_id = @NivelId, 
                         grado_id = @GradoId, 
                         curso_id = @CursoId, 
                         titulo_curso_id = @TituloCursoId, 
                         afectacion_igv_id = @AfectacionIgvId, 
                         estado_id = @EstadoId, 
                         updated_at = GETDATE() 
                         WHERE id = @Id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", productoActual.Id);
                    cmd.Parameters.AddWithValue("@Descripcion", TxtDescripcion.Text);
                    cmd.Parameters.AddWithValue("@Abreviatura", string.IsNullOrEmpty(TxtAbreviatura.Text) ? DBNull.Value : (object)TxtAbreviatura.Text);
                    cmd.Parameters.AddWithValue("@UnidadMedidaId", CmbUnidadMedida.SelectedItem != null ? ((ComboBoxItem)CmbUnidadMedida.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@TipoProductoId", CmbTipoProducto.SelectedItem != null ? ((ComboBoxItem)CmbTipoProducto.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@PrecioUnitario", string.IsNullOrWhiteSpace(TxtPrecioUnitario.Text) ? 0.00m : decimal.Parse(TxtPrecioUnitario.Text));
                    cmd.Parameters.AddWithValue("@Porcentaje", string.IsNullOrWhiteSpace(TxtPorcentaje.Text) ? 0.00m : decimal.Parse(TxtPorcentaje.Text));
                    cmd.Parameters.AddWithValue("@NivelId", CmbNivel.SelectedItem != null ? ((ComboBoxItem)CmbNivel.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@GradoId", CmbGrado.SelectedItem != null ? ((ComboBoxItem)CmbGrado.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@CursoId", CmbCurso.SelectedItem != null ? ((ComboBoxItem)CmbCurso.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@TituloCursoId", ChkTitulo.IsChecked == true && CmbTitulo.SelectedItem != null ? ((ComboBoxItem)CmbTitulo.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@AfectacionIgvId", CmbAfectacionIgv.SelectedItem != null ? ((ComboBoxItem)CmbAfectacionIgv.SelectedItem).Tag : DBNull.Value);
                    cmd.Parameters.AddWithValue("@EstadoId", CmbEstado.SelectedItem != null ? ((ComboBoxItem)CmbEstado.SelectedItem).Tag : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
            MessageBox.Show("Producto actualizado con éxito.");
        }

        private void EliminarProducto(int id)
        {
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "DELETE FROM productos WHERE id = @Id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void CargarUnidadesMedida()
        {
            CmbUnidadMedida.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, descripcion FROM unidad_medida WHERE estado_id = 1 ORDER BY descripcion";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbUnidadMedida.Items.Add(new ComboBoxItem
                            {
                                Content = reader["descripcion"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarTiposProducto()
        {
            CmbTipoProducto.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM tipo_producto ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbTipoProducto.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarNiveles()
        {
            CmbNivel.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM niveles ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbNivel.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarGrados(int nivelId)
        {
            CmbGrado.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM grados WHERE nivel_id = @NivelId ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@NivelId", nivelId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbGrado.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarCursos(int nivelId)
        {
            CmbCurso.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM curso WHERE nivel_id = @NivelId ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@NivelId", nivelId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbCurso.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarTitulos(int cursoId)
        {
            CmbTitulo.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM titulo_curso WHERE curso_id = @CursoId AND estado_id = 1 ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CursoId", cursoId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbTitulo.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarAfectacionesIgv()
        {
            CmbAfectacionIgv.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM afectacion_igv ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbAfectacionIgv.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void CargarEstados()
        {
            CmbEstado.Items.Clear();
            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                string query = "SELECT id, nombre FROM estados ORDER BY nombre";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CmbEstado.Items.Add(new ComboBoxItem
                            {
                                Content = reader["nombre"].ToString(),
                                Tag = reader.GetInt32(0)
                            });
                        }
                    }
                }
            }
        }

        private void SeleccionarItemEnCombo(ComboBox comboBox, int? id)
        {
            if (id.HasValue)
            {
                var item = comboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == id.Value);
                comboBox.SelectedItem = item;
            }
            else
            {
                comboBox.SelectedIndex = -1;
            }
        }

        private void TxtDecimal_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (decimal.TryParse(textBox.Text, out decimal value))
                {
                    textBox.Text = value.ToString("N2");
                }
                else
                {
                    textBox.Text = "0.00";
                }
            }
        }
    }
}