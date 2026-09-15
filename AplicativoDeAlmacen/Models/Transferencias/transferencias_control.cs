using System;

namespace AplicativoDeAlmacen.Models.Transferencias
{
    public class TransferenciaControl
    {
        public int Id { get; set; }
        public int MovimientoSalidaId { get; set; }
        public int? MovimientoIngresoId { get; set; }
        public int AlmacenOrigenId { get; set; }
        public int AlmacenDestinoId { get; set; }
        public DateTime FechaEnvio { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public string Estado { get; set; } = "PENDIENTE"; // "PENDIENTE" o "RECEPCIONADO"
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Propiedades de conveniencia para reportes y vistas
        public bool EstaPendiente => Estado.Equals("PENDIENTE", StringComparison.OrdinalIgnoreCase);

        public TimeSpan? TiempoTraslado => FechaRecepcion.HasValue
            ? FechaRecepcion.Value - FechaEnvio
            : null;
    }
}