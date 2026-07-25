using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Reportes
{
    public class PieData
    {
        public string Name { get; set; }

        public IEnumerable<double> Values { get; set; }

        public PieData(string name, double value)
        {
            Name = name;
            Values = new[] { value };
        }
    }
}
