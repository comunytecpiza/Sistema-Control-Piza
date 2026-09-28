using System;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionPagoDetalle
    {
        public long Id { get; set; }
        public int FacturacionCabeceraId { get; set; }
        public int MedioPagoId { get; set; }
        public decimal Monto { get; set; }
        public string? NumeroOperacion { get; set; }
        public string? Observacion { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Propiedad auxiliar de visualización
        public string? MedioPagoNombre { get; set; }
    }
}