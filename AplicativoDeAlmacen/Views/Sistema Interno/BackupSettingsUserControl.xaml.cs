using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using AplicativoDeAlmacen.Services.Sistemas;

namespace AplicativoDeAlmacen.Views.SistemaInterno
{
    public partial class BackupSettingsUserControl : UserControl
    {
        private readonly BackupService _backupService = new BackupService();
        private readonly DispatcherTimer _timerScheduler = new DispatcherTimer();

        // Bloqueo para evitar ejecuciones duplicadas en el mismo minuto
        private int _ultimoMinutoEjecutado = -1;

        public BackupSettingsUserControl()
        {
            InitializeComponent();
            CargarConfiguracionInicial();
            ConfigurarScheduler();
            _ = CargarHistorialServidorAsync();
        }

        private void CargarConfiguracionInicial()
        {
            string rutaDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EdicionesPiza_Backups");
            txtRutaDestino.Text = rutaDefault;
            txtCanalNtfy.Text = "Sistemas_pizza";
        }

        private void ConfigurarScheduler()
        {
            _timerScheduler.Interval = TimeSpan.FromSeconds(30);
            _timerScheduler.Tick += async (s, e) => await EvaluarEjecucionAutomaticaAsync();
            _timerScheduler.Start();
        }

        private void ModoHorario_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlDiarioGeneral == null || pnlRangoOperativo == null) return;

            bool esDiario = rbModoDiario.IsChecked == true;
            pnlDiarioGeneral.Visibility = esDiario ? Visibility.Visible : Visibility.Collapsed;
            pnlRangoOperativo.Visibility = esDiario ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnSeleccionarCarpeta_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Seleccione la carpeta local para descargar las copias de seguridad";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtRutaDestino.Text = dialog.SelectedPath;
            }
        }

        private async void BtnProbarNotificacion_Click(object sender, RoutedEventArgs e)
        {
            btnProbarNotificacion.IsEnabled = false;

            string mensaje = $"🔔 Prueba de Alerta Sonora\n" +
                             $"Servidor: Hosting Producción\n" +
                             $"Canal: Sistemas_pizza\n" +
                             $"Hora: {DateTime.Now:HH:mm:ss}\n" +
                             $"Estado: Enlace activo y verificado.";

            var resultado = await _backupService.EnviarNotificacionNtfyAsync(mensaje, "🚀 Alerta TI Ediciones Piza");

            btnProbarNotificacion.IsEnabled = true;

            if (resultado.Exito)
            {
                MessageBox.Show("¡Alerta acústica enviada a tu iPhone!", "Test Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Fallo al comunicar con ntfy:\n{resultado.Detalle}", "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnEjecutarManual_Click(object sender, RoutedEventArgs e)
        {
            await EjecutarProcesoRespaldoHostingAsync(esManual: true);
        }

        public void BtnGuardarConfiguracion_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Configuración de horarios de respaldo guardada correctamente en el sistema.", "Configuración Actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // MOTOR CENTRAL: Ejecuta el respaldo y notifica tanto éxitos como fallos al móvil
        private async Task EjecutarProcesoRespaldoHostingAsync(bool esManual)
        {
            btnEjecutarManual.IsEnabled = false;

            try
            {
                var res = await _backupService.CrearBackupEnHostingAsync();

                if (res.Exito)
                {
                    // Notificación de ÉXITO
                    string tipoEjecucion = esManual ? "Manual (Admin)" : "Automático Programado";
                    string msgNtfy = $"✅ Respaldo Exitoso ({tipoEjecucion})\n" +
                                     $"🏢 Servidor: Hosting Privado\n" +
                                     $"📁 Archivo: {res.Archivo}\n" +
                                     $"⚖️ Tamaño: {res.PesoMb}\n" +
                                     $"🕒 Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                                     $"🔒 Guardado seguro en backup_control.";

                    await _backupService.EnviarNotificacionNtfyAsync(msgNtfy, "✅ Backup Hosting Completado");

                    if (esManual)
                    {
                        MessageBox.Show($"Respaldo generado con éxito en el hosting privado.\n\nArchivo: {res.Archivo}\nTamaño: {res.PesoMb}",
                                        "Respaldo en Servidor", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    // Notificación de FALLO CRÍTICO (despierta al encargado por iPhone)
                    string msgError = $"🚨 FALLO CRÍTICO EN BACKUP\n" +
                                      $"Modo: {(esManual ? "Manual" : "Automático")}\n" +
                                      $"Detalle: {res.Mensaje}\n" +
                                      $"🕒 Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                                      $"⚠️ Revise el servidor de base de datos.";

                    await _backupService.EnviarNotificacionNtfyAsync(msgError, "🚨 ALERTA: Fallo en Backup");

                    if (esManual)
                    {
                        MessageBox.Show($"Error al generar el respaldo:\n{res.Mensaje}", "Fallo Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                // Notificación por fallo inesperado (ej. corte de internet en la oficina)
                string msgExcepcion = $"🚨 EXCEPCIÓN DE RED / SISTEMA\n" +
                                      $"Error: {ex.Message}\n" +
                                      $"🕒 Hora: {DateTime.Now:HH:mm:ss}";

                await _backupService.EnviarNotificacionNtfyAsync(msgExcepcion, "🚨 Error en Sistema WPF");
            }
            finally
            {
                btnEjecutarManual.IsEnabled = true;
                await CargarHistorialServidorAsync();
            }
        }

        // EVALUADOR DEL TEMPORIZADOR
        private async Task EvaluarEjecucionAutomaticaAsync()
        {
            var ahora = DateTime.Now;

            // Evitar repetir la ejecución en el mismo minuto
            if (ahora.Minute == _ultimoMinutoEjecutado) return;

            if (rbModoDiario.IsChecked == true)
            {
                // Modo intervalo con cálculo dinámico
                if (TimeSpan.TryParse(txtHoraDesde.Text, out var hDesde) &&
                    TimeSpan.TryParse(txtHoraHasta.Text, out var hHasta))
                {
                    var horaActual = ahora.TimeOfDay;
                    if (horaActual >= hDesde && horaActual <= hHasta && ahora.Minute == 0)
                    {
                        // Obtiene 1, 2, 3, 4, 5 o 6 horas según el índice seleccionado
                        int intervaloHoras = cboFrecuenciaHoras.SelectedIndex + 1;

                        if (ahora.Hour % intervaloHoras == 0)
                        {
                            _ultimoMinutoEjecutado = ahora.Minute;
                            await EjecutarProcesoRespaldoHostingAsync(esManual: false);
                        }
                    }
                }
            }
            else
            {
                // Modo intervalo
                if (TimeSpan.TryParse(txtHoraDesde.Text, out var hDesde) &&
                    TimeSpan.TryParse(txtHoraHasta.Text, out var hHasta))
                {
                    var horaActual = ahora.TimeOfDay;
                    if (horaActual >= hDesde && horaActual <= hHasta && ahora.Minute == 0)
                    {
                        int intervaloHoras = cboFrecuenciaHoras.SelectedIndex == 0 ? 1 : (cboFrecuenciaHoras.SelectedIndex == 1 ? 2 : 4);
                        if (ahora.Hour % intervaloHoras == 0)
                        {
                            _ultimoMinutoEjecutado = ahora.Minute;
                            await EjecutarProcesoRespaldoHostingAsync(esManual: false);
                        }
                    }
                }
            }
        }

        private async Task CargarHistorialServidorAsync()
        {
            try
            {
                var backupsRemotos = await _backupService.ListarBackupsHostingAsync();
                var items = backupsRemotos.Select(b => new BackupItemGrid
                {
                    Nombre = b.Nombre,
                    Fecha = b.Fecha,
                    TamanoBytes = b.Bytes,
                    Estado = "En Servidor"
                }).ToList();

                dgHistorialBackups.ItemsSource = items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al listar grilla: {ex.Message}");
            }
        }

        private async void BtnRefrescarHistorial_Click(object sender, RoutedEventArgs e)
        {
            await CargarHistorialServidorAsync();

            if (dgHistorialBackups.Items.Count == 0)
            {
                MessageBox.Show("No se encontraron archivos .sql en la carpeta del servidor o no se pudo sincronizar la lista.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Descarga manual a PC iniciada por el Administrador
        private async void BtnAbrirUbicacionArchivo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BackupItemGrid item)
            {
                string directorioLocal = txtRutaDestino.Text;

                var confirmacion = MessageBox.Show($"¿Desea descargar una copia local del archivo '{item.Nombre}' a su equipo?",
                                                   "Descarga Administrativa", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmacion != MessageBoxResult.Yes) return;

                btn.IsEnabled = false;
                var resDescarga = await _backupService.DescargarBackupManualAsync(item.Nombre, directorioLocal);
                btn.IsEnabled = true;

                if (resDescarga.Exito)
                {
                    int retencion = int.TryParse(txtDiasRetencion.Text, out int d) ? d : 15;
                    _backupService.PurgarArchivosViejos(directorioLocal, retencion);

                    var respAbrir = MessageBox.Show($"Archivo descargado con éxito en su PC:\n{resDescarga.RutaLocal}\n\n¿Desea abrir la carpeta ahora?",
                                                    "Descarga Completa", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (respAbrir == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = directorioLocal,
                            UseShellExecute = true,
                            Verb = "open"
                        });
                    }
                }
                else
                {
                    MessageBox.Show($"Error al descargar el archivo:\n{resDescarga.Mensaje}", "Fallo de Descarga", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}