using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AplicativoDeAlmacen.Models.Facturación
{
    public class MedioPago
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; // EFECTIVO, YAPE, TRANSF. BCP, etc.
        public string? CodigoSunat { get; set; }
        public string Tipo { get; set; } = "CONTADO";      // CONTADO, BANCARIO, BILLETERA_DIGITAL, CREDITO
        public bool EsActivo { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}