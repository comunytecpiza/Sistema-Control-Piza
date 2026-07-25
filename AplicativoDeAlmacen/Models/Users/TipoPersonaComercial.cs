using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Users
{
    public partial class TipoPersonaComercial
    {
        public int Id { get; set; }

        public string Nombre { get; set; }

        public string Descripcion { get; set; }

        // Si tienes un modelo 'Estado', puedes usarlo, sino usamos el ID directo
        public int? EstadoId { get; set; }

        public DateTime? CreatedAt { get; set; }
    }
}
