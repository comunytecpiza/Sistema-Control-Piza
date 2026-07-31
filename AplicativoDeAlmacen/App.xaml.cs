using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;

namespace AplicativoDeAlmacen
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🌟 Forzar idioma español (Perú / España) para toda la aplicación (incluyendo calendarios y formatos de fecha)
            var culturaEspanol = new CultureInfo("es-PE"); // O "es-ES"
            Thread.CurrentThread.CurrentCulture = culturaEspanol;
            Thread.CurrentThread.CurrentUICulture = culturaEspanol;

            // Asegurar compatibilidad global con WPF
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culturaEspanol.IetfLanguageTag))
            );
        }
    }

}
