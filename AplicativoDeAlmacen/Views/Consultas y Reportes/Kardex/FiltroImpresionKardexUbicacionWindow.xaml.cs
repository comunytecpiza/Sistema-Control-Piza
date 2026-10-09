#nullable enable
using System;
using System.Windows;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Kardex
{
    public partial class FiltroImpresionKardexUbicacionWindow : Window
    {
        public bool SeConfirmoImpresion { get; private set; } = false;

        // 1 = Guías (Docente), 2 = Ventas (Alumno), null = Ambos
        public int? CategoriaIdSeleccionada => RbReporteGuias.IsChecked == true ? 1 : 2;

        public string CampanaSeleccionada => string.IsNullOrWhiteSpace(TxtCampana.Text)
            ? $"C-{DateTime.Now.Year}"
            : TxtCampana.Text.Trim();

        // 🌟 ENUM Y PROPIEDAD RESTAURADOS PARA RESOLVER CS0117 Y CS1061
        public enum ModoAlcanceMatriz
        {
            SoloActual,
            TodosLosPromotores,
            TotalConsolidado
        }

        public ModoAlcanceMatriz AlcanceMatriz
        {
            get
            {
                if (RbAlcanceUbicacionActual.IsChecked == true || RbAlcanceProductoEnUbicacion.IsChecked == true)
                    return ModoAlcanceMatriz.SoloActual;

                if (RbAlcanceGeneralConsolidado.IsChecked == true)
                    return ModoAlcanceMatriz.TotalConsolidado;

                return ModoAlcanceMatriz.TodosLosPromotores;
            }
        }

        // Compatibilidad con los servicios existentes de Excel
        public bool EsModoAvanzado => CboEstructuraSalida.SelectedIndex == 0;
        public bool IncluirCodigosPorFila => CboEstructuraSalida.SelectedIndex == 1;
        public bool IncluirTablaLateral => CboEstructuraSalida.SelectedIndex == 1;

        public FiltroImpresionKardexUbicacionWindow()
        {
            InitializeComponent();
            TxtCampana.Text = $"C-{DateTime.Now.Year}";
            ActualizarMensajeResumen();
        }

        public void ConfigurarContextoInicial(bool tieneProducto, bool tieneUbicacion, string nombreProducto, string nombreUbicacion)
        {
            if (tieneProducto && tieneUbicacion)
            {
                RbAlcanceProductoEnUbicacion.IsChecked = true;
            }
            else if (tieneProducto)
            {
                RbAlcanceProductoActual.IsChecked = true;
            }
            else if (tieneUbicacion)
            {
                RbAlcanceUbicacionActual.IsChecked = true;
            }
            else
            {
                RbAlcanceGeneralConsolidado.IsChecked = true;
            }

            ActualizarMensajeResumen(nombreProducto, nombreUbicacion);
        }

        private void TipoReporte_SelectionChanged(object sender, RoutedEventArgs e)
        {
            ActualizarMensajeResumen();
        }

        private void Alcance_SelectionChanged(object sender, RoutedEventArgs e)
        {
            ActualizarMensajeResumen();
        }

        private void ActualizarMensajeResumen(string? prod = null, string? ubi = null)
        {
            if (TxtResumenAccion == null) return;

            string tipoTexto = RbReporteGuias?.IsChecked == true ? "Libros Guía (Docente)" : "Libros Venta (Alumno)";
            string alcanceTexto = "la ubicación actual";

            if (RbAlcanceProductoActual?.IsChecked == true)
                alcanceTexto = !string.IsNullOrEmpty(prod) ? $"el producto '{prod}' en todas las ubicaciones" : "el producto seleccionado";
            else if (RbAlcanceProductoEnUbicacion?.IsChecked == true)
                alcanceTexto = "el producto específico en la ubicación consultada";
            else if (RbAlcanceGeneralConsolidado?.IsChecked == true)
                alcanceTexto = "todas las ubicaciones y ferias del sistema (Consolidado)";

            TxtResumenAccion.Text = $"Exportando: {tipoTexto} para {alcanceTexto}.";
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