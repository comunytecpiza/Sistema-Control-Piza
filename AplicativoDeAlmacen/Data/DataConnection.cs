using System;
using System.Data;
using System.Data.SqlClient;       // Para SQL Server
using MySql.Data.MySqlClient;     // Para MySQL / MariaDB (XAMPP o Hosting)

namespace AplicativoDeAlmacen.Data
{
    public class DataConnection
    {
        public class DatabaseConnection
        {
            public IDbConnection GetConnection()
            {
                string connectionString = ConfigManager.ObtenerCadenaConexion();
                string motor = ConfigManager.ObtenerMotor();

                // Evaluamos dinámicamente qué objeto instanciar
                if (motor.Contains("MySQL"))
                {
                    return new MySqlConnection(connectionString);
                }
                else
                {
                    return new SqlConnection(connectionString);
                }
            }
        }
    }
}