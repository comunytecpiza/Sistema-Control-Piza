#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Services.Facturacion
{
    public class VentasMatrizService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public VentasMatrizService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        private static void AgregarParametro(DbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<(
            List<UbicacionMatrizDTO> UbicacionesComerciales,
            List<UbicacionMatrizDTO> AlmacenesReales,
            List<ProductoColumnaDTO> CatalogoProductos,
            List<Almacen> AlmacenesRegistrados,
            Dictionary<int, decimal> IngresosCentralPorProducto)> ObtenerMatrizVentasCompletaAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            int? ubicacionIdFiltro = null,
            int almacenId = 1)
        {
            var ubicacionesMap = new Dictionary<string, UbicacionMatrizDTO>(StringComparer.OrdinalIgnoreCase);
            var almacenesRealesMap = new Dictionary<int, UbicacionMatrizDTO>();
            var catalogo = new List<ProductoColumnaDTO>();
            var almacenesList = new List<Almacen>();
            var ingresosCentralMap = new Dictionary<int, decimal>();

            // 1. Pestañas operativas fijas (siempre existen)
            ubicacionesMap["CONSIGNACION"] = new UbicacionMatrizDTO
            {
                UbicacionId = 99901,
                Nombre = "CONSIGNACION",
                TipoUbicacionId = 5,
                TipoUbicacionNombre = "CONSIGNACION"
            };

            ubicacionesMap["FERIAS"] = new UbicacionMatrizDTO
            {
                UbicacionId = 99902,
                Nombre = "FERIAS",
                TipoUbicacionId = 2,
                TipoUbicacionNombre = "FERIAS"
            };

            ubicacionesMap["OTROS"] = new UbicacionMatrizDTO
            {
                UbicacionId = 99903,
                Nombre = "OTROS",
                TipoUbicacionId = 2,
                TipoUbicacionNombre = "OTROS / DONACIONES"
            };

            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();

            string nolock = QueryAdapter.EsMySQL ? "" : "WITH (NOLOCK)";
            DateTime fechaHastaFinDia = fechaHasta.Date.AddDays(1).AddTicks(-1);

            // =========================================================================
            // 2. SEDES FÍSICAS (Almacenes Reales)
            // =========================================================================
            string qAlm = $"SELECT id, nombre, codigo, direccion, estado_id FROM almacenes {nolock} WHERE estado_id = 1 ORDER BY id ASC";
            using (var cmdAlm = dbConn.CreateCommand())
            {
                cmdAlm.CommandText = QueryAdapter.FormatearConsulta(qAlm);
                using var rdrAlm = await cmdAlm.ExecuteReaderAsync();
                while (await rdrAlm.ReadAsync())
                {
                    int aId = rdrAlm.GetInt32(0);
                    string aNom = rdrAlm.GetString(1).Trim().ToUpper();

                    almacenesList.Add(new Almacen
                    {
                        Id = aId,
                        Nombre = aNom,
                        Codigo = rdrAlm.IsDBNull(2) ? "" : rdrAlm.GetString(2),
                        Direccion = rdrAlm.IsDBNull(3) ? "" : rdrAlm.GetString(3),
                        EstadoId = rdrAlm.IsDBNull(4) ? 1 : rdrAlm.GetInt32(4)
                    });

                    almacenesRealesMap[aId] = new UbicacionMatrizDTO
                    {
                        UbicacionId = aId,
                        Nombre = aNom,
                        TipoUbicacionId = 1,
                        TipoUbicacionNombre = "ALMACEN REAL"
                    };
                }
            }

            // =========================================================================
            // 3. CATÁLOGO ACADÉMICO (SOLO LIBROS VENTA CON CÓDIGO Y NIVEL)
            //    Los productos sin código / sin nivel (familia "OTROS") quedan descartados.
            // =========================================================================
            string qProd = $@"
SELECT 
    p.id,
    p.abreviatura,
    p.descripcion,
    p.nivel_id,
    COALESCE(n.nombre, 'OTROS') AS nivel_nombre,
    COALESCE(p.grado_id, 0) AS grado_id,
    COALESCE(g.nombre, '') AS grado_nombre,
    COALESCE(p.titulo_curso_id, 0) AS titulo_curso_id,
    COALESCE(tc.nombre, 'VARIOS') AS titulo_curso_nombre
FROM productos p {nolock}
LEFT JOIN niveles n {nolock} ON p.nivel_id = n.id
LEFT JOIN grados g {nolock} ON p.grado_id = g.id
LEFT JOIN titulo_curso tc {nolock} ON p.titulo_curso_id = tc.id
WHERE p.estado_id = 1
  AND p.nivel_id IS NOT NULL
  AND p.abreviatura IS NOT NULL
  AND LTRIM(RTRIM(p.abreviatura)) <> ''
ORDER BY 
    p.nivel_id ASC, 
    CASE 
        WHEN g.nombre LIKE '%2%' THEN 2
        WHEN g.nombre LIKE '%3%' THEN 3
        WHEN g.nombre LIKE '%4%' THEN 4
        WHEN g.nombre LIKE '%5%' THEN 5
        WHEN g.nombre LIKE '%1%' THEN 6
        WHEN g.nombre LIKE '%6%' THEN 11
        ELSE 99 
    END ASC,
    p.titulo_curso_id ASC,
    p.grado_id ASC, 
    p.id ASC";

            using (var cmdProd = dbConn.CreateCommand())
            {
                cmdProd.CommandText = QueryAdapter.FormatearConsulta(qProd);
                using var rdrProd = await cmdProd.ExecuteReaderAsync();
                while (await rdrProd.ReadAsync())
                {
                    int pId = rdrProd.GetInt32(0);
                    string abrevRaw = rdrProd.IsDBNull(1) ? "" : rdrProd.GetString(1);
                    string desc = rdrProd.IsDBNull(2) ? "" : rdrProd.GetString(2);
                    bool tieneNivel = !rdrProd.IsDBNull(3);
                    string nivelNombre = rdrProd.IsDBNull(4) ? "OTROS" : rdrProd.GetString(4);
                    int gradoId = rdrProd.GetInt32(5);
                    string gradoNombre = rdrProd.IsDBNull(6) ? "" : rdrProd.GetString(6);
                    string familia = rdrProd.IsDBNull(8) ? "VARIOS" : rdrProd.GetString(8);

                    // 🛑 Reporte exclusivo de libros / productos con código
                    if (string.IsNullOrWhiteSpace(abrevRaw) || !tieneNivel) continue;

                    string abrevUp = abrevRaw.ToUpper();
                    string tipoEdicion = (abrevUp.Contains("-V-V") || abrevUp.Contains("-V") || desc.ToUpper().Contains("VENTA")) ? "V" : "G";

                    if (tipoEdicion != "V") continue;

                    catalogo.Add(new ProductoColumnaDTO
                    {
                        ProductoId = pId,
                        Codigo = abrevRaw,
                        Descripcion = desc,
                        NivelId = rdrProd.GetInt32(3),
                        NivelNombre = nivelNombre.ToUpper().Trim(),
                        GradoId = gradoId,
                        GradoNombre = gradoNombre,
                        FamiliaNombre = familia.ToUpper().Trim(),
                        TipoEdicion = "V"
                    });
                }
            }

            // =========================================================================
            // 4. INGRESOS FIJOS EN CENTRAL
            // =========================================================================
            string qIngCentral = $@"
SELECT 
    md.producto_id,
    COALESCE(SUM(md.cantidad_ingreso), 0) AS total_ingreso_central
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
WHERE mp.tipo_movimiento_id = 1
  AND m.motivo_producto_id IN (1, 13)
  AND COALESCE(m.almacen_destino_id, COALESCE(m.almacen_id, 1)) = 1
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 4
GROUP BY md.producto_id";

            using (var cmdIngCentral = dbConn.CreateCommand())
            {
                cmdIngCentral.CommandText = QueryAdapter.FormatearConsulta(qIngCentral);
                AgregarParametro(cmdIngCentral, "@FechaHasta", fechaHastaFinDia);
                using var rdrIng = await cmdIngCentral.ExecuteReaderAsync();
                while (await rdrIng.ReadAsync())
                {
                    ingresosCentralMap[rdrIng.GetInt32(0)] = rdrIng.GetDecimal(1);
                }
            }

            // =========================================================================
            // 5. CARGA DE UBICACIONES COMERCIALES
            // =========================================================================
            string qUbi = $@"
SELECT 
    u.id, 
    u.descripcion, 
    COALESCE(u.tipo_ubicacion_id, 2) AS tipo_ubicacion_id, 
    COALESCE(tu.nombre, 'PUNTO DE VENTA') AS tipo_nombre 
FROM ubicaciones u {nolock}
LEFT JOIN tipo_ubicacion tu {nolock} ON u.tipo_ubicacion_id = tu.id
WHERE u.estado_id = 1 
  AND u.tipo_ubicacion_id IN (1, 2, 5)
ORDER BY u.tipo_ubicacion_id ASC, u.descripcion ASC";

            using (var cmdUbi = dbConn.CreateCommand())
            {
                cmdUbi.CommandText = QueryAdapter.FormatearConsulta(qUbi);
                using var rdrU = await cmdUbi.ExecuteReaderAsync();
                while (await rdrU.ReadAsync())
                {
                    int uId = rdrU.GetInt32(0);
                    if (ubicacionIdFiltro.HasValue && ubicacionIdFiltro.Value > 0 && uId != ubicacionIdFiltro.Value)
                        continue;

                    string clave = $"PTO_{uId}";
                    int tId = rdrU.GetInt32(2);

                    ubicacionesMap[clave] = new UbicacionMatrizDTO
                    {
                        UbicacionId = uId,
                        Nombre = rdrU.GetString(1).Trim().ToUpper(),
                        TipoUbicacionId = tId,
                        TipoUbicacionNombre = tId == 1 ? "ALMACEN REFERENCIAL" : (tId == 5 ? "PUNTO DE VENTA EXTERNO" : "PUNTO DE VENTA")
                    };
                }
            }

            // =========================================================================
            // 6. MOVIMIENTOS Y ASIGNACIÓN EXACTA
            // =========================================================================
            string qMovs = $@"
SELECT 
    m.id,
    m.motivo_producto_id,
    mp.tipo_movimiento_id,
    CONCAT(COALESCE(m.serie_documento, ''), '-', COALESCE(m.numero_documento, '')) AS registro,
    CONCAT(COALESCE(m.serie_guia, '000'), '-', COALESCE(m.numero_guia, '0000000')) AS guia,
    COALESCE(pc.razon_social, CONCAT(pc.nombres, ' ', pc.apellido_paterno), u.descripcion, 'CLIENTES VARIOS') AS entidad_nombre,
    COALESCE(alm_orig.nombre, 'ALMACEN PRINCIPAL TRUJILLO') AS almacen_origen_nombre,
    COALESCE(m.almacen_origen_id, m.almacen_id, 1) AS alm_origen_id,
    COALESCE(m.almacen_destino_id, m.almacen_id, 1) AS alm_destino_id,
    m.fecha_movimiento,
    md.producto_id,
    CASE WHEN mp.tipo_movimiento_id = 1 THEN md.cantidad_ingreso ELSE md.cantidad_salida END AS cantidad,
    m.ubicacion_id,
    COALESCE(u.descripcion, '') AS ubicacion_nombre
FROM movimiento_detalles md {nolock}
INNER JOIN movimientos m {nolock} ON md.movimiento_id = m.id
INNER JOIN motivo_productos mp {nolock} ON m.motivo_producto_id = mp.id
LEFT JOIN personas_comerciales pc {nolock} ON m.persona_comercial_id = pc.id
LEFT JOIN ubicaciones u {nolock} ON m.ubicacion_id = u.id
LEFT JOIN almacenes alm_orig {nolock} ON m.almacen_origen_id = alm_orig.id
WHERE m.fecha_movimiento >= @FechaDesde
  AND m.fecha_movimiento <= @FechaHasta
  AND m.estado_id != 4
ORDER BY m.fecha_movimiento ASC, m.id ASC";

            using (var cmdMovs = dbConn.CreateCommand())
            {
                cmdMovs.CommandText = QueryAdapter.FormatearConsulta(qMovs);
                AgregarParametro(cmdMovs, "@FechaDesde", fechaDesde.Date);
                AgregarParametro(cmdMovs, "@FechaHasta", fechaHastaFinDia);

                // Solo movimientos de productos del catálogo (libros con código)
                var catalogoIds = new HashSet<int>(catalogo.Select(p => p.ProductoId));

                using var rdrMov = await cmdMovs.ExecuteReaderAsync();
                while (await rdrMov.ReadAsync())
                {
                    int prodId = rdrMov.GetInt32(10);
                    if (!catalogoIds.Contains(prodId)) continue;

                    int movId = rdrMov.GetInt32(0);
                    int motivoId = rdrMov.GetInt32(1);
                    int tipoMovId = rdrMov.GetInt32(2);
                    string reg = Convert.ToString(rdrMov.GetValue(3)) ?? "";
                    string guia = Convert.ToString(rdrMov.GetValue(4)) ?? "";
                    string entidad = Convert.ToString(rdrMov.GetValue(5)) ?? "CLIENTES VARIOS";
                    string almOrigNom = Convert.ToString(rdrMov.GetValue(6)) ?? "ALMACEN PRINCIPAL TRUJILLO";
                    int almOrigId = rdrMov.GetInt32(7);
                    int almDestId = rdrMov.GetInt32(8);
                    DateTime fec = rdrMov.IsDBNull(9) ? DateTime.MinValue : rdrMov.GetDateTime(9);
                    decimal cant = rdrMov.GetDecimal(11);
                    int? uId = rdrMov.IsDBNull(12) ? (int?)null : rdrMov.GetInt32(12);
                    string ubiNom = Convert.ToString(rdrMov.GetValue(13)) ?? "";

                    string textoRastreo = $"{entidad} {ubiNom}".ToUpperInvariant();

                    bool esEntradaNeta = false;

                    if (tipoMovId == 1)
                    {
                        if (motivoId == 1 || motivoId == 13)
                        {
                            esEntradaNeta = true;
                        }
                        else if (motivoId == 4 || motivoId == 10) // Transferencias entre sedes
                        {
                            if (almDestId != 1)
                            {
                                bool vieneDePunto = uId.HasValue
                                    && ubicacionesMap.TryGetValue($"PTO_{uId.Value}", out var ubiOrigen)
                                    && ubiOrigen.TipoUbicacionId != 1;
                                if (!vieneDePunto) esEntradaNeta = true;
                            }
                        }
                    }

                    // =========================================================================
                    // CASO 1: INGRESOS NETOS A UNA SEDE REAL
                    // =========================================================================
                    if (tipoMovId == 1 && esEntradaNeta)
                    {
                        if (almacenesRealesMap.ContainsKey(almDestId))
                        {
                            almacenesRealesMap[almDestId].Movimientos.Add(new MatrizKardexItemDTO
                            {
                                MovimientoId = movId,
                                BloqueTipo = 1,
                                OrdenDocumento = reg,
                                NumeroGuia = guia,
                                OrigenAlmacen = almOrigNom,
                                OrigenAlmacenId = almOrigId,
                                Fecha = fec,
                                ProductoId = prodId,
                                Cantidad = cant
                            });
                        }
                        continue;
                    }

                    // =========================================================================
                    // CASO 2: DEVOLUCIONES RECIBIDAS (TIPO 1 Y NO FUE ENTRADA NETA)
                    // =========================================================================
                    if (tipoMovId == 1)
                    {
                        var itemDevolucion = new MatrizKardexItemDTO
                        {
                            MovimientoId = movId,
                            BloqueTipo = 3,
                            OrdenDocumento = reg,
                            NumeroGuia = guia,
                            OrigenAlmacen = entidad,
                            OrigenAlmacenId = almOrigId,
                            Fecha = fec,
                            ProductoId = prodId,
                            Cantidad = cant
                        };

                        if (uId.HasValue && uId.Value > 1 && ubicacionesMap.ContainsKey($"PTO_{uId.Value}"))
                        {
                            ubicacionesMap[$"PTO_{uId.Value}"].Movimientos.Add(itemDevolucion);
                        }
                        else
                        {
                            var ubiCoincidente = ubicacionesMap.Values.FirstOrDefault(u =>
                                u.UbicacionId != 1 && (textoRastreo.Contains(u.Nombre) || u.Nombre.Contains(entidad.ToUpperInvariant())));

                            if (ubiCoincidente != null)
                            {
                                ubiCoincidente.Movimientos.Add(itemDevolucion);
                            }
                            else if (almDestId == 1 && almacenesRealesMap.ContainsKey(almOrigId) && almOrigId != 1)
                            {
                                almacenesRealesMap[almOrigId].Movimientos.Add(itemDevolucion);
                            }
                            else if (textoRastreo.Contains("FERIA") || motivoId == 8)
                            {
                                ubicacionesMap["FERIAS"].Movimientos.Add(itemDevolucion);
                            }
                            else
                            {
                                ubicacionesMap["CONSIGNACION"].Movimientos.Add(itemDevolucion);
                            }
                        }
                        continue;
                    }

                    // =========================================================================
                    // CASO 3: SALIDAS (TIPO 2)  -> UN SOLO BLOQUE (antes estaba duplicado)
                    // =========================================================================
                    if (tipoMovId == 2)
                    {
                        var itemSalida = new MatrizKardexItemDTO
                        {
                            MovimientoId = movId,
                            BloqueTipo = 2,
                            OrdenDocumento = reg,
                            NumeroGuia = guia,
                            OrigenAlmacen = entidad, // Nombre del colegio / entidad
                            OrigenAlmacenId = almOrigId,
                            Fecha = fec,
                            ProductoId = prodId,
                            Cantidad = cant
                        };

                        // 1. Donación (7) u Otros salida (12) -> exclusivo a "OTROS"
                        if (motivoId == 7 || motivoId == 12)
                        {
                            ubicacionesMap["OTROS"].Movimientos.Add(itemSalida);
                        }
                        // 2. Feria (8)
                        else if (motivoId == 8 || textoRastreo.Contains("FERIA"))
                        {
                            ubicacionesMap["FERIAS"].Movimientos.Add(itemSalida);
                        }
                        // 3. Consignación (5)
                        else if (motivoId == 5 || textoRastreo.Contains("CONSIGNAC"))
                        {
                            ubicacionesMap["CONSIGNACION"].Movimientos.Add(itemSalida);
                        }
                        // 4. Ubicación comercial (puntos de venta, promotorías)
                        else if (uId.HasValue && ubicacionesMap.ContainsKey($"PTO_{uId.Value}"))
                        {
                            if (ubicacionesMap[$"PTO_{uId.Value}"].TipoUbicacionId != 1)
                            {
                                ubicacionesMap[$"PTO_{uId.Value}"].Movimientos.Add(itemSalida);
                            }
                        }
                        // 5. Coincidencia por nombre de entidad comercial
                        else
                        {
                            var ubiCoincidente = ubicacionesMap.Values.FirstOrDefault(u =>
                                u.UbicacionId != 1 && (textoRastreo.Contains(u.Nombre) || u.Nombre.Contains(entidad.ToUpperInvariant())));

                            if (ubiCoincidente != null)
                                ubiCoincidente.Movimientos.Add(itemSalida);
                            else
                                ubicacionesMap["CONSIGNACION"].Movimientos.Add(itemSalida);
                        }
                    }
                }
            }

            // Las 3 pestañas fijas se mantienen en la lista final
            var ubicacionesFinales = ubicacionesMap.Values
                .Where(u => u.Movimientos.Any() || u.UbicacionId >= 99901)
                .ToList();

            return (ubicacionesFinales, almacenesRealesMap.Values.ToList(), catalogo, almacenesList, ingresosCentralMap);
        }
    }
}