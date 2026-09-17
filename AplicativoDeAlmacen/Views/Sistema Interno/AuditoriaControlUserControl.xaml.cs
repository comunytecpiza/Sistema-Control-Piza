using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Sistemas;
using AplicativoDeAlmacen.Services.Sistemas;

namespace AplicativoDeAlmacen.Views.SistemaInterno
{
    public partial class AuditoriaControlUserControl : UserControl
    {
        private readonly AuditoriaService _auditoriaService = new AuditoriaService();
        private readonly DispatcherTimer _autoRefreshTimer = new DispatcherTimer();

        public AuditoriaControlUserControl()
        {
            InitializeComponent();
            _ = CargarDatosInicialesAsync();

            // Refresco automático de sesiones vivas cada 30 segundos
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(30);
            _autoRefreshTimer.Tick += async (s, e) => await CargarSesionesEnVivoAsync();
            _autoRefreshTimer.Start();
        }

        private async Task CargarDatosInicialesAsync()
        {
            await Task.WhenAll(
                CargarSesionesEnVivoAsync(),
                CargarSedesIpsAsync(),
                CargarBitacoraHistoricaAsync()
            );
        }

        private async void BtnRefrescarTodo_Click(object sender, RoutedEventArgs e)
        {
            await CargarDatosInicialesAsync();
        }

        // ==============================================================
        // 1. SESIONES ACTIVAS & EXPULSIÓN
        // ==============================================================
        private async Task CargarSesionesEnVivoAsync()
        {
            try
            {
                var sesiones = await _auditoriaService.ObtenerSesionesActivasEnVivoAsync();
                dgSesionesActivas.ItemsSource = sesiones;
                lblTotalSesiones.Text = sesiones.Count.ToString();
            }
            catch { }
        }

        private async void BtnExpulsarSesion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SesionActivaModel sesion)
            {
                // Validación preventiva antes de preguntar
                if (sesion.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("El Administrador General no puede ser expulsado.", "Seguridad TI", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                if (sesion.TokenSesion == SesionSistema.TokenSesionActual)
                {
                    MessageBox.Show("Esta es su sesión actual. Si desea salir, use el botón Salir del menú.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirmacion = MessageBox.Show(
                    $"¿Desea cerrar la sesión de '{sesion.Username}' en el equipo '{sesion.NombrePc}'?",
                    "Confirmar Expulsión", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirmacion != MessageBoxResult.Yes) return;

                btn.IsEnabled = false;
                var resultado = await _auditoriaService.ForzarCierreSesionRemotoAsync(
                    sesion.TokenSesion,
                    SesionSistema.TokenSesionActual,
                    sesion.Username);
                btn.IsEnabled = true;

                if (resultado.Exito)
                {
                    MessageBox.Show(resultado.Mensaje, "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    await CargarSesionesEnVivoAsync();
                }
                else
                {
                    MessageBox.Show(resultado.Mensaje, "Restricción de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ==============================================================
        // 2. GESTIÓN DE SEDES E IPS
        // ==============================================================
        private async Task CargarSedesIpsAsync()
        {
            try
            {
                var sedes = await _auditoriaService.ObtenerSedesIpsAsync();
                dgSedesIps.ItemsSource = sedes;
                lblTotalSedes.Text = sedes.Count.ToString();
            }
            catch { }
        }

        private async void BtnDetectarMiIp_Click(object sender, RoutedEventArgs e)
        {
            btnDetectarMiIp.IsEnabled = false;
            var tel = await NetworkHelper.CapturarTelemetriaAsync();
            btnDetectarMiIp.IsEnabled = true;

            if (!string.IsNullOrWhiteSpace(tel.IpPublica) && tel.IpPublica != "No Disponible")
            {
                txtIpPublicaSede.Text = tel.IpPublica;
                if (string.IsNullOrWhiteSpace(txtNombreSede.Text))
                {
                    txtNombreSede.Text = "Sede Principal";
                }
            }
            else
            {
                MessageBox.Show("No se pudo resolver la IP pública actual. Compruebe la conexión a internet.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnGuardarSede_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNombreSede.Text.Trim();
            string ip = txtIpPublicaSede.Text.Trim();
            string desc = txtDescripcionSede.Text.Trim();

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Ingrese el nombre de la sede y su IP pública.", "Campos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnGuardarSede.IsEnabled = false;
            bool guardado = await _auditoriaService.GuardarSedeIpAsync(nombre, ip, desc);
            btnGuardarSede.IsEnabled = true;

            if (guardado)
            {
                txtNombreSede.Clear();
                txtIpPublicaSede.Clear();
                txtDescripcionSede.Clear();
                await CargarSedesIpsAsync();
                MessageBox.Show("Sede guardada en la lista blanca con éxito.", "Sede Registrada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Error al registrar la sede.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnEliminarSede_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SedeIpModel sede)
            {
                if (MessageBox.Show($"¿Desea eliminar la sede '{sede.NombreSede}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var res = await _auditoriaService.EliminarSedeIpAsync(sede.Id);
                    if (res.Exito)
                    {
                        await CargarSedesIpsAsync();
                    }
                    else
                    {
                        MessageBox.Show(res.Mensaje, "Acción Denegada", MessageBoxButton.OK, MessageBoxImage.Stop);
                    }
                }
            }
        }

        // ==============================================================
        // 3. BITÁCORA HISTÓRICA & DESBLOQUEO MANUAL
        // ==============================================================
        private async Task CargarBitacoraHistoricaAsync()
        {
            try
            {
                // Consultar los últimos 100 eventos de acceso
                var historial = await _auditoriaService.ObtenerHistorialAccesosAsync();
                dgAuditoriaHistorial.ItemsSource = historial;

                // Contar bloqueos activos
                int bloqueos = await _auditoriaService.ContarBloqueosActivosAsync();
                lblTotalBloqueos.Text = bloqueos.ToString();
            }
            catch { }
        }

        private async void BtnLimpiarBloqueos_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Desea desbloquear todos los usuarios y equipos penalizados por 3 intentos fallidos?",
                                "Desbloqueo de Emergencia", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _auditoriaService.LimpiarTodosLosBloqueosAsync();
                await CargarBitacoraHistoricaAsync();
                MessageBox.Show("Todos los bloqueos por intentos fallidos han sido eliminados.", "Bloqueos Restablecidos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Ubicación: Al final de AuditoriaControlUserControl.xaml.cs
        private async void BtnExportarTxt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logs = await _auditoriaService.ObtenerHistorialAccesosAsync(500);

                if (!logs.Any())
                {
                    MessageBox.Show("No hay registros de auditoría para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Auditoria_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    DefaultExt = ".txt",
                    Filter = "Archivos de texto (*.txt)|*.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    using var writer = new System.IO.StreamWriter(saveDialog.FileName, false, System.Text.Encoding.UTF8);
                    writer.WriteLine("==========================================================================================================");
                    writer.WriteLine($"REPORTE DE AUDITORÍA Y TELEMETRÍA TI - EDICIONES PIZA | Generado: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                    writer.WriteLine("==========================================================================================================");
                    writer.WriteLine(string.Format("{0,-20} | {1,-12} | {2,-18} | {3,-15} | {4,-15} | {5}",
                        "FECHA/HORA", "USUARIO", "EVENTO", "SEDE", "IP LOCAL", "DETALLES"));
                    writer.WriteLine(new string('-', 106));

                    foreach (var item in logs)
                    {
                        writer.WriteLine(string.Format("{0,-20} | {1,-12} | {2,-18} | {3,-15} | {4,-15} | {5}",
                            item.FechaRegistro.ToString("dd/MM/yyyy HH:mm:ss"),
                            item.Username,
                            item.Evento,
                            item.TipoRed,
                            item.IpLocal,
                            item.Detalles));
                    }

                    writer.WriteLine("==========================================================================================================");
                    MessageBox.Show("Archivo de auditoría exportado exitosamente.", "Exportación Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar los logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    } 
}
    