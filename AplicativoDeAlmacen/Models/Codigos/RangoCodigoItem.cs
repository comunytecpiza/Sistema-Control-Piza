using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Models
{
    public class RangoCodigoItem
    {
        // Relación opcional al detalle del movimiento (se usa al cargar movimiento desde BD)
        public int MovimientoDetalleId { get; set; }
      //  public int rangoId { get; set; }
        public int productoId { get; set; }
        public string Cantidad { get; set; }        // Columna visual 'Cantidad'
        public string Desde { get; set; }           // Código completo inicial (Ej: LMA3C26-V-0000100)
        public string Hasta { get; set; }           // Código completo final (Ej: LMA3C26-V-0000104)
        public string ColeccionTipo { get; set; }   // Texto en la grilla (Ej: "C2026 / LIBRO VENTA")

        public string DesdeDisplay => $"CODE-{DesdeNum}"; // O el formato que uses
        public string HastaDisplay => $"CODE-{HastaNum}";

        // Propiedades de control internas indispensables para modificar
        public int DesdeNum { get; set; }           // El correlativo puro inicial (Ej: 100)
        public int HastaNum { get; set; }           // El correlativo puro final (Ej: 104)
        public string AbreviaturaBase { get; set; } // La abreviatura del producto (Ej: "LMA3C26-V")
        public int CategoriaProductoId { get; set; } // 1 = Libro Guía, 2 = Libro Venta (¡La clave de todo!)
    }
}
