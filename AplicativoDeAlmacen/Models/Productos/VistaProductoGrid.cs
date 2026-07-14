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
        // 🌟 SOLUCIÓN 1: Inicializar la propiedad para que nazca con un objeto vacío y nunca sea null
        public MovimientoDetalle Detalle { get; set; } = new MovimientoDetalle();

        public string CodigoProducto { get; set; }     // Para la columna "Código"
        public string Descripcion { get; set; }        // Para la columna "Descripción"
        public string UnidadMedida { get; set; }       // Para la columna "U. Medida"

        private decimal _cantidad;
        public decimal Cantidad
        {
            get => _cantidad;
            set
            {
                _cantidad = value;

                // 🌟 SOLUCIÓN 2: Preguntar si existe antes de tocarlo
                if (Detalle != null)
                {
                    // Nota: Si usas la misma clase para Ingreso y Salida, es más seguro 
                    // simplemente actualizar ambos o el que ya tenga valor.
                    if (Detalle.CantidadSalida > 0)
                        Detalle.CantidadSalida = value;
                    else
                        Detalle.CantidadIngreso = value;
                }
            }
        }

        public int ProductoId { get; set; }
    }
}
