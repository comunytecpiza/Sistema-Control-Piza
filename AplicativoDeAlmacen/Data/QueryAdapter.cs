using System;
using System.Text.RegularExpressions;
using AplicativoDeAlmacen.Core;

namespace AplicativoDeAlmacen.Data
{
    public static class QueryAdapter
    {
        // Propiedad que verifica el motor
        public static bool EsMySQL => ConfigManager.ObtenerMotor()?.Contains("MySQL") ?? false;

        public static string FormatearConsulta(string queryBase)
        {
            if (string.IsNullOrWhiteSpace(queryBase)) return queryBase;

            if (EsMySQL)
            {
                // 1. Limpiar hints/bloqueos propios de T-SQL que MySQL no soporta
                queryBase = queryBase.Replace("WITH (NOLOCK)", "")
                                     .Replace("WITH(NOLOCK)", "")
                                     .Replace("WITH (TABLOCKX, HOLDLOCK)", "")
                                     .Replace("WITH(TABLOCKX, HOLDLOCK)", "");

                // 2. Reemplazar funciones de Fecha, Cadenas y Nulos
                queryBase = queryBase.Replace("GETDATE()", "NOW()")
                                     .Replace("ISNULL(", "IFNULL(")
                                     .Replace("LEN(", "LENGTH(")
                                     .Replace("CAST(p.id AS VARCHAR)", "CAST(p.id AS CHAR)");

                // 3. Reemplazar ISNUMERIC(...) por expresión REGEXP de MySQL
                // Convierte ISNUMERIC(expresion) = 1  --->  expresion REGEXP '^[0-9]+$'
                queryBase = Regex.Replace(
                    queryBase,
                    @"ISNUMERIC\((.*?)\)\s*=\s*1",
                    "$1 REGEXP '^[0-9]+$'",
                    RegexOptions.IgnoreCase
                );

                // 4. Reemplazar TOP X por LIMIT X si no se usó un if/else separado
                if (Regex.IsMatch(queryBase, @"SELECT\s+TOP\s+(\d+)", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(queryBase, @"SELECT\s+TOP\s+(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string limite = match.Groups[1].Value;
                        // Quita "TOP X" del SELECT
                        queryBase = Regex.Replace(queryBase, @"SELECT\s+TOP\s+\d+", "SELECT", RegexOptions.IgnoreCase);
                        // Agrega "LIMIT X" al final de la consulta si no lo tiene
                        if (!queryBase.ToUpper().Contains("LIMIT"))
                        {
                            queryBase = queryBase.TrimEnd(';', ' ') + $" LIMIT {limite};";
                        }
                    }
                }

                // 5. Manejo de OUTPUT INSERTED.ID / SCOPE_IDENTITY()
                if (queryBase.Contains("OUTPUT INSERTED.ID"))
                {
                    queryBase = queryBase.Replace("OUTPUT INSERTED.ID", "");
                    if (!queryBase.Contains("LAST_INSERT_ID()"))
                    {
                        queryBase = queryBase.TrimEnd(';', ' ') + "; SELECT LAST_INSERT_ID();";
                    }
                }
                queryBase = queryBase.Replace("SCOPE_IDENTITY()", "LAST_INSERT_ID()");

                // 6. Tablas temporales (#temp -> temp_)
                queryBase = queryBase.Replace("#temp_", "temp_");
                queryBase = queryBase.Replace("CREATE TABLE temp_", "CREATE TEMPORARY TABLE IF NOT EXISTS temp_");
                queryBase = queryBase.Replace("DROP TABLE IF EXISTS #", "DROP TEMPORARY TABLE IF EXISTS ");
            }

            return queryBase;
        }
    }
}