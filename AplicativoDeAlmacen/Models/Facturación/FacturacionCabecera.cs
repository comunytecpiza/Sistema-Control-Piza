using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionCabecera
    {
        public int Id { get; set; }
        public string TipoDocumento { get; set; } = string.Empty;   // 01: Factura, 03: Boleta, 07: Nota Crédito
        public string SerieDocumento { get; set; } = string.Empty;  // Ej: F001, B001, 0001
        public string NumeroDocumento { get; set; } = string.Empty; // Ej: 0000001
        public DateTime FechaEmision { get; set; } = DateTime.Now;

        // 🌟 SEDE / PUNTO DE VENTA
        public int PuntoVentaId { get; set; }
        public int? AlmacenId { get; set; } // Sede física de despacho / facturación

        // 🌟 CLIENTE / ENTIDAD
        public int? CompradorId { get; set; }
        public int? InstitucionId { get; set; }
        public string? Observacion { get; set; }

        // 🌟 MONTOS TRIBUTARIOS Y TOTALES
        public decimal TotalGravado { get; set; }
        public decimal TotalInafecto { get; set; }
        public decimal TotalExonerado { get; set; }
        public decimal TotalIgv { get; set; }
        public decimal ImporteTotal { get; set; }
        public decimal PorcentajeIgv { get; set; } = 18.00m;

        // 🌟 AUDITORÍA DE CREACIÓN Y EDICIÓN
        public DateTime FechaRegistro { get; set; } = DateTime.Now;
        public int UsuarioId { get; set; }
        public int? UsuarioUpdateId { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // 🌟 ESTADO Y AUDITORÍA DE ANULACIÓN
        public bool EstadoRegistro { get; set; } = true; // true: Activo / Emitido, false: Anulado
        public int? UsuarioAnulacionId { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }

        // 🌟 DETALLES ASOCIADOS
        public List<FacturacionDetalle> Detalles { get; set; } = new List<FacturacionDetalle>();
    }
}