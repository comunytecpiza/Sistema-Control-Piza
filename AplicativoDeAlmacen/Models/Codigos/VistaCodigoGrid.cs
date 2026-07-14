using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models
{
    public class VistaCodigoGrid
    {
        public MovimientoCodigo MovCodigo { get; set; } = new MovimientoCodigo();
        public int NumeroFila { get; set; }
        public string CodigoUnique { get; set; } = "";
        public string ColeccionTipo { get; set; } = "";
        public string Codigo => CodigoUnique;

        // Agregamos el set público para que no dé error de solo lectura
        public decimal PrecioUnitario { get; set; }
        public int ProductoId { get; set; }

        public int MovimientoDetalleId => MovCodigo?.MovimientoDetalleId ?? 0;
        public string EstadoValidacion { get; set; } = "";
    }
}
