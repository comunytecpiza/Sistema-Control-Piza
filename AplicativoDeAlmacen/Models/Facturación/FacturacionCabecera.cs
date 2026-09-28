using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionCabecera
    {
        public int Id { get; set; }

        // 🌟 EMPRESA EMISORA (Piza SAC / Piza EIRL)
        public int? EmpresaId { get; set; }
        

        public string TipoDocumento { get; set; } = string.Empty;   // BOLETA, FACTURA
        public string SerieDocumento { get; set; } = string.Empty;  // B001, BA01, FA01
        public string NumeroDocumento { get; set; } = string.Empty; // 0001739
        public DateTime FechaEmision { get; set; } = DateTime.Now;

        // 🌟 SEDE / PUNTO DE VENTA
        public int PuntoVentaId { get; set; }
        public int? AlmacenId { get; set; }

        // 🌟 CLIENTE COMODÍN / COMPRADOR (CLIENTES VARIOS)
        public int? CompradorId { get; set; }
        public int? InstitucionId { get; set; }
        public string? Observacion { get; set; }

        // 🌟 MONEDA Y CONDICIÓN PRINCIPAL
        public int MonedaId { get; set; } = 1;
        public int? CondicionPagoId { get; set; }

        // 🌟 MONTOS TRIBUTARIOS Y TOTALES
        public decimal TotalGravado { get; set; }
        public decimal TotalInafecto { get; set; }
        public decimal TotalExonerado { get; set; }
        public decimal TotalIgv { get; set; }
        public decimal ImporteTotal { get; set; }
        public decimal MontoDelivery { get; set; } = 0.00m;
        public decimal PorcentajeIgv { get; set; } = 18.00m;

        // 🌟 AUDITORÍA
        public DateTime FechaRegistro { get; set; } = DateTime.Now;
        public int UsuarioId { get; set; }
        public int? UsuarioUpdateId { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // 🌟 ESTADO Y ANULACIÓN
        public bool EstadoRegistro { get; set; } = true;
        public int? UsuarioAnulacionId { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }

        // 🌟 DETALLES ASOCIADOS
        public List<FacturacionDetalle> Detalles { get; set; } = new List<FacturacionDetalle>();
        public List<FacturacionPagoDetalle> Pagos { get; set; } = new List<FacturacionPagoDetalle>();
    }
}