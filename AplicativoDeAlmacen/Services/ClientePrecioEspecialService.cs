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
    public class ClientePrecioEspecialService
    {
        private readonly DatabaseConnection _database;

        public ClientePrecioEspecialService()
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

        /// <summary>
        /// Obtiene TODO el catálogo de productos y muestra si el cliente tiene un precio especial asignado.
        /// Ideal para llenar la grilla de "Definición de Precios".
        /// </summary>
        public async Task<List<dynamic>> ObtenerCatalogoPreciosPorClienteAsync(int clienteId)
        {
            var lista = new List<dynamic>();

            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 Agregamos p.porcentaje AS porcentaje_base
                string query = @"
                    SELECT 
                        p.id AS producto_id,
                        p.descripcion AS producto_descripcion,
                        um.descripcion AS unidad_medida, 
                        p.precio_unitario AS precio_base,
                        p.porcentaje AS porcentaje_base,
                        cpe.id AS precio_especial_id,
                        cpe.precio_unitario AS precio_especial,
                        cpe.porcentaje_bonificacion,
                        cpe.estado_id
                    FROM productos p
                    LEFT JOIN unidad_medida um ON p.unidad_medida_id = um.id
                    LEFT JOIN cliente_precios_especiales cpe 
                           ON p.id = cpe.producto_id 
                          AND cpe.persona_comercial_id = @ClienteId 
                          AND cpe.estado_id = 1
                    WHERE p.estado_id = 1
                    ORDER BY p.descripcion ASC";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                    AgregarParametro(cmd, "@ClienteId", clienteId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new
                            {
                                ProductoId = Convert.ToInt32(reader["producto_id"]),
                                Descripcion = reader["producto_descripcion"] as string,
                                UnidadMedida = reader["unidad_medida"] as string ?? "UNIDAD",
                                PrecioBase = Convert.ToDecimal(reader["precio_base"]),
                                PorcentajeBase = reader["porcentaje_base"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje_base"]) : 0m,

                                PrecioEspecialId = reader["precio_especial_id"] != DBNull.Value ? Convert.ToInt32(reader["precio_especial_id"]) : 0,
                                PrecioEspecial = reader["precio_especial"] != DBNull.Value ? Convert.ToDecimal(reader["precio_especial"]) : 0m,
                                Porcentaje = reader["porcentaje_bonificacion"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje_bonificacion"]) : 0m,

                                TienePrecioEspecial = reader["precio_especial_id"] != DBNull.Value
                            });
                        }
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Guarda (inserta o actualiza) un precio especial para un cliente específico.
        /// </summary>
        public async Task GuardarPrecioEspecialAsync(ClientePrecioEspecial precioEspecial)
        {
            using (var conn = _database.GetConnection())
            {
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                bool esEdicion = precioEspecial.Id > 0;

                string query = esEdicion
                    ? @"UPDATE cliente_precios_especiales 
                        SET precio_unitario = @Precio,
                            porcentaje_bonificacion = @Porcentaje,
                            estado_id = @EstadoId,
                            updated_at = GETDATE()
                        WHERE id = @Id"
                    : @"INSERT INTO cliente_precios_especiales 
                        (persona_comercial_id, producto_id, precio_unitario, porcentaje_bonificacion, usuario_id, estado_id)
                        VALUES (@ClienteId, @ProductoId, @Precio, @Porcentaje, @UsuarioId, @EstadoId)";

                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                    AgregarParametro(cmd, "@Precio", precioEspecial.PrecioUnitario);
                    AgregarParametro(cmd, "@Porcentaje", precioEspecial.PorcentajeBonificacion);
                    AgregarParametro(cmd, "@EstadoId", precioEspecial.EstadoId ?? 1);

                    if (esEdicion)
                    {
                        AgregarParametro(cmd, "@Id", precioEspecial.Id);
                    }
                    else
                    {
                        AgregarParametro(cmd, "@ClienteId", precioEspecial.PersonaComercialId);
                        AgregarParametro(cmd, "@ProductoId", precioEspecial.ProductoId);
                        AgregarParametro(cmd, "@UsuarioId", precioEspecial.UsuarioId);
                    }

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}