using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Documentos
{
    public class Documento
    {
        public string Codigo { get; set; } = string.Empty; // cod_docu (ej. "01", "03", "07")
        public string Descripcion { get; set; } = string.Empty; // des_docu
        public string? Abreviatura { get; set; } // abreviatura (ej. "FAC", "BOL")
        public bool Estado { get; set; } = true; // est_regi (1 o 0)
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
