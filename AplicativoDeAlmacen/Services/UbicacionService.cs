using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;

using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class UbicacionService
    {
        private readonly DatabaseConnection _database;

        public UbicacionService()
        {
            _database = new DatabaseConnection();
        }
        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        public List<Ubicacion> ObtenerTodas()
        {
            var lista = new List<Ubicacion>();

            string query = @"
            SELECT u.id, u.descripcion, u.direccion,
                   u.tipo_ubicacion_id, tu.nombre AS tipo_ubicacion_nombre,
                   u.localidad_id, l.nombre AS localidad_nombre,
                   u.estado_id, e.nombre AS estado_nombre,
                   u.departamento_id, d.nombre AS departamento_nombre,
                   u.provincia_id, p.nombre AS provincia_nombre,
                   u.distrito_id, di.nombre AS distrito_nombre
            FROM ubicaciones u
            LEFT JOIN tipo_ubicacion tu ON u.tipo_ubicacion_id = tu.id
            LEFT JOIN localidades l ON u.localidad_id = l.id
            LEFT JOIN departamentos d ON u.departamento_id = d.id
            LEFT JOIN provincias p ON u.provincia_id = p.id
            LEFT JOIN distritos di ON u.distrito_id = di.id
            LEFT JOIN estados e ON u.estado_id = e.id";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var u = new Ubicacion
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Descripcion = reader["descripcion"]?.ToString() ?? string.Empty,
                        Direccion = reader["direccion"]?.ToString() ?? string.Empty,

                        TipoUbicacion = new TipoUbicacion
                        {
                            Id = Convert.ToInt32(reader["tipo_ubicacion_id"]),
                            Nombre = reader["tipo_ubicacion_nombre"]?.ToString() ?? string.Empty
                        },

                        Localidad = new Localidad
                        {
                            Id = Convert.ToInt32(reader["localidad_id"]),
                            Nombre = reader["localidad_nombre"]?.ToString() ?? string.Empty
                        },

                        Estado = new Estado
                        {
                            Id = Convert.ToInt32(reader["estado_id"]),
                            Nombre = reader["estado_nombre"]?.ToString() ?? string.Empty
                        }
                    };

                    if (reader["departamento_id"] != DBNull.Value)
                    {
                        u.Departamento = new Departamento
                        {
                            Id = Convert.ToInt32(reader["departamento_id"]),
                            Nombre = reader["departamento_nombre"]?.ToString() ?? string.Empty
                        };
                    }

                    if (reader["provincia_id"] != DBNull.Value)
                    {
                        u.Provincia = new Provincia
                        {
                            Id = Convert.ToInt32(reader["provincia_id"]),
                            Nombre = reader["provincia_nombre"]?.ToString() ?? string.Empty
                        };
                    }

                    if (reader["distrito_id"] != DBNull.Value)
                    {
                        u.Distrito = new Distrito
                        {
                            Id = Convert.ToInt32(reader["distrito_id"]),
                            Nombre = reader["distrito_nombre"]?.ToString() ?? string.Empty
                        };
                    }

                    lista.Add(u);
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener ubicaciones: " + ex.Message);
            }
        }


        public void Insertar(Ubicacion u)
        {
            string query = @"
            INSERT INTO ubicaciones
            (
                descripcion,
                tipo_ubicacion_id,
                localidad_id,
                direccion,
                departamento_id,
                provincia_id,
                distrito_id,
                estado_id
            )
            VALUES
            (
                @Descripcion,
                @TipoUbicacionId,
                @LocalidadId,
                @Direccion,
                @DepartamentoId,
                @ProvinciaId,
                @DistritoId,
                @EstadoId
            )";

            using var conn = _database.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Descripcion", u.Descripcion ?? string.Empty);
            AgregarParametro(cmd, "@Direccion", u.Direccion ?? string.Empty);

            AgregarParametro(cmd, "@TipoUbicacionId",
                 (object?)u.TipoUbicacion?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@LocalidadId",
                (object?)u.Localidad?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@EstadoId",
                (object?)u.Estado?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@DepartamentoId",
                (object?)u.Departamento?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@ProvinciaId",
                (object?)u.Provincia?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@DistritoId",
                (object?)u.Distrito?.Id ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public void Actualizar(Ubicacion u)
        {
            string query = @"
            UPDATE ubicaciones
            SET
                descripcion = @Descripcion,
                tipo_ubicacion_id = @TipoUbicacionId,
                localidad_id = @LocalidadId,
                direccion = @Direccion,
                departamento_id = @DepartamentoId,
                provincia_id = @ProvinciaId,
                distrito_id = @DistritoId,
                estado_id = @EstadoId
            WHERE id = @Id";

            using var conn = _database.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            AgregarParametro(cmd, "@Id", u.Id);
            AgregarParametro(cmd, "@Descripcion", u.Descripcion ?? string.Empty);
            AgregarParametro(cmd, "@Direccion", u.Direccion ?? string.Empty);

            AgregarParametro(cmd, "@TipoUbicacionId",
                (object?)u.TipoUbicacion?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@LocalidadId",
                (object?)u.Localidad?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@EstadoId",
                (object?)u.Estado?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@DepartamentoId",
                (object?)u.Departamento?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@ProvinciaId",
                (object?)u.Provincia?.Id ?? DBNull.Value);

            AgregarParametro(cmd, "@DistritoId",
                (object?)u.Distrito?.Id ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        // =========================================================================
        // MÉTODOS DE CARGA INDISPENSABLES PARA LOS COMBOBOXES DE LA UI (CON DATOS)
        // =========================================================================

        public List<TipoUbicacion> ObtenerTiposUbicacion()
        {
            var lista = new List<TipoUbicacion>();
            string query = "SELECT id, nombre FROM tipo_ubicacion ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new TipoUbicacion
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar tipos de ubicación: " + ex.Message);
            }
        }

        public List<Localidad> ObtenerLocalidades()
        {
            var lista = new List<Localidad>();
            string query = "SELECT id, nombre FROM localidades ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Localidad
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar localidades: " + ex.Message);
            }
        }

        public List<Departamento> ObtenerDepartamentos()
        {
            var lista = new List<Departamento>();
            string query = "SELECT id, nombre FROM departamentos ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Departamento
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar departamentos: " + ex.Message);
            }
        }

        public List<Provincia> ObtenerProvincias(int departamentoId)
        {
            var lista = new List<Provincia>();
            string query = "SELECT id, nombre FROM provincias WHERE departamento_id = @DepartamentoId ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                AgregarParametro(cmd, "@DepartamentoId", departamentoId);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Provincia
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar provincias: " + ex.Message);
            }
        }

        public List<Distrito> ObtenerDistritos(int provinciaId)
        {
            var lista = new List<Distrito>();
            string query = "SELECT id, nombre FROM distritos WHERE provincia_id = @ProvinciaId ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                AgregarParametro(cmd, "@ProvinciaId", provinciaId);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Distrito
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar distritos: " + ex.Message);
            }
        }

        public List<Estado> ObtenerEstados()
        {
            var lista = new List<Estado>();
            string query = "SELECT id, nombre FROM estados ORDER BY nombre";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new Estado
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al cargar estados: " + ex.Message);
            }
        }
    }
}