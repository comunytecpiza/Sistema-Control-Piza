using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Services.Facturación;
using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AplicativoDeAlmacen.Views.Facturacion
{
    public partial class MediosPagoUserControl : UserControl
    {
        private readonly MedioPagoService _medioPagoService = new MedioPagoService();
        private List<MedioPago> _todosLosMedios = new List<MedioPago>();
        private int? _medioEditandoId = null;

        private readonly DispatcherTimer _timerBuscar;

        public MediosPagoUserControl()
        {
            InitializeComponent();

            _timerBuscar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerBuscar.Tick += (s, e) =>
            {
                _timerBuscar.Stop();
                FiltrarMedios();
            };

            Loaded += async (s, e) => await CargarMediosAsync();
        }

        private async Task CargarMediosAsync()
        {
            try
            {
                Cursor = Cursors.Wait;
                _todosLosMedios = await _medioPagoService.ObtenerMediosPagoAsync(soloActivos: false);
                DgMediosPago.ItemsSource = _todosLosMedios;
                LblTotalMedios.Text = $"Total canales registrados: {_todosLosMedios.Count}";
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al listar medios de pago: {ex.Message}");
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void BtnRefrescarMedios_Click(object sender, RoutedEventArgs e)
        {
            await CargarMediosAsync();
            Growl.Success("Catálogo de medios de pago actualizado.");
        }

        private void TxtBuscarMedio_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timerBuscar.Stop();
            _timerBuscar.Start();
        }

        private void FiltrarMedios()
        {
            string f = TxtBuscarMedio.Text.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(f))
            {
                DgMediosPago.ItemsSource = _todosLosMedios;
                PopupSugerenciasMedio.IsOpen = false;
            }
            else
            {
                var filtrados = _todosLosMedios.Where(x =>
                    x.Nombre.ToLower().Contains(f) ||
                    (x.Tipo != null && x.Tipo.ToLower().Contains(f))
                ).ToList();

                DgMediosPago.ItemsSource = filtrados;
                LstSugerenciasMedio.ItemsSource = filtrados.Take(6).Select(x => $"{x.Nombre} ({x.Tipo})").ToList();
                PopupSugerenciasMedio.IsOpen = filtrados.Any() && !string.IsNullOrWhiteSpace(TxtBuscarMedio.Text);
            }
            LblTotalMedios.Text = $"Total canales: {DgMediosPago.Items.Count}";
        }

        private void LstSugerenciasMedio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSugerenciasMedio.SelectedItem is string sel)
            {
                string nombre = sel.Split('(')[0].Trim();
                var match = _todosLosMedios.FirstOrDefault(x => x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    DgMediosPago.SelectedItem = match;
                    CargarEnFormulario(match);
                }
                PopupSugerenciasMedio.IsOpen = false;
            }
        }

        private void CargarEnFormulario(MedioPago mp)
        {
            _medioEditandoId = mp.Id;
            TxtNombreMedio.Text = mp.Nombre;
            TxtCodigoSunat.Text = mp.CodigoSunat ?? "";
            ChkMedioActivo.IsChecked = mp.EsActivo;

            foreach (ComboBoxItem item in CboTipoMedio.Items)
            {
                if (item.Content?.ToString() == mp.Tipo)
                {
                    CboTipoMedio.SelectedItem = item;
                    break;
                }
            }

            BtnGuardarMedio.Content = "✏️ Actualizar Canal";
        }

        private void LimpiarFormulario()
        {
            _medioEditandoId = null;
            TxtNombreMedio.Clear();
            TxtCodigoSunat.Clear();
            CboTipoMedio.SelectedIndex = 0;
            ChkMedioActivo.IsChecked = true;
            BtnGuardarMedio.Content = "💾 Guardar Canal";
        }

        private void BtnNuevoMedio_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
            TxtNombreMedio.Focus();
        }

        private void BtnLimpiarMedio_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }

        private void DgMediosPago_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgMediosPago.SelectedItem is MedioPago mp)
            {
                CargarEnFormulario(mp);
            }
        }

        private void DgMediosPago_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgMediosPago.SelectedItem is MedioPago mp)
            {
                CargarEnFormulario(mp);
            }
        }

        private async void BtnGuardarMedio_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombreMedio.Text.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                Growl.Warning("Ingrese el nombre del medio o canal de pago.");
                TxtNombreMedio.Focus();
                return;
            }

            try
            {
                var mp = new MedioPago
                {
                    Id = _medioEditandoId ?? 0,
                    Nombre = nombre.ToUpper(),
                    Tipo = (CboTipoMedio.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CONTADO",
                    CodigoSunat = TxtCodigoSunat.Text.Trim(),
                    EsActivo = ChkMedioActivo.IsChecked ?? true
                };

                await _medioPagoService.GuardarMedioPagoAsync(mp);
                Growl.Success("Medio de pago guardado.");
                LimpiarFormulario();
                await CargarMediosAsync();
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al guardar medio de pago: {ex.Message}");
            }
        }
    }
}