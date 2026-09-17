using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Sistemas;

namespace AplicativoDeAlmacen.Services.Sistemas
{
    public static class NetworkHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // Coloca aquí la IP pública fija de la oficina cuando la conozcas (o déjala vacía)
        private const string IP_PUBLICA_OFICINA = "190.235.0.0";

        public static async Task<TelemetriaEquipo> CapturarTelemetriaAsync()
        {
            var telemetria = new TelemetriaEquipo
            {
                NombrePc = Environment.MachineName,
                UsuarioWindows = Environment.UserName,
                IpLocal = ObtenerIpLocal()
            };

            telemetria.IpPublica = await ObtenerIpPublicaAsync();

            // Determinar si está en oficina o remoto
            if (!string.IsNullOrWhiteSpace(IP_PUBLICA_OFICINA) && telemetria.IpPublica == IP_PUBLICA_OFICINA)
            {
                telemetria.TipoRed = "OFICINA";
            }
            else if (telemetria.IpLocal.StartsWith("192.168.") || telemetria.IpLocal.StartsWith("10.") || telemetria.IpLocal.StartsWith("172."))
            {
                // En local o red LAN interna
                telemetria.TipoRed = "OFICINA";
            }
            else
            {
                telemetria.TipoRed = "REMOTO_EXTERNO";
            }

            return telemetria;
        }

        private static string ObtenerIpLocal()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static async Task<string> ObtenerIpPublicaAsync()
        {
            try
            {
                // Endpoint ultra rápido para resolver IP pública sin credenciales
                string ip = await _httpClient.GetStringAsync("https://api.ipify.org");
                return ip.Trim();
            }
            catch
            {
                return "No Disponible";
            }
        }
    }
}