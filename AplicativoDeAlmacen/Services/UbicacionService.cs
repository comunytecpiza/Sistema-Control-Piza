using System;
using System.Collections.Generic;
using System.Data.SqlClient;
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var u = new Ubicacion
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Direccion = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),

                                // Asignamos objetos completos cargados con sus datos
                                TipoUbicacion = new TipoUbicacion { Id = reader.GetInt32(3), Nombre = reader.IsDBNull(4) ? string.Empty : reader.GetString(4) },
                                Localidad = new Localidad { Id = reader.GetInt32(5), Nombre = reader.IsDBNull(6) ? string.Empty : reader.GetString(6) },
                                Estado = new Estado { Id = reader.GetInt32(7), Nombre = reader.IsDBNull(8) ? string.Empty : reader.GetString(8) }
                            };

                            // Mapeo condicional para objetos opcionales (Ubigeo)
                            if (!reader.IsDBNull(9))
                                u.Departamento = new Departamento { Id = reader.GetInt32(9), Nombre = reader.GetString(10) };

                            if (!reader.IsDBNull(11))
                                u.Provincia = new Provincia { Id = reader.GetInt32(11), Nombre = reader.GetString(12) };

                            if (!reader.IsDBNull(13))
                                u.Distrito = new Distrito { Id = reader.GetInt32(13), Nombre = reader.GetString(14) };

                            lista.Add(u);
                        }
                    }
                }
                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener ubicaciones: " + ex.Message);
            }
        }

        // ... Los métodos ObtenerLocalidades(), ObtenerDepartamentos(), etc., se mantienen retornando List<T> de sus respectivos objetos ...

        public void Insertar(Ubicacion u)
        {
            string query = @"INSERT INTO ubicaciones (descripcion, tipo_ubicacion_id, localidad_id, direccion, departamento_id, provincia_id, distrito_id, estado_id) 
                             VALUES (@Descripcion, @TipoUbicacionId, @LocalidadId, @Direccion, @DepartamentoId, @ProvinciaId, @DistritoId, @EstadoId)";

            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Descripcion", u.Descripcion ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Direccion", u.Direccion ?? string.Empty);

                    // Extraemos los IDs desde las propiedades de objeto reales del modelo
                    cmd.Parameters.AddWithValue("@TipoUbicacionId", u.TipoUbicacion.Id);
                    cmd.Parameters.AddWithValue("@LocalidadId", u.Localidad.Id);
                    cmd.Parameters.AddWithValue("@EstadoId", u.Estado.Id);

                    cmd.Parameters.AddWithValue("@DepartamentoId", (object)u.Departamento?.Id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ProvinciaId", (object)u.Provincia?.Id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DistritoId", (object)u.Distrito?.Id ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void Actualizar(Ubicacion u)
        {
            string query = @"UPDATE ubicaciones 
                             SET descripcion = @Descripcion, tipo_ubicacion_id = @TipoUbicacionId, localidad_id = @LocalidadId, 
                                 direccion = @Direccion, departamento_id = @DepartamentoId, provincia_id = @ProvinciaId, 
                                 distrito_id = @DistritoId, estado_id = @EstadoId
                             WHERE id = @Id";

            using (SqlConnection conn = _database.GetConnection())
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", u.Id);
                    cmd.Parameters.AddWithValue("@Descripcion", u.Descripcion ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Direccion", u.Direccion ?? string.Empty);

                    cmd.Parameters.AddWithValue("@TipoUbicacionId", u.TipoUbicacion.Id);
                    cmd.Parameters.AddWithValue("@LocalidadId", u.Localidad.Id);
                    cmd.Parameters.AddWithValue("@EstadoId", u.Estado.Id);

                    cmd.Parameters.AddWithValue("@DepartamentoId", (object)u.Departamento?.Id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ProvinciaId", (object)u.Provincia?.Id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DistritoId", (object)u.Distrito?.Id ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new TipoUbicacion
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                            });
                        }
                    }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new Localidad
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                            });
                        }
                    }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new Departamento
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                            });
                        }
                    }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@DepartamentoId", departamentoId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lista.Add(new Provincia
                                {
                                    Id = reader.GetInt32(0),
                                    Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                                });
                            }
                        }
                    }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ProvinciaId", provinciaId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lista.Add(new Distrito
                                {
                                    Id = reader.GetInt32(0),
                                    Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                                });
                            }
                        }
                    }
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
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new Estado
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                            });
                        }
                    }
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