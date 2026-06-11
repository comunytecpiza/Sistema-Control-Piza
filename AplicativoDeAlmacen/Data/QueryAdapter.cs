using System;
using AplicativoDeAlmacen.Core; // Asegúrate de tener este using

namespace AplicativoDeAlmacen.Data
{
    public static class QueryAdapter
    {
        // Propiedad que verifica el motor
        public static bool EsMySQL => ConfigManager.ObtenerMotor()?.Contains("MySQL") ?? false;

        public static string FormatearConsulta(string queryBase)
        {
            if (EsMySQL)
            {
                // Traducimos funciones de SQL Server a MySQL
                queryBase = queryBase.Replace("GETDATE()", "NOW()")
                                     .Replace("ISNULL", "IFNULL")
                                     .Replace("LEN(", "LENGTH(") // ¡Aquí arreglamos el error de LEN!
                                     .Replace("CAST(p.id AS VARCHAR)", "CAST(p.id AS CHAR)");

                // Manejo del OUTPUT INSERTED.ID
                if (queryBase.Contains("OUTPUT INSERTED.ID"))
                {
                    queryBase = queryBase.Replace("OUTPUT INSERTED.ID", "");
                    queryBase += "; SELECT LAST_INSERT_ID();";
                }
            }
            return queryBase;
        }
    }
}