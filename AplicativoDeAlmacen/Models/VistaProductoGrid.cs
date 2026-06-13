using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models
{
    public class VistaProductoGrid
    {

        public MovimientoDetalle Detalle { get; set; } // Modelo EF original
        public string CodigoProducto { get; set; }     // Para la columna "Código"
        public string Descripcion { get; set; }        // Para la columna "Descripción"
        public string UnidadMedida { get; set; }       // Para la columna "U. Medida"
        public decimal Cantidad => Detalle.CantidadIngreso > 0 ? Detalle.CantidadIngreso : Detalle.CantidadSalida;

        public int ProductoId;
    }
}
