using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class SaldosProductosUserControl : UserControl
    {
        private readonly KardexService _kardexService;
        private List<SaldoProductoItem> _todosLosSaldos; // Guarda en memoria para el filtro rápido

        public SaldosProductosUserControl()
        {
            InitializeComponent();
            _kardexService = new KardexService();
            _todosLosSaldos = new List<SaldoProductoItem>();

            // Fechas iniciales por defecto (Desde inicio de año hasta hoy)
            DpDesde.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpHasta.SelectedDate = DateTime.Today;
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime desde = DpDesde.SelectedDate ?? DateTime.Today;
                DateTime hasta = DpHasta.SelectedDate ?? DateTime.Today;

                // Bloqueamos el botón mientras carga
                ((Button)sender).IsEnabled = false;

                // Llamamos a la base de datos
                _todosLosSaldos = await _kardexService.ObtenerSaldosYMovimientosAsync(desde, hasta);

                // Aplicamos el filtro por si hay algo escrito
                FiltrarData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al obtener saldos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ((Button)sender).IsEnabled = true;
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
                SaldosDataGrid.ItemsSource = _todosLosSaldos;
            }
            else
            {
                // Filtramos por descripción o código al vuelo
                var filtrados = _todosLosSaldos.Where(p =>
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(filtro)) ||
                    (p.Codigo != null && p.Codigo.ToLower().Contains(filtro))
                ).ToList();

                SaldosDataGrid.ItemsSource = filtrados;
            }
        }
    }
}