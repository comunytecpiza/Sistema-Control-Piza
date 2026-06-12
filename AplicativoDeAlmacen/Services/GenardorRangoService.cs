using AplicativoDeAlmacen.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using static AplicativoDeAlmacen.Data.DataConnection; 

namespace AplicativoDeAlmacen.Services
{
    public class GenardorRangoService
    {
        private readonly DatabaseConnection _database;

        public GenardorRangoService()
        {
            _database = new DatabaseConnection();
        }

        /// <summary>
        /// Valida en SQL si un rango correlativo de códigos choca con las existencias actuales del Kardex.
        /// </summary>
        public List<string> ValidarCodigosKardexDirecto(string abreviatura, int desde, int hasta, string tipoMovimiento)
        {
            List<string> codigosConConflicto = new List<string>();
            string query = "";

            // 1. Elegimos la validación según el sentido del Kardex
            // Nota: Quitamos el [dbo]. para que no explote si se usa MySQL
            if (tipoMovimiento.ToUpper() == "INGRESO")
            {
                query = @"
                    SELECT codigo 
                    FROM codigos_creados
                    WHERE codigo LIKE @Abreviatura
                      AND ISNUMERIC(RIGHT(codigo, 7)) = 1
                      AND CAST(RIGHT(codigo, 7) AS INT) BETWEEN @Desde AND @Hasta
                      AND estado_id = 1";
            }
            else if (tipoMovimiento.ToUpper() == "SALIDA")
            {
                query = @"
                    SELECT codigo 
                    FROM codigos_creados
                    WHERE codigo LIKE @Abreviatura
                      AND ISNUMERIC(RIGHT(codigo, 7)) = 1
                      AND CAST(RIGHT(codigo, 7) AS INT) BETWEEN @Desde AND @Hasta
                      AND estado_id = 2";
            }

            // 2. Usamos la conexión abstracta (soporta SQL Server y MySQL)
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;

                try
                {
                    if (dbConn.State == ConnectionState.Closed)
                    {
                        dbConn.Open();
                    }

                    using (var cmd = dbConn.CreateCommand())
                    {
                        // Formateamos la consulta por si están usando MySQL
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                        // Creación de parámetros universales (DbParameter)
                        var paramAbr = cmd.CreateParameter();
                        paramAbr.ParameterName = "@Abreviatura";
                        paramAbr.Value = abreviatura + "-%"; // El comodín % se agrega aquí, es más seguro
                        cmd.Parameters.Add(paramAbr);

                        var paramDesde = cmd.CreateParameter();
                        paramDesde.ParameterName = "@Desde";
                        paramDesde.Value = desde;
                        cmd.Parameters.Add(paramDesde);

                        var paramHasta = cmd.CreateParameter();
                        paramHasta.ParameterName = "@Hasta";
                        paramHasta.Value = hasta;
                        cmd.Parameters.Add(paramHasta);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                codigosConConflicto.Add(reader["codigo"].ToString());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al validar el rango de códigos en el Kardex: " + ex.Message);
                }
            }

            return codigosConConflicto;
        }
    }
}