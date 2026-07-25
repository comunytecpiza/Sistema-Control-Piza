using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Transferencias
{
    public class TransaccionDetalleDTO
    {
        public int DetalleId { get; set; }
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoDescripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal CostoUnitario { get; set; }
        public List<string> CodigosUnicos { get; set; } = new List<string>();
    }
}
