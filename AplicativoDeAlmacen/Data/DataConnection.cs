using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;

namespace AplicativoDeAlmacen.Data
{
    class DataConnection
    {
        public class DatabaseConnection
        {
            private readonly string _connectionString;

            public SqlConnection GetConnection()
            {
                // Cada vez que un servicio pida conectarse, leerá el archivo TXT
                string connectionString = ConfigManager.ObtenerCadenaConexion();
                return new SqlConnection(connectionString);
            }

            
        }
    }
}