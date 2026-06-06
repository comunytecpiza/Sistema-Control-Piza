using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using AplicativoDeAlmacen.Models.Models; // Aquí deben estar tus clases Localidad y ZonaPromotoria
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class ZonaPromotoriaService
    {
        private readonly DatabaseConnection _database;

        public ZonaPromotoriaService()
        {
            // Usamos tu clase de conexión real
            _database = new DatabaseConnection();
        }

        // Obtener todas las localidades como una Lista de OBJETOS
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
                            // Creamos el objeto en memoria y mapeamos sus propiedades
                            var localidad = new Localidad
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Nombre = reader["nombre"].ToString()
                            };
                            lista.Add(localidad);
                        }
                    }
                }
                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener las localidades: " + ex.Message);
            }
        }

        // Obtener zonas como una Lista de OBJETOS filtrada por ID de localidad
        public List<ZonaPromotoria> ObtenerZonasPorLocalidad(int localidadId)
        {
            var lista = new List<ZonaPromotoria>();
            string query = "SELECT id, descripcion, localidad_id FROM zona_promotoria WHERE localidad_id = @LocalidadId";

            try
            {
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@LocalidadId", localidadId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Creamos el objeto ZonaPromotoria real
                                var zona = new ZonaPromotoria
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    Descripcion = reader["descripcion"].ToString(),
                                    LocalidadId = Convert.ToInt32(reader["localidad_id"])
                                };
                                lista.Add(zona);
                            }
                        }
                    }
                }
                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener las zonas: " + ex.Message);
            }
        }

        // Registrar una nueva zona
        public void RegistrarZona(string descripcion, int localidadId)
        {
            string query = "INSERT INTO zona_promotoria (descripcion, localidad_id) VALUES (@Descripcion, @LocalidadId)";

            try
            {
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                        cmd.Parameters.AddWithValue("@LocalidadId", localidadId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error al registrar la zona: " + ex.Message);
            }
        }

        // Eliminar una zona por su ID
        public void EliminarZona(int zonaId)
        {
            string query = "DELETE FROM zona_promotoria WHERE id = @ZonaId";

            try
            {
                using (SqlConnection conn = _database.GetConnection())
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ZonaId", zonaId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error al eliminar la zona: " + ex.Message);
            }
        }
    }
}