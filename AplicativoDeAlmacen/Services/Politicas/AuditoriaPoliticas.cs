using System;
using System.Data.Common;
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Politicas
{
    public static class AuditoriaPoliticas
    {
        /// <summary>
        /// Valida si el rol tiene permiso ilimitado (checkbox EDITAR marcado en el panel) 
        /// o si debe regirse por el límite de 3 días hábiles.
        /// </summary>
        public static bool ValidarPlazoEdicion(DateTime? fechaCreacion, int rolUsuarioId, string codigoModulo, out string mensajeError)
        {
            mensajeError = string.Empty;

            // 👑 Administrador (Rol 1) siempre tiene permiso ilimitado
            if (rolUsuarioId == 1) return true;

            // 🔑 Si en el panel de permisos marcaron "EDITAR" para este módulo, tiene permiso para siempre
            if (TienePermisoEditarActivo(rolUsuarioId, codigoModulo))
            {
                return true;
            }

            // ⏱️ Si no está marcado el permiso de edición ilimitada, se rige por los días hábiles
            return ValidarPlazoEdicion(fechaCreacion, rolUsuarioId, out mensajeError);
        }

        private static bool TienePermisoEditarActivo(int rolUsuarioId, string codigoModulo)
        {
            try
            {
                var database = new DatabaseConnection();
                using var conn = database.GetConnection();
                var dbConn = (DbConnection)conn;
                dbConn.Open();

                string query = @"
                    SELECT COALESCE(rp.puede_editar, 0)
                    FROM rol_permisos rp
                    INNER JOIN modulos_sistema m ON rp.modulo_id = m.id
                    WHERE rp.rol_usuario_id = @RolId
                      AND (m.codigo_modulo = @ModCodigo OR m.nombre_modulo LIKE @ModNombre)";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                var pRol = cmd.CreateParameter();
                pRol.ParameterName = "@RolId";
                pRol.Value = rolUsuarioId;
                cmd.Parameters.Add(pRol);

                var pMod = cmd.CreateParameter();
                pMod.ParameterName = "@ModCodigo";
                pMod.Value = codigoModulo;
                cmd.Parameters.Add(pMod);

                var pNom = cmd.CreateParameter();
                pNom.ParameterName = "@ModNombre";
                pNom.Value = "%" + codigoModulo + "%";
                cmd.Parameters.Add(pNom);

                object? res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    return Convert.ToBoolean(res);
                }
            }
            catch
            {
                // En caso de fallo de red/conexión, sigue la validación estricta por tiempo
            }

            return false;
        }

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
                    if (hora >= new TimeSpan(8, 0, 0) && hora < new TimeSpan(17, 30, 0))
                    {
                        horasHabilesTranscurridas += (siguientePaso - cursor).TotalHours;
                    }
                }
                else if (dia == DayOfWeek.Saturday)
                {
                    if (hora >= new TimeSpan(8, 0, 0) && hora < new TimeSpan(13, 30, 0))
                    {
                        horasHabilesTranscurridas += (siguientePaso - cursor).TotalHours;
                    }
                }

                cursor = siguientePaso;
            }

            if (horasHabilesTranscurridas > LIMITE_HORAS_HABIL)
            {
                mensajeError = $"⛔ PLAZO DE EDICIÓN VENCIDO:\n\n" +
                               $"Este movimiento fue registrado el {fechaCreacion.Value:dd/MM/yyyy HH:mm}.\n" +
                               $"El límite permitido para edición por almacén (3 días hábiles) ha caducado.\n\n" +
                               $"Active el permiso de EDITAR para este rol o solicite la modificación a un Administrador.";
                return false;
            }

            return true;
        }
    }
}