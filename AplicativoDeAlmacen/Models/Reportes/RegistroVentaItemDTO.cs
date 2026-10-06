using System;

namespace AplicativoDeAlmacen.Models.Reportes
{
    public class RegistroVentaItemDTO
    {
        public DateTime FechaEmision { get; set; }        // 📅 Fecha legal del comprobante
        public DateTime FechaRegistro { get; set; }       // 🕒 Fecha en que se digitó en el sistema
        public DateTime? FechaModificacion { get; set; }   // 🕒 Última fecha de edición
        public string Documento { get; set; } = string.Empty;
        public string Serie { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string TipoDoc { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string RucDni { get; set; } = string.Empty;

        // Importes fiscales
        public decimal TotalGravado { get; set; }
        public decimal TotalExonerado { get; set; }
        public decimal TotalInafecto { get; set; }
        public decimal TotalIgv { get; set; }
        public decimal MontoDelivery { get; set; }        // 🚚 Delivery / Otros
        public decimal ImporteTotal { get; set; }

        // Auditoría
        public string UsuarioCreador { get; set; } = string.Empty;
        public string UsuarioEditor { get; set; } = string.Empty;
    }
}