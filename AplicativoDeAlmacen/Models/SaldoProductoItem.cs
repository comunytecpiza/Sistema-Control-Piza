using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Models
{
    public class SaldoProductoItem
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public decimal StockInicial { get; set; }
        public decimal TotalIngresos { get; set; }
        public decimal TotalSalidas { get; set; }

        // El Stock Final se calcula en automático
        public decimal StockFinal => StockInicial + TotalIngresos - TotalSalidas;
    }
}