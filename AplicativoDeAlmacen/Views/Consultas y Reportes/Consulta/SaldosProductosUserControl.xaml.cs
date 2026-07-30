using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Services.Reportes;
using AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta;
using AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class SaldosProductosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private List<SaldoProductoItem> _todosLosSaldos;
        private readonly ReporteExcelService _reporteExcel;
        private bool _isCargando = false;

        private readonly DatabaseConnection _db = new DatabaseConnection();

        public SaldosProductosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _todosLosSaldos = new List<SaldoProductoItem>();
            _reporteExcel = new ReporteExcelService();

            EventBus.OnMovimientosChanged += () => Application.Current.Dispatcher.InvokeAsync(() => {
                if (this.IsVisible) BtnEjecutar_Click(null, null);
            });

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;

            Loaded += SaldosProductosUserControl_Loaded;
        }

        private void SaldosProductosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigurarMascaraFecha(DpDesde);
            ConfigurarMascaraFecha(DpHasta);

            DpDesde.PreviewKeyDown += Filtros_PreviewKeyDown;
            DpHasta.PreviewKeyDown += Filtros_PreviewKeyDown;
            TxtFiltro.PreviewKeyDown += Filtros_PreviewKeyDown;
        }

        private void Filtros_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnEjecutar_Click(null, null);
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_isCargando) return;

            Button btnEjecutar = sender as Button;
            string textoOriginal = btnEjecutar?.Content?.ToString() ?? "Ejecutar";

            try
            {
                _isCargando = true;
                Mouse.OverrideCursor = Cursors.Wait;

                if (btnEjecutar != null)
                {
                    btnEjecutar.IsEnabled = false;
                    btnEjecutar.Content = "⏳ Cargando...";
                }

                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                // 🌟 TOMA AUTOMÁTICAMENTE EL ALMACÉN DE LA SESIÓN ACTIVA
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                _todosLosSaldos = await _kardexService.ObtenerSaldosYMovimientosAsync(desde, hasta, miAlmacenId);

                FiltrarData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al obtener saldos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btnEjecutar != null)
                {
                    btnEjecutar.IsEnabled = true;
                    btnEjecutar.Content = textoOriginal;
                }

                Mouse.OverrideCursor = null;
                _isCargando = false;
            }
        }

        private async void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_todosLosSaldos == null || !_todosLosSaldos.Any())
                {
                    MessageBox.Show("No existen registros para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await _reporteExcel.ExportarSaldosProductosAsync(_todosLosSaldos);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtFiltro_TextChanged(object sender, TextChangedEventArgs e)
        {
            FiltrarData();
        }

        private void FiltrarData()
        {
            if (_todosLosSaldos == null || !_todosLosSaldos.Any()) return;

            string filtro = TxtFiltro.Text.ToLower().Trim();

            if (string.IsNullOrWhiteSpace(filtro))
            {
                SaldosDataGrid.ItemsSource = null;
                SaldosDataGrid.ItemsSource = _todosLosSaldos;
            }
            else
            {
                var filtrados = _todosLosSaldos.Where(p =>
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(filtro)) ||
                    (p.Codigo != null && p.Codigo.ToLower().Contains(filtro))
                ).ToList();

                SaldosDataGrid.ItemsSource = null;
                SaldosDataGrid.ItemsSource = filtrados;
            }
        }

        private bool _isFormattingDate = false;



        // 🌟 DOBLE CLIC EN STOCK INICIAL (Col 2), ENTRADAS (Col 3) O SALIDAS (Col 4)
        private async void SaldosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SaldosDataGrid.SelectedItem is not SaldoProductoItem productoSeleccionado) return;

            var column = SaldosDataGrid.CurrentCell.Column;
            if (column == null) return;

            string header = column.Header?.ToString() ?? string.Empty;

            DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
            DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;
            int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

            // 1. 🌟 SI HACE DOBLE CLIC EN STOCK FINAL -> ABRE LA NUEVA VENTANA DE DESGLOSE
            if (header == "STOCK FINAL")
            {
                // Necesitamos el ID real para buscar el desglose en la BD
                int productoIdReal = 0;
                using (var conn = _db.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    await dbConn.OpenAsync();
                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM productos WHERE abreviatura = @cod OR descripcion = @desc LIMIT 1");

                    var p1 = cmd.CreateParameter(); p1.ParameterName = "@cod"; p1.Value = productoSeleccionado.Codigo ?? ""; cmd.Parameters.Add(p1);
                    var p2 = cmd.CreateParameter(); p2.ParameterName = "@desc"; p2.Value = productoSeleccionado.Descripcion ?? ""; cmd.Parameters.Add(p2);

                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value) productoIdReal = Convert.ToInt32(res);
                }

                if (productoIdReal > 0)
                {
                    var detalleWindow = new DetalleStockFinalWindow(
                        productoIdReal,
                        productoSeleccionado.Descripcion,
                        miAlmacenId,
                        hasta
                    )
                    {
                        Owner = Window.GetWindow(this)
                    };
                    detalleWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show("No se pudo identificar el ID del producto seleccionado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            // 2. 🌟 SI HACE DOBLE CLIC EN STOCK INICIAL, ENTRADAS O SALIDAS -> ABRE EL KÁRDEX EN PESTAÑA
            else if (header == "STOCK INICIAL" || header == "ENTRADAS" || header == "SALIDAS")
            {
                // 🔍 Buscamos el ID del producto consultando a la BD por su código o descripción
                int productoIdReal = 0;
                using (var conn = _db.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    await dbConn.OpenAsync();
                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id FROM productos WHERE abreviatura = @cod OR descripcion = @desc LIMIT 1");

                    var p1 = cmd.CreateParameter(); p1.ParameterName = "@cod"; p1.Value = productoSeleccionado.Codigo ?? ""; cmd.Parameters.Add(p1);
                    var p2 = cmd.CreateParameter(); p2.ParameterName = "@desc"; p2.Value = productoSeleccionado.Descripcion ?? ""; cmd.Parameters.Add(p2);

                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null && res != DBNull.Value) productoIdReal = Convert.ToInt32(res);
                }

                if (productoIdReal <= 0)
                {
                    MessageBox.Show("No se pudo identificar el ID del producto seleccionado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var mainWindow = Window.GetWindow(this);

                if (mainWindow is MainShell mainShell)
                {
                    var kardexControl = new KardexUserControl();
                    string tituloPestaña = $"📦 Kárdex: {productoSeleccionado.Descripcion}";

                    mainShell.AbrirPestaña(tituloPestaña, kardexControl);

                    // Inyectamos usando el ID real encontrado
                    kardexControl.CargarKardexDirecto(
                        productoIdReal,
                        productoSeleccionado.Descripcion,
                        desde,
                        hasta
                    );
                }
            }
        }
        private void ConfigurarMascaraFecha(DatePicker datePicker)
        {
            datePicker.ApplyTemplate();

            if (datePicker.Template.FindName("PART_TextBox", datePicker) is TextBox textBox)
            {
                textBox.MaxLength = 10;
                textBox.TextChanged += (s, ev) =>
                {
                    if (_isFormattingDate) return;

                    var tb = s as TextBox;
                    if (tb == null) return;

                    if (ev.Changes.Any(c => c.RemovedLength > 0 && c.AddedLength == 0)) return;

                    _isFormattingDate = true;

                    string numeros = new string(tb.Text.Where(char.IsDigit).ToArray());

                    if (numeros.Length >= 2 && numeros.Length < 4)
                    {
                        tb.Text = numeros.Insert(2, "/");
                        tb.CaretIndex = tb.Text.Length;
                    }
                    else if (numeros.Length >= 4 && numeros.Length <= 8)
                    {
                        tb.Text = numeros.Insert(2, "/").Insert(5, "/");
                        tb.CaretIndex = tb.Text.Length;
                    }

                    _isFormattingDate = false;
                };
            }
        }
    }
}