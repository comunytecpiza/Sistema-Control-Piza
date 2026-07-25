using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Documentos
{
    public class SerieDocumento
    {
        public int Id { get; set; } // Sugiero que en tu nueva BD tenga un ID autoincremental

        public int UbicacionId { get; set; } // Reemplaza al antiguo 'cod_ubic' de texto
        public Ubicacion Ubicacion { get; set; } // Propiedad de navegación opcional

        public string NumeroSerie { get; set; } // num_seri (ej. "F001", "B001", "0002")
        public string TipoSerie { get; set; } // tip_seri: "E" (Electrónica) o "M" (Manual)

        // Correlativos
        public int CorrelativoFactura { get; set; } // num_fact
        public int CorrelativoBoleta { get; set; } // num_bole
        public int CorrelativoRecibo { get; set; } // num_reci

        public DateTime FechaRegistro { get; set; } // fec_regi
        public string CodigoUsuario { get; set; } // cod_usua
        public int EstadoId { get; set; } // est_regi (1 = Activo, 0 = Inactivo)
    }
}
