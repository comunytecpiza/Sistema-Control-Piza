using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionCabecera
    {
        public int Id { get; set; }
        public string TipoDocumento { get; set; }
        public string SerieDocumento { get; set; }
        public string NumeroDocumento { get; set; }
        public DateTime FechaEmision { get; set; }

        public int PuntoVentaId { get; set; }
        public int? CompradorId { get; set; }
        public int? InstitucionId { get; set; }
        public string Observacion { get; set; }

        public decimal TotalGravado { get; set; }
        public decimal TotalInafecto { get; set; }
        public decimal TotalExonerado { get; set; }
        public decimal TotalIgv { get; set; }
        public decimal ImporteTotal { get; set; }
        public decimal PorcentajeIgv { get; set; }

        public DateTime FechaRegistro { get; set; }
        public int UsuarioId { get; set; }
        public bool EstadoRegistro { get; set; }

        public List<FacturacionDetalle> Detalles { get; set; } = new List<FacturacionDetalle>();
    }
}