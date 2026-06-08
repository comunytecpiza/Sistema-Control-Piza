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

        // Totales de Entradas
        public decimal TotalIngresos { get; set; }
        public decimal TotalDevIngresos { get; set; }

        // Totales de Salidas
        public decimal TotalSalidas { get; set; }
        public decimal TotalDevSalidas { get; set; }

        // Resumen Final (Se elimina StockInicial)
        public decimal StockFinal { get; set; }
    }
}