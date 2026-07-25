using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Movimientos.Lectora
{
    public partial class LectorGlobalWindow : Window
    {
        private readonly LectoraGlobalService _service;

        // 🌟 CAMBIO CLAVE: Ahora espera un Task<bool> para saber si fue repetido
        private readonly Func<LectoraResultDTO, Task<bool>> _callback;

        private bool _leyendo;

        // 🌟 VARIABLES PARA EL DASHBOARD
        private Dictionary<string, int> _contadores = new Dictionary<string, int>();
        private int _totalGeneral = 0;

        public LectorGlobalWindow(Func<LectoraResultDTO, Task<bool>> callback)
        {
            InitializeComponent();
            _service = new LectoraGlobalService();
            _callback = callback;
            Loaded += (s, e) => TxtLector.Focus();
        }

        private async void TxtLector_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_leyendo) return;

            string codigo = TxtLector.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo)) return;

            _leyendo = true;
            TxtLector.IsEnabled = false;

            try
            {
                var resultado = await _service.ObtenerCodigoAsync(codigo);

                // 🌟 Le preguntamos a tu vista principal si el código es válido/nuevo
                bool fueAgregado = await _callback(resultado);

                if (fueAgregado)
                {
                    AgregarLog("✔ " + resultado.CodigoCompleto);

                    // Actualizamos Contadores (Usamos la descripción del producto)
                    string nombreProducto = resultado.DescripcionProducto ?? "Desconocido";

                    if (_contadores.ContainsKey(nombreProducto))
                        _contadores[nombreProducto]++;
                    else
                        _contadores[nombreProducto] = 1;

                    _totalGeneral++;
                    ActualizarStats();
                }
                // Si fueAgregado es False (es repetido), simplemente lo ignora en silencio.
            }
            catch (Exception ex)
            {
                AgregarLog("✖ " + ex.Message);
                System.Media.SystemSounds.Beep.Play();
            }

            TxtLector.Text = "";
            TxtLector.IsEnabled = true;
            TxtLector.Focus();
            _leyendo = false;
        }

        private void ActualizarStats()
        {
            string resumen = $"TOTAL GENERAL: {_totalGeneral}\n--------------------------------------\n\n";
            foreach (var kvp in _contadores)
            {
                resumen += $"● {kvp.Key}: {kvp.Value}\n";
            }
            LblStats.Text = resumen;
        }

        private void AgregarLog(string texto)
        {
            LstLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}   {texto}");
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}