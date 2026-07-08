using System.Windows;
using System.Windows.Controls;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Views.Consultas_y_Reportes.Graficos;

namespace AplicativoDeAlmacen.Views.Dashboards
{
    public partial class DashboardPrincipalUserControl : UserControl
    {
        public DashboardPrincipalUserControl()
        {
            InitializeComponent();

            Loaded += DashboardPrincipalUserControl_Loaded;
        }

        private void DashboardPrincipalUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            CargarWidgetsPorRol();
        }

        private void CargarWidgetsPorRol()
        {
            TabDashboard.Items.Clear();

            int rolId = SesionSistema.UsuarioActual?.RolUsuarioId ?? 0;

            if (rolId == 1 || rolId == 3)
            {
                TabDashboard.Items.Add(
                    new TabItem
                    {
                        Header = "📈 Volatilidad de Stock",
                        FontWeight = FontWeights.SemiBold,
                        Content = new WidgetVelasAlmacenUserControl()
                    });

                TabDashboard.Items.Add(
                    new TabItem
                    {
                        Header = "🚨 Stock Crítico",
                        FontWeight = FontWeights.SemiBold,
                        Content = new ReporteStockCriticoUserControl() // Instanciamos la nueva vista
                    });

                TabDashboard.Items.Add(
                    new TabItem
                    {
                        Header = "📊 Distribución Stock",
                        FontWeight = FontWeights.SemiBold,
                        Content = new WidgetDistribucionAlmacenUserControl() // <--- Tu nuevo widget
                    });
            }

            if (TabDashboard.Items.Count > 0)
                TabDashboard.SelectedIndex = 0;
        }
    }
}