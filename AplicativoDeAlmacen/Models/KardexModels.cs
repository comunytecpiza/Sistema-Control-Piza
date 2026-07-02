using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Models
{
    // Representa una sola fila en el DataGrid
    public class KardexFisicoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RazonSocialUbicacion { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;
        public decimal CostoUnitario { get; set; }

        // Columnas desglosadas
        public decimal IngresoNormal { get; set; }
        public decimal IngresoDevolucion { get; set; }
        public decimal SalidaNormal { get; set; }
        public decimal SalidaDevolucion { get; set; }

        public decimal SaldoFinal { get; set; }
    }


    // Representa el reporte completo con sus totales (la parte inferior amarilla)
    public class KardexFisicoReporte
    {
        public List<KardexFisicoItem> Detalles { get; set; } = new List<KardexFisicoItem>();
        public List<ConsultaCodigoItem> Codigos { get; set; } = new List<ConsultaCodigoItem>();
        // Totales de Entradas
        public decimal TotalIngresos { get; set; }
        public decimal TotalDevIngresos { get; set; }

        // Totales de Salidas
        public decimal TotalSalidas { get; set; }
        public decimal TotalDevSalidas { get; set; }
        public string NumeroRegistro { get; set; }
        // Resumen Final (Se elimina StockInicial)
        public decimal StockFinal { get; set; }
    }

    public class KardexValorizadoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; }
        public string Registro { get; set; }
        public string RazonSocialUbicacion { get; set; }
        public string Guia { get; set; }

        // Columnas Valorizadas
        public decimal CostoUnitario { get; set; }
        public decimal CostoPromedio { get; set; }

        // Columnas Físicas
        public decimal IngresoFisico { get; set; }
        public decimal SalidaFisico { get; set; }
        public decimal SaldoFisico { get; set; }

        // Totales Monetarios (Fisico * Costo)
        public decimal IngresoValorado { get; set; }
        public decimal SalidaValorado { get; set; }
        public decimal SaldoValorado { get; set; }
    }
    public class KardexValorizadoReporte
    {
        public List<KardexValorizadoItem> Detalles { get; set; } = new List<KardexValorizadoItem>();

        // Totales Finales
        public decimal TotalIngresoFisico { get; set; }
        public decimal TotalSalidaFisico { get; set; }
        public decimal StockFinalFisico { get; set; }

        public decimal TotalIngresoValorado { get; set; }
        public decimal TotalSalidaValorado { get; set; }
        public decimal SaldoFinalValorado { get; set; }
    }
}