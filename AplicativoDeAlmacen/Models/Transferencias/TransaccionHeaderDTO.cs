using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Transferencias
{
    public class TransaccionHeaderDTO
    {
        public int MovimientoId { get; set; }
        public string SerieNumero { get; set; } = string.Empty;
        public string GuiaRemision { get; set; } = string.Empty;
        public DateTime FechaMovimiento { get; set; }
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

        // 🌟 PROPIEDADES DINÁMICAS PARA EL BOTÓN SEGÚN ROL Y ESTADO
        public bool SoyElEmisor { get; set; }

        public string TextoBotonAccion => SoyElEmisor
            ? "👁️ Ver Salida"
            : (EsPendiente ? "📥 RECIBIR" : "👁️ Ver Entrada");

        public string ColorBotonAccion => SoyElEmisor
            ? "#2563EB" // Azul
            : (EsPendiente ? "#16A34A" : "#6B7280"); // Verde si es Recibir, Gris si ya fue recibido
    }
}
