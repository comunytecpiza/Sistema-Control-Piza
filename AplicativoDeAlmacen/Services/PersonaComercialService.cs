using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Models.Users;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
                        tpc.nombre AS tipo_persona_comercial,
                        l.nombre AS localidad,
                        zp.descripcion AS zona_promotoria,
                        e.nombre AS estado,
                        d.nombre AS departamento,
                        p.nombre AS provincia,
                        di.nombre AS distrito
                    FROM personas_comerciales pc
                    LEFT JOIN tipo_persona tp ON pc.tipo_persona_id = tp.id
                    LEFT JOIN tipos_persona_comercial tpc ON pc.tipo_persona_comercial_id = tpc.id
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

        // 🌟 BÚSQUEDA RÁPIDA OPTIMIZADA CON LIMIT / TOP
        public async Task<List<PersonaComercial>> BuscarPorRazonSocialAsync(string filtro)
        {
            var lista = new List<PersonaComercial>();
            if (string.IsNullOrWhiteSpace(filtro)) return lista;

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string topClause = QueryAdapter.EsMySQL ? "" : "TOP 30";
                string limitClause = QueryAdapter.EsMySQL ? "LIMIT 30" : "";

                string query = $@"
                    SELECT {topClause} pc.*,
                        tp.nombre AS tipo_persona,
                        tpc.nombre AS tipo_persona_comercial,
                        l.nombre AS localidad,
                        zp.descripcion AS zona_promotoria,
                        e.nombre AS estado,
                        d.nombre AS departamento,
                        p.nombre AS provincia,
                        di.nombre AS distrito
                    FROM personas_comerciales pc
                    LEFT JOIN tipo_persona tp ON pc.tipo_persona_id = tp.id
                    LEFT JOIN tipos_persona_comercial tpc ON pc.tipo_persona_comercial_id = tpc.id
                    LEFT JOIN localidades l ON pc.localidad_id = l.id
                    LEFT JOIN zona_promotoria zp ON pc.zona_promotoria_id = zp.id
                    LEFT JOIN estados e ON pc.estado_id = e.id
                    LEFT JOIN departamentos d ON pc.departamento_id = d.id
                    LEFT JOIN provincias p ON pc.provincia_id = p.id
                    LEFT JOIN distritos di ON pc.distrito_id = di.id
                    WHERE pc.razon_social LIKE @filtro 
                       OR pc.nombres LIKE @filtro 
                       OR pc.nombre_comercial LIKE @filtro
                    ORDER BY pc.razon_social ASC
                    {limitClause}";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@filtro", $"%{filtro.Trim()}%");

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
                    ? @"UPDATE personas_comerciales
                        SET tipo_persona_id = @tipoPersonaId,
                            tipo_persona_comercial_id = @tipoPersonaComercialId,
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
                            distrito_id = @distritoId,
                            usuario_id = @usuarioId
                        WHERE id = @id"
                    : @"INSERT INTO personas_comerciales
                        (tipo_persona_id, tipo_persona_comercial_id, nombres, apellido_paterno, apellido_materno,
                         razon_social, nombre_comercial, ruc, dni, direccion,
                         localidad_id, zona_promotoria_id, estado_id, departamento_id,
                         provincia_id, distrito_id, usuario_id)
                        VALUES
                        (@tipoPersonaId, @tipoPersonaComercialId, @nombres, @apellidoPaterno, @apellidoMaterno,
                         @razonSocial, @nombreComercial, @ruc, @dni, @direccion,
                         @localidadId, @zonaPromotoriaId, @estadoId, @departamentoId,
                         @provinciaId, @distritoId, @usuarioId)";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    AgregarParametro(cmd, "@tipoPersonaId", persona.TipoPersona?.Id);
                    AgregarParametro(cmd, "@tipoPersonaComercialId", persona.TipoPersonaComercial?.Id);
                    AgregarParametro(cmd, "@nombres", persona.Nombres);
                    AgregarParametro(cmd, "@apellidoPaterno", persona.ApellidoPaterno);
                    AgregarParametro(cmd, "@apellidoMaterno", persona.ApellidoMaterno);
                    AgregarParametro(cmd, "@razonSocial", persona.RazonSocial);
                    AgregarParametro(cmd, "@nombreComercial", persona.NombreComercial);
                    AgregarParametro(cmd, "@ruc", persona.Ruc);
                    AgregarParametro(cmd, "@dni", persona.Dni);
                    AgregarParametro(cmd, "@direccion", persona.Direccion);
                    AgregarParametro(cmd, "@localidadId", persona.Localidad?.Id);
                    AgregarParametro(cmd, "@zonaPromotoriaId", persona.ZonaPromotoria?.Id);
                    AgregarParametro(cmd, "@estadoId", persona.Estado?.Id);
                    AgregarParametro(cmd, "@departamentoId", persona.Departamento?.Id);
                    AgregarParametro(cmd, "@provinciaId", persona.Provincia?.Id);
                    AgregarParametro(cmd, "@distritoId", persona.Distrito?.Id);
                    AgregarParametro(cmd, "@usuarioId", persona.UsuarioId);

                    if (esEdicion) AgregarParametro(cmd, "@id", persona.Id);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private PersonaComercial MapearPersonaComercial(DbDataReader reader)
        {
            Localidad localidad = null;
            if (!reader.IsDBNull(reader.GetOrdinal("localidad_id")))
            {
                localidad = new Localidad
                {
                    Id = reader.GetInt32(reader.GetOrdinal("localidad_id")),
                    Nombre = reader["localidad"] as string ?? ""
                };
            }

            ZonaPromotoria zonaPromotoria = null;
            if (!reader.IsDBNull(reader.GetOrdinal("zona_promotoria_id")))
            {
                zonaPromotoria = new ZonaPromotoria
                {
                    Id = reader.GetInt32(reader.GetOrdinal("zona_promotoria_id")),
                    Descripcion = reader["zona_promotoria"] as string ?? ""
                };
            }

            TipoPersonaComercial tipoPersonaComercial = null;
            if (!reader.IsDBNull(reader.GetOrdinal("tipo_persona_comercial_id")))
            {
                tipoPersonaComercial = new TipoPersonaComercial
                {
                    Id = reader.GetInt32(reader.GetOrdinal("tipo_persona_comercial_id")),
                    Nombre = reader["tipo_persona_comercial"] as string ?? ""
                };
            }

            Departamento departamento = !reader.IsDBNull(reader.GetOrdinal("departamento_id")) ? new Departamento { Id = reader.GetInt32(reader.GetOrdinal("departamento_id")), Nombre = reader["departamento"] as string } : null;
            Provincia provincia = !reader.IsDBNull(reader.GetOrdinal("provincia_id")) ? new Provincia { Id = reader.GetInt32(reader.GetOrdinal("provincia_id")), Nombre = reader["provincia"] as string } : null;
            Distrito distrito = !reader.IsDBNull(reader.GetOrdinal("distrito_id")) ? new Distrito { Id = reader.GetInt32(reader.GetOrdinal("distrito_id")), Nombre = reader["distrito"] as string } : null;
            Estado estado = !reader.IsDBNull(reader.GetOrdinal("estado_id")) ? new Estado { Id = reader.GetInt32(reader.GetOrdinal("estado_id")), Nombre = reader["estado"] as string } : null;
            TipoPersona tipoPersona = !reader.IsDBNull(reader.GetOrdinal("tipo_persona_id")) ? new TipoPersona { Id = reader.GetInt32(reader.GetOrdinal("tipo_persona_id")), Nombre = reader["tipo_persona"] as string } : null;

            return new PersonaComercial
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                TipoPersona = tipoPersona,
                TipoPersonaComercial = tipoPersonaComercial,
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
                ZonaPromotoria = zonaPromotoria,
                UsuarioId = reader["usuario_id"] != DBNull.Value ? Convert.ToInt32(reader["usuario_id"]) : (int?)null
            };
        }

        public async Task<PersonaComercial> ObtenerPorIdAsync(int id)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                string query = @"
                SELECT p.id, p.razon_social, p.direccion, p.ruc, p.dni, p.nombres, p.apellido_paterno, 
                       l.nombre as localidad_nombre, z.descripcion as zona_desc
                FROM personas_comerciales p
                LEFT JOIN localidades l ON p.localidad_id = l.id
                LEFT JOIN zona_promotoria z ON p.zona_promotoria_id = z.id
                WHERE p.id = @id";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    var p = cmd.CreateParameter();
                    p.ParameterName = "@id";
                    p.Value = id;
                    cmd.Parameters.Add(p);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new PersonaComercial
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                RazonSocial = reader["razon_social"] != DBNull.Value ? reader["razon_social"].ToString() : null,
                                Direccion = reader["direccion"] != DBNull.Value ? reader["direccion"].ToString() : null,
                                Ruc = reader["ruc"] != DBNull.Value ? reader["ruc"].ToString() : null,
                                Dni = reader["dni"] != DBNull.Value ? reader["dni"].ToString() : null,
                                Nombres = reader["nombres"] != DBNull.Value ? reader["nombres"].ToString() : null,
                                ApellidoPaterno = reader["apellido_paterno"] != DBNull.Value ? reader["apellido_paterno"].ToString() : null,
                                Localidad = new Localidad
                                {
                                    Nombre = reader["localidad_nombre"] != DBNull.Value ? reader["localidad_nombre"].ToString() : string.Empty
                                },
                                ZonaPromotoria = new ZonaPromotoria
                                {
                                    Descripcion = reader["zona_desc"] != DBNull.Value ? reader["zona_desc"].ToString() : string.Empty
                                }
                            };
                        }
                    }
                }
            }
            return null;
        }
    }
}