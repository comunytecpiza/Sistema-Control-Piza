using AplicativoDeAlmacen.Models.Documentos;
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

namespace AplicativoDeAlmacen.Views.Empresas
{
    public partial class EmpresasSeriesUserControl : UserControl
    {
        private readonly EmpresaService _empresaService = new EmpresaService();

        private List<Empresa> _todasLasEmpresas = new List<Empresa>();
        private List<SerieDocumento> _todasLasSeries = new List<SerieDocumento>();
        private List<Moneda> _todasLasMonedas = new List<Moneda>();

        private int? _empresaEditandoId = null;
        private int? _monedaEditandoId = null;
        private SerieDocumento? _serieSeleccionada = null;

        private readonly DispatcherTimer _timerBuscarEmpresa;
        private readonly DispatcherTimer _timerBuscarSerie;

        public EmpresasSeriesUserControl()
        {
            InitializeComponent();

            _timerBuscarEmpresa = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerBuscarEmpresa.Tick += (s, e) =>
            {
                _timerBuscarEmpresa.Stop();
                FiltrarEmpresasEnGrilla();
            };

            _timerBuscarSerie = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timerBuscarSerie.Tick += (s, e) =>
            {
                _timerBuscarSerie.Stop();
                FiltrarSeriesEnGrilla();
            };

            Loaded += async (s, e) => await CargarDatosAsync();
        }

        private async Task CargarDatosAsync()
        {
            try
            {
                Cursor = Cursors.Wait;
                _todasLasEmpresas = await _empresaService.ObtenerEmpresasAsync(soloActivas: false);
                _todasLasSeries = await _empresaService.ObtenerSeriesConEmpresaAsync();
                _todasLasMonedas = await _empresaService.ObtenerMonedasAsync(soloActivas: false);

                DgEmpresas.ItemsSource = _todasLasEmpresas;
                LblTotalEmpresas.Text = $"Total empresas: {_todasLasEmpresas.Count}";

                DgSeriesDocumentos.ItemsSource = _todasLasSeries;

                CboEmpresasParaSerie.ItemsSource = _todasLasEmpresas.Where(x => x.EsActivo).ToList();

                DgMonedas.ItemsSource = _todasLasMonedas;
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al cargar catálogos: {ex.Message}");
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void BtnRefrescarTodo_Click(object sender, RoutedEventArgs e)
        {
            await CargarDatosAsync();
            Growl.Success("Catálogos sincronizados.");
        }

        // =========================================================================
        // PESTAÑA 1: EMPRESAS
        // =========================================================================
        private void TxtBuscarEmpresa_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timerBuscarEmpresa.Stop();
            _timerBuscarEmpresa.Start();
        }

        private void FiltrarEmpresasEnGrilla()
        {
            string filtro = TxtBuscarEmpresa.Text.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(filtro))
            {
                DgEmpresas.ItemsSource = _todasLasEmpresas;
                PopupSugerenciasEmpresa.IsOpen = false;
            }
            else
            {
                var filtradas = _todasLasEmpresas.Where(x =>
                    x.Ruc.Contains(filtro) ||
                    x.RazonSocial.ToLower().Contains(filtro) ||
                    (x.NombreComercial != null && x.NombreComercial.ToLower().Contains(filtro))
                ).ToList();

                DgEmpresas.ItemsSource = filtradas;
                LstSugerenciasEmpresa.ItemsSource = filtradas.Take(6).Select(x => $"{x.Ruc} - {x.RazonSocial}").ToList();
                PopupSugerenciasEmpresa.IsOpen = filtradas.Any() && !string.IsNullOrWhiteSpace(TxtBuscarEmpresa.Text);
            }
            LblTotalEmpresas.Text = $"Total empresas: {DgEmpresas.Items.Count}";
        }

        private void LstSugerenciasEmpresa_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSugerenciasEmpresa.SelectedItem is string seleccion)
            {
                string ruc = seleccion.Split('-')[0].Trim();
                var emp = _todasLasEmpresas.FirstOrDefault(x => x.Ruc == ruc);
                if (emp != null)
                {
                    DgEmpresas.SelectedItem = emp;
                    CargarEmpresaEnFormulario(emp);
                }
                PopupSugerenciasEmpresa.IsOpen = false;
            }
        }

        private void BtnNuevaEmpresa_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormularioEmpresa();
            TxtRuc.Focus();
        }

        private void LimpiarFormularioEmpresa()
        {
            _empresaEditandoId = null;
            TxtRuc.Clear();
            TxtRazonSocial.Clear();
            TxtNombreComercial.Clear();
            TxtDireccionFiscal.Clear();
            ChkEmpresaActiva.IsChecked = true;
            BtnGuardarEmpresa.Content = "💾 Guardar Empresa";
        }

        private void CargarEmpresaEnFormulario(Empresa emp)
        {
            _empresaEditandoId = emp.Id;
            TxtRuc.Text = emp.Ruc;
            TxtRazonSocial.Text = emp.RazonSocial;
            TxtNombreComercial.Text = emp.NombreComercial ?? "";
            TxtDireccionFiscal.Text = emp.Direccion ?? "";
            ChkEmpresaActiva.IsChecked = emp.EsActivo;
            BtnGuardarEmpresa.Content = "✏️ Actualizar Empresa";
        }

        private void DgEmpresas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgEmpresas.SelectedItem is Empresa emp)
            {
                CargarEmpresaEnFormulario(emp);
            }
        }

        private void DgEmpresas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgEmpresas.SelectedItem is Empresa emp)
            {
                CargarEmpresaEnFormulario(emp);
            }
        }

        private void BtnEditarEmpresaFila_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Empresa emp)
            {
                CargarEmpresaEnFormulario(emp);
            }
        }

        private void BtnCancelarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormularioEmpresa();
        }

        private async void BtnGuardarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            string ruc = TxtRuc.Text.Trim();
            string razon = TxtRazonSocial.Text.Trim();

            if (ruc.Length != 11 || !ruc.All(char.IsDigit))
            {
                Growl.Warning("El RUC debe tener exactamente 11 dígitos numéricos.");
                TxtRuc.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(razon))
            {
                Growl.Warning("Ingrese la Razón Social de la empresa.");
                TxtRazonSocial.Focus();
                return;
            }

            try
            {
                var emp = new Empresa
                {
                    Id = _empresaEditandoId ?? 0,
                    Ruc = ruc,
                    RazonSocial = razon,
                    NombreComercial = TxtNombreComercial.Text.Trim(),
                    Direccion = TxtDireccionFiscal.Text.Trim(),
                    EsActivo = ChkEmpresaActiva.IsChecked ?? true
                };

                await _empresaService.GuardarEmpresaAsync(emp);
                Growl.Success("Empresa guardada con éxito.");
                LimpiarFormularioEmpresa();
                await CargarDatosAsync();
            }
            catch (Exception ex)
            {
                Growl.Error($"No se pudo guardar la empresa: {ex.Message}");
            }
        }

        // =========================================================================
        // PESTAÑA 2: SERIES POR EMPRESA
        // =========================================================================
        private void TxtBuscarSerie_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timerBuscarSerie.Stop();
            _timerBuscarSerie.Start();
        }

        private void FiltrarSeriesEnGrilla()
        {
            string f = TxtBuscarSerie.Text.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(f))
            {
                DgSeriesDocumentos.ItemsSource = _todasLasSeries;
            }
            else
            {
                DgSeriesDocumentos.ItemsSource = _todasLasSeries.Where(x =>
                    x.NumeroSerie.ToLower().Contains(f) ||
                    (x.Empresa != null && x.Empresa.RazonSocial.ToLower().Contains(f))
                ).ToList();
            }
        }

        private void DgSeriesDocumentos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgSeriesDocumentos.SelectedItem is SerieDocumento serie)
            {
                _serieSeleccionada = serie;
                LblSerieSeleccionadaInfo.Text = $"Serie: {serie.NumeroSerie} (Tipo: {serie.TipoSerie})";
                if (serie.EmpresaId.HasValue)
                {
                    CboEmpresasParaSerie.SelectedValue = serie.EmpresaId.Value;
                }
                else
                {
                    CboEmpresasParaSerie.SelectedIndex = -1;
                }
            }
        }

        private async void BtnAsignarEmpresaSerie_Click(object sender, RoutedEventArgs e)
        {
            if (_serieSeleccionada == null)
            {
                Growl.Warning("Seleccione primero una Serie de la lista.");
                return;
            }

            int? empresaId = CboEmpresasParaSerie.SelectedValue != null ? Convert.ToInt32(CboEmpresasParaSerie.SelectedValue) : (int?)null;

            try
            {
                await _empresaService.AsignarEmpresaASerieAsync(_serieSeleccionada.Id, empresaId);
                Growl.Success($"Serie {_serieSeleccionada.NumeroSerie} asignada con éxito.");
                await CargarDatosAsync();
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al asignar empresa: {ex.Message}");
            }
        }

        // =========================================================================
        // PESTAÑA 3: MONEDAS
        // =========================================================================
        private void DgMonedas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgMonedas.SelectedItem is Moneda m)
            {
                _monedaEditandoId = m.Id;
                TxtCodigoMoneda.Text = m.CodigoSunat;
                TxtDescripcionMoneda.Text = m.Descripcion;
                TxtSimboloMoneda.Text = m.Simbolo;
                ChkMonedaActiva.IsChecked = m.EsActivo;
                BtnGuardarMoneda.Content = "✏️ Actualizar Moneda";
            }
        }

        private void BtnLimpiarMoneda_Click(object sender, RoutedEventArgs e)
        {
            _monedaEditandoId = null;
            TxtCodigoMoneda.Clear();
            TxtDescripcionMoneda.Clear();
            TxtSimboloMoneda.Clear();
            ChkMonedaActiva.IsChecked = true;
            BtnGuardarMoneda.Content = "💾 Guardar Moneda";
        }

        private async void BtnGuardarMoneda_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtCodigoMoneda.Text) || string.IsNullOrWhiteSpace(TxtDescripcionMoneda.Text))
            {
                Growl.Warning("Complete el código ISO y la descripción de la moneda.");
                return;
            }

            try
            {
                var mon = new Moneda
                {
                    Id = _monedaEditandoId ?? 0,
                    CodigoSunat = TxtCodigoMoneda.Text.Trim().ToUpper(),
                    Descripcion = TxtDescripcionMoneda.Text.Trim().ToUpper(),
                    Simbolo = string.IsNullOrWhiteSpace(TxtSimboloMoneda.Text) ? "S/" : TxtSimboloMoneda.Text.Trim(),
                    EsActivo = ChkMonedaActiva.IsChecked ?? true
                };

                await _empresaService.GuardarMonedaAsync(mon);
                Growl.Success("Moneda registrada.");
                BtnLimpiarMoneda_Click(null, null);
                await CargarDatosAsync();
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al guardar moneda: {ex.Message}");
            }
        }
    }
}