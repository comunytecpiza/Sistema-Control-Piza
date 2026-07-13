using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class ItemGridDTO
    {
        public int NumLine { get; set; }

        public int ProductoId { get; set; }
        public int MovimientoId { get; set; }
        public int CodProd { get; set; }
        public string DescripcionProducto { get; set; }
        public string UnidadMedida { get; set; }
        public decimal CanProd { get; set; }
        public decimal PreUnit { get; set; }

        // Propiedad calculada de solo lectura para la grilla
        public decimal ImpTota { get; set; }

        public List<CodigoLeidoDTO> Codigos { get; set; } = new List<CodigoLeidoDTO>();
    }

    public class CodigoLeidoDTO
    {
        public int CodigoCreadoId { get; set; }
        public string CodigoString { get; set; }
        public int Cantidad { get; set; }
        public string Coleccion { get; set; }
    }

    

}
