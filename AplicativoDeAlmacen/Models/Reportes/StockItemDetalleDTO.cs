using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Reportes
{
    public class StockItemDetalleDTO
    {
        public int Id { get; set; }
        public string CodigoUnico { get; set; }
        public string TipoCategoria { get; set; } // "GUÍA" o "VENTA"
        public string NombreCondicion { get; set; } // "OK / Operativo", "Dañado / Defectuoso", etc.
        public bool PermiteSalida { get; set; }
    }
}
