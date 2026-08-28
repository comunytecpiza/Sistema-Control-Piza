using System;
using System.Windows;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex
{
    public partial class FiltroImpresionKardexUbicacionWindow : Window
    {
        public bool IncluirCodigosPorFila => ChkCodigosPorFila.IsChecked ?? false;
        public bool IncluirTablaLateral => ChkTablaLateralCodigos.IsChecked ?? false;
        public bool SeConfirmoImpresion { get; private set; } = false;

        public bool EsModoAvanzado => TabOpciones.SelectedIndex == 1;
        public string CampanaSeleccionada => string.IsNullOrWhiteSpace(TxtCampana.Text) ? $"C-{DateTime.Now.Year}" : TxtCampana.Text.Trim();

        // 1 = Solo Guías, 2 = Solo Ventas, null = Todos
        public int? CategoriaIdSeleccionada => CboTipoAvanzado.SelectedIndex == 0 ? 1 : (CboTipoAvanzado.SelectedIndex == 1 ? 2 : (int?)null);

        public enum ModoAlcanceMatriz { SoloActual, TodosLosPromotores, TotalConsolidado }
        public ModoAlcanceMatriz AlcanceMatriz =>
            RbSoloActual.IsChecked == true ? ModoAlcanceMatriz.SoloActual :
            (RbTodosPaginados.IsChecked == true ? ModoAlcanceMatriz.TodosLosPromotores : ModoAlcanceMatriz.TotalConsolidado);

        public FiltroImpresionKardexUbicacionWindow()
        {
            InitializeComponent();
            TxtCampana.Text = $"C-{DateTime.Now.Year}";
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            SeConfirmoImpresion = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            SeConfirmoImpresion = false;
            this.Close();
        }
    }
}