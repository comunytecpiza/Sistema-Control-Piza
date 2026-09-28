using System;

namespace AplicativoDeAlmacen.Models.Reportes
{
    public class RegistroVentaItemDTO
    {
        public DateTime FechaEmision { get; set; }
        public string Documento { get; set; } = string.Empty;
        public string Serie { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string TipoDoc { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string RucDni { get; set; } = string.Empty;
        public decimal TotalGravado { get; set; }
        public decimal TotalExonerado { get; set; }
        public decimal TotalInafecto { get; set; }
        public decimal TotalIgv { get; set; }
        public decimal ImporteTotal { get; set; }
    }
}