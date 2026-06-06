using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services
{
    public class CodigoCreadoService
    {
        public async Task<List<CodigoCreado>> ObtenerPorRegistroIdAsync(int registroCodigoId)
        {
            var lista = new List<CodigoCreado>();
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();

            string query = "SELECT id, registro_codigo_id, codigo FROM codigos_creados WHERE registro_codigo_id = @id ORDER BY codigo ASC";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", registroCodigoId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new CodigoCreado
                {
                    Id = reader.GetInt32(0),
                    RegistroCodigoId = reader.GetInt32(1),
                    Codigo = reader.GetString(2)
                });
            }
            return lista;
        }
    }
}