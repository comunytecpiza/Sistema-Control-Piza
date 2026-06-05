using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Data.SqlClient;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Views
{

    public partial class IngresoProductosWindow : Window
    {

        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private readonly PersonaComercialService _personaService;

        // Inicializamos las colecciones para evitar warnings CS8618
        private ObservableCollection<Producto> productosRecibidos;
        private ObservableCollection<CodigoDetalle> codigosDetalle;
        private int? personaComercialSeleccionadaId;

        // PersonaComercialService _personaService;
        public IngresoProductosWindow()
        {
            _personaService = new PersonaComercialService();
            InitializeComponent();
            productosRecibidos = new();
            codigosDetalle = new();
            ConfigurarVentana();
            InitializeCollections();
            AgregarEventosControles();
        }

        #region Clases de Modelo
        /*
         public sealed record ProductoRecibido
         {
             public required int Id { get; init; }
             public required string Codigo { get; init; }
             public required string Descripcion { get; init; }
             public required string UnidadMedida { get; init; }
             public required int Cantidad { get; init; }
             public required decimal CostoUnitario { get; init; }
         }*/

        public sealed record CodigoDetalle
        {
            public required int NumeroFila { get; init;}
            public required string Codigo { get; init;}
            public required string ColeccionTipo { get; init;}
        }
        #endregion

        #region Inicialización y Configuración

        private void InitializeCollections()
        {
            /*Tablas de datagrid*/
            dgProductos.ItemsSource = productosRecibidos;
            dgCodigos.ItemsSource = codigosDetalle;
        }

        private void ConfigurarVentana()
        {
            //la fecha actual
            dtpFechaRecepcion.SelectedDate = DateTime.Today;
            //cargar los motivos movimientos
            CargarMotivos();
            grdFormulario.IsEnabled = false;
            //limpiar campos
            LimpiarFormulario();
        }

        private void AgregarEventosControles()
        {
            // Botones principales
            btnAgregar.Click += btnAgregar_Click;
            btnEditar.Click += btnEditar_Click;
            btnImprimir.Click += btnImprimir_Click;
            btnAnular.Click += btnAnular_Click;
            btnGrabar.Click += btnGrabar_Click;
            btnCancelar.Click += btnCancelar_Click;
            btnDescargarExcel.Click += btnDescargarExcel_Click;

            // Botones de productos
            btnAgregarProducto.Click += btnAgregarProducto_Click;
            btnModificar.Click += btnModificar_Click;
            btnEliminar.Click += btnEliminar_Click;
            btnImportar.Click += btnImportar_Click;

            // Otros controles
            cboMotivo.SelectionChanged += cboMotivo_SelectionChanged;
            txtRazonSocial.TextChanged += txtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += lstSugerencias_SelectionChanged;
         /*   lstSugerencias.PreviewMouseLeftButtonUp += lstSugerencias_PreviewMouseLeftButtonUp;*/

        }

        #endregion

        #region Eventos de Botones Principales

        private void btnAgregar_Click(object sender, RoutedEventArgs e)
        {
            grdFormulario.IsEnabled = true;
            LimpiarFormulario();
            GenerarNuevoRegistro();
        }

        private void btnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtRegistro.Text))
            {
                MessageBox.Show("Seleccione un registro para editar", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            grdFormulario.IsEnabled = true;
        }

        private void btnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtRegistro.Text))
            {
                MessageBox.Show("Seleccione un registro para imprimir", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Implementar lógica de impresión
        }

        private void btnAnular_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtRegistro.Text))
            {
                MessageBox.Show("Seleccione un registro para anular", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("¿Está seguro de anular este registro?", "Confirmar anulación",
                                       MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Implementar lógica de anulación
            }
        }

        private void btnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarFormulario()) return;

            try
            {
                GuardarMovimiento();
                MessageBox.Show("Registro guardado exitosamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                LimpiarFormulario();
                grdFormulario.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("¿Está seguro de cancelar el registro?", "Confirmar cancelación",
                                       MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LimpiarFormulario();
                grdFormulario.IsEnabled = false;
            }
        }

        private async void btnDescargarExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExportarCodigosExcel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Eventos de Productos

        private void AgregarProductoSeleccionado(Producto producto)
        {
            if (producto is null) return;

            productosRecibidos.Add(new Producto
            {
                Id = producto.Id,

                Descripcion = producto.Descripcion,
                UnidadMedida = producto.UnidadMedida,
                Cantidad = producto.Cantidad,
                PrecioUnitario = producto.PrecioUnitario
            });

         /*   GenerarCodigosProducto(producto.Id, producto.Cantidad);*/
        }

        private void btnAgregarProducto_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Función en desarrollo", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnModificar_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Función en desarrollo", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnEliminar_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Función en desarrollo", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnImportar_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Función en desarrollo", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Eventos de Controles


        private void cboMotivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Implementar lógica adicional si es necesario
        }

        private async void txtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtRazonSocial.Text.Length < 2)
                return;

            var sugerencias = await _personaService.BuscarAsync(txtRazonSocial.Text);

            lstSugerencias.ItemsSource = sugerencias;
            popupSugerencias.IsOpen = sugerencias.Any();


            if (sugerencias.Count == 1 &&
                    string.Equals(sugerencias[0].RazonSocial, txtRazonSocial.Text, StringComparison.OrdinalIgnoreCase))
            {
              /*  SeleccionarRazonSocial(sugerencias[0]);*/
            }


        }

        private void lstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
          /*  if (lstSugerencias.SelectedItem is PersonaComercial selected)
            {
                SeleccionarRazonSocial(selected);
            }*/
        }



       /* private void SeleccionarRazonSocial(PersonaComercial selected)
        {
            try
            {
                txtRazonSocial.Text = selected.RazonSocial;
                txtCodigoRazonSocial.Text = selected.CodigoMostrar; // Mostramos el ID
                txtDireccion.Text = selected.Direccion;
                personaComercialSeleccionadaId = selected.Id;
                popupSugerencias.IsOpen = false;
                lstSugerencias.ItemsSource = null;
                // Mover el cursor al final del texto
                txtRazonSocial.CaretIndex = txtRazonSocial.Text.Length;
                // Enfocar el siguiente control
                txtObservacion.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al seleccionar razón social: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }*/

      /*  private void lstSugerencias_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (lstSugerencias.SelectedItem is PersonaComercial selected)
            {
                SeleccionarRazonSocial(selected);
            }
        }
        #endregion
      */




        #region Métodos Auxiliares

        private void LimpiarFormulario()
        {
            txtRegistro.Text = string.Empty;
            dtpFechaRecepcion.SelectedDate = DateTime.Today;
            cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Text = string.Empty;
            txtCodigoRazonSocial.Text = string.Empty;
            txtDireccion.Text = string.Empty;
            txtObservacion.Text = string.Empty;
            personaComercialSeleccionadaId = null;
            productosRecibidos.Clear();
            codigosDetalle.Clear();
        }

        private void CargarMotivos()
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT id, descripcion 
                    FROM motivo_productos 
                    WHERE tipo_movimiento = 'entrada'
                    ORDER BY id", conn);

                using var reader = cmd.ExecuteReader();
                var motivos = new List<dynamic>();

                while (reader.Read())
                {
                    motivos.Add(new
                    {
                        Id = reader.GetInt32(0),
                        Descripcion = reader.GetString(1)
                    });
                }

                cboMotivo.ItemsSource = motivos;
                cboMotivo.DisplayMemberPath = "Descripcion";
                cboMotivo.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar motivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void GenerarNuevoRegistro()
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand("SELECT ISNULL(MAX(id), 0) + 1 FROM movimientos_productos", conn);
                var nuevoId = cmd.ExecuteScalar();
                txtRegistro.Text = nuevoId?.ToString() ?? "1";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar nuevo registro: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidarFormulario()
        {
            if (cboMotivo.SelectedItem is null)
            {
                MessageBox.Show("Debe seleccionar un motivo", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!personaComercialSeleccionadaId.HasValue)
            {
                MessageBox.Show("Debe seleccionar una razón social válida", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!productosRecibidos.Any())
            {
                MessageBox.Show("Debe agregar al menos un producto", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!dtpFechaRecepcion.SelectedDate.HasValue)
            {
                MessageBox.Show("Debe seleccionar una fecha de recepción", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void AbrirVentanaSeleccionProducto()
        {

        }

        private void GuardarMovimiento()
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                foreach (var producto in productosRecibidos)
                {
                    using var cmd = new SqlCommand(@"
                        INSERT INTO movimientos_productos 
                        (producto_id, cantidad, fecha_movimiento, tipo_movimiento,
                         motivo_producto_id, personas_comerciales_id, guia,
                         observacion, usuario_id)
                        OUTPUT INSERTED.ID
                        VALUES 
                        (@productoId, @cantidad, @fechaMovimiento, 'entrada',
                         @motivoId, @personaComercialId, @guia,
                         @observacion, @usuarioId)", conn, transaction);

                    cmd.Parameters.AddWithValue("@productoId", producto.Id);
                    cmd.Parameters.AddWithValue("@cantidad", producto.Cantidad);
                    cmd.Parameters.AddWithValue("@fechaMovimiento", dtpFechaRecepcion.SelectedDate!.Value);
                    cmd.Parameters.AddWithValue("@motivoId", cboMotivo.SelectedValue);
                    cmd.Parameters.AddWithValue("@personaComercialId", personaComercialSeleccionadaId!.Value);
                    cmd.Parameters.AddWithValue("@guia", txtRegistro.Text);
                    cmd.Parameters.AddWithValue("@observacion", txtObservacion.Text);
                    cmd.Parameters.AddWithValue("@usuarioId", 1); // Cambiar por el usuario actual del sistema

                    int movimientoId = Convert.ToInt32(cmd.ExecuteScalar());

                    // Actualizar el stock del producto
                    using var updateCmd = new SqlCommand(@"
                        UPDATE productos 
                        SET cantidad = cantidad + @cantidad 
                        WHERE id = @productoId", conn, transaction);

                    updateCmd.Parameters.AddWithValue("@cantidad", producto.Cantidad);
                    updateCmd.Parameters.AddWithValue("@productoId", producto.Id);
                    updateCmd.ExecuteNonQuery();


                    // Guardar los códigos generados
                    GuardarCodigos(producto.Id, movimientoId, transaction, conn);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        /*
           private void btnImportar_Click(object sender, RoutedEventArgs e)
           {
               // 1. Instanciamos la ventana
               ImportarCodigos ventanaImportar = new ImportarCodigos();

               // 2. La mostramos como "Dialog" (esto bloquea la principal hasta que se cierre la otra)
               if (ventanaImportar.ShowDialog() == true)
               {
                   // 3. Si el usuario aceptó, traemos los códigos de la otra ventana
                   // Asumiendo que en 'ImportarCodigos' creaste una lista pública llamada 'CodigosImportados'
                   foreach (var item in ventanaImportar.CodigosImportados)
                   {
                       codigosDetalle.Add(new CodigoDetalle
                       {
                           NumeroFila = codigosDetalle.Count + 1,
                           Codigo = item,
                           ColeccionTipo = DateTime.Now.Year.ToString()
                       });
                   }

           }
           }*/


        #endregion

        #region Gestión de Códigos

        private void GenerarCodigosProducto(int productoId, int cantidad)
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT ISNULL(MAX(numero_secuencia), 0)
                    FROM codigos_qr_productos 
                    WHERE producto_id = @productoId", conn);
                cmd.Parameters.AddWithValue("@productoId", productoId);

                var ultimoNumero = cmd.ExecuteScalar();
                int numeroInicial = (ultimoNumero == DBNull.Value ? 0 : Convert.ToInt32(ultimoNumero)) + 1;

                for (int i = 0; i < cantidad; i++)
                {
                    var codigo = new CodigoDetalle
                    {
                        NumeroFila = numeroInicial + i,
                        Codigo = $"PROD-{productoId}-{numeroInicial + i:D5}",
                        ColeccionTipo = DateTime.Now.Year.ToString()
                    };
                    codigosDetalle.Add(codigo);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar códigos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GuardarCodigos(int productoId, int movimientoId, SqlTransaction transaction, SqlConnection conn)
        {
            foreach (var codigo in codigosDetalle.Where(c => c.Codigo.Contains($"PROD-{productoId}")))
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO codigos_qr_productos 
                    (producto_id, numero_secuencia, codigo_unico, estado_id, created_at)
                    VALUES 
                    (@productoId, @numeroSecuencia, @codigoUnico, 1, GETDATE())", conn, transaction);

                cmd.Parameters.AddWithValue("@productoId", productoId);
                cmd.Parameters.AddWithValue("@numeroSecuencia", codigo.NumeroFila);
                cmd.Parameters.AddWithValue("@codigoUnico", codigo.Codigo);
                cmd.ExecuteNonQuery();
            }
        }

        private void btnAgregar_Click_1(object sender, RoutedEventArgs e)
        {

        }

        private async Task ExportarCodigosExcel()
        {
            if (!codigosDetalle.Any())
            {
                MessageBox.Show("No hay códigos para exportar", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Aquí implementarás la lógica de exportación a Excel
                // Por ahora solo mostramos un mensaje
               
                await Task.Delay(100);
                MessageBox.Show("Función de exportación en desarrollo", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    
        #endregion
    }
}
#endregion