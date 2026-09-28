using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class PagoManualDTO
    {
        public int MedioPagoId { get; set; }
        public string MedioPagoNombre { get; set; } = "EFECTIVO";
        public decimal Monto { get; set; }
        public string NumeroOperacion { get; set; } = string.Empty;
    }
}