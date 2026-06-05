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

            while (await reader.ReadAsync())
            {
                lista.Add(new PersonaComercial
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),

                    TipoPersona = reader.IsDBNull(reader.GetOrdinal("tipo_persona"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("tipo_persona")),

                    Nombres = reader.IsDBNull(reader.GetOrdinal("nombres"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("nombres")),

                    ApellidoPaterno = reader.IsDBNull(reader.GetOrdinal("apellido_paterno"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("apellido_paterno")),

                    ApellidoMaterno = reader.IsDBNull(reader.GetOrdinal("apellido_materno"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("apellido_materno")),

                    RazonSocial = reader.IsDBNull(reader.GetOrdinal("razon_social"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("razon_social")),

                    NombreComercial = reader.IsDBNull(reader.GetOrdinal("nombre_comercial"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("nombre_comercial")),

                    Ruc = reader.IsDBNull(reader.GetOrdinal("ruc"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("ruc")),

                    Dni = reader.IsDBNull(reader.GetOrdinal("dni"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("dni")),

                    Direccion = reader.IsDBNull(reader.GetOrdinal("direccion"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("direccion")),

                    Localidad = reader.IsDBNull(reader.GetOrdinal("localidad_id"))
                        ? null
                        : new Localidad()
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("localidad_id")),
                            Nombre = reader.GetString(reader.GetOrdinal("localidad"))
                        },

                    Departamento = reader.IsDBNull(reader.GetOrdinal("departamento_id"))
                        ? null
                        : new Departamento ()
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("departamento_id")),
                            Nombre = reader.GetString(reader.GetOrdinal("departamento"))
                        },

                    Provincia = reader.IsDBNull(reader.GetOrdinal("provincia_id"))
                        ? null
                        : new Provincia()
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("provincia_id")),
                            Nombre = reader.GetString(reader.GetOrdinal("provincia"))
                        },

                    Distrito = reader.IsDBNull(reader.GetOrdinal("distrito_id"))
                        ? null
                        : new Distrito()
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("distrito_id")),
                            Nombre = reader.GetString(reader.GetOrdinal("distrito"))
                        },

                    ZonaPromotoria = reader.IsDBNull(reader.GetOrdinal("zona_promotoria_id"))
                        ? null
                        : new ZonaPromotoria
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("zona_promotoria_id")),
                            Descripcion = reader.GetString(reader.GetOrdinal("zona_promotoria"))
                        },

                    Estado = reader.IsDBNull(reader.GetOrdinal("estado_id"))
                        ? null
                        : new Estado
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("estado_id")),
                            Nombre = reader.GetString(reader.GetOrdinal("estado"))
                        }
                });
            }

            return lista;
        }
    }
}
