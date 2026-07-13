using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionDetalleCodigos
    {
        public int Id { get; set; }
        public int FacturacionDetalleId { get; set; }
        public int CodigoCreadoId { get; set; }

        // Opcional: Esto te ayuda a traer el código real (ej. LMA3 C26...) 
        // sin necesidad de hacer JOINs complejos todo el tiempo
        public string CodigoTexto { get; set; }
    }
}