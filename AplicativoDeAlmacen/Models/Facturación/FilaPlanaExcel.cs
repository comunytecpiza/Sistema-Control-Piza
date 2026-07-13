using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FilaPlanaExcel
    {
        public string Documento { get; set; }
        public string Serie { get; set; }
        public string Numero { get; set; }
        public string RazonSocial { get; set; }
        public string Moneda { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Afecto { get; set; }
        public decimal IGV { get; set; }
        public decimal Exonerado { get; set; }
        public decimal Importe { get; set; }
        public string Producto { get; set; }
        public decimal Precio { get; set; }
        public string Institucion { get; set; }
        public string CodigoInterno { get; set; }
        public int Cantidad { get; set; }
    }
}
