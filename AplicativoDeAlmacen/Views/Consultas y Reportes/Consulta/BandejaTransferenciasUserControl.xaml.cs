using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Transferencias;
using AplicativoDeAlmacen.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class BandejaTransferenciasUserControl : UserControl
    {
        private readonly TransaccionesService _transaccionesService = new TransaccionesService();

        public BandejaTransferenciasUserControl()
        {
            InitializeComponent();
            DtpDesde.SelectedDate = DateTime.Today.AddDays(-30);
            DtpHasta.SelectedDate = DateTime.Today;
            _ = CargarBandejaAsync();
        }

        private async Task CargarBandejaAsync()
        {
            try
            {
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                string filtroEstado = "TODOS";
                if (CboFiltroEstado?.SelectedItem is ComboBoxItem cbi)
                {
                    filtroEstado = cbi.Content.ToString() ?? "TODOS";
                }

                var lista = await _transaccionesService.ObtenerHistorialTransferenciasAsync(
                    miAlmacenId,
                    DtpDesde.SelectedDate,
                    DtpHasta.SelectedDate,
                    filtroEstado);

                // 🌟 ASIGNAMOS SI SOY EL EMISOR PARA CADA TRANSACCIÓN
                foreach (var item in lista)
                {
                    item.SoyElEmisor = (item.AlmacenOrigenId == miAlmacenId);
                }

                DgTransferencias.ItemsSource = lista;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar la bandeja: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnBuscar_Click(object sender, RoutedEventArgs e) => await CargarBandejaAsync();
        private async void BtnRefrescar_Click(object sender, RoutedEventArgs e) => await CargarBandejaAsync();

        private async void CboFiltroEstado_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await CargarBandejaAsync();
        }

        private void BtnVerDetalle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TransaccionHeaderDTO trans)
            {
                if (Window.GetWindow(this) is IMainWindow mainShell)
                {
                    int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                    // 1. SI SOY EL EMISOR (Origen) -> Abrir la pantalla de SALIDAS en modo lectura
                    if (trans.AlmacenOrigenId == miAlmacenId)
                    {
                        var vistaSalida = new SalidasUserControl();

                        var partes = trans.SerieNumero.Split('-');
                        if (partes.Length >= 2)
                        {
                            vistaSalida.CargarDocumentoParaConsulta(partes[0], partes[1]);
                        }

                        mainShell.AbrirPestaña($"📤 Salida / Envío: {trans.SerieNumero}", vistaSalida);
                    }
                    // 2. SI SOY EL RECEPTOR (Destino) -> Abrir INGRESOS
                    else
                    {
                        var vistaIngreso = new IngresoUserControl();

                        if (trans.EsPendiente)
                        {
                            // 📥 AÚN NO SE HA RECIBIDO: Cargar datos de la Salida para que Lima procese su Entrada
                            vistaIngreso.CargarDocumentoParaConsulta(trans.MovimientoId);
                            mainShell.AbrirPestaña($"📥 Procesar Recepción: {trans.GuiaRemision}", vistaIngreso);
                        }
                        else
                        {
                            // 👁️ YA FUE RECIBIDO: Cargar el registro de Entrada que guardó Lima
                            vistaIngreso.CargarDocumentoParaConsulta(trans.MovimientoId);
                            mainShell.AbrirPestaña($"📥 Recepción Registrada: {trans.SerieNumero}", vistaIngreso);
                        }
                    }
                }
            }
        }
    }
}