using System;
using System.Collections.Generic;
using System.Data;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class ZonaPromotoriaService
    {
        private readonly DatabaseConnection _database;

        public ZonaPromotoriaService()
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
                throw new Exception("Error al obtener las localidades: " + ex.Message);
            }
        }

        public List<ZonaPromotoria> ObtenerZonasPorLocalidad(int localidadId)
        {
            var lista = new List<ZonaPromotoria>();

            string query = @"
                SELECT id, descripcion, localidad_id
                FROM zona_promotoria
                WHERE localidad_id = @LocalidadId";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                AgregarParametro(cmd, "@LocalidadId", localidadId);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new ZonaPromotoria
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Descripcion = reader["descripcion"]?.ToString() ?? string.Empty,
                        LocalidadId = Convert.ToInt32(reader["localidad_id"])
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al obtener las zonas: " + ex.Message);
            }
        }

        public void RegistrarZona(string descripcion, int localidadId)
        {
            string query = @"
                INSERT INTO zona_promotoria
                (
                    descripcion,
                    localidad_id
                )
                VALUES
                (
                    @Descripcion,
                    @LocalidadId
                )";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                AgregarParametro(cmd, "@Descripcion", descripcion);
                AgregarParametro(cmd, "@LocalidadId", localidadId);

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Error al registrar la zona: " + ex.Message);
            }
        }

        public void EliminarZona(int zonaId)
        {
            string query = "DELETE FROM zona_promotoria WHERE id = @ZonaId";

            try
            {
                using var conn = _database.GetConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                AgregarParametro(cmd, "@ZonaId", zonaId);

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception("Error al eliminar la zona: " + ex.Message);
            }
        }
    }
}