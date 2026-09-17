using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services.Sistemas
{
    public class BackupService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string BASE_URL = "https://edicionespiza.pe/api_backup.php";
        private const string TOKEN_SECRETO = "PizaSecureBackup2026_xK9#";

        // MODO 1: Solo genera el archivo en la carpeta privada del hosting (NO lo descarga a la PC)
        public async Task<(bool Exito, string Archivo, string PesoMb, string Mensaje)> CrearBackupEnHostingAsync()
        {
            try
            {
                string url = $"{BASE_URL}?accion=crear";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Backup-Token", TOKEN_SECRETO);
                request.Headers.Add("User-Agent", "EdicionesPiza-WPF-Client/1.0");

                var response = await _httpClient.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.GetProperty("exito").GetBoolean())
                    {
                        string archivo = root.GetProperty("archivo").GetString() ?? "";
                        long bytes = root.GetProperty("bytes").GetInt64();
                        string pesoMb = $"{bytes / (1024.0 * 1024.0):F2} MB";

                        return (true, archivo, pesoMb, "OK");
                    }
                    else
                    {
                        string detalle = root.TryGetProperty("detalle", out var d) ? d.GetString() ?? "" : "";
                        return (false, "", "", $"Error en hosting: {detalle}");
                    }
                }

                return (false, "", "", $"HTTP {(int)response.StatusCode}: {json}");
            }
            catch (Exception ex)
            {
                return (false, "", "", ex.Message);
            }
        }

        // MODO 2: Consulta la lista de backups que existen en la carpeta privada del servidor
        public async Task<List<BackupRemotoItem>> ListarBackupsHostingAsync()
        {
            var lista = new List<BackupRemotoItem>();
            try
            {
                string url = $"{BASE_URL}?accion=listar";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Backup-Token", TOKEN_SECRETO);
                request.Headers.Add("User-Agent", "EdicionesPiza-WPF-Client/1.0");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.GetProperty("exito").GetBoolean() && root.TryGetProperty("archivos", out var arrArchivos))
                    {
                        foreach (var item in arrArchivos.EnumerateArray())
                        {
                            lista.Add(new BackupRemotoItem
                            {
                                Nombre = item.GetProperty("nombre").GetString() ?? "",
                                Bytes = item.GetProperty("bytes").GetInt64(),
                                Fecha = DateTime.TryParse(item.GetProperty("fecha").GetString(), out var f) ? f : DateTime.Now
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al listar backups del hosting: {ex.Message}");
            }
            return lista;
        }

        // MODO 3: Descarga manual de un archivo puntual del hosting hacia la PC del Administrador
        public async Task<(bool Exito, string Mensaje, string RutaLocal)> DescargarBackupManualAsync(string nombreArchivo, string directorioLocal)
        {
            try
            {
                if (!Directory.Exists(directorioLocal))
                    Directory.CreateDirectory(directorioLocal);

                string rutaLocal = Path.Combine(directorioLocal, nombreArchivo);
                string url = $"{BASE_URL}?accion=descargar&archivo={Uri.EscapeDataString(nombreArchivo)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Backup-Token", TOKEN_SECRETO);
                request.Headers.Add("User-Agent", "EdicionesPiza-WPF-Client/1.0");

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    return (false, $"Fallo de descarga ({(int)response.StatusCode}): {err}", string.Empty);
                }

                using (var streamRemoto = await response.Content.ReadAsStreamAsync())
                using (var fs = new FileStream(rutaLocal, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await streamRemoto.CopyToAsync(fs);
                }

                return (true, "Descargado con éxito en la PC", rutaLocal);
            }
            catch (Exception ex)
            {
                return (false, $"Error durante la transferencia: {ex.Message}", string.Empty);
            }
        }

        // Purga local de archivos con más de X días de retención en la PC
        public void PurgarArchivosViejos(string directorio, int diasRetencion)
        {
            try
            {
                if (!Directory.Exists(directorio)) return;

                var dir = new DirectoryInfo(directorio);
                var limite = DateTime.Now.AddDays(-diasRetencion);

                foreach (var file in dir.GetFiles())
                {
                    if ((file.Extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)) &&
                        file.CreationTime < limite)
                    {
                        file.Delete();
                        Debug.WriteLine($"Archivo purgado: {file.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error durante purga local: {ex.Message}");
            }
        }

        // Enviar notificaciones push directas con alarma auditiva a tu iPhone
        public async Task<(bool Exito, string Detalle)> EnviarNotificacionNtfyAsync(string mensaje, string titulo = "Ediciones Piza - Sistemas")
        {
            try
            {
                string url = "https://ntfy.sh/Sistemas_pizza";

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(mensaje, System.Text.Encoding.UTF8, "text/plain")
                };

                request.Headers.Add("User-Agent", "EdicionesPiza-WPF-Client/1.0");

                // Limpiar caracteres no ASCII del encabezado Title para evitar la excepción
                string tituloAscii = System.Text.RegularExpressions.Regex.Replace(titulo, @"[^\u0000-\u007F]+", "").Trim();
                if (string.IsNullOrWhiteSpace(tituloAscii)) tituloAscii = "Ediciones Piza - Sistemas";

                request.Headers.Add("Title", tituloAscii);
                request.Headers.Add("Priority", "5");
                request.Headers.Add("X-Sound", "alarm");
                request.Headers.Add("Tags", "package,floppy_disk");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "OK");
                }
                else
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    return (false, $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errBody}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }

    public class BackupRemotoItem
    {
        public string Nombre { get; set; } = string.Empty;
        public long Bytes { get; set; }
        public DateTime Fecha { get; set; }
        public string TamanoFormateado => $"{Bytes / (1024.0 * 1024.0):F2} MB";
    }

    public class BackupItemGrid
    {
        public string Nombre { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public long TamanoBytes { get; set; }
        public string TamanoFormateado => $"{TamanoBytes / (1024.0 * 1024.0):F2} MB";
        public string Estado { get; set; } = "En Servidor";
    }
}