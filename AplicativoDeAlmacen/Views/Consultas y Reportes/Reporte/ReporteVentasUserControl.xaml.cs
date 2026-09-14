#nullable enable

using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views.Contabilidad
{
    public partial class ReporteVentasUserControl : UserControl
    {
        private readonly DataConnection.DatabaseConnection _database;
        private List<ReporteVentaItemDTO> _ventasActuales = new List<ReporteVentaItemDTO>();

        public ReporteVentasUserControl()
        {
            InitializeComponent();
            _database = new DataConnection.DatabaseConnection();

            CargarMeses();
            dtpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtpHasta.SelectedDate = DateTime.Today;

            Loaded += async (s, e) => await CargarSeriesPorSedeAsync();
        }

        private void CargarMeses()
        {
            string[] meses = { "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE" };
            for (int i = 0; i < meses.Length; i++)
            {
                cboMeses.Items.Add(new ComboBoxItem { Content = meses[i], Tag = i + 1 });
            }
            cboMeses.SelectedIndex = DateTime.Today.Month - 1;
            txtAnio.Text = DateTime.Today.Year.ToString();
        }

        private async Task CargarSeriesPorSedeAsync()
        {
            try
            {
                cboSeries.Items.Clear();
                int idAlmacenActual = SesionSistema.AlmacenActual?.Id ?? 1;

                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
                string query = $@"
                    SELECT DISTINCT serie_documento 
                    FROM facturacion_cabecera {nolock}
                    WHERE (almacen_id = @almId OR almacen_id IS NULL)
                      AND estado_registro = 1
                    ORDER BY serie_documento ASC";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var p = cmd.CreateParameter();
                p.ParameterName = "@almId";
                p.Value = idAlmacenActual;
                cmd.Parameters.Add(p);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    cboSeries.Items.Add(rdr.GetString(0));
                }

                if (cboSeries.Items.Count > 0) cboSeries.SelectedIndex = 0;
            }
            catch { }
        }

        private void ChkModoFecha_Changed(object sender, RoutedEventArgs e)
        {
            if (chkPorMes == null || chkPorPeriodo == null) return;

            if (sender == chkPorMes && chkPorMes.IsChecked == true)
            {
                chkPorPeriodo.IsChecked = false;
                cboMeses.IsEnabled = true;
                txtAnio.IsEnabled = true;
                dtpDesde.IsEnabled = false;
                dtpHasta.IsEnabled = false;
            }
            else if (sender == chkPorPeriodo && chkPorPeriodo.IsChecked == true)
            {
                chkPorMes.IsChecked = false;
                cboMeses.IsEnabled = false;
                txtAnio.IsEnabled = false;
                dtpDesde.IsEnabled = true;
                dtpHasta.IsEnabled = true;
            }
        }

        private void ChkFiltros_Changed(object sender, RoutedEventArgs e)
        {
            if (cboTipoDocumento != null) cboTipoDocumento.IsEnabled = chkTodosDocumentos.IsChecked == false;
            if (cboSeries != null) cboSeries.IsEnabled = chkTodasSeries.IsChecked == false;
        }

        private void CboTipoDocumento_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                btnEjecutar.IsEnabled = false;
                DateTime? fDesde = null;
                DateTime? fHasta = null;

                // 🌟 Caso 1: Filtro por Mes específico
                if (chkPorMes.IsChecked == true)
                {
                    int mes = ((ComboBoxItem)cboMeses.SelectedItem).Tag is int mVal ? mVal : (cboMeses.SelectedIndex + 1);
                    int anio = int.TryParse(txtAnio.Text.Trim(), out int aVal) ? aVal : DateTime.Today.Year;
                    fDesde = new DateTime(anio, mes, 1);
                    fHasta = fDesde.Value.AddMonths(1).AddDays(-1);
                }
                // 🌟 Caso 2: Rango manual
                else if (chkPorPeriodo.IsChecked == true)
                {
                    fDesde = dtpDesde.SelectedDate ?? DateTime.Today;
                    fHasta = dtpHasta.SelectedDate ?? DateTime.Today;
                }
                // 🌟 Caso 3: Ambos desmarcados -> fDesde y fHasta quedan en null (TRAE TODO EL HISTORIAL)

                string? tipoDoc = (chkTodosDocumentos.IsChecked == false && cboTipoDocumento.SelectedItem is ComboBoxItem itemDoc)
                    ? itemDoc.Tag?.ToString()
                    : null;

                string? serieFiltro = (chkTodasSeries.IsChecked == false && cboSeries.SelectedItem != null)
                    ? cboSeries.SelectedItem.ToString()
                    : null;

                _ventasActuales = await ConsultarVentasAsync(fDesde, fHasta, tipoDoc, serieFiltro);
                dgReporteVentas.ItemsSource = _ventasActuales;

                // Totales
                lblCantidadDocumentos.Text = _ventasActuales.Count.ToString("N0");
                lblTotalGravado.Text = _ventasActuales.Sum(x => x.Gravado).ToString("N2");
                lblTotalExonerado.Text = _ventasActuales.Sum(x => x.Exonerado).ToString("N2");
                lblTotalIgv.Text = _ventasActuales.Sum(x => x.IGV).ToString("N2");
                lblTotalNeto.Text = _ventasActuales.Sum(x => x.Total).ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar el registro de ventas: {ex.Message}", "Reportes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnEjecutar.IsEnabled = true;
            }
        }

        private async Task<List<ReporteVentaItemDTO>> ConsultarVentasAsync(DateTime? desde, DateTime? hasta, string? tipoDoc = null, string? serie = null)
        {
            var list = new List<ReporteVentaItemDTO>();
            int idAlmacenActual = SesionSistema.AlmacenActual?.Id ?? 1;

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";

            string query = $@"
        SELECT 
            fc.id,
            fc.fecha_emision,
            fc.tipo_documento,
            fc.serie_documento,
            fc.numero_documento,
            COALESCE(p.razon_social, CONCAT(COALESCE(p.nombres, ''), ' ', COALESCE(p.apellido_paterno, ''))) AS cliente_nombre,
            fc.total_gravado,
            fc.total_exonerado,
            fc.total_inafecto,
            fc.total_igv,
            fc.importe_total
        FROM facturacion_cabecera fc {nolock}
        LEFT JOIN personas_comerciales p {nolock} ON fc.comprador_id = p.id
        WHERE fc.estado_registro = 1
          AND (fc.almacen_id = @almId OR fc.almacen_id IS NULL)";

            // 🌟 Si desde y hasta tienen valor se agregan; si son null se ignoran y trae TODO
            if (desde.HasValue) query += " AND fc.fecha_emision >= @desde";
            if (hasta.HasValue) query += " AND fc.fecha_emision <= @hasta";
            if (!string.IsNullOrEmpty(tipoDoc)) query += " AND fc.tipo_documento = @tipoDoc";
            if (!string.IsNullOrEmpty(serie)) query += " AND fc.serie_documento = @serie";

            query += " ORDER BY fc.fecha_emision ASC, fc.id ASC";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            var pAlm = cmd.CreateParameter(); pAlm.ParameterName = "@almId"; pAlm.Value = idAlmacenActual; cmd.Parameters.Add(pAlm);

            if (desde.HasValue)
            {
                var pD = cmd.CreateParameter(); pD.ParameterName = "@desde"; pD.Value = desde.Value.Date; cmd.Parameters.Add(pD);
            }

            if (hasta.HasValue)
            {
                var pH = cmd.CreateParameter(); pH.ParameterName = "@hasta"; pH.Value = hasta.Value.Date.AddDays(1).AddTicks(-1); cmd.Parameters.Add(pH);
            }

            if (!string.IsNullOrEmpty(tipoDoc))
            {
                var pT = cmd.CreateParameter(); pT.ParameterName = "@tipoDoc"; pT.Value = tipoDoc; cmd.Parameters.Add(pT);
            }

            if (!string.IsNullOrEmpty(serie))
            {
                var pS = cmd.CreateParameter(); pS.ParameterName = "@serie"; pS.Value = serie; cmd.Parameters.Add(pS);
            }

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new ReporteVentaItemDTO
                {
                    Id = rdr.GetInt32(0),
                    FechaEmision = rdr.GetDateTime(1),
                    TipoDocumento = rdr.GetString(2),
                    SerieDocumento = rdr.GetString(3),
                    NumeroDocumento = rdr.GetString(4),
                    Cliente = rdr.IsDBNull(5) ? "CLIENTES VARIOS" : rdr.GetString(5),
                    DocumentoIdentidad = string.Empty,
                    Gravado = rdr.IsDBNull(6) ? 0 : rdr.GetDecimal(6),
                    Exonerado = rdr.IsDBNull(7) ? 0 : rdr.GetDecimal(7),
                    Inafecto = rdr.IsDBNull(8) ? 0 : rdr.GetDecimal(8),
                    IGV = rdr.IsDBNull(9) ? 0 : rdr.GetDecimal(9),
                    Total = rdr.IsDBNull(10) ? 0 : rdr.GetDecimal(10)
                });
            }

            return list;
        }

        // 🌟 DOBLE CLIC: Abre el comprobante en Registro de Comprobantes (Modo Vista Previa / Imprimir)
        private async void DgReporteVentas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgReporteVentas.SelectedItem is not ReporteVentaItemDTO ventaSeleccionada) return;

            try
            {
                if (Window.GetWindow(this) is not MainShell mainShell) return;

                // 1. Limpiar serie (por si viene con prefijo como FAC-F003)
                string serieLimpia = ventaSeleccionada.SerieDocumento.Trim();
                if (serieLimpia.Contains("-"))
                {
                    var partes = serieLimpia.Split('-');
                    serieLimpia = partes.Length > 1 ? partes[1] : partes[0];
                }

                string numeroLimpio = int.TryParse(ventaSeleccionada.NumeroDocumento, out int nVal)
                    ? nVal.ToString("D7")
                    : ventaSeleccionada.NumeroDocumento.Trim();

                // 🌟 Título dinámico para la pestaña
                string tituloTab = $"📄 Consulta: {serieLimpia}-{numeroLimpio}";

                // 2. Buscar si ya existe la pestaña abierta con ese mismo documento o una previa
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

                // 3. Si no existe, abrir la pestaña con el nombre oficial del documento
                if (controlComprobantes == null)
                {
                    controlComprobantes = new RegistroComprobantesUserControl();
                    mainShell.AbrirPestaña(tituloTab, controlComprobantes);
                }

                // 4. Cargar los datos y aplicar el bloqueo de seguridad
                await controlComprobantes.CargarComprobanteParaConsultaAsync(
                    serieLimpia,
                    numeroLimpio
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir la vista previa: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnImprimirExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!_ventasActuales.Any())
            {
                MessageBox.Show("No hay datos en pantalla para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string nombreSede = SesionSistema.AlmacenActual?.Nombre ?? "SEDE_CENTRAL";
            var sfd = new SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"Registro_Ventas_{nombreSede}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Registro de Ventas");

                ws.Cell(1, 1).Value = "FECHA";
                ws.Cell(1, 2).Value = "TIPO";
                ws.Cell(1, 3).Value = "SERIE";
                ws.Cell(1, 4).Value = "NÚMERO";
                ws.Cell(1, 5).Value = "DOC. IDENTIDAD";
                ws.Cell(1, 6).Value = "CLIENTE / RAZÓN SOCIAL";
                ws.Cell(1, 7).Value = "GRAVADO";
                ws.Cell(1, 8).Value = "EXONERADO";
                ws.Cell(1, 9).Value = "IGV";
                ws.Cell(1, 10).Value = "TOTAL";

                var headerRange = ws.Range(1, 1, 1, 10);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int row = 2;
                foreach (var v in _ventasActuales)
                {
                    ws.Cell(row, 1).Value = v.FechaEmision.ToString("dd/MM/yyyy");
                    ws.Cell(row, 2).Value = v.TipoDocumento;
                    ws.Cell(row, 3).Value = v.SerieDocumento;
                    ws.Cell(row, 4).Value = v.NumeroDocumento;
                    ws.Cell(row, 5).Value = v.DocumentoIdentidad;
                    ws.Cell(row, 6).Value = v.Cliente;
                    ws.Cell(row, 7).Value = v.Gravado;
                    ws.Cell(row, 8).Value = v.Exonerado;
                    ws.Cell(row, 9).Value = v.IGV;
                    ws.Cell(row, 10).Value = v.Total;

                    ws.Range(row, 7, row, 10).Style.NumberFormat.Format = "#,##0.00";
                    row++;
                }

                ws.Cell(row, 6).Value = "TOTALES:";
                ws.Cell(row, 6).Style.Font.Bold = true;
                ws.Cell(row, 7).FormulaA1 = $"SUM(G2:G{row - 1})";
                ws.Cell(row, 8).FormulaA1 = $"SUM(H2:H{row - 1})";
                ws.Cell(row, 9).FormulaA1 = $"SUM(I2:I{row - 1})";
                ws.Cell(row, 10).FormulaA1 = $"SUM(J2:J{row - 1})";
                ws.Range(row, 7, row, 10).Style.Font.Bold = true;
                ws.Range(row, 7, row, 10).Style.NumberFormat.Format = "#,##0.00";

                ws.Columns().AdjustToContents();
                wb.SaveAs(sfd.FileName);

                MessageBox.Show("Archivo Excel exportado con éxito.", "Exportación Contable", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSunat_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Generador de estructura PLE / SIRE 14.1 preparado para la sede activa.", "SUNAT", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}