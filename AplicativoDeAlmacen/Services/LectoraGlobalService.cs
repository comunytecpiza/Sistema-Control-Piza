using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Facturación;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class LectoraGlobalService
    {
        private readonly DatabaseConnection _database = new DatabaseConnection();

        public async Task<LectoraResultDTO> ObtenerCodigoAsync(string codigoEscaneado)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;

            await dbConn.OpenAsync();

            string codigoLimpio = codigoEscaneado.Replace("'", "-").Trim();

            string topClause = QueryAdapter.EsMySQL ? "" : "TOP 1";
            string limitClause = QueryAdapter.EsMySQL ? "LIMIT 1" : "";

            // 🌟 Consulta directa ultrarrápida usando tu esquema exacto
            string sql = $@"
SELECT {topClause}
    cc.id AS codigo_id,
    cc.codigo,
    cc.estado_id,
    cc.almacen_id,
    rc.producto_id,
    rc.categoria_producto_id,
    cp.nombre AS categoria_producto,
    p.descripcion,
    COALESCE(p.precio_unitario, 0) AS precio_unitario,
    CASE WHEN cc.estado_id = 4 THEN 1 ELSE 0 END AS tiene_salida
FROM codigos_creados cc
INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
INNER JOIN productos p ON rc.producto_id = p.id
LEFT JOIN categoria_producto cp ON rc.categoria_producto_id = cp.id
WHERE cc.codigo = @codigo OR REPLACE(cc.codigo, '''', '-') = @codigo
{limitClause};";

            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 5; // Evita que la app se cuelgue si la conexión oscila

            var prm = cmd.CreateParameter();
            prm.ParameterName = "@codigo";
            prm.Value = codigoLimpio;
            cmd.Parameters.Add(prm);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new Exception($"El código '{codigoLimpio}' no existe en el sistema.");

            return new LectoraResultDTO
            {
                CodigoCreadoId = Convert.ToInt32(reader["codigo_id"]),
                CodigoCompleto = reader["codigo"]?.ToString() ?? string.Empty,
                EstadoId = Convert.ToInt32(reader["estado_id"]),
                AlmacenId = reader["almacen_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["almacen_id"]),
                ProductoId = Convert.ToInt32(reader["producto_id"]),
                CategoriaProductoId = Convert.ToInt32(reader["categoria_producto_id"]),
                CategoriaProducto = reader["categoria_producto"]?.ToString() ?? string.Empty,
                DescripcionProducto = reader["descripcion"]?.ToString() ?? string.Empty,
                PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                UnidadMedida = "PACK",
                MovimientoId = 0,
                TipoMovimiento = string.Empty,
                TieneSalida = Convert.ToInt32(reader["tiene_salida"]) == 1
            };
        }
    }
}