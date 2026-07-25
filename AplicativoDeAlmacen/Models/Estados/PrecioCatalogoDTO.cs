using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Estados
{
    public class PrecioCatalogoDTO
    {
        public int ProductoId { get; set; }
        public string Descripcion { get; set; }
        public string UnidadMedida { get; set; }

        public decimal PrecioBase { get; set; }
        public decimal PorcentajeBase { get; set; } // 🌟 Nuevo

        public int PrecioEspecialId { get; set; }
        public decimal PrecioEspecial { get; set; }
        public decimal Porcentaje { get; set; }
        public bool TienePrecioEspecial { get; set; }

        // 🌟 Nueva propiedad calculada para que la grilla sepa qué porcentaje mostrar
        public decimal PorcentajeMostrar => TienePrecioEspecial ? Porcentaje : PorcentajeBase;
    }
}
