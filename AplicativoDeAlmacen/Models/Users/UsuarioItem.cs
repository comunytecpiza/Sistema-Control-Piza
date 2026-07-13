using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Models
{
    public class UsuarioItem
    {
        public int Id { get; set; }
        public string Codigo { get; set; } // Ejemplo: USR01
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public string Nombres { get; set; }

        // Razón Social generada automáticamente
        public string RazonSocial => $"{ApellidoPaterno} {ApellidoMaterno} {Nombres}".Trim();

        public string Password { get; set; }
        public string TipoAcceso { get; set; } // Admin, Almacén, Contador
        public string Estado { get; set; } // ACTIVO, INACTIVO
    }
}
