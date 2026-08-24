using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Services.Politicas
{
    public static class AuditoriaPoliticas
    {
        public static bool ValidarPlazoEdicion(DateTime? fechaCreacion, int rolUsuarioId, out string mensajeError)
        {
            mensajeError = string.Empty;

            // 👑 Administrador (Rol 1) tiene permiso ilimitado
            if (rolUsuarioId == 1) return true;

            if (!fechaCreacion.HasValue) return true;

            DateTime fechaInicio = fechaCreacion.Value.Date;
            DateTime fechaActual = DateTime.Today;

            if (fechaInicio >= fechaActual) return true;

            int diasHabiles = 0;
            DateTime cursor = fechaInicio;

            while (cursor < fechaActual)
            {
                cursor = cursor.AddDays(1);
                if (cursor.DayOfWeek != DayOfWeek.Saturday && cursor.DayOfWeek != DayOfWeek.Sunday)
                {
                    diasHabiles++;
                }
            }

            if (diasHabiles > 5)
            {
                mensajeError = $"⛔ PLAZO DE EDICIÓN VENCIDO:\n\n" +
                               $"Este movimiento fue registrado el {fechaCreacion.Value:dd/MM/yyyy HH:mm}.\n" +
                               $"Han transcurrido {diasHabiles} días hábiles (el límite permitido es 5 días hábiles).\n\n" +
                               $"Solo un usuario con rol de Administrador puede autorizar o modificar este registro.";
                return false;
            }

            return true;
        }
    }
}
