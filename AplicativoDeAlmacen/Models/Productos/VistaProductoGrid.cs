using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Models
{
    public class VistaProductoGrid
    {
        public MovimientoDetalle Detalle { get; set; } = new MovimientoDetalle();

        public string CodigoProducto { get; set; }
        public string Descripcion { get; set; }
        public string UnidadMedida { get; set; }
        public int ProductoId { get; set; }

        public bool EsProductoSinCodigo { get; set; }
        // 🌟 SOLUCIÓN: Separamos la lógica.
        // La cantidad de la grilla debe ser solo una propiedad de lectura/escritura 
        // que NO gatilla cambios automáticos en el objeto Detalle.
        private decimal _cantidad;
        public decimal Cantidad
        {
            get => _cantidad;
            set => _cantidad = value; // Solo setea el valor en memoria de la grilla
        }
    }
}
