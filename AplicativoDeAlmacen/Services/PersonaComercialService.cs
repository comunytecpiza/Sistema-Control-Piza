using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common; // Clave para la compatibilidad multi-motor
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

        // =======================================================
        // FUNCIÓN AYUDANTE MULTI-MOTOR
        // =======================================================
        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var parametro = cmd.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(parametro);
        }

        public async Task<List<PersonaComercial>> ObtenerTodosAsync()
        {
            var lista = new List<PersonaComercial>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

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

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(MapearPersonaComercial(reader));
                        }
                    }
                }
            }

            return lista;
        }

        public async Task GuardarAsync(PersonaComercial persona)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                bool esEdicion = persona.Id > 0;

                string query = esEdicion
                    ? @"
                    UPDATE personas_comerciales
                    SET
                        tipo_persona_id = @tipoPersonaId,
                        nombres = @nombres,
                        apellido_paterno = @apellidoPaterno,
                        apellido_materno = @apellidoMaterno,
                        razon_social = @razonSocial,
                        nombre_comercial = @nombreComercial,
                        ruc = @ruc,
                        dni = @dni,
                        direccion = @direccion,
                        localidad_id = @localidadId,
                        zona_promotoria_id = @zonaPromotoriaId,
                        estado_id = @estadoId,
                        departamento_id = @departamentoId,
                        provincia_id = @provinciaId,
                        distrito_id = @distritoId
                    WHERE id = @id"
                    : @"
                    INSERT INTO personas_comerciales
                    (
                        tipo_persona_id, nombres, apellido_paterno, apellido_materno,
                        razon_social, nombre_comercial, ruc, dni, direccion,
                        localidad_id, zona_promotoria_id, estado_id, departamento_id,
                        provincia_id, distrito_id
                    )
                    VALUES
                    (
                        @tipoPersonaId, @nombres, @apellidoPaterno, @apellidoMaterno,
                        @razonSocial, @nombreComercial, @ruc, @dni, @direccion,
                        @localidadId, @zonaPromotoriaId, @estadoId, @departamentoId,
                        @provinciaId, @distritoId
                    )";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    AgregarParametro(cmd, "@tipoPersonaId", (object?)persona.TipoPersona?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@nombres", (object?)persona.Nombres ?? DBNull.Value);
                    AgregarParametro(cmd, "@apellidoPaterno", (object?)persona.ApellidoPaterno ?? DBNull.Value);
                    AgregarParametro(cmd, "@apellidoMaterno", (object?)persona.ApellidoMaterno ?? DBNull.Value);
                    AgregarParametro(cmd, "@razonSocial", (object?)persona.RazonSocial ?? DBNull.Value);
                    AgregarParametro(cmd, "@nombreComercial", (object?)persona.NombreComercial ?? DBNull.Value);
                    AgregarParametro(cmd, "@ruc", (object?)persona.Ruc ?? DBNull.Value);
                    AgregarParametro(cmd, "@dni", (object?)persona.Dni ?? DBNull.Value);
                    AgregarParametro(cmd, "@direccion", (object?)persona.Direccion ?? DBNull.Value);
                    AgregarParametro(cmd, "@localidadId", (object?)persona.Localidad?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@zonaPromotoriaId", (object?)persona.ZonaPromotoria?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@estadoId", (object?)persona.Estado?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@departamentoId", (object?)persona.Departamento?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@provinciaId", (object?)persona.Provincia?.Id ?? DBNull.Value);
                    AgregarParametro(cmd, "@distritoId", (object?)persona.Distrito?.Id ?? DBNull.Value);

                    if (esEdicion)
                    {
                        AgregarParametro(cmd, "@id", persona.Id);
                    }

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<List<PersonaComercial>> BuscarPorRazonSocialAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();

            if (string.IsNullOrWhiteSpace(filtro)) return lista;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

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
                    LEFT JOIN distritos di ON pc.distrito_id = di.id
                    WHERE pc.razon_social LIKE @filtro OR pc.nombres LIKE @filtro
                    ORDER BY pc.razon_social ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    // Aquí estaba el error de tu compañero. Ahora usamos AgregarParametro.
                    AgregarParametro(cmd, "@filtro", $"%{filtro}%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(MapearPersonaComercial(reader));
                        }
                    }
                }
            }

            return lista;
        }

        // =======================================================
        // METODO AUXILIAR: Para no repetir el código de mapeo
        // =======================================================
        private PersonaComercial MapearPersonaComercial(DbDataReader reader)
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

            return new PersonaComercial
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
        }
    }
}