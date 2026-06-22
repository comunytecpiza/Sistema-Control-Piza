
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace AplicativoDeAlmacen.Models
{
    public class ProductoStock
    {
        public string Descripcion { get; set; }
        public int StockActual { get; set; }

        // 🌟 Añadimos esto para poder filtrar por "Plan Lector" o "Texto Escolar"
        public int TipoProductoId { get; set; }

        // 🌟 Añadimos esto para poder agrupar visualmente en la pantalla
        public string GradoNombre { get; set; }
    }
}