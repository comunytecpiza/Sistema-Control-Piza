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

            public DatabaseConnection()
            {
                // ¡CORREGIDO! Sin Integrated Security, para que obligatoriamente use 'sa'
                _connectionString = @"Server=192.168.1.103;
                                      Database=EdicionesPizaControl;
                                      User Id=sa;
                                      Password=123456;
                                      TrustServerCertificate=True;";
            }

            public SqlConnection GetConnection()
            {
                return new SqlConnection(_connectionString);
            }
        }
    }
}