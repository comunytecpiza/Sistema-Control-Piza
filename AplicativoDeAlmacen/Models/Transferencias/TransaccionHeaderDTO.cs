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
        public string SerieNumero { get; set; } = string.Empty; // Ej: "0001-0000123"
        public string GuiaRemision { get; set; } = string.Empty; // Ej: "0001-0000456"
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

        // Estado visual de la bandeja
        public bool EsPendiente { get; set; } // true = PENDIENTE (Resaltado), false = RECIBIDO (Gris)
    }
}
