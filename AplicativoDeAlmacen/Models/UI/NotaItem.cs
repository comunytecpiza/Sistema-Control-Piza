using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.UI
{
    public class NotaItem
    {
        public string Texto { get; set; }
        public bool IsCompleted { get; set; }
        public string ColorEtiqueta { get; set; } // Ej: "#EF4444" para urgente
        public bool IsBold { get; set; }
    }
}
