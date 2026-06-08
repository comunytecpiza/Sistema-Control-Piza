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
        private readonly PersonaComercialService _personaService;
        private readonly MovimientoService _movimientoService;

        private ObservableCollection<Producto> productosRecibidos;
        private ObservableCollection<CodigoDetalle> codigosDetalle;

        // Manejo de Estado orientado a Objetos nativos
        private PersonaComercial? _personaSeleccionada;

        public IngresoProductosWindow()
        {
            _personaService = new PersonaComercialService();
            _movimientoService = new MovimientoService();

            InitializeComponent();

            productosRecibidos = new ObservableCollection<Producto>();
            codigosDetalle = new ObservableCollection<CodigoDetalle>();

            ConfigurarVentana();
            InitializeCollections();
            AgregarEventosControles();
        }

        public sealed record CodigoDetalle
        {
            public required int NumeroFila { get; init; }
            public required string Codigo { get; init; }
            public required string ColeccionTipo { get; init; }
        }

        private void InitializeCollections()
        {
            dgProductos.ItemsSource = productosRecibidos;
            dgCodigos.ItemsSource = codigosDetalle;
        }

        private void ConfigurarVentana()
        {
            dtpFechaRecepcion.SelectedDate = DateTime.Today;
            CargarMotivos();
            grdFormulario.IsEnabled = false;
            LimpiarFormulario();
        }

        private void AgregarEventosControles()
        {
            btnAgregar.Click += btnAgregar_Click;
            btnEditar.Click += btnEditar_Click;
            btnImprimir.Click += btnImprimir_Click;
            btnAnular.Click += btnAnular_Click;
            btnGrabar.Click += btnGrabar_Click;
            btnCancelar.Click += btnCancelar_Click;
            btnDescargarExcel.Click += btnDescargarExcel_Click;

            btnAgregarProducto.Click += btnAgregarProducto_Click;
            btnModificar.Click += btnModificar_Click;
            btnEliminar.Click += btnEliminar_Click;
            btnImportar.Click += btnImportar_Click;

            txtRazonSocial.TextChanged += txtRazonSocial_TextChanged;
            lstSugerencias.SelectionChanged += lstSugerencias_SelectionChanged;
        }

        private void CargarMotivos()
        {
            try
            {
                var motivos = _movimientoService.ObtenerMotivosEntrada();
                cboMotivo.ItemsSource = motivos;
                cboMotivo.DisplayMemberPath = "Descripcion";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerarNuevoRegistro()
        {
            try
            {
                int siguienteId = _movimientoService.GenerarSiguienteIdMovimiento();
                txtRegistro.Text = siguienteId.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

         
        }

        private void GenerarCodigosProducto(int productoId, int cantidad)
        {
            try
            {
                int ultimoNumero = _movimientoService.ObtenerUltimoSecuencialCodigo(productoId);
                int numeroInicial = ultimoNumero + 1;

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
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnGrabar_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarFormulario()) return;

            try
            {
                // Instanciamos el objeto Movimiento mapeando sus objetos hijos
                var nuevoMovimiento = new Movimiento
                {
                    FechaMovimiento = DateOnly.FromDateTime(dtpFechaRecepcion.SelectedDate ?? DateTime.Today),
                    NumeroGuia = txtRegistro.Text,
                    Observacion = txtObservacion.Text,
                    MotivoProducto = (MotivoProducto)cboMotivo.SelectedItem,
                    PersonaComercial = _personaSeleccionada,
                    Usuario = new Usuario { Id = 1 }, // Reemplazar por sesión global real
                    Estado = new Estado { Id = 1 }
                };

                // Enviamos toda la estructura orientada a objetos al servicio transaccional
                _movimientoService.RegistrarIngresoProductos(nuevoMovimiento, productosRecibidos.ToList(), codigosDetalle.ToList());

                MessageBox.Show("Registro de inventario e ingresos procesados con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                LimpiarFormulario();
                grdFormulario.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al grabar el movimiento: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void txtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtRazonSocial.Text.Length < 2) return;

            var sugerencias = await _personaService.BuscarAsync(txtRazonSocial.Text);
            lstSugerencias.ItemsSource = sugerencias;
            popupSugerencias.IsOpen = sugerencias.Any();
        }

        private void lstSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSugerencias.SelectedItem is PersonaComercial selected)
            {
                _personaSeleccionada = selected;
                txtRazonSocial.Text = selected.RazonSocial;
                txtCodigoRazonSocial.Text = selected.Id.ToString();
                txtDireccion.Text = selected.Direccion;

                popupSugerencias.IsOpen = false;
                txtObservacion.Focus();
            }
        }

        private bool ValidarFormulario()
        {
            if (cboMotivo.SelectedItem is null)
            {
                MessageBox.Show("Debe seleccionar un motivo de inventario.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_personaSeleccionada is null)
            {
                MessageBox.Show("Debe asociar una persona comercial válida.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!productosRecibidos.Any())
            {
                MessageBox.Show("La lista de productos entrantes no puede estar vacía.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void LimpiarFormulario()
        {
            txtRegistro.Text = string.Empty;
            dtpFechaRecepcion.SelectedDate = DateTime.Today;
            cboMotivo.SelectedIndex = -1;
            txtRazonSocial.Text = string.Empty;
            txtCodigoRazonSocial.Text = string.Empty;
            txtDireccion.Text = string.Empty;
            txtObservacion.Text = string.Empty;
            _personaSeleccionada = null;
            productosRecibidos.Clear();
            codigosDetalle.Clear();
        }

        private void btnAgregar_Click(object sender, RoutedEventArgs e) { grdFormulario.IsEnabled = true; LimpiarFormulario(); GenerarNuevoRegistro(); }
        private void btnCancelar_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("¿Cancelar el registro?", "Confirmar", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { LimpiarFormulario(); grdFormulario.IsEnabled = false; } }
        private void btnEditar_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrEmpty(txtRegistro.Text)) grdFormulario.IsEnabled = true; }
        private void btnImprimir_Click(object sender, RoutedEventArgs e) { }
        private void btnAnular_Click(object sender, RoutedEventArgs e) { }
        private void btnAgregarProducto_Click(object sender, RoutedEventArgs e) { }
        private void btnModificar_Click(object sender, RoutedEventArgs e) { }
        private void btnEliminar_Click(object sender, RoutedEventArgs e) { }
        private void btnImportar_Click(object sender, RoutedEventArgs e) { }
        private async void btnDescargarExcel_Click(object sender, RoutedEventArgs e) => await ExportarCodigosExcel();
        private async Task ExportarCodigosExcel() { await Task.Delay(10); }
    }
}
