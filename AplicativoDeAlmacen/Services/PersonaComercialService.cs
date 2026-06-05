using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using System.Data.SqlClient;


namespace AplicativoDeAlmacen.Services
{
    public class PersonaComercialService
    {
        private readonly DatabaseConnection _database;

        public PersonaComercialService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<PersonaComercial>> BuscarAsync(string texto)
        {
            var lista = new List<PersonaComercial>();

            using var conn = _database.GetConnection();

            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                SELECT TOP 10
                    id,
                    razon_social,
                    ISNULL(direccion,''),
                    ISNULL(ruc,''),
                    CAST(id AS VARCHAR(20))
                FROM personas_comerciales
                WHERE razon_social LIKE @texto", conn);

            cmd.Parameters.AddWithValue("@texto", $"%{texto}%");
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new PersonaComercial
                {
                    Id = reader.GetInt32(0),
                    RazonSocial = reader.GetString(1),
                    Direccion = reader.GetString(2),
                    Ruc = reader.GetString(3),
                   // CodigoMostrar = reader.GetString(4)
                });
            }
            return lista;
        }
    }
}
