using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private int _productoSeleccionadoId;
        private bool _estaSeleccionando;

        // LA MEMORIA DEL SISTEMA: Aquí guardamos todos los códigos en secreto
        private List<ConsultaCodigoItem> _todosLosCodigos;

        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;
        }

        private void Control_Loaded(object sender, RoutedEventArgs e)
        {
            var txt = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (txt != null)
            {
                txt.TextChanged += TxtProducto_TextChanged;
            }
        }

        private void ChkFiltros_Click(object sender, RoutedEventArgs e)
        {
            if (TxtRazonSocial != null)
            {
                TxtRazonSocial.IsEnabled = ChkRazonSocial.IsChecked == true;
                if (ChkRazonSocial.IsChecked == false) TxtRazonSocial.Text = string.Empty;
            }
            if (TxtUbicacion != null)
            {
                TxtUbicacion.IsEnabled = ChkUbicacion.IsChecked == true;
                if (ChkUbicacion.IsChecked == false) TxtUbicacion.Text = string.Empty;
            }
        }

        private async void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;

            try
            {
                string texto = ((TextBox)sender).Text;
                if (string.IsNullOrWhiteSpace(texto))
                {
                    CboProductos.ItemsSource = null;
                    CboProductos.IsDropDownOpen = false;
                    _productoSeleccionadoId = 0;
                    return;
                }

                var productos = await _productoService.BuscarProductos(texto);
                string textoActual = texto;
                CboProductos.ItemsSource = productos;
                CboProductos.DisplayMemberPath = "Descripcion";
                CboProductos.Text = textoActual;
                CboProductos.IsDropDownOpen = productos != null && productos.Count > 0;
            }
            catch { /* Falla silenciosa permitida en tipeos rápidos */ }
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;
                _productoSeleccionadoId = producto.Id;
                CboProductos.Text = producto.Descripcion;
                CboProductos.IsDropDownOpen = false;
                _estaSeleccionando = false;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_productoSeleccionadoId == 0)
            {
                MessageBox.Show("Por favor, seleccione un producto maestro para generar la auditoría.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ((Button)sender).IsEnabled = false;

                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                // 1. Ejecutamos la consulta en base de datos
                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(_productoSeleccionadoId, desde, hasta);
                var movimientosFiltrados = reporte.Movimientos.AsEnumerable();

                // 2. Aplicamos filtros de texto (Memoria)
                if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
                {
                    string filtroRazon = TxtRazonSocial.Text.ToLower().Trim();
                    movimientosFiltrados = movimientosFiltrados.Where(m => m.RazonSocialUbicacion != null && m.RazonSocialUbicacion.ToLower().Contains(filtroRazon));
                }

                if (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text))
                {
                    string filtroUbi = TxtUbicacion.Text.ToLower().Trim();
                    movimientosFiltrados = movimientosFiltrados.Where(m => m.RazonSocialUbicacion != null && m.RazonSocialUbicacion.ToLower().Contains(filtroUbi));
                }

                // 3. Aplicamos Filtros de RadioButtons (Guías vs Ventas/Facturas)
                if (RbGuia.IsChecked == true)
                {
                    // Asumimos que Guía es cuando el comprobante (registro) está vacío o es solo guiones
                    movimientosFiltrados = movimientosFiltrados.Where(m => m.NumeroRegistro == "-" || string.IsNullOrWhiteSpace(m.NumeroRegistro.Replace("-", "")));
                }
                else if (RbVenta.IsChecked == true)
                {
                    // Asumimos que Venta es cuando SÍ hay un número de comprobante (factura/boleta)
                    movimientosFiltrados = movimientosFiltrados.Where(m => m.NumeroRegistro != "-" && !string.IsNullOrWhiteSpace(m.NumeroRegistro.Replace("-", "")));
                }

                var listaFinalMovimientos = movimientosFiltrados.ToList();

                // =======================================================
                // MAGIA MAESTRO-DETALLE
                // =======================================================
                // Guardamos los códigos internamente, pero NO los mostramos aún
                _todosLosCodigos = reporte.Codigos;

                // Llenamos solo la tabla de la izquierda
                MovimientosDataGrid.ItemsSource = listaFinalMovimientos;
                CodigosDataGrid.ItemsSource = null; // La tabla de códigos arranca vacía

                // Actualizamos las Tarjetas de Dashboards (Cards)
                decimal sumaIngresos = listaFinalMovimientos.Sum(m => m.Ingreso);
                decimal sumaSalidas = listaFinalMovimientos.Sum(m => m.Salida);

                TxtTotalIngreso.Text = sumaIngresos.ToString("N2");
                TxtTotalSalida.Text = sumaSalidas.ToString("N2");
                TxtTotalVendidos.Text = sumaSalidas.ToString("N2");

                TxtTotalCodigos.Text = "Seleccione un movimiento de la lista izquierda para auditar sus códigos";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error de auditoría: " + ex.Message, "Error de Datos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ((Button)sender).IsEnabled = true;
            }
        }

        // ==============================================================
        // EVENTO DEL CLIC: Llena la tabla derecha al tocar un movimiento
        // ==============================================================
        private void MovimientosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_todosLosCodigos == null || !_todosLosCodigos.Any()) return;

            // Extraemos qué fila tocó el contador
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem movimientoSeleccionado)
            {
                // Filtramos la memoria: buscamos qué códigos están "amarrados" a este documento/registro
                var codigosDelMovimiento = _todosLosCodigos
                    .Where(c => c.NumeroRegistro == movimientoSeleccionado.NumeroRegistro)
                    .ToList();

                // Llenamos la tabla derecha solo con esa pequeña fracción
                CodigosDataGrid.ItemsSource = codigosDelMovimiento;

                // Refrescamos la píldora informativa
                TxtTotalCodigos.Text = $"Se auditaron {codigosDelMovimiento.Count} Códigos Físicos en esta operación";
            }
        }
    }
}