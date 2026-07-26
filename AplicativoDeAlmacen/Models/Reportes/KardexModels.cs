using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Models
{
    // Representa una sola fila en el DataGrid de Kárdex Físico
    public class KardexFisicoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RazonSocialUbicacion { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;
        public decimal CostoUnitario { get; set; }

        // 🌟 Identificación de Almacén por Fila
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        // Columnas desglosadas
        public decimal IngresoNormal { get; set; }
        public decimal IngresoDevolucion { get; set; }
        public decimal SalidaNormal { get; set; }
        public decimal SalidaDevolucion { get; set; }

        public decimal SaldoFinal { get; set; }

        // Indica si el movimiento fue anulado
        public bool IsAnulado { get; set; }
    }

    // Representa el reporte completo con sus totales
    public class KardexFisicoReporte
    {
        // 🌟 Información del Almacén Filtrado
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        public List<KardexFisicoItem> Detalles { get; set; } = new List<KardexFisicoItem>();
        public List<ConsultaCodigoItem> Codigos { get; set; } = new List<ConsultaCodigoItem>();

        // Totales de Entradas
        public decimal TotalIngresos { get; set; }
        public decimal TotalDevIngresos { get; set; }

        // Totales de Salidas
        public decimal TotalSalidas { get; set; }
        public decimal TotalDevSalidas { get; set; }
        public string NumeroRegistro { get; set; } = string.Empty;

        // Resumen Final
        public decimal StockFinal { get; set; }
    }

    // Item de Kárdex Valorizado
    public class KardexValorizadoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RazonSocialUbicacion { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;

        // 🌟 Identificación de Almacén por Fila
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        // Columnas Valorizadas
        public decimal CostoUnitario { get; set; }
        public decimal CostoPromedio { get; set; }

        // Columnas Físicas
        public decimal IngresoFisico { get; set; }
        public decimal SalidaFisico { get; set; }
        public decimal SaldoFisico { get; set; }

        // Totales Monetarios
        public decimal IngresoValorado { get; set; }
        public decimal SalidaValorado { get; set; }
        public decimal SaldoValorado { get; set; }
        public bool IsAnulado { get; set; }
    }

    // Reporte Kárdex Valorizado Completo
    public class KardexValorizadoReporte
    {
        // 🌟 Información del Almacén Filtrado
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

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