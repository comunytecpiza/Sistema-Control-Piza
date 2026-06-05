using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Views;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;


namespace AplicativoDeAlmacen.Services
{
    public class PersonaComercialService
    {
        private readonly DatabaseConnection _database;
        public PersonaComercialService()
        {
            _database = new DatabaseConnection();
        }

        public async Task<List<PersonaComercial>> ObtenerTodosAsync()
        {
            var lista = new List<PersonaComercial>();

            using var conn = _database.GetConnection();
            await conn.OpenAsync();

                    string query = @"
            SELECT
                pc.*,
                tp.nombre AS tipo_persona,
                l.nombre AS localidad,
                zp.descripcion AS zona_promotoria,
                e.nombre AS estado,
                d.nombre AS departamento,
                p.nombre AS provincia,
                di.nombre AS distrito
            FROM personas_comerciales pc
            LEFT JOIN tipo_persona tp ON pc.tipo_persona_id = tp.id
            LEFT JOIN localidades l ON pc.localidad_id = l.id
            LEFT JOIN zona_promotoria zp ON pc.zona_promotoria_id = zp.id
            LEFT JOIN estados e ON pc.estado_id = e.id
            LEFT JOIN departamentos d ON pc.departamento_id = d.id
            LEFT JOIN provincias p ON pc.provincia_id = p.id
            LEFT JOIN distritos di ON pc.distrito_id = di.id";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();


            while (reader.Read())
            {
                var persona = new PersonaComercial
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    TipoPersona = reader["tipo_persona"] as string,
                    Nombres = reader["nombres"] as string,
                    ApellidoPaterno = reader["apellido_paterno"] as string,
                    ApellidoMaterno = reader["apellido_materno"] as string,
                    RazonSocial = reader["razon_social"] as string,
                    NombreComercial = reader["nombre_comercial"] as string,
                    Ruc = reader["ruc"] as string,
                    Dni = reader["dni"] as string,
                    Direccion = reader["direccion"] as string
                };

                persona.Localidad = new Localidad
                {
                    Nombre = reader["localidad"] as string
                };

                persona.Departamento = new Departamento
                {
                    Nombre = reader["departamento"] as string
                };

                persona.Provincia = new Provincia
                {
                    Nombre = reader["provincia"] as string
                };

                persona.Distrito = new Distrito
                {
                    Nombre = reader["distrito"] as string
                };

                persona.ZonaPromotoria = new ZonaPromotoria
                {
                    Descripcion = reader["zona_promotoria"] as string
                };

                persona.Estado = new Estado
                {
                    Nombre = reader["estado"] as string
                };
            }

            return lista;
        }
    }
}
