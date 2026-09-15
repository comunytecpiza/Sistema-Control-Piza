using System;

namespace AplicativoDeAlmacen.Models.Transferencias
{
    public class TransaccionHeaderDTO
    {
        public int MovimientoId { get; set; }
        public string SerieNumero { get; set; } = string.Empty;
        public string GuiaRemision { get; set; } = string.Empty;
        public DateTime FechaMovimiento { get; set; }

        // 🌟 Nuevos campos para trazabilidad logística
        public DateTime FechaEnvio { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        public int AlmacenOrigenId { get; set; }
        public string AlmacenOrigenNombre { get; set; } = string.Empty;
        public int AlmacenDestinoId { get; set; }
        public string AlmacenDestinoNombre { get; set; } = string.Empty;
        public string UsuarioEmisorNombre { get; set; } = string.Empty;
        public string MotivoDescripcion { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
        public int TotalProductos { get; set; }
        public int TotalCodigos { get; set; }
        public bool EsPendiente { get; set; }

        // 🌟 Propiedades dinámicas para la UI
        public bool SoyElEmisor { get; set; }

        public string TextoBotonAccion => SoyElEmisor
            ? "👁️ Ver Salida"
            : (EsPendiente ? "📥 RECIBIR" : "👁️ Ver Entrada");

        public string ColorBotonAccion => SoyElEmisor
            ? "#2563EB" // Azul
            : (EsPendiente ? "#16A34A" : "#6B7280"); // Verde si es Recibir, Gris si ya fue recibido

        public string TiempoTrasladoFormateado
        {
            get
            {
                if (!FechaRecepcion.HasValue) return "En tránsito";
                var dif = FechaRecepcion.Value - FechaEnvio;
                if (dif.TotalDays >= 1)
                    return $"{(int)dif.TotalDays}d {dif.Hours}h";
                if (dif.TotalHours >= 1)
                    return $"{(int)dif.TotalHours}h {dif.Minutes}m";
                return $"{Math.Max(1, (int)dif.TotalMinutes)} min";
            }
        }
    }
}