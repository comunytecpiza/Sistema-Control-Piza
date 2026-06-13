using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models
{
    public class VistaCodigoGrid
    {
        public MovimientoCodigo MovCodigo { get; set; } // Modelo EF original
        public int NumeroFila => MovCodigo.MovimientoDetalleId;
        public string CodigoUnique { get; set; }         // El código físico (Ej: "LIB0000123")
        public string ColeccionTipo { get; set; }        // Descripción complementaria
        // 💡 AGREGA ESTA LÍNEA: Sirve de puente para que tu DataGrid XAML lea la propiedad "Codigo"
        public string Codigo => CodigoUnique;

        public int ProductoId;

    }
}
