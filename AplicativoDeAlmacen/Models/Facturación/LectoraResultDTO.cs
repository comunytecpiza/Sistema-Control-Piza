using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class LectoraResultDTO
    {
        public int ProductoId { get; set; }

        public string DescripcionProducto { get; set; }

        public string UnidadMedida { get; set; }

        public decimal PrecioUnitario { get; set; }

        public int CodigoCreadoId { get; set; }

        public string CodigoCompleto { get; set; }

        public int MovimientoId { get; set; }

        public int EstadoId { get; set; }

        public string TipoMovimiento { get; set; }

        public int CategoriaProductoId { get; set; }

        public string CategoriaProducto { get; set; }

        public bool TieneSalida { get; set; }
    }
}
