using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AplicativoDeAlmacen.Views.Movimientos.Lectora
{
    public class ItemLogLectora
    {
        public string Hora { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty; // GUÍA o VENTA
        public bool EsAceptado { get; set; }
        public string Detalle { get; set; } = string.Empty;

        public string TextoCompleto => $"{Hora}   {(EsAceptado ? "✔" : "✖")} [{Categoria}] {Codigo} - {Detalle}";
        public Brush ColorBrush => EsAceptado
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
    }

    public partial class LectorGlobalWindow : Window
    {
        private readonly LectoraGlobalService _service;
        private readonly Func<LectoraResultDTO, Task<(bool Exito, string Mensaje)>> _callback;

        private bool _leyendo;
        private readonly List<ItemLogLectora> _historialCompleto = new List<ItemLogLectora>();
        private readonly Dictionary<string, int> _contadores = new Dictionary<string, int>();

        private int _totalGeneral = 0;
        private int _totalAceptados = 0;
        private int _totalRechazados = 0;
        private int _totalVenta = 0;
        private int _totalGuia = 0;

        public LectorGlobalWindow(Func<LectoraResultDTO, Task<(bool Exito, string Mensaje)>> callback)
        {
            InitializeComponent();
            _service = new LectoraGlobalService();
            _callback = callback;
            Loaded += (s, e) => TxtLector.Focus();
        }

        private void TxtLector_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtLector.SelectAll();
        }

        private async void TxtLector_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_leyendo) return;

            string codigo = TxtLector.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo)) return;

            _leyendo = true;
            TxtLector.IsEnabled = false;

            string hora = DateTime.Now.ToString("HH:mm:ss");

            try
            {
                var resultado = await _service.ObtenerCodigoAsync(codigo);

                string norm = resultado.CodigoCompleto.ToUpperInvariant();
                string categoria = (norm.Contains("-V-") || norm.Contains("VENTA") || resultado.CategoriaProductoId == 2) ? "VENTA" : "GUÍA";

                var (exito, mensaje) = await _callback(resultado);

                _totalGeneral++;

                if (exito)
                {
                    _totalAceptados++;
                    if (categoria == "VENTA") _totalVenta++; else _totalGuia++;

                    string prodNombre = resultado.DescripcionProducto ?? "Desconocido";
                    if (_contadores.ContainsKey(prodNombre)) _contadores[prodNombre]++;
                    else _contadores[prodNombre] = 1;

                    _historialCompleto.Insert(0, new ItemLogLectora
                    {
                        Hora = hora,
                        Codigo = resultado.CodigoCompleto,
                        Producto = prodNombre,
                        Categoria = categoria,
                        EsAceptado = true,
                        Detalle = "Válido y agregado"
                    });

                    // 🔊 Sonido Agudo de Éxito
                    Task.Run(() => Console.Beep(2400, 100));
                }
                else
                {
                    _totalRechazados++;

                    _historialCompleto.Insert(0, new ItemLogLectora
                    {
                        Hora = hora,
                        Codigo = resultado.CodigoCompleto,
                        Producto = resultado.DescripcionProducto ?? "-",
                        Categoria = categoria,
                        EsAceptado = false,
                        Detalle = mensaje
                    });

                    // 🔊 Doble Tono Grave de Rechazo
                    Task.Run(() =>
                    {
                        Console.Beep(900, 150);
                        Console.Beep(500, 250);
                    });
                }

                ActualizarUI();
            }
            catch (Exception ex)
            {
                _totalGeneral++;
                _totalRechazados++;

                _historialCompleto.Insert(0, new ItemLogLectora
                {
                    Hora = hora,
                    Codigo = codigo,
                    Producto = "-",
                    Categoria = "DESCONOCIDO",
                    EsAceptado = false,
                    Detalle = ex.Message
                });

                Task.Run(() =>
                {
                    Console.Beep(900, 150);
                    Console.Beep(500, 250);
                });

                ActualizarUI();
            }
            finally
            {
                TxtLector.Text = "";
                TxtLector.IsEnabled = true;
                TxtLector.Focus();
                _leyendo = false;
            }
        }

        private void ActualizarUI()
        {
            // 🛡️ Evitar NullReferenceException durante el InitializeComponent
            if (TxtMetricTotal == null || LblStats == null) return;

            TxtMetricTotal.Text = _totalGeneral.ToString();
            TxtMetricAceptados.Text = _totalAceptados.ToString();
            TxtMetricRechazados.Text = _totalRechazados.ToString();
            TxtMetricVenta.Text = _totalVenta.ToString();
            TxtMetricGuia.Text = _totalGuia.ToString();

            string resumen = $"TOTAL ACEPTADOS: {_totalAceptados:N0}\n";
            resumen += $"• Venta: {_totalVenta} | Guía: {_totalGuia}\n--------------------------------------\n\n";
            foreach (var kvp in _contadores)
            {
                resumen += $"● {kvp.Key}\n   └ Cantidad: {kvp.Value} unidades\n\n";
            }
            LblStats.Text = resumen;

            RefrescarListaLog();
        }

        private void RefrescarListaLog()
        {
            // 🛡️ Candado contra ejecución temprana durante InitializeComponent()
            if (LstLog == null || _historialCompleto == null) return;

            if (RbFiltroBuenos != null && RbFiltroBuenos.IsChecked == true)
                LstLog.ItemsSource = _historialCompleto.Where(x => x.EsAceptado).ToList();
            else if (RbFiltroMalos != null && RbFiltroMalos.IsChecked == true)
                LstLog.ItemsSource = _historialCompleto.Where(x => !x.EsAceptado).ToList();
            else
                LstLog.ItemsSource = _historialCompleto.ToList();
        }

        private void FiltroLog_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return; // 👈 Evita que se ejecute antes de que la ventana esté 100% cargada
            RefrescarListaLog();
        }

        private void BtnExportarModal_Click(object sender, RoutedEventArgs e)
        {
            var modal = new ModalExportarLectora(_historialCompleto) { Owner = this };
            modal.ShowDialog();
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}