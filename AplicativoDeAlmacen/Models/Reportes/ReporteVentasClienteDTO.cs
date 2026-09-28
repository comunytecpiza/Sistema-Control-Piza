using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Reportes
{
    public class ReporteVentaProductoResumenDTO
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = "UND";
        public decimal Cantidad { get; set; }
        public decimal Importe { get; set; }
    }

    public class ReporteVentaCodigoDetalleDTO
    {
        public int ProductoId { get; set; }
        public int Cantidad { get; set; } = 1;
        public string Codigo { get; set; } = string.Empty;
        public string ColeccionTipo { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Documento { get; set; } = string.Empty;
        public decimal Importe { get; set; }
    }
}