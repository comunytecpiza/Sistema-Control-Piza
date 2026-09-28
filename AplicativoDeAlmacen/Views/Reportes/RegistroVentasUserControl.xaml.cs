#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Reportes;
using AplicativoDeAlmacen.Services.Reportes;

namespace AplicativoDeAlmacen.Views.Reportes
{
    public partial class RegistroVentasUserControl : UserControl
    {
        private readonly RegistroVentasService _service;
        private readonly ReporteExcelService _excelService;
        private readonly ObservableCollection<RegistroVentaItemDTO> _ventasList = new();

        private readonly int _miSedeId;
        private readonly string _miSedeNombre;

        public RegistroVentasUserControl()
        {
            InitializeComponent();
            _service = new RegistroVentasService();
            _excelService = new ReporteExcelService();

            DgVentas.ItemsSource = _ventasList;

            _miSedeId = SesionSistema.AlmacenActual?.Id ?? 1;
            _miSedeNombre = SesionSistema.AlmacenActual?.Nombre ?? "Sede Principal";

            InicializarFiltros();
            _ = CargarSeriesAsync();
        }

        private void InicializarFiltros()
        {
            string[] meses = { "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE" };
            CmbMes.ItemsSource = meses;
            CmbMes.SelectedIndex = DateTime.Today.Month - 1;
            TxtAno.Text = DateTime.Today.Year.ToString();

            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpHasta.SelectedDate = DateTime.Today;
        }

        private async Task CargarSeriesAsync()
        {
            try
            {
                string? tipoDoc = null;
                if (ChkTodosDocumentos.IsChecked == false && CmbDocumento.SelectedItem is ComboBoxItem item)
                {
                    tipoDoc = item.Tag?.ToString();
                }

                var series = await _service.ObtenerSeriesPorSedeAsync(_miSedeId, tipoDoc);
                CmbSerie.ItemsSource = series;
                if (series.Any()) CmbSerie.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar series: {ex.Message}");
            }
        }

        private void ChkFiltros_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbDocumento == null || CmbSerie == null) return;

            CmbDocumento.IsEnabled = ChkTodosDocumentos.IsChecked == false;
            CmbSerie.IsEnabled = ChkTodasSeries.IsChecked == false;
        }

        private void CmbDocumento_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = CargarSeriesAsync();
        }

        private void RbModoFecha_Checked(object sender, RoutedEventArgs e)
        {
            if (CmbMes == null || DpDesde == null) return;

            bool porMes = RbPorMes.IsChecked == true;
            CmbMes.IsEnabled = porMes;
            TxtAno.IsEnabled = porMes;

            DpDesde.IsEnabled = !porMes;
            DpHasta.IsEnabled = !porMes;
        }

        private (DateTime Desde, DateTime Hasta, string Periodo) ResolverRangoFechas()
        {
            if (RbPorMes.IsChecked == true)
            {
                int ano = int.TryParse(TxtAno.Text.Trim(), out int a) ? a : DateTime.Today.Year;
                int mes = CmbMes.SelectedIndex + 1;
                int diasEnMes = DateTime.DaysInMonth(ano, mes);

                DateTime desde = new DateTime(ano, mes, 1);
                DateTime hasta = new DateTime(ano, mes, diasEnMes);
                string periodo = $"{CmbMes.SelectedItem} {ano}";
                return (desde, hasta, periodo);
            }
            else
            {
                DateTime desde = DpDesde.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;
                string periodo = $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
                return (desde, hasta, periodo);
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            var rango = ResolverRangoFechas();
            if (rango.Desde > rango.Hasta)
            {
                MessageBox.Show("La fecha 'Desde' no puede ser mayor que la fecha 'Hasta'.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? tipoDoc = (ChkTodosDocumentos.IsChecked == false && CmbDocumento.SelectedItem is ComboBoxItem item) ? item.Tag?.ToString() : null;
            string? serie = (ChkTodasSeries.IsChecked == false && CmbSerie.SelectedItem != null) ? CmbSerie.SelectedItem.ToString() : null;

            try
            {
                Cursor = Cursors.Wait;
                _ventasList.Clear();

                var datos = await _service.ConsultarRegistroVentasAsync(_miSedeId, rango.Desde, rango.Hasta, tipoDoc, serie);

                foreach (var v in datos)
                {
                    _ventasList.Add(v);
                }

                // Calcular totales del pie
                TxtTotalGravado.Text = _ventasList.Sum(x => x.TotalGravado).ToString("N2");
                TxtTotalExonerado.Text = _ventasList.Sum(x => x.TotalExonerado).ToString("N2");
                TxtTotalIgv.Text = _ventasList.Sum(x => x.TotalIgv).ToString("N2");
                TxtTotalImporte.Text = _ventasList.Sum(x => x.ImporteTotal).ToString("N2");

                if (!_ventasList.Any())
                {
                    MessageBox.Show("No se encontraron comprobantes registrados en el período seleccionado.", "Sin Registros", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar el registro de ventas: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void BtnImprimir_Click(object sender, RoutedEventArgs e)
        {
            if (!_ventasList.Any())
            {
                MessageBox.Show("No hay datos en pantalla para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.Wait;
                var rango = ResolverRangoFechas();

                _excelService.ExportarRegistroVentas(rango.Periodo, _miSedeNombre, _ventasList.ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el reporte en Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            if (Parent is Panel p)
                p.Children.Remove(this);
            else if (Parent is ContentControl cc)
                cc.Content = null;
        }
    }
}