using System.Collections.Generic;
using System.Linq;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Core
{
    public static class SesionSistema
    {
        public static Usuario UsuarioActual { get; set; }
        public static List<RolPermiso> PermisosActuales { get; set; } = new List<RolPermiso>();

        // Método inteligente para buscar si tienes un permiso específico
        public static RolPermiso ObtenerPermiso(string codigoModulo)
        {
            return PermisosActuales.FirstOrDefault(p => p.CodigoModulo == codigoModulo);
        }
    }
}