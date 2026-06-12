using AplicativoDeAlmacen.Data;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class GenardorRangoService
    {
        // Instanciamos tu clase de conexión respetando la directiva static superior
        private readonly DatabaseConnection _database;

        public GenardorRangoService()
        {
            _database = new DatabaseConnection();
        }

        /// <summary>
        /// Valida en SQL si un rango correlativo de códigos choca con las existencias actuales del Kardex.
        /// </summary>
        /// <param name="abreviatura">Raíz del producto (Ej: 'JA2-C25-G')</param>
        /// <param name="desde">Número inicial (Ej: 1)</param>
        /// <param name="hasta">Número final (Ej: 5)</param>
        /// <param name="tipoMovimiento">'INGRESO' o 'SALIDA'</param>
        /// <returns>Lista de códigos que causan conflicto. Si está vacía, el rango es válido.</returns>
        public List<string> ValidarCodigosKardexDirecto(string abreviatura, int desde, int hasta, string tipoMovimiento)
        {
            List<string> codigosConConflicto = new List<string>();
            string query = "";

            // 1. Elegimos la validación según el sentido del Kardex (estado_id 1: Disponible, 2: Vendido)
            if (tipoMovimiento.ToUpper() == "INGRESO")
            {
                // ERROR: Si el código ya existe en el almacén como DISPONIBLE (No se permite duplicar entrada)
                query = @"
                    SELECT codigo 
                    FROM [dbo].[codigos_creados]
                    WHERE codigo LIKE @Abreviatura + '-%'
                      AND ISNUMERIC(RIGHT(codigo, 7)) = 1
                      AND CAST(RIGHT(codigo, 7) AS INT) BETWEEN @Desde AND @Hasta
                      AND estado_id = 1";
            }
            else if (tipoMovimiento.ToUpper() == "SALIDA")
            {
                // ERROR: Si el código que se intenta despachar ya figura como VENDIDO / ENTREGADO
                query = @"
                    SELECT codigo 
                    FROM [dbo].[codigos_creados]
                    WHERE codigo LIKE @Abreviatura + '-%'
                      AND ISNUMERIC(RIGHT(codigo, 7)) = 1
                      AND CAST(RIGHT(codigo, 7) AS INT) BETWEEN @Desde AND @Hasta
                      AND estado_id = 2";
            }

            // 2. Consumimos tu método real GetConnection() corregido 🚀
            using (SqlConnection conn = _database.GetConnection())
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // Pasamos los parámetros de forma segura contra Inyección SQL
                    cmd.Parameters.AddWithValue("@Abreviatura", abreviatura);
                    cmd.Parameters.AddWithValue("@Desde", desde);
                    cmd.Parameters.AddWithValue("@Hasta", hasta);

                    try
                    {
                        if (conn.State == ConnectionState.Closed)
                        {
                            conn.Open(); // Abre la conexión creada por tu GetConnection
                        }

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Añadimos los códigos infractores
                                codigosConConflicto.Add(reader["codigo"].ToString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error al validar el rango de códigos en el Kardex: " + ex.Message);
                    }
                    // El bloque 'using' se encargará de cerrar y destruir la conexión 'conn' automáticamente al terminar
                }
            }

            return codigosConConflicto;
        }
    }
}