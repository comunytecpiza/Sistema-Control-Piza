using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Ubicaciones;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConsultaMovimientosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private readonly ProductoService _productoService;
        private readonly PersonaComercialService _personaService;
        private readonly UbicacionService _ubicacionService;
        private readonly DatabaseConnection _dbConnHelper;

        private int _productoSeleccionadoId;
        private int? _almacenFiltroId = null;
        private bool _estaSeleccionando;
        private bool _isUpdatingFromSelection = false;
        private bool _isCargando = false;
        private bool _necesitaRecargar = false;

        private List<ConsultaCodigoItem> _todosLosCodigos;
        private List<Producto> _todosLosProductos = new List<Producto>();
        private List<ConsultaMovimientoItem> _todosLosMovimientosRaw = new List<ConsultaMovimientoItem>();

        public ConsultaMovimientosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _productoService = new ProductoService();
            _personaService = new PersonaComercialService();
            _ubicacionService = new UbicacionService();
            _dbConnHelper = new DatabaseConnection();

            _todosLosCodigos = new List<ConsultaCodigoItem>();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += Control_Loaded;

            // 🌟 SUSCRIPCIÓN AL EVENTBUS
            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                if (this.IsVisible && _productoSeleccionadoId > 0)
                {
                    BtnEjecutar_Click(null, null);
                }
                else
                {
                    _necesitaRecargar = true;
                }
            });

            this.IsVisibleChanged += (s, e) => {
                if (this.IsVisible && _necesitaRecargar && _productoSeleccionadoId > 0)
                {
                    _necesitaRecargar = false;
                    BtnEjecutar_Click(null, null);
                }
            };
        }

        private async void Control_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var dbProductos = await _productoService.ObtenerTodosAsync();
                _todosLosProductos = dbProductos.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar productos: " + ex.Message);
            }

            var txtProd = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
            if (txtProd != null)
            {
                txtProd.TextChanged += TxtProducto_TextChanged;
                txtProd.PreviewKeyDown += Filtros_PreviewKeyDown; // 👈 Enter directo en la caja de texto del producto
            }

            ConfigurarMascaraFecha(DpDesde);
            ConfigurarMascaraFecha(DpHasta);

            // 🌟 Enlace de ENTER para todos los campos de búsqueda y filtros
            CboProductos.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtAlmacen.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtRazonSocial.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtUbicacion.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        private void ChkFiltros_Click(object sender, RoutedEventArgs e)
        {
            TxtAlmacen.IsEnabled = ChkAlmacen.IsChecked == true;
            if (ChkAlmacen.IsChecked == false)
            {
                TxtAlmacen.Text = string.Empty;
                _almacenFiltroId = null;
            }

            TxtRazonSocial.IsEnabled = ChkRazonSocial.IsChecked == true;
            if (ChkRazonSocial.IsChecked == false) TxtRazonSocial.Text = string.Empty;

            TxtUbicacion.IsEnabled = ChkUbicacion.IsChecked == true;
            if (ChkUbicacion.IsChecked == false) TxtUbicacion.Text = string.Empty;

            AjustarModoPerspectivaUI();
        }

        private void ChkMostrarAnulados_Click(object sender, RoutedEventArgs e)
        {
            RefrescarVistaMovimientos();
        }

        private async void TxtAlmacen_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtAlmacen.IsEnabled || _isUpdatingFromSelection) return;
            string filtro = TxtAlmacen.Text.Trim();

            if (filtro.Length >= 1)
            {
                try
                {
                    var sugerencias = await BuscarAlmacenesFiltradosAsync(filtro);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstAlmacen.ItemsSource = sugerencias;
                        PopupAlmacen.IsOpen = true;
                    }
                    else PopupAlmacen.IsOpen = false;
                }
                catch { PopupAlmacen.IsOpen = false; }
            }
            else PopupAlmacen.IsOpen = false;
        }

        private async Task<List<Almacen>> BuscarAlmacenesFiltradosAsync(string filtro)
        {
            var lista = new List<Almacen>();
            using var conn = _dbConnHelper.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
            const int ALMACEN_CENTRAL_ID = 1;

            string query;
            if (miAlmacenId == ALMACEN_CENTRAL_ID)
            {
                query = "SELECT id, nombre FROM almacenes WHERE nombre LIKE @filtro AND id != @miAlmacen AND estado_id = 1 ORDER BY nombre ASC";
            }
            else
            {
                query = "SELECT id, nombre FROM almacenes WHERE id = @centralId AND estado_id = 1";
            }

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@filtro"; p1.Value = "%" + filtro + "%"; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@miAlmacen"; p2.Value = miAlmacenId; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@centralId"; p3.Value = ALMACEN_CENTRAL_ID; cmd.Parameters.Add(p3);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new Almacen
                {
                    Id = rdr.GetInt32(0),
                    Nombre = rdr.GetString(1)
                });
            }

            return lista;
        }

        private void LstAlmacen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstAlmacen.SelectedItem is Almacen almacenSeleccionado)
            {
                _isUpdatingFromSelection = true;
                TxtAlmacen.Text = almacenSeleccionado.Nombre;
                _almacenFiltroId = almacenSeleccionado.Id;
                PopupAlmacen.IsOpen = false;
                LstAlmacen.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
                AjustarModoPerspectivaUI();
            }
        }

        private void AjustarModoPerspectivaUI()
        {
            bool hayFiltroAlmacen = ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text);
            bool hayFiltroTercero = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            if (MovimientosDataGrid.Columns.Count >= 7)
            {
                var ingresoStyle = (Style)FindResource("IngresoStyle");
                var salidaStyle = (Style)FindResource("SalidaStyle");

                var colSalida = MovimientosDataGrid.Columns[4] as DataGridTextColumn;
                var colIngreso = MovimientosDataGrid.Columns[5] as DataGridTextColumn;

                if (hayFiltroAlmacen)
                {
                    // 🏢 PERSPECTIVA ENTRE ALMACENES (Transferencias Inter-Sedes)
                    if (colSalida != null)
                    {
                        colSalida.Header = "RECIBIDO";
                        colSalida.CellStyle = ingresoStyle; // Verde para lo recibido del otro almacén
                        colSalida.Binding = new System.Windows.Data.Binding("Ingreso") { StringFormat = "N0" };
                    }
                    if (colIngreso != null)
                    {
                        colIngreso.Header = "ENVIADO";
                        colIngreso.CellStyle = salidaStyle; // Rojo para lo enviado al otro almacén
                        colIngreso.Binding = new System.Windows.Data.Binding("Salida") { StringFormat = "N0" };
                    }

                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Visible;
                    var colSaldo = MovimientosDataGrid.Columns[6] as DataGridTextColumn;
                    if (colSaldo != null) colSaldo.Header = "NETO INTER-ALMACÉN";

                    LblCard1.Text = "Total Recibido";
                    LblCard2.Text = "Total Enviado";
                    LblCard3.Text = "Balance Neto con Almacén";

                    // Tarjeta 1: Recibido -> Verde
                    Card1.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
                    Card1.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                    LblCard1.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#065F46"));
                    TxtTotalIngreso.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#047857"));

                    // Tarjeta 2: Enviado -> Rojo
                    Card2.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF2F2"));
                    Card2.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                    LblCard2.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    TxtTotalSalida.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B91C1C"));

                    Card3.Visibility = Visibility.Visible;
                }
                else if (hayFiltroTercero)
                {
                    // 👤 PERSPECTIVA PROMOTORA / TERCEROS / UBICACIÓN
                    if (colSalida != null)
                    {
                        colSalida.Header = "ENVIADO";
                        colSalida.CellStyle = salidaStyle;
                        colSalida.Binding = new System.Windows.Data.Binding("Salida") { StringFormat = "N0" };
                    }
                    if (colIngreso != null)
                    {
                        colIngreso.Header = "DEVUELTO";
                        colIngreso.CellStyle = ingresoStyle;
                        colIngreso.Binding = new System.Windows.Data.Binding("Ingreso") { StringFormat = "N0" };
                    }

                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Visible;
                    var colSaldo = MovimientosDataGrid.Columns[6] as DataGridTextColumn;
                    if (colSaldo != null) colSaldo.Header = "TOTAL EN PODER";

                    LblCard1.Text = "Total Entregado (Salidas)";
                    LblCard2.Text = "Total Devoluciones";
                    LblCard3.Text = "Saldo Pendiente en Poder";

                    Card1.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF2F2"));
                    Card1.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                    LblCard1.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    TxtTotalIngreso.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B91C1C"));

                    Card2.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
                    Card2.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                    LblCard2.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#065F46"));
                    TxtTotalSalida.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#047857"));

                    Card3.Visibility = Visibility.Visible;
                }
                else
                {
                    // 🔵 VISTA GENERAL DEL ALMACÉN ACTUAL
                    if (colSalida != null)
                    {
                        colSalida.Header = "INGRESO";
                        colSalida.CellStyle = ingresoStyle;
                        colSalida.Binding = new System.Windows.Data.Binding("Ingreso") { StringFormat = "N0" };
                    }
                    if (colIngreso != null)
                    {
                        colIngreso.Header = "SALIDA";
                        colIngreso.CellStyle = salidaStyle;
                        colIngreso.Binding = new System.Windows.Data.Binding("Salida") { StringFormat = "N0" };
                    }

                    MovimientosDataGrid.Columns[6].Visibility = Visibility.Collapsed;

                    LblCard1.Text = "Total Entradas";
                    LblCard2.Text = "Total Salidas";
                    LblCard3.Text = string.Empty;

                    Card1.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
                    Card1.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                    LblCard1.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#065F46"));
                    TxtTotalIngreso.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#047857"));

                    Card2.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF2F2"));
                    Card2.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                    LblCard2.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    TxtTotalSalida.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B91C1C"));

                    Card3.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BtnAbrirOpcionesImpresion_Click(object sender, RoutedEventArgs e)
        {
            // Verificamos los 3 filtros posibles: Razón Social, Ubicación o Almacén
            bool razonSocialActiva = ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text);
            bool ubicacionActiva = ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text);
            bool almacenActivo = ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text);

            var ventanaFiltro = new AplicativoDeAlmacen.Views.FiltroImpresionKardexWindow(razonSocialActiva, ubicacionActiva, almacenActivo);

            var windowPadre = Window.GetWindow(this);
            if (windowPadre != null)
            {
                ventanaFiltro.Owner = windowPadre;
            }

            ventanaFiltro.ShowDialog();

            if (ventanaFiltro.SeConfirmoImpresion)
            {
                bool enviar = ventanaFiltro.IncluirEnviados;
                bool devolver = ventanaFiltro.IncluirDevueltos;
                bool enPoder = ventanaFiltro.IncluirVendidos;

                ExportarReporteExcel(enviar, devolver, enPoder);
            }
        }

        private void ExportarReporteExcel(bool enviar, bool devolver, bool transferidosEnPoder)
        {
            try
            {
                if (CboProductos.SelectedItem == null && string.IsNullOrWhiteSpace(CboProductos.Text))
                {
                    MessageBox.Show("Seleccione un producto para exportar el reporte.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_todosLosMovimientosRaw == null || !_todosLosMovimientosRaw.Any())
                {
                    MessageBox.Show("Primero haga clic en 'Ejecutar' para cargar los movimientos a exportar.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var movimientos = _todosLosMovimientosRaw.AsEnumerable();

                // 1. Filtrar Anulados
                bool mostrarAnulados = ChkMostrarAnulados.IsChecked == true;
                if (!mostrarAnulados)
                {
                    movimientos = movimientos.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO"));
                }

                // 2. Filtro por Almacén / Razón Social / Ubicación (Evaluación por texto)
                bool hayFiltroAlmacen = ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text);
                if (hayFiltroAlmacen)
                {
                    string almBuscado = TxtAlmacen.Text.Trim();
                    movimientos = movimientos.Where(m => m.RazonSocialUbicacion.Contains(almBuscado, StringComparison.OrdinalIgnoreCase) ||
                                                        (_almacenFiltroId.HasValue && m.AlmacenId == _almacenFiltroId.Value));
                }

                // 3. Reglas de Selección de Opciones de Impresión
                bool incluirSalidas = enviar || transferidosEnPoder; // 👈 Si marca "En su poder", DEBE incluir los despachos/salidas
                bool incluirIngresos = devolver;

                // Si no marcó ningún check, exporta todo
                if (!enviar && !devolver && !transferidosEnPoder)
                {
                    incluirSalidas = true;
                    incluirIngresos = true;
                }

                var movimientosFiltrados = movimientos.Where(m =>
                    (incluirSalidas && m.Salida > 0) ||
                    (incluirIngresos && m.Ingreso > 0) ||
                    (m.Ingreso == 0 && m.Salida == 0)
                ).ToList();

                if (!movimientosFiltrados.Any())
                {
                    MessageBox.Show("No se encontraron registros con los filtros seleccionados.", "Sin Datos", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 4. Enlazar Códigos Físicos a cada Movimiento
                // Si seleccionó exclusivamente "Transferidos en su poder", calcula los códigos netos no retornados
                if (transferidosEnPoder && !enviar && !devolver)
                {
                    var codigosNetosEnPoder = _todosLosCodigos
                        .GroupBy(c => c.Codigo)
                        .Where(g => g.Count(x => x.TipoMovimiento == "SALIDA") > g.Count(x => x.TipoMovimiento == "ENTRADA"))
                        .Select(g => g.First())
                        .ToList();

                    foreach (var mov in movimientosFiltrados)
                    {
                        string regLimpio = mov.NumeroRegistro.Replace("❌ ANULADO - ", "").Trim();
                        mov.CodigosAsociados = codigosNetosEnPoder
                            .Where(c => c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    movimientosFiltrados = movimientosFiltrados.Where(m => m.CodigosAsociados.Any()).ToList();
                }
                else
                {
                    // Mapeo general estándar
                    foreach (var mov in movimientosFiltrados)
                    {
                        string regLimpio = mov.NumeroRegistro.Replace("❌ ANULADO - ", "").Trim();
                        bool esIngreso = mov.Ingreso > 0;
                        string tipoEsperado = esIngreso ? "ENTRADA" : "SALIDA";

                        mov.CodigosAsociados = _todosLosCodigos
                            .Where(c => c.NumeroRegistro.Equals(regLimpio, StringComparison.OrdinalIgnoreCase) && c.TipoMovimiento == tipoEsperado)
                            .ToList();
                    }
                }

                if (!movimientosFiltrados.Any())
                {
                    MessageBox.Show("No se encontraron códigos asociados a la selección.", "Sin Datos", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 5. Textos de Cabecera para el Excel
                string nombreProd = CboProductos.Text;
                string tipoProd = (RbGuia?.IsChecked == true) ? "SOLO GUÍAS" : ((RbVenta?.IsChecked == true) ? "SOLO VENTAS" : "TODOS");
                string unidadMed = "PACKS";

                string origenDest = "TODOS";
                if (hayFiltroAlmacen) origenDest = TxtAlmacen.Text;
                else if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) origenDest = TxtRazonSocial.Text;
                else if (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text)) origenDest = TxtUbicacion.Text;

                DateTime fechaDesde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime fechaHasta = DpHasta.SelectedDate ?? DateTime.Today;

                // 6. Generación del Archivo Excel
                var excelService = new AplicativoDeAlmacen.Services.Reportes.ReporteExcelService();
                excelService.ExportarKardexConCodigos(
                    nombreProd,
                    tipoProd,
                    unidadMed,
                    origenDest,
                    fechaDesde,
                    fechaHasta,
                    movimientosFiltrados,
                    enviar,
                    devolver,
                    transferidosEnPoder
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void RefrescarVistaMovimientos()
        {
            var movimientos = _todosLosMovimientosRaw.AsEnumerable();

            bool mostrarAnulados = ChkMostrarAnulados.IsChecked == true;
            if (!mostrarAnulados)
            {
                movimientos = movimientos.Where(m => !m.IsAnulado && !m.NumeroRegistro.Contains("ANULADO"));
            }

            bool hayFiltroAlmacen = ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text);
            bool hayFiltroTercero = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            var listaFinal = movimientos.ToList();

            decimal saldoAcumulado = 0;
            foreach (var item in listaFinal)
            {
                if (!item.IsAnulado)
                {
                    if (hayFiltroAlmacen || hayFiltroTercero)
                    {
                        // 🌟 PERSPECTIVA DE ENTIDAD/ALMACÉN EXTERNO:
                        // Se calcula la diferencia acumulada y aseguramos el entero positivo con Math.Abs
                        decimal diferencia = item.Salida - item.Ingreso;
                        saldoAcumulado += diferencia;
                    }
                }

                // 🌟 Muestra el acumulado acumulado siempre en positivo positivo (absoluto)
                item.SaldoAcumulado = (hayFiltroAlmacen || hayFiltroTercero) ? Math.Abs(saldoAcumulado) : 0;
            }

            MovimientosDataGrid.ItemsSource = null;
            MovimientosDataGrid.ItemsSource = listaFinal;

            decimal totalIngresos = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Ingreso);
            decimal totalSalidas = listaFinal.Where(m => !m.IsAnulado).Sum(m => m.Salida);

            if (hayFiltroAlmacen)
            {
                TxtTotalIngreso.Text = totalIngresos.ToString("N0"); // Total Recibido
                TxtTotalSalida.Text = totalSalidas.ToString("N0");   // Total Enviado

                // 📦 Transferidos que el otro almacén aún tiene en su poder (Enviados - Devueltos)
                decimal transferidosEnPoder = Math.Abs(totalSalidas - totalIngresos);
                TxtTotalVendidos.Text = transferidosEnPoder.ToString("N0");
                LblCard3.Text = "Transferidos en su poder";
            }
            else if (hayFiltroTercero)
            {
                TxtTotalIngreso.Text = totalSalidas.ToString("N0");  // Total Entregado
                TxtTotalSalida.Text = totalIngresos.ToString("N0");  // Total Devoluciones

                decimal saldoEnPoder = Math.Abs(totalSalidas - totalIngresos);
                TxtTotalVendidos.Text = saldoEnPoder.ToString("N0");
            }
            else
            {
                TxtTotalIngreso.Text = totalIngresos.ToString("N0");
                TxtTotalSalida.Text = totalSalidas.ToString("N0");
                TxtTotalVendidos.Text = "---";
            }

            AjustarModoPerspectivaUI();

            CodigosDataGrid.ItemsSource = null;
            TxtTotalCodigos.Text = "Seleccione un movimiento para auditar";
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 1. Si el desplegable de productos está abierto y tiene un ítem seleccionado, deja que el Enter elija el producto
                if (CboProductos.IsDropDownOpen && CboProductos.SelectedItem != null)
                {
                    CboProductos.IsDropDownOpen = false;
                    e.Handled = true;
                    return;
                }

                // 2. Si el popup de Almacén está abierto y hay selección, aplícala y cierra
                if (PopupAlmacen.IsOpen && LstAlmacen.SelectedItem != null)
                {
                    LstAlmacen_SelectionChanged(LstAlmacen, null);
                    PopupAlmacen.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                // 3. Si el popup de Razón Social está abierto y hay selección, aplícala y cierra
                if (PopupRazonSocial.IsOpen && LstRazonSocial.SelectedItem != null)
                {
                    LstRazonSocial_SelectionChanged(LstRazonSocial, null);
                    PopupRazonSocial.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                // 4. Si el popup de Ubicación está abierto y hay selección, aplícala y cierra
                if (PopupUbicacion.IsOpen && LstUbicacion.SelectedItem != null)
                {
                    LstUbicacion_SelectionChanged(LstUbicacion, null);
                    PopupUbicacion.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                // 5. Cierra cualquier popup flotante residual
                CboProductos.IsDropDownOpen = false;
                PopupAlmacen.IsOpen = false;
                PopupRazonSocial.IsOpen = false;
                PopupUbicacion.IsOpen = false;

                // 6. 🚀 Ejecuta la consulta de inmediato sin necesidad de hacer clic en el botón
                e.Handled = true;
                BtnEjecutar_Click(BtnEjecutar, null);
            }
        }

        private void TxtProducto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_estaSeleccionando) return;
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string searchText = textBox.Text;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                CboProductos.IsDropDownOpen = false;
                CboProductos.ItemsSource = null;
                _productoSeleccionadoId = 0;
                return;
            }

            _estaSeleccionando = true;
            int cursorPosition = textBox.CaretIndex;

            var filtrados = _todosLosProductos
                .Where(p => p.Descripcion != null && p.Descripcion.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList();

            CboProductos.ItemsSource = filtrados;
            CboProductos.IsDropDownOpen = filtrados.Any();

            textBox.Text = searchText;
            textBox.CaretIndex = cursorPosition;
            _estaSeleccionando = false;
        }

        private void CboProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboProductos.SelectedItem is Producto producto)
            {
                _estaSeleccionando = true;
                _productoSeleccionadoId = producto.Id;

                var textBox = CboProductos.Template.FindName("PART_EditableTextBox", CboProductos) as TextBox;
                if (textBox != null)
                {
                    textBox.Text = producto.Descripcion;
                    textBox.CaretIndex = textBox.Text.Length;
                }
                CboProductos.IsDropDownOpen = false;
                _estaSeleccionando = false;
            }
        }

        private async void TxtRazonSocial_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtRazonSocial.IsEnabled || _isUpdatingFromSelection) return;
            string textoBusqueda = TxtRazonSocial.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    var sugerencias = await _personaService.BuscarPorRazonSocialAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstRazonSocial.ItemsSource = sugerencias;
                        PopupRazonSocial.IsOpen = true;
                    }
                    else PopupRazonSocial.IsOpen = false;
                }
                catch { PopupRazonSocial.IsOpen = false; }
            }
            else PopupRazonSocial.IsOpen = false;
        }

        private void LstRazonSocial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstRazonSocial.SelectedItem is PersonaComercial personaSeleccionada)
            {
                _isUpdatingFromSelection = true;
                TxtRazonSocial.Text = !string.IsNullOrEmpty(personaSeleccionada.RazonSocial)
                                        ? personaSeleccionada.RazonSocial
                                        : $"{personaSeleccionada.Nombres} {personaSeleccionada.ApellidoPaterno}";
                PopupRazonSocial.IsOpen = false;
                LstRazonSocial.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
                AjustarModoPerspectivaUI();
            }
        }

        private async void TxtUbicacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtUbicacion.IsEnabled || _isUpdatingFromSelection) return;
            string textoBusqueda = TxtUbicacion.Text.Trim();

            if (textoBusqueda.Length >= 2)
            {
                try
                {
                    var sugerencias = await _ubicacionService.BuscarUbicacionesPorNombreAsync(textoBusqueda);
                    if (sugerencias != null && sugerencias.Count > 0)
                    {
                        LstUbicacion.ItemsSource = sugerencias;
                        PopupUbicacion.IsOpen = true;
                    }
                    else PopupUbicacion.IsOpen = false;
                }
                catch { PopupUbicacion.IsOpen = false; }
            }
            else PopupUbicacion.IsOpen = false;
        }

        private void LstUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstUbicacion.SelectedItem is Ubicacion ubiSeleccionada)
            {
                _isUpdatingFromSelection = true;
                TxtUbicacion.Text = ubiSeleccionada.Descripcion;
                PopupUbicacion.IsOpen = false;
                LstUbicacion.SelectedIndex = -1;
                _isUpdatingFromSelection = false;
                AjustarModoPerspectivaUI();
            }
        }

        private void RbFiltro_Click(object sender, RoutedEventArgs e) { }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_isCargando) return;
            if (_productoSeleccionadoId == 0) { MessageBox.Show("Seleccione un producto maestro para auditar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Button btn = sender as Button;
            string txtOriginal = btn?.Content?.ToString() ?? "Ejecutar";

            _isCargando = true;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ Consultando..."; }
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                string? filtroRazon = null;
                if (ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text))
                {
                    filtroRazon = TxtAlmacen.Text;
                }
                else if (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
                {
                    filtroRazon = TxtRazonSocial.Text;
                }

                string? filtroUbicacion = ChkUbicacion.IsChecked == true ? TxtUbicacion.Text : null;

                int? categoriaIdFiltro = null;
                if (RbGuia != null && RbGuia.IsChecked == true) categoriaIdFiltro = 1;
                else if (RbVenta != null && RbVenta.IsChecked == true) categoriaIdFiltro = 2;

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                var reporte = await _kardexService.ConsultarMovimientosDetalladosAsync(
                    _productoSeleccionadoId,
                    desde,
                    hasta,
                    filtroRazon,
                    filtroUbicacion,
                    categoriaIdFiltro,
                    miAlmacenId);

                _todosLosMovimientosRaw = reporte.Movimientos;
                _todosLosCodigos = reporte.Codigos;
                BtnAbrirOpcionesImpresion.IsEnabled = true;
                AjustarModoPerspectivaUI();
                RefrescarVistaMovimientos();
            }
            catch (Exception ex) { MessageBox.Show("Error al ejecutar consulta: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                _isCargando = false;
                Mouse.OverrideCursor = null;
                if (btn != null) { btn.IsEnabled = true; btn.Content = txtOriginal; }
            }
        }

        private void CardSaldoEnPoder_Click(object sender, MouseButtonEventArgs e)
        {
            if (_productoSeleccionadoId == 0) return;

            bool hayFiltroAlmacen = ChkAlmacen.IsChecked == true && !string.IsNullOrWhiteSpace(TxtAlmacen.Text);
            bool hayFiltroTercero = (ChkRazonSocial.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRazonSocial.Text)) ||
                                    (ChkUbicacion.IsChecked == true && !string.IsNullOrWhiteSpace(TxtUbicacion.Text));

            string entidadSeleccionada = hayFiltroAlmacen
                ? TxtAlmacen.Text
                : (hayFiltroTercero ? (!string.IsNullOrWhiteSpace(TxtUbicacion.Text) ? TxtUbicacion.Text : TxtRazonSocial.Text) : "MI ALMACÉN");

            var codigosEnPoder = _todosLosCodigos
                .GroupBy(c => c.Codigo)
                .Where(g => g.Count() % 2 != 0)
                .Select(g => g.First())
                .ToList();

            CodigosDataGrid.ItemsSource = null;
            CodigosDataGrid.ItemsSource = codigosEnPoder;

            TxtTotalCodigos.Text = $"📦 Hay {codigosEnPoder.Count} códigos físicos asociados a: {entidadSeleccionada}";
        }

        private void MovimientosDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (MovimientosDataGrid.CurrentCell.Column != null)
            {
                int columnIndex = MovimientosDataGrid.Columns.IndexOf(MovimientosDataGrid.CurrentCell.Column);

                if (columnIndex == 6 && MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem itemSeleccionado)
                {
                    MostrarCodigosPendientesEnPoder();
                }
                else if ((columnIndex == 4 || columnIndex == 5) && MovimientosDataGrid.CurrentItem is ConsultaMovimientoItem current)
                {
                    MostrarCodigosParaMovimiento(current);
                }
            }
        }

        private void MostrarCodigosParaMovimiento(ConsultaMovimientoItem movimiento)
        {
            if (movimiento == null) return;

            string registroLimpio = movimiento.NumeroRegistro?
                .Replace("❌ ANULADO - ", "")
                .Trim() ?? string.Empty;

            bool esIngreso = movimiento.Ingreso > 0;
            string tipoBuscado = esIngreso ? "ENTRADA" : "SALIDA";

            var codigos = _todosLosCodigos
                .Where(c => (c.NumeroRegistro.Equals(registroLimpio, StringComparison.OrdinalIgnoreCase) ||
                            c.NumeroRegistro.Equals(movimiento.NumeroRegistro, StringComparison.OrdinalIgnoreCase))
                            && c.TipoMovimiento == tipoBuscado)
                .ToList();

            CodigosDataGrid.ItemsSource = null;
            CodigosDataGrid.ItemsSource = codigos;

            if (movimiento.IsAnulado || movimiento.NumeroRegistro.Contains("ANULADO"))
            {
                TxtTotalCodigos.Text = $"⚠️ [ANULADO] {codigos.Count} códigos en esta operación.";
            }
            else
            {
                TxtTotalCodigos.Text = $"Se auditaron {codigos.Count} Códigos Físicos";
            }
        }

        private void MostrarCodigosPendientesEnPoder()
        {
            if (_productoSeleccionadoId == 0) return;

            var balanceCodigos = _todosLosCodigos
                .GroupBy(c => c.Codigo)
                .Select(g => new {
                    CodigoItem = g.First(),
                    UltimoMovimiento = g.OrderByDescending(x => x.NumeroRegistro).FirstOrDefault()
                })
                .Where(x => x.UltimoMovimiento != null && x.UltimoMovimiento.TipoMovimiento == "SALIDA")
                .Select(x => x.CodigoItem)
                .ToList();

            CodigosDataGrid.ItemsSource = null;
            CodigosDataGrid.ItemsSource = balanceCodigos;
            TxtTotalCodigos.Text = $"📦 Hay {balanceCodigos.Count} códigos físicos pendientes en su poder.";
        }

        private void MovimientosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem movimiento)
            {
                MostrarCodigosParaMovimiento(movimiento);
            }
        }

        private void MovimientosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MovimientosDataGrid.SelectedItem is ConsultaMovimientoItem seleccionado)
            {
                string registroLimpio = seleccionado.NumeroRegistro?
                    .Replace("❌ ANULADO - ", "")
                    .Trim() ?? string.Empty;

                var partes = registroLimpio.Split('-');

                if (partes.Length >= 2)
                {
                    string serie = partes[0];
                    string numero = partes[1];

                    // 🌟 DETECCIÓN DIRECTA E INFALIBLE:
                    // Si en la fila seleccionada el valor de Salida es mayor que 0, ES UNA SALIDA.
                    bool esSalidaReal = seleccionado.Salida > 0;

                    var mainShell = Application.Current.Windows.OfType<MainShell>().FirstOrDefault();
                    if (mainShell == null) return;

                    if (esSalidaReal)
                    {
                        // 📤 Abrir Vista de SALIDA en modo consulta
                        var salidasControl = new SalidasUserControl();
                        mainShell.AbrirPestaña($"Salida {serie}-{numero} (Consulta)", salidasControl);
                        salidasControl.CargarDocumentoParaConsulta(serie, numero);
                    }
                    else
                    {
                        // 📥 Abrir Vista de INGRESO en modo consulta
                        var ingresoControl = new IngresoUserControl();
                        mainShell.AbrirPestaña($"Ingreso {serie}-{numero} (Consulta)", ingresoControl);
                        ingresoControl.CargarDocumentoParaConsulta(serie, numero);
                    }
                }
            }
        }

        private bool _isFormattingDate = false;
        private void ConfigurarMascaraFecha(DatePicker dp)
        {
            dp.ApplyTemplate();
            if (dp.Template.FindName("PART_TextBox", dp) is TextBox tb)
            {
                tb.MaxLength = 10;
                tb.TextChanged += (s, ev) => {
                    if (_isFormattingDate || ev.Changes.Any(c => c.RemovedLength > 0)) return;
                    _isFormattingDate = true;
                    string n = new string(tb.Text.Where(char.IsDigit).ToArray());
                    if (n.Length >= 2 && n.Length < 4) tb.Text = n.Insert(2, "/");
                    else if (n.Length >= 4) tb.Text = n.Insert(2, "/").Insert(5, "/");
                    tb.CaretIndex = tb.Text.Length;
                    _isFormattingDate = false;
                };
            }
        }
    }
}