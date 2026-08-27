using System;

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

            DateTime inicio = fechaCreacion.Value;
            DateTime fin = DateTime.Now;

            if (inicio >= fin) return true;

            // Jornadas operativas:
            // Lun-Vie: 8:00 a 17:30 = 9.5 hrs/día
            // Sáb: 8:00 a 13:30 = 5.5 hrs/día
            // Límite fijado a 3 días hábiles completos (28.5 horas)
            const double LIMITE_HORAS_HABIL = 28.5;

            double horasHabilesTranscurridas = 0;
            DateTime cursor = inicio;

            while (cursor < fin)
            {
                DateTime siguientePaso = cursor.AddMinutes(30);
                if (siguientePaso > fin) siguientePaso = fin;

                DayOfWeek dia = cursor.DayOfWeek;
                TimeSpan hora = cursor.TimeOfDay;

                if (dia >= DayOfWeek.Monday && dia <= DayOfWeek.Friday)
                {
                    // Lunes a Viernes: 08:00 a 17:30
                    if (hora >= new TimeSpan(8, 0, 0) && hora < new TimeSpan(17, 30, 0))
                    {
                        horasHabilesTranscurridas += (siguientePaso - cursor).TotalHours;
                    }
                }
                else if (dia == DayOfWeek.Saturday)
                {
                    // Sábados: 08:00 a 13:30
                    if (hora >= new TimeSpan(8, 0, 0) && hora < new TimeSpan(13, 30, 0))
                    {
                        horasHabilesTranscurridas += (siguientePaso - cursor).TotalHours;
                    }
                }
                // Domingo se omite

                cursor = siguientePaso;
            }

            if (horasHabilesTranscurridas > LIMITE_HORAS_HABIL)
            {
                mensajeError = $"⛔ PLAZO DE EDICIÓN VENCIDO:\n\n" +
                               $"Este movimiento fue registrado el {fechaCreacion.Value:dd/MM/yyyy HH:mm}.\n" +
                               $"El límite permitido para edición por almacén (3 días hábiles) ha caducado.\n\n" +
                               $"Solo un usuario con rol de Administrador puede autorizar o modificar este registro.";
                return false;
            }

            return true;
        }
    }
}