using AutoUpdaterDotNET;
using System;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;

namespace AplicativoDeAlmacen
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🌟 0. PROTOCOLOS DE SEGURIDAD HTTPS / TLS PARA TODAS LAS PCS Y LAPTOPS
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls13;

            // 🌟 1. IDIOMA Y CULTURA
            var culturaEspanol = new CultureInfo("es-PE");
            Thread.CurrentThread.CurrentCulture = culturaEspanol;
            Thread.CurrentThread.CurrentUICulture = culturaEspanol;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culturaEspanol.IetfLanguageTag))
            );

            // 🌟 2. CONFIGURACIÓN DE ACTUALIZACIONES ESTRICTAS
            AutoUpdater.RunUpdateAsAdmin = false;

            // Descarga en la carpeta temporal de Windows (evita bloqueos de permisos en Documentos/Program Files)
            AutoUpdater.DownloadPath = Path.GetTempPath();

            // Ocultar botones de aplazar y omitir
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.ShowSkipButton = false;

            // Modo obligatorio
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.Forced;

            // 🌟 3. AUTENTICACIÓN
            AutoUpdater.BasicAuthXML = new BasicAuthentication("Piz@2027", "2027zapi");
            AutoUpdater.BasicAuthDownload = new BasicAuthentication("Piz@2027", "2027zapi");

            // URL del XML
            AutoUpdater.Start("https://edicionespiza.pe/actualizaciones_sistema/update.xml");
        }
    }
}