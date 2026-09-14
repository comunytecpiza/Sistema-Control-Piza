#nullable enable

using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Contabilidad
{
    public partial class ReporteVentasClienteUserControl : UserControl
    {
        private readonly DataConnection.DatabaseConnection _database;
        private readonly PersonaComercialService _personaService;
        private readonly ProductoService _productoService;
        private int? _clienteIdSeleccionado = null;

        private List<ProductoVendidoClienteDTO> _listaProductos = new List<ProductoVendidoClienteDTO>();
        private List<DetalleCodigoVentaClienteDTO> _listaCodigosGlobal = new List<DetalleCodigoVentaClienteDTO>();

        public ReporteVentasClienteUserControl()
        {
            InitializeComponent();
            _database = new DataConnection.DatabaseConnection();
            _personaService = new PersonaComercialService();
            _productoService = new ProductoService();

            DtpDesde.SelectedDate = DateTime.Today;
            DtpHasta.SelectedDate = DateTime.Today;
        }

        private void ChkFiltrarFechas_Changed(object sender, RoutedEventArgs e)
        {
            bool activo = ChkFiltrarFechas.IsChecked == true;
            DtpDesde.IsEnabled = activo;
            DtpHasta.IsEnabled = activo;
        }

        private async void TxtClienteBuscador_TextChanged(object sender, TextChangedEventArgs e)
        {
            string texto = TxtClienteBuscador.Text.Trim();
            if (texto.Length < 2)
            {
                PopCliente.IsOpen = false;
                return;
            }

            try
            {
                var personas = await _personaService.BuscarPorRazonSocialAsync(texto);
                var lista = personas?.Take(12).ToList();
                LstClientes.ItemsSource = lista;
                PopCliente.IsOpen = lista != null && lista.Any();
            }
            catch
            {
                PopCliente.IsOpen = false;
            }
        }

        private void LstClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstClientes.SelectedItem is PersonaComercial p)
            {
                PopCliente.IsOpen = false;
                _clienteIdSeleccionado = p.Id;
                TxtClienteBuscador.Text = p.RazonSocial ?? $"{p.Nombres} {p.ApellidoPaterno}";

                string loc = p.Localidad?.Nombre ?? "";
                string zona = p.ZonaPromotoria?.Descripcion ?? "";
                TxtLocalidad.Text = $"{loc} / {zona}".Trim(' ', '/');
            }
        }

        private void BtnLimpiarCliente_Click(object sender, RoutedEventArgs e)
        {
            _clienteIdSeleccionado = null;
            TxtClienteBuscador.Text = string.Empty;
            TxtLocalidad.Text = string.Empty;
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnEjecutar.IsEnabled = false;

                DateTime? fDesde = ChkFiltrarFechas.IsChecked == true ? DtpDesde.SelectedDate : null;
                DateTime? fHasta = ChkFiltrarFechas.IsChecked == true ? DtpHasta.SelectedDate : null;

                await ConsultarVentasClienteAsync(_clienteIdSeleccionado, fDesde, fHasta);

                DgProductos.ItemsSource = null;
                DgProductos.ItemsSource = _listaProductos;

                DgCodigos.ItemsSource = null;
                DgCodigos.ItemsSource = _listaCodigosGlobal;

                LblTotalItems.Text = _listaProductos.Sum(p => p.Cantidad).ToString("N0");
                LblTotalCodigos.Text = _listaCodigosGlobal.Count.ToString("N0");
                LblTotalImporte.Text = _listaProductos.Sum(p => p.ImporteTotal).ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar reporte: {ex.Message}", "Reporte", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEjecutar.IsEnabled = true;
            }
        }

        private async Task ConsultarVentasClienteAsync(int? compradorId, DateTime? desde, DateTime? hasta)
        {
            _listaProductos.Clear();
            _listaCodigosGlobal.Clear();

            int idAlmacenActual = SesionSistema.AlmacenActual?.Id ?? 1;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            // 1. Obtener primero los códigos reales facturados
            string qCodigos = $@"
        SELECT 
            cc.codigo,
            fc.fecha_emision,
            fc.tipo_documento,
            fc.serie_documento,
            fc.numero_documento,
            COALESCE(fd.precio_unitario, 0) AS precio_unitario,
            fd.producto_id
        FROM facturacion_detalle_codigos fdc {nolock}
        INNER JOIN facturacion_detalle fd {nolock} ON fdc.facturacion_detalle_id = fd.id
        INNER JOIN facturacion_cabecera fc {nolock} ON fd.facturacion_cabecera_id = fc.id
        INNER JOIN codigos_creados cc {nolock} ON fdc.codigo_creado_id = cc.id
        WHERE fc.estado_registro = 1
          AND (fc.almacen_id = @almId OR fc.almacen_id IS NULL)";

            if (compradorId.HasValue) qCodigos += " AND (fc.comprador_id = @compClie OR fc.institucion_id = @compClie)";
            if (desde.HasValue) qCodigos += " AND fc.fecha_emision >= @desde";
            if (hasta.HasValue) qCodigos += " AND fc.fecha_emision <= @hasta";

            qCodigos += " ORDER BY fc.fecha_emision DESC, cc.codigo ASC";

            using (var cmdCod = dbConn.CreateCommand())
            {
                cmdCod.CommandText = QueryAdapter.FormatearConsulta(qCodigos);
                AgregarParametro(cmdCod, "@almId", idAlmacenActual);
                if (compradorId.HasValue) AgregarParametro(cmdCod, "@compClie", compradorId.Value);
                if (desde.HasValue) AgregarParametro(cmdCod, "@desde", desde.Value.Date);
                if (hasta.HasValue) AgregarParametro(cmdCod, "@hasta", hasta.Value.Date.AddDays(1).AddTicks(-1));

                using var rdrC = await cmdCod.ExecuteReaderAsync();
                while (await rdrC.ReadAsync())
                {
                    _listaCodigosGlobal.Add(new DetalleCodigoVentaClienteDTO
                    {
                        Cantidad = 1,
                        Codigo = rdrC.GetString(0),
                        ColeccionTipo = "KARDEX FACTURADO",
                        FechaEmision = rdrC.GetDateTime(1),
                        TipoDocumento = rdrC.GetString(2),
                        SerieDocumento = rdrC.GetString(3),
                        NumeroDocumento = rdrC.GetString(4),
                        Importe = rdrC.GetDecimal(5), // 180.00 por cada código
                        ProductoId = rdrC.GetInt32(6)
                    });
                }
            }

            // 2. Agrupar los productos vendidos directamente desde los códigos reales obtenidos
            var productosAgrupados = _listaCodigosGlobal
                .GroupBy(c => c.ProductoId)
                .Select(g => new
                {
                    ProductoId = g.Key,
                    CantidadReal = g.Count(),
                    ImporteReal = g.Sum(x => x.Importe)
                })
                .ToList();

            foreach (var item in productosAgrupados)
            {
                var prodBD = await _productoService.ObtenerPorIdAsync(item.ProductoId);
                _listaProductos.Add(new ProductoVendidoClienteDTO
                {
                    ProductoId = item.ProductoId,
                    CodigoProducto = item.ProductoId.ToString("D6"),
                    Descripcion = prodBD?.Descripcion ?? "PRODUCTO GENERAL",
                    UnidadMedida = prodBD?.UnidadMedida?.Descripcion ?? "UND",
                    Cantidad = item.CantidadReal,           // 5 unidades reales
                    ImporteTotal = item.ImporteReal         // 900.00 (5 x 180.00)
                });
            }
        }

        private void DgProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgProductos.SelectedItem is ProductoVendidoClienteDTO prod)
            {
                DgCodigos.ItemsSource = _listaCodigosGlobal.Where(c => c.ProductoId == prod.ProductoId).ToList();
            }
            else
            {
                DgCodigos.ItemsSource = _listaCodigosGlobal;
            }
        }

        // Doble clic sobre un código -> abre el comprobante en MainShell
        private async void DgCodigos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgCodigos.SelectedItem is not DetalleCodigoVentaClienteDTO codigoSeleccionado) return;

            try
            {
                if (Window.GetWindow(this) is not MainShell mainShell) return;

                string serieLimpia = codigoSeleccionado.SerieDocumento.Trim();
                if (serieLimpia.Contains("-"))
                {
                    var partes = serieLimpia.Split('-');
                    serieLimpia = partes.Length > 1 ? partes[1] : partes[0];
                }

                string numeroLimpio = int.TryParse(codigoSeleccionado.NumeroDocumento, out int nVal)
                    ? nVal.ToString("D7")
                    : codigoSeleccionado.NumeroDocumento.Trim();

                string tituloTab = $"📄 Consulta: {serieLimpia}-{numeroLimpio}";
                RegistroComprobantesUserControl? controlComprobantes = null;

                foreach (TabItem tab in mainShell.MainTabControl.Items)
                {
                    if (tab.Header is StackPanel sp && sp.Children.Count > 0 &&
                        sp.Children[0] is TextBlock tb && tb.Text == tituloTab)
                    {
                        mainShell.MainTabControl.SelectedItem = tab;
                        controlComprobantes = tab.Content as RegistroComprobantesUserControl;
                        break;
                    }
                }

                if (controlComprobantes == null)
                {
                    controlComprobantes = new RegistroComprobantesUserControl();
                    mainShell.AbrirPestaña(tituloTab, controlComprobantes);
                }

                await controlComprobantes.CargarComprobanteParaConsultaAsync(serieLimpia, numeroLimpio);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el comprobante: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder: generación pendiente para servicio externo
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}