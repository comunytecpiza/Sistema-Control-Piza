using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models
{
    public class SolicitudMovimiento
    {
        public Movimiento Movimiento { get; set; }

        public List<VistaProductoGrid> Productos { get; set; }

        public List<VistaCodigoGrid> Codigos { get; set; }

        public int? MovimientoId { get; set; }

        //asdasdasd
    }
}
