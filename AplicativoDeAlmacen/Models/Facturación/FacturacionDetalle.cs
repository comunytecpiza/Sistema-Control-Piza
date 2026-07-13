using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionDetalle
    {
        public int Id { get; set; }
        public int FacturacionCabeceraId { get; set; }
        public int MovimientoId { get; set; }
        public int ProductoId { get; set; }
        public int NumeroLinea { get; set; }
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }

        public decimal ValorGravado { get; set; }
        public decimal ValorInafecto { get; set; }
        public decimal ValorExonerado { get; set; }
        public decimal ValorIgv { get; set; }
        public decimal ImporteTotal { get; set; }

        public List<FacturacionDetalleCodigos> Codigos { get; set; } = new List<FacturacionDetalleCodigos>();
    }

    public class ValidacionCodigoResult
    {
        public int Id { get; set; }
        public string CodigoCompleto { get; set; }
        public int MovimientoId { get; set; } // 🌟 Nuevo campo
    }


}