using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class Moneda
    {
        public int Id { get; set; }
        public string CodigoSunat { get; set; } = "PEN"; // PEN, USD
        public string Descripcion { get; set; } = "SOLES";
        public string Simbolo { get; set; } = "S/";
        public bool EsActivo { get; set; } = true;
    }
}