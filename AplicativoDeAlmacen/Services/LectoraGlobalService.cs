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

            string sql = @"

SELECT TOP 1

cc.id,
cc.codigo,
cc.estado_id,

rc.producto_id,

cp.id AS categoria_producto_id,
cp.nombre AS categoria_producto,

p.descripcion,
p.precio_unitario,

m.id AS movimiento_id,
mp.tipo_movimiento,

CASE
    WHEN EXISTS
    (
        SELECT 1
        FROM movimiento_codigos mc2
        INNER JOIN movimientos m2
            ON mc2.movimiento_id = m2.id
        INNER JOIN motivo_productos mp2
            ON m2.motivo_producto_id = mp2.id
        WHERE mc2.codigo_creado_id = cc.id
        AND LOWER(mp2.tipo_movimiento)='salida'
    )
    THEN 1
    ELSE 0
END AS tiene_salida

FROM codigos_creados cc

INNER JOIN registro_codigos rc
ON cc.registro_codigo_id=rc.id

INNER JOIN categoria_producto cp
ON rc.categoria_producto_id=cp.id

INNER JOIN productos p
ON rc.producto_id=p.id

LEFT JOIN movimiento_codigos mc
ON cc.id=mc.codigo_creado_id

LEFT JOIN movimiento_detalles md
ON mc.movimiento_detalle_id=md.id

LEFT JOIN movimientos m
ON md.movimiento_id=m.id

LEFT JOIN motivo_productos mp
ON m.motivo_producto_id=mp.id

WHERE REPLACE(cc.codigo,'''','-')=@codigo

ORDER BY
m.fecha_movimiento DESC,
m.created_at DESC";

            using var cmd = dbConn.CreateCommand();

            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);

            AgregarParametro(cmd, "@codigo", codigoLimpio);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new Exception("Código inexistente.");

            return new LectoraResultDTO
            {
                CodigoCreadoId = Convert.ToInt32(reader["id"]),
                CodigoCompleto = reader["codigo"].ToString(),

                EstadoId = Convert.ToInt32(reader["estado_id"]),

                ProductoId = Convert.ToInt32(reader["producto_id"]),

                DescripcionProducto = reader["descripcion"].ToString(),

                PrecioUnitario =
                    reader["precio_unitario"] == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(reader["precio_unitario"]),

                MovimientoId =
                    reader["movimiento_id"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(reader["movimiento_id"]),

                TipoMovimiento =
                    reader["tipo_movimiento"]?.ToString()?.ToLower(),

                CategoriaProductoId =
                    Convert.ToInt32(reader["categoria_producto_id"]),

                CategoriaProducto =
                    reader["categoria_producto"].ToString(),

                TieneSalida =
                    Convert.ToBoolean(reader["tiene_salida"])
            };
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();

            p.ParameterName = nombre;
            p.Value = valor;

            cmd.Parameters.Add(p);
        }
    }
}