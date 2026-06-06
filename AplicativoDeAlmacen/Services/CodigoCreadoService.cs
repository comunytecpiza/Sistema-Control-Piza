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

            // Ahora seleccionamos es_manual y estado_id
            string query = "SELECT id, registro_codigo_id, codigo, es_manual, estado_id FROM codigos_creados WHERE registro_codigo_id = @id ORDER BY es_manual ASC, codigo ASC";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", registroCodigoId);
            using var reader = await cmd.ExecuteReaderAsync();

            // En tu método ObtenerPorRegistroIdAsync:
            while (await reader.ReadAsync())
            {
                lista.Add(new CodigoCreado
                {
                    Id = reader.GetInt32(0),
                    RegistroCodigoId = reader.GetInt32(1),
                    // CAMBIO: Verificamos si es nulo antes de leer el string
                    Codigo = reader.IsDBNull(2) ? "SIN CÓDIGO" : reader.GetString(2),
                    EsManual = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                    EstadoId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                });
            }
            return lista;
        }

        // NUEVO: Método para insertar el código suelto
        public async Task RegistrarManualAsync(int registroId, string codigo)
        {
            using var conn = new SqlConnection(ConfigManager.ObtenerCadenaConexion());
            await conn.OpenAsync();
            string query = "INSERT INTO codigos_creados (registro_codigo_id, codigo, es_manual, estado_id) VALUES (@rid, @cod, 1, 1)";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@rid", registroId);
            cmd.Parameters.AddWithValue("@cod", codigo);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}