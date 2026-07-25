using System.Collections.Generic;
using AplicativoDeAlmacen.Models.Models;
// 🌟 Asegúrate de incluir el namespace correcto de tu clase Almacen si la pusiste en .Models.Almacen:
using AplicativoDeAlmacen.Models.Almacen;

namespace AplicativoDeAlmacen.Core
{
    public static class SesionSistema
    {
        public static Usuario? UsuarioActual { get; set; }
        public static List<RolPermiso>? PermisosActuales { get; set; }

        // 🌟 PROPIEDADES GLOBALES MULTI-ALMACÉN (ESTAS FALTABAN)
        public static Almacen? AlmacenActual { get; set; }
        public static List<Almacen> AlmacenesPermitidos { get; set; } = new List<Almacen>();
    }
}