using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Motivo_y_Movimientos
{
    public class TransferenciaPendienteDTO
    {
        public int MovimientoSalidaId { get; set; }
        public string GuiaOrigen { get; set; } = string.Empty;
        public string AlmacenOrigenNombre { get; set; } = string.Empty;
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public int CantidadEnTransito { get; set; }
        public DateTime FechaEnvio { get; set; }
        public bool Seleccionado { get; set; } // Para el Checkbox de la Bandeja
    }
}
