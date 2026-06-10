using System;
using System.Text.RegularExpressions;


namespace AplicativoDeAlmacen.Data
{
    public static class QueryAdapter
    {
        public static string FormatearConsulta(string queryBase)
        {
            string motor = ConfigManager.ObtenerMotor();

            if (motor != null && motor.Contains("MySQL"))
            {
                // Traduce funciones de fecha, nulos y tipos de conversión al vuelo
                queryBase = queryBase.Replace("GETDATE()", "NOW()")
                                     .Replace("ISNULL", "IFNULL")
                                     .Replace("CAST(p.id AS VARCHAR)", "CAST(p.id AS CHAR)");

                // INTEGRACIÓN DE TU INSTRUCCIÓN: Traduce el OUTPUT de SQL Server al LAST_INSERT_ID de MySQL
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