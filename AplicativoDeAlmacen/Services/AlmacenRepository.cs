using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using AplicativoDeAlmacen.Models.Almacen;

namespace AplicativoDeAlmacen.Data
{
    public class AlmacenRepository
    {
        private readonly string _connectionString = "Server=localhost;Database=tu_bd;Uid=root;Pwd=;";

        public List<Almacen> ObtenerTodos()
        {
            var lista = new List<Almacen>();
            using (var con = new MySqlConnection(_connectionString))
            {
                con.Open();
                string query = "SELECT id, nombre, codigo, direccion, estado_id FROM almacenes ORDER BY id DESC";
                using (var cmd = new MySqlCommand(query, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Almacen
                        {
                            Id = reader.GetInt32("id"),
                            Nombre = reader.GetString("nombre"),
                            Codigo = reader.IsDBNull(reader.GetOrdinal("codigo")) ? string.Empty : reader.GetString("codigo"),
                            Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString("direccion"),
                            EstadoId = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 1 : reader.GetInt32("estado_id")
                        });
                    }
                }
            }
            return lista;
        }

        public bool Guardar(Almacen a)
        {
            using (var con = new MySqlConnection(_connectionString))
            {
                con.Open();
                string query = @"INSERT INTO almacenes (nombre, codigo, direccion, estado_id) 
                                VALUES (@nombre, @codigo, @direccion, @estado_id)";
                using (var cmd = new MySqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@nombre", a.Nombre);
                    cmd.Parameters.AddWithValue("@codigo", string.IsNullOrWhiteSpace(a.Codigo) ? (object)DBNull.Value : a.Codigo);
                    cmd.Parameters.AddWithValue("@direccion", string.IsNullOrWhiteSpace(a.Direccion) ? (object)DBNull.Value : a.Direccion);
                    cmd.Parameters.AddWithValue("@estado_id", a.EstadoId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool Actualizar(Almacen a)
        {
            using (var con = new MySqlConnection(_connectionString))
            {
                con.Open();
                string query = @"UPDATE almacenes 
                                SET nombre = @nombre, codigo = @codigo, direccion = @direccion, estado_id = @estado_id 
                                WHERE id = @id";
                using (var cmd = new MySqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@id", a.Id);
                    cmd.Parameters.AddWithValue("@nombre", a.Nombre);
                    cmd.Parameters.AddWithValue("@codigo", string.IsNullOrWhiteSpace(a.Codigo) ? (object)DBNull.Value : a.Codigo);
                    cmd.Parameters.AddWithValue("@direccion", string.IsNullOrWhiteSpace(a.Direccion) ? (object)DBNull.Value : a.Direccion);
                    cmd.Parameters.AddWithValue("@estado_id", a.EstadoId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool CambiarEstado(int id, int nuevoEstado)
        {
            using (var con = new MySqlConnection(_connectionString))
            {
                con.Open();
                string query = "UPDATE almacenes SET estado_id = @estado WHERE id = @id";
                using (var cmd = new MySqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@estado", nuevoEstado);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
    }
}