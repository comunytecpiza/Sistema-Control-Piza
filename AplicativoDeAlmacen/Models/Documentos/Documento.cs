using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Documentos
{
    public class Documento
    {
        public string Codigo { get; set; } // cod_docu (ej. "01", "02", "03")
        public string Descripcion { get; set; } // des_docu (ej. "FACTURA")
    }
}
