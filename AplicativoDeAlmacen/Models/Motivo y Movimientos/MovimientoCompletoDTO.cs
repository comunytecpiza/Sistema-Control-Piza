using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Motivo_y_Movimientos
{
    public class MovimientoCompletoDTO
    {
        public Movimiento Movimiento { get; set; } = new Movimiento();
        public List<MovimientoDetalle> Detalles { get; set; } = new();
    }
}
