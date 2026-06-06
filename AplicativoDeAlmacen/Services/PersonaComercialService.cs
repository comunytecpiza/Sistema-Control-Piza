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
            SELECT pc.*,
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
                Localidad localidad = null;
                if (!reader.IsDBNull(reader.GetOrdinal("localidad_id")))
                {
                    localidad = new Localidad
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("localidad_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("localidad"))
                    };
                }

                Departamento departamento = null;
                if (!reader.IsDBNull(reader.GetOrdinal("departamento_id")))
                {
                    departamento = new Departamento
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("departamento_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("departamento"))
                    };
                }

                Provincia provincia = null;
                if (!reader.IsDBNull(reader.GetOrdinal("provincia_id")))
                {
                    provincia = new Provincia
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("provincia_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("provincia"))
                    };
                }

                Distrito distrito = null;
                if (!reader.IsDBNull(reader.GetOrdinal("distrito_id")))
                {
                    distrito = new Distrito
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("distrito_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("distrito"))
                    };
                }

                ZonaPromotoria zonaPromotoria = null;
                if (!reader.IsDBNull(reader.GetOrdinal("zona_promotoria_id")))
                {
                    zonaPromotoria = new ZonaPromotoria
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("zona_promotoria_id")),
                        Descripcion = reader.GetString(reader.GetOrdinal("zona_promotoria"))
                    };
                }

                Estado estado = null;
                if (!reader.IsDBNull(reader.GetOrdinal("estado_id")))
                {
                    estado = new Estado
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("estado_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("estado"))
                    };
                }


                TipoPersona tipoPersona = null;
                if (!reader.IsDBNull(reader.GetOrdinal("tipo_persona_id")))
                {
                    tipoPersona = new TipoPersona
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("tipo_persona_id")),
                        Nombre = reader.GetString(reader.GetOrdinal("tipo_persona"))
                    };
                }


                PersonaComercial persona = new PersonaComercial
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    TipoPersona = tipoPersona,
                    Nombres = reader["nombres"] as string,
                    ApellidoPaterno = reader["apellido_paterno"] as string,
                    ApellidoMaterno = reader["apellido_materno"] as string,
                    RazonSocial = reader["razon_social"] as string,
                    NombreComercial = reader["nombre_comercial"] as string,
                    Ruc = reader["ruc"] as string,
                    Dni = reader["dni"] as string,
                    Direccion = reader["direccion"] as string,

                    Localidad = localidad,
                    Departamento = departamento,
                    Provincia = provincia,
                    Distrito = distrito,
                    Estado = estado,
                    ZonaPromotoria = zonaPromotoria
                };
                

                lista.Add(persona);
            }

            return lista;
        }


        public async Task GuardarAsync(PersonaComercial persona)
        {
            using var conn = _database.GetConnection();
            await conn.OpenAsync();
            bool esEdicion = persona.Id > 0;
            string query = esEdicion
                ? @"
                    UPDATE personas_comerciales
                    SET
                        tipo_persona_id = @tipoPersonaId,nombres = @nombres,apellido_paterno = @apellidoPaterno,apellido_materno = @apellidoMaterno,
                        razon_social = @razonSocial, nombre_comercial = @nombreComercial,
                        ruc = @ruc,dni = @dni,direccion = @direccion,localidad_id = @localidadId, zona_promotoria_id = @zonaPromotoriaId,estado_id = @estadoId,
                        departamento_id = @departamentoId, provincia_id = @provinciaId,
                        distrito_id = @distritoId
                    WHERE id = @id"
                            : @"
                    INSERT INTO personas_comerciales
                    (
                        tipo_persona_id,nombres,apellido_paterno, apellido_materno,razon_social, nombre_comercial, ruc, dni,direccion,localidad_id,
                        zona_promotoria_id,estado_id,departamento_id,provincia_id,distrito_id
                    )
                    VALUES
                    (
                        @tipoPersonaId,@nombres,@apellidoPaterno,@apellidoMaterno, @razonSocial,@nombreComercial,
                        @ruc,@dni, @direccion, @localidadId,@zonaPromotoriaId, @estadoId,@departamentoId,@provinciaId,@distritoId
                    )";

            using var cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@tipoPersonaId", persona.TipoPersona?.Id);
            cmd.Parameters.AddWithValue("@nombres", (object?)persona.Nombres ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@apellidoPaterno", (object?)persona.ApellidoPaterno ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@apellidoMaterno",(object?)persona.ApellidoMaterno ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@razonSocial",(object?)persona.RazonSocial ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nombreComercial", (object?)persona.NombreComercial ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ruc",(object?)persona.Ruc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dni", (object?)persona.Dni ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@direccion", (object?)persona.Direccion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@localidadId", persona.Localidad?.Id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@zonaPromotoriaId", persona.ZonaPromotoria?.Id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@estadoId",persona.Estado?.Id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@departamentoId",persona.Departamento?.Id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@provinciaId",persona.Provincia?.Id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@distritoId",persona.Distrito?.Id ?? (object)DBNull.Value);

            if (esEdicion)
            {
                cmd.Parameters.AddWithValue("@id", persona.Id);
            }

            await cmd.ExecuteNonQueryAsync();
        }
    }

}
