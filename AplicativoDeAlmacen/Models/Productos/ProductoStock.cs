
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace AplicativoDeAlmacen.Models
{
    public class ProductoStock
    {
        public int Id { get; set; }
        public string Descripcion { get; set; }
        public int StockActual { get; set; }
        public int StockMinimo { get; set; }
        public int TipoProductoId { get; set; }
        public string GradoNombre { get; set; }

        // Propiedad calculada útil para saber cuántos faltan para llegar al mínimo
        public int Faltante => StockMinimo - StockActual > 0 ? StockMinimo - StockActual : 0;
    }
}