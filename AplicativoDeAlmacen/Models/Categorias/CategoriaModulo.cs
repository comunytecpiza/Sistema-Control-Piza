using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models
{
    public class CategoriaModulo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Icono { get; set; }
        public string Color { get; set; }
        public int Orden { get; set; }
        public bool Estado { get; set; }
    }

    public class ModuloSistema
    {
        public int Id { get; set; }
        public string CodigoModulo { get; set; }
        public string NombreModulo { get; set; }
        public int CategoriaId { get; set; }
        public int Orden { get; set; }
        public string ControlWpf { get; set; }
        public bool Estado { get; set; }

        // Propiedad extendida para la UI
        public string NombreCategoria { get; set; }
    }

    public class VistaDetectada
    {
        public string NombreVista { get; set; }
        public string RutaCompleta { get; set; }
        public bool Seleccionada { get; set; }
    }
}
