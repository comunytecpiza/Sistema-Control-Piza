using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Tests
{
    public class PruebasKardexSistemaTests : IAsyncLifetime
    {
        private readonly IngresoMovimientoService _ingresoService;
        private readonly SalidaMovimientoService _salidaService;
        private readonly DatabaseConnection _database;

        // Variables Globales de Prueba (Aquí está declarado PRODUCTO_LIBRO_ID)
        private const int PRODUCTO_LIBRO_ID = 385;
        private const int PRODUCTO_GENERICO_ID = 9999;
        private const int ALMACEN_TRUJILLO = 1;
        private const int ALMACEN_LIMA = 2;
        private const int PROMOTOR_A = 8001;
        private const int PROMOTOR_B = 8002;
        private const int USUARIO_TEST = 1;

        public PruebasKardexSistemaTests()
        {
            _ingresoService = new IngresoMovimientoService();
            _salidaService = new SalidaMovimientoService();
            _database = new DatabaseConnection();
        }

        public async Task InitializeAsync()
        {
            await PrepararDependenciasBDAsync();
        }

        public async Task DisposeAsync()
        {
            await LimpiarBasuraDePruebasAsync();
        }

        // =========================================================================
        // 🧪 BLOQUE 1: VALIDACIONES BÁSICAS Y FORMATOS
        // =========================================================================

        [Theory]
        [InlineData("lma3 c26-g-g-1", "LMA3 C26-G-G-0000001")]
        [InlineData("lma4 c26-v-5", "LMA4 C26-V-0000005")]
        public void T01_Normalizador_DebeFormatearCodigosSuciosACodigosPerfectos(string entrada, string esperado)
        {
            Assert.Equal(esperado, _ingresoService.NormalizarCodigo(entrada));
        }

        [Fact]
        public async Task T02_IngresoTransferencia_MotivoBloqueado_DebeRebotarInmediatamente()
        {
            // Se envía un motivo no permitido para Ingresos (por ejemplo, Motivo 5 = Salida Comercial/Venta)
            var cabecera = GenerarCabeceraEdicion(0, 5);
            cabecera.AlmacenId = ALMACEN_TRUJILLO;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ingresoService.RegistrarMovimientoCompletoAsync(cabecera, new List<VistaProductoGrid>(), new List<RangoCodigoItem>(), 0));

            Assert.Contains("no es válido para este registro de ingreso", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================================
        // 🧪 BLOQUE 2: COMPRAS, EDICIONES DE COMPRAS Y PROTECCIÓN KÁRDEX
        // =========================================================================

        [Fact]
        public async Task T03_IngresoCompraNormal_DebePasarAEstado3YSubirStock()
        {
            var (movId, codigosIds, prefijo, prodId) = await CrearMovimientoIngresoPruebaAsync(3);
            Assert.True(movId > 0, "El Movimiento ID debe ser mayor a 0");

            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[0]}");
            Assert.Equal(3, estado);
        }

        [Fact]
        public async Task T04_EditarCompra_QuitandoCodigoSinFuturo_DebeVolverAEstado1()
        {
            var (movId, codigosIds, prefijo, prodId) = await CrearMovimientoIngresoPruebaAsync(3);

            var cabecera = GenerarCabeceraEdicion(movId, 1);
            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 2 } } };
            var rangos = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 1, HastaNum = 2, Cantidad = "2", CategoriaProductoId = 2 } };

            bool exito = await _ingresoService.RegistrarMovimientoCompletoAsync(cabecera, prods, rangos, 0, movId);
            Assert.True(exito);

            int estadoCod3 = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[2]}");
            Assert.Equal(1, estadoCod3);
        }

        [Fact]
        public async Task T05_EditarCompra_QuitarCodigoConSalida_DebeLanzarErrorDeSeguridadKardex()
        {
            var (movCompraId, codigosIds, prefijo, prodId) = await CrearMovimientoIngresoPruebaAsync(3);

            await CrearSalidaComercialPruebaAsync(new List<int> { codigosIds[0] }, prodId, PROMOTOR_A, DateTime.Today.AddDays(1));

            var cabecera = GenerarCabeceraEdicion(movCompraId, 1);
            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 2 } } };
            var rangos = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 2, HastaNum = 3, Cantidad = "2", CategoriaProductoId = 2 } };

            // Se captura InvalidOperationException y se valida el texto actualizado del servicio
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ingresoService.RegistrarMovimientoCompletoAsync(cabecera, prods, rangos, 0, movCompraId));

            Assert.Contains("Trazabilidad de Kárdex", ex.Message);
        }

        // =========================================================================
        // 🧪 BLOQUE 3: SALIDAS COMERCIALES Y PROMOTORÍAS
        // =========================================================================

        [Fact]
        public async Task T06_SalidaVenta_DebePasarAEstado4()
        {
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(2);
            int movSalidaId = await CrearSalidaComercialPruebaAsync(codigosIds, prodId, PROMOTOR_A);

            Assert.True(movSalidaId > 0);
            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[0]}");
            Assert.Equal(4, estado);
        }

        [Fact]
        public async Task T07_IntentarVenderCodigoDeOtraSede_DebeRebotar()
        {
            // 1. Creamos un código legítimo comprado en Trujillo (Almacén 1)
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(1);
            int codigoTrujilloId = codigosIds[0];

            // 2. Simulamos que ese código fue transferido formalmente a Lima (Almacén 2)
            // Cambiamos su sede actual en la base de datos a Lima
            await EjecutarSqlEscalarAsync($"UPDATE codigos_creados SET almacen_id = {ALMACEN_LIMA}, estado_id = 3 WHERE id = {codigoTrujilloId}");

            // 3. Intentamos despacharlo por Venta (Motivo 5) desde TRUJILLO (Almacén 1)
            var cabecera = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_A);
            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1 } };
            var cods = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoTrujilloId } } };

            // 4. ACT & ASSERT: El servicio de salida DEBE detectar que el código está en Lima y lanzar la excepción de restricción de sede
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _salidaService.RegistrarSalidaCompletaAsync(cabecera, prods, cods, USUARIO_TEST, 1));

            Assert.NotNull(ex.Message);
        }

        [Fact]
        public async Task T08_EditarSalida_QuitarCodigo_DebeRegresarAEstado3()
        {
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(2);
            int movSalidaId = await CrearSalidaComercialPruebaAsync(codigosIds, prodId, PROMOTOR_A);

            var cabecera = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_A, movSalidaId);
            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var cods = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigosIds[0] } } };

            bool exito = await _salidaService.RegistrarSalidaCompletaAsync(cabecera, prods, cods, USUARIO_TEST, 1, movSalidaId);
            Assert.True(exito);

            int estadoCod2 = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[1]}");
            Assert.Equal(3, estadoCod2);
        }

        [Fact]
        public async Task T09_IntercambioEntrePromotores_DebeReasignarYMantenerEstado4()
        {
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(2);

            int movSalidaA = await CrearSalidaComercialPruebaAsync(new List<int> { codigosIds[0], codigosIds[1] }, prodId, PROMOTOR_A);
            int movSalidaB = await CrearSalidaComercialPruebaAsync(new List<int>(), prodId, PROMOTOR_B);

            var cabA = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_A, movSalidaA);
            var prodsA = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var codsA = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigosIds[0] } } };
            await _salidaService.RegistrarSalidaCompletaAsync(cabA, prodsA, codsA, USUARIO_TEST, 1, movSalidaA);

            var cabB = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_B, movSalidaB);
            var prodsB = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var codsB = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigosIds[1] } } };
            await _salidaService.RegistrarSalidaCompletaAsync(cabB, prodsB, codsB, USUARIO_TEST, 1, movSalidaB);

            int estadoCod2 = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[1]}");
            Assert.Equal(4, estadoCod2);
        }

        // =========================================================================
        // 🧪 BLOQUE 4: TRANSFERENCIAS Y RECEPCIÓN
        // =========================================================================

        [Fact]
        public async Task T10_TransferenciaSalidaALima_DebePasarAEstado5()
        {
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(1);

            var cabecera = GenerarCabeceraSalida(10, ALMACEN_TRUJILLO, null);
            cabecera.AlmacenDestinoId = ALMACEN_LIMA;

            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var cods = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigosIds[0] } } };

            bool exito = await _salidaService.RegistrarSalidaCompletaAsync(cabecera, prods, cods, USUARIO_TEST, 1);
            Assert.True(exito);

            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[0]}");
            Assert.Equal(5, estado);
        }

        // =========================================================================
        // 🧪 BLOQUE 5: ANULACIONES COMPLETAS
        // =========================================================================

        [Fact]
        public async Task T11_AnularSalida_DebeRetornarCodigosAEstado3()
        {
            var (_, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(1);
            int movSalidaId = await CrearSalidaComercialPruebaAsync(codigosIds, prodId, PROMOTOR_A);

            bool anulado = await _salidaService.AnularMovimientoSalidaCompletoAsync(movSalidaId);
            Assert.True(anulado);

            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigosIds[0]}");
            Assert.Equal(3, estado);
        }

        [Fact]
        public async Task T12_AnularCompraConSalidasFuturas_DebeRebotar()
        {
            var (movCompraId, codigosIds, _, prodId) = await CrearMovimientoIngresoPruebaAsync(1);
            await CrearSalidaComercialPruebaAsync(codigosIds, prodId, PROMOTOR_A, DateTime.Today.AddDays(1));

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _ingresoService.AnularMovimientoCompletoAsync(movCompraId));

            Assert.Contains("registra movimientos", ex.Message);
        }

        // =========================================================================
        // 🧪 BLOQUE 6: PRODUCTOS GENÉRICOS (MOCHILAS / NINGÚN CÓDIGO)
        // =========================================================================

        [Fact]
        public async Task T13_IngresoYSalidaMochila_NoTocaCodigosSoloSubeStock()
        {
            int prodMochilaId = new Random().Next(10000, 99999);
            await EjecutarSqlEscalarAsync($"INSERT IGNORE INTO productos (id, descripcion, estado_id) VALUES ({prodMochilaId}, 'MOCHILA TEST {prodMochilaId}', 1)");

            string selloIn = "PRUEBA_" + Guid.NewGuid().ToString("N");
            string selloOut = "PRUEBA_" + Guid.NewGuid().ToString("N");

            var cabIngreso = GenerarCabeceraEdicion(0, 1);
            cabIngreso.Observacion = selloIn;
            var prodsI = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodMochilaId, Cantidad = 100, Detalle = new MovimientoDetalle { ProductoId = prodMochilaId, CantidadIngreso = 100 } } };
            bool inExito = await _ingresoService.RegistrarMovimientoCompletoAsync(cabIngreso, prodsI, new List<RangoCodigoItem>(), 0);
            Assert.True(inExito);

            var cabSalida = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_A);
            cabSalida.Observacion = selloOut;
            var prodsS = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodMochilaId, Cantidad = 20, Detalle = new MovimientoDetalle { ProductoId = prodMochilaId, CantidadSalida = 20 } } };
            bool outExito = await _salidaService.RegistrarSalidaCompletaAsync(cabSalida, prodsS, new List<VistaCodigoGrid>(), USUARIO_TEST, 1);
            Assert.True(outExito);

            int stock = await EjecutarSqlEscalarAsync($"SELECT stock_actual FROM stock_almacen WHERE producto_id = {prodMochilaId} AND almacen_id = {ALMACEN_TRUJILLO}");
            Assert.Equal(80, stock);
        }

        // =========================================================================
        // 🧪 BLOQUE 7: CÓDIGOS ALFANUMÉRICOS INDIVIDUALES (SIN SECUENCIA)
        // =========================================================================

        [Fact]
        public async Task T14_IngresoAlfanumericos_DebeProcesarlosIndividualmenteConMenosUno()
        {
            string codigoAlfaUnico = $"ALFA-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
            string now = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE();";

            // 🌟 1. Creamos el registro y el código físico previo en la BD para que exista
            int regId = await EjecutarSqlEscalarAsync($"INSERT INTO registro_codigos (coleccion_id, cantidad, categoria_producto_id, producto_id, usuario_id, created_at) VALUES (1, 1, 2, {PRODUCTO_LIBRO_ID}, 1, {now}); SELECT LAST_INSERT_ID();");
            int codigoIdBD = await EjecutarSqlEscalarAsync($"INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id, almacen_id, usuario_id, created_at) VALUES ({regId}, '{codigoAlfaUnico}', 1, {ALMACEN_TRUJILLO}, 1, {now}); SELECT LAST_INSERT_ID();");

            var cabecera = GenerarCabeceraEdicion(0, 1);
            cabecera.Observacion = "PRUEBA_ALFA_" + Guid.NewGuid().ToString();

            var prods = new List<VistaProductoGrid>
            {
                new VistaProductoGrid { ProductoId = PRODUCTO_LIBRO_ID, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = PRODUCTO_LIBRO_ID, CantidadIngreso = 1 } }
            };

            var vistaCodigos = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = PRODUCTO_LIBRO_ID, CodigoUnique = codigoAlfaUnico, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoIdBD } } };
            var rangosGenerados = _ingresoService.GenerarRangosDesdeCodigos(vistaCodigos);

            bool exito = await _ingresoService.RegistrarMovimientoCompletoAsync(cabecera, prods, rangosGenerados, 0);

            Assert.True(exito, "Debe guardar los alfanuméricos sin explotar.");

            foreach (var rango in rangosGenerados)
            {
                Assert.Equal(-1, rango.DesdeNum);
                Assert.Equal(-1, rango.HastaNum);
                Assert.Equal("1", rango.Cantidad);
            }

            // Ahora sí, como el código fue comprado/ingresado, su estado en la BD subirá a 3 (Disponible)
            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigoIdBD}");
            Assert.Equal(3, estado);
        }

        [Fact]
        public async Task T15_SalidaAlfanumericos_DebeDespacharYPasarAEstado4()
        {
            string codigo1 = $"MIX-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";
            string codigo2 = $"MIX-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";

            string now = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";
            int regId = await EjecutarSqlEscalarAsync($"INSERT INTO registro_codigos (coleccion_id, cantidad, categoria_producto_id, producto_id, usuario_id, created_at) VALUES (1, 2, 2, {PRODUCTO_LIBRO_ID}, 1, {now}); SELECT LAST_INSERT_ID();");

            int cod1Id = await EjecutarSqlEscalarAsync($"INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id, almacen_id, usuario_id, created_at) VALUES ({regId}, '{codigo1}', 3, {ALMACEN_TRUJILLO}, 1, {now}); SELECT LAST_INSERT_ID();");
            int cod2Id = await EjecutarSqlEscalarAsync($"INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id, almacen_id, usuario_id, created_at) VALUES ({regId}, '{codigo2}', 3, {ALMACEN_TRUJILLO}, 1, {now}); SELECT LAST_INSERT_ID();");

            var cabeceraSalida = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_A);
            cabeceraSalida.Observacion = "PRUEBA_ALFA_OUT_" + Guid.NewGuid().ToString();

            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = PRODUCTO_LIBRO_ID, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = PRODUCTO_LIBRO_ID, CantidadSalida = 2 } } };

            var cods = new List<VistaCodigoGrid>
            {
                new VistaCodigoGrid { ProductoId = PRODUCTO_LIBRO_ID, CodigoUnique = codigo1, MovCodigo = new MovimientoCodigo { CodigoCreadoId = cod1Id } },
                new VistaCodigoGrid { ProductoId = PRODUCTO_LIBRO_ID, CodigoUnique = codigo2, MovCodigo = new MovimientoCodigo { CodigoCreadoId = cod2Id } }
            };

            bool outExito = await _salidaService.RegistrarSalidaCompletaAsync(cabeceraSalida, prods, cods, USUARIO_TEST, 1);

            Assert.True(outExito, "Debe despachar los códigos alfanuméricos correctamente.");

            int estado = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {cod1Id}");
            Assert.Equal(4, estado);
        }


        // =========================================================================
        // 🧪 BLOQUE 8: TRANSFERENCIAS, EDICIÓN Y REASIGNACIÓN DE CÓDIGOS
        // =========================================================================

        [Fact]
        public async Task T16_EditarIngresoTransferencia_QuitarCodigoConSalidaPosterior_DebeRebotarPorKardex()
        {
            // 1. Ingreso de Compra inicial en Trujillo (Almacén 1)
            var (movCompraId, codigosIds, prefijo, prodId) = await CrearMovimientoIngresoPruebaAsync(2);
            int codigoObjetivoId = codigosIds[0];

            // 2. Salida por Transferencia (Motivo 10) de Trujillo a Lima (Almacén 2)
            var cabSalidaTransf = GenerarCabeceraSalida(10, ALMACEN_TRUJILLO, null);
            cabSalidaTransf.AlmacenDestinoId = ALMACEN_LIMA;
            var prodsSal = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 2 } } };
            var codsSal = codigosIds.Select(id => new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = id } }).ToList();
            await _salidaService.RegistrarSalidaCompletaAsync(cabSalidaTransf, prodsSal, codsSal, USUARIO_TEST, 1);

            // 3. Recepción/Ingreso por Transferencia (Motivo 4) en Lima
            string selloInLima = "PRUEBA_REC_LIMA_" + Guid.NewGuid().ToString("N");
            var cabIngresoTransf = GenerarCabeceraEdicion(0, 4);
            cabIngresoTransf.Observacion = selloInLima;
            cabIngresoTransf.AlmacenId = ALMACEN_LIMA;
            cabIngresoTransf.AlmacenOrigenId = ALMACEN_TRUJILLO;
            cabIngresoTransf.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsIng = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 2 } } };
            var rangosIng = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 1, HastaNum = 2, Cantidad = "2", CategoriaProductoId = 2 } };
            await _ingresoService.RegistrarMovimientoCompletoAsync(cabIngresoTransf, prodsIng, rangosIng, 0);
            int movIngresoLimaId = await EjecutarSqlEscalarAsync($"SELECT id FROM movimientos WHERE observacion = '{selloInLima}'");

            // 4. Salida comercial / promotoría posterior en Lima usando el código
            var cabSalidaProm = GenerarCabeceraSalida(5, ALMACEN_LIMA, PROMOTOR_A);
            cabSalidaProm.FechaMovimiento = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var prodsProm = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var codsProm = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoObjetivoId } } };
            await _salidaService.RegistrarSalidaCompletaAsync(cabSalidaProm, prodsProm, codsProm, USUARIO_TEST, 1);

            // 5. ACT & ASSERT: Intentar editar la recepción de Lima para quitar ese código debe rebotar
            var cabEdicionLima = GenerarCabeceraEdicion(movIngresoLimaId, 4);
            cabEdicionLima.AlmacenId = ALMACEN_LIMA;
            cabEdicionLima.AlmacenOrigenId = ALMACEN_TRUJILLO;
            cabEdicionLima.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsEdicion = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 1 } } };
            var rangosEdicion = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 2, HastaNum = 2, Cantidad = "1", CategoriaProductoId = 2 } };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ingresoService.RegistrarMovimientoCompletoAsync(cabEdicionLima, prodsEdicion, rangosEdicion, 0, movIngresoLimaId));

            Assert.Contains("Trazabilidad de Kárdex", ex.Message);
        }

        [Fact]
        public async Task T17_FlujoCompleto_QuitarDeRecepcion_QuitarDeSalidaOrigen_YReasignarAOtroPromotor()
        {
            // 1. Ingreso inicial en Trujillo (Almacén 1)
            var (movCompraId, codigosIds, prefijo, prodId) = await CrearMovimientoIngresoPruebaAsync(2);
            int codigoAfectadoId = codigosIds[0];
            int codigoConservadoId = codigosIds[1];

            // 2. Salida por Transferencia (Motivo 10) de Trujillo a Lima (Almacén 2)
            string selloSalidaTransf = "PRUEBA_SAL_TRANSF_" + Guid.NewGuid().ToString("N");
            var cabSalidaTransf = GenerarCabeceraSalida(10, ALMACEN_TRUJILLO, null);
            cabSalidaTransf.Observacion = selloSalidaTransf;
            cabSalidaTransf.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsSal = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 2 } } };
            var codsSal = codigosIds.Select(id => new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = id } }).ToList();
            await _salidaService.RegistrarSalidaCompletaAsync(cabSalidaTransf, prodsSal, codsSal, USUARIO_TEST, 1);
            int movSalidaTransfId = await EjecutarSqlEscalarAsync($"SELECT id FROM movimientos WHERE observacion = '{selloSalidaTransf}'");

            // 3. Recepción en Lima (Motivo 4)
            string selloIngresoLima = "PRUEBA_ING_LIMA_" + Guid.NewGuid().ToString("N");
            var cabIngresoLima = GenerarCabeceraEdicion(0, 4);
            cabIngresoLima.Observacion = selloIngresoLima;
            cabIngresoLima.AlmacenId = ALMACEN_LIMA;
            cabIngresoLima.AlmacenOrigenId = ALMACEN_TRUJILLO;
            cabIngresoLima.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsIngLima = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 2, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 2 } } };
            var rangosIngLima = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 1, HastaNum = 2, Cantidad = "2", CategoriaProductoId = 2 } };
            await _ingresoService.RegistrarMovimientoCompletoAsync(cabIngresoLima, prodsIngLima, rangosIngLima, 0);
            int movIngresoLimaId = await EjecutarSqlEscalarAsync($"SELECT id FROM movimientos WHERE observacion = '{selloIngresoLima}'");

            // --- FASE 1: Quitar código afectado de la Recepción en Lima ---
            var cabEditIngLima = GenerarCabeceraEdicion(movIngresoLimaId, 4);
            cabEditIngLima.AlmacenId = ALMACEN_LIMA;
            cabEditIngLima.AlmacenOrigenId = ALMACEN_TRUJILLO;
            cabEditIngLima.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsEditLima = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = 1 } } };
            var rangosEditLima = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 2, HastaNum = 2, Cantidad = "1", CategoriaProductoId = 2 } };

            bool exitoQuitarRecepcion = await _ingresoService.RegistrarMovimientoCompletoAsync(cabEditIngLima, prodsEditLima, rangosEditLima, 0, movIngresoLimaId);
            Assert.True(exitoQuitarRecepcion);

            // Verificación del descarte: El servicio de ingreso retornó el código a Estado 3 en Trujillo automáticamente
            int estadoTrasQuitarIngreso = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigoAfectadoId}");
            int almacenTrasQuitarIngreso = await EjecutarSqlEscalarAsync($"SELECT almacen_id FROM codigos_creados WHERE id = {codigoAfectadoId}");
            Assert.Equal(3, estadoTrasQuitarIngreso);
            Assert.Equal(ALMACEN_TRUJILLO, almacenTrasQuitarIngreso);

            // --- FASE 2: Quitar el código de la Salida de Transferencia de Trujillo (Almacén A) ---
            var cabEditSalTransf = GenerarCabeceraSalida(10, ALMACEN_TRUJILLO, null, movSalidaTransfId);
            cabEditSalTransf.AlmacenDestinoId = ALMACEN_LIMA;

            var prodsEditSal = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var codsEditSal = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoConservadoId } } };

            bool exitoQuitarSalida = await _salidaService.RegistrarSalidaCompletaAsync(cabEditSalTransf, prodsEditSal, codsEditSal, USUARIO_TEST, 1, movSalidaTransfId);
            Assert.True(exitoQuitarSalida);

            // Verificar desvinculación total de la salida
            int existeEnSalida = await EjecutarSqlEscalarAsync($"SELECT COUNT(*) FROM movimiento_codigos WHERE movimiento_id = {movSalidaTransfId} AND codigo_creado_id = {codigoAfectadoId}");
            Assert.Equal(0, existeEnSalida);

            int estadoDisponibleTrujillo = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigoAfectadoId}");
            int almacenActualTrujillo = await EjecutarSqlEscalarAsync($"SELECT almacen_id FROM codigos_creados WHERE id = {codigoAfectadoId}");
            Assert.Equal(3, estadoDisponibleTrujillo);
            Assert.Equal(ALMACEN_TRUJILLO, almacenActualTrujillo);

            // --- FASE 3: Despachar el código liberado a otro Promotor (Promotor B) desde Trujillo ---
            string selloSalidaPromB = "PRUEBA_SAL_PROMB_" + Guid.NewGuid().ToString("N");
            var cabSalidaPromB = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, PROMOTOR_B);
            cabSalidaPromB.Observacion = selloSalidaPromB;

            var prodsPromB = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = 1, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadSalida = 1 } } };
            var codsPromB = new List<VistaCodigoGrid> { new VistaCodigoGrid { ProductoId = prodId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = codigoAfectadoId } } };

            bool exitoSalidaB = await _salidaService.RegistrarSalidaCompletaAsync(cabSalidaPromB, prodsPromB, codsPromB, USUARIO_TEST, 1);
            Assert.True(exitoSalidaB);

            int estadoFinal = await EjecutarSqlEscalarAsync($"SELECT estado_id FROM codigos_creados WHERE id = {codigoAfectadoId}");
            Assert.Equal(4, estadoFinal);
        }

        // =========================================================================
        // 🛠️ HELPERS (Manejadores de Base de Datos)
        // =========================================================================

        private async Task<(int MovId, List<int> Codigos, string Prefijo, int ProductoId)> CrearMovimientoIngresoPruebaAsync(int cantidad)
        {
            string sello = "PRUEBA_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            string prefijo = "T" + new Random().Next(100, 999) + "-V";
            string now = QueryAdapter.EsMySQL ? "NOW()" : "GETDATE()";

            int prodId = new Random().Next(10000, 99999);
            await EjecutarSqlEscalarAsync($"INSERT IGNORE INTO productos (id, descripcion, estado_id) VALUES ({prodId}, 'LIBRO TEST {prodId}', 1)");

            int regId = await EjecutarSqlEscalarAsync($"INSERT INTO registro_codigos (coleccion_id, cantidad, categoria_producto_id, producto_id, usuario_id, created_at) VALUES (1, {cantidad}, 2, {prodId}, 1, {now}); SELECT LAST_INSERT_ID();");

            var codigos = new List<int>();
            for (int i = 1; i <= cantidad; i++)
            {
                int cId = await EjecutarSqlEscalarAsync($"INSERT INTO codigos_creados (registro_codigo_id, codigo, estado_id, almacen_id, usuario_id, created_at) VALUES ({regId}, '{prefijo}-{i:D7}', 1, 1, 1, {now}); SELECT LAST_INSERT_ID();");
                codigos.Add(cId);
            }

            var cabecera = GenerarCabeceraEdicion(0, 1);
            cabecera.Observacion = sello;
            var prods = new List<VistaProductoGrid> { new VistaProductoGrid { ProductoId = prodId, Cantidad = cantidad, Detalle = new MovimientoDetalle { ProductoId = prodId, CantidadIngreso = cantidad } } };
            var rangos = new List<RangoCodigoItem> { new RangoCodigoItem { productoId = prodId, AbreviaturaBase = prefijo, DesdeNum = 1, HastaNum = cantidad, Cantidad = cantidad.ToString(), CategoriaProductoId = 2 } };

            await _ingresoService.RegistrarMovimientoCompletoAsync(cabecera, prods, rangos, 0);
            int movId = await EjecutarSqlEscalarAsync($"SELECT id FROM movimientos WHERE observacion = '{sello}'");

            return (movId, codigos, prefijo, prodId);
        }

        private async Task<int> CrearSalidaComercialPruebaAsync(List<int> codigosIds, int productoId, int? clienteId = null, DateTime? fechaMov = null)
        {
            string sello = "PRUEBA_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var cabecera = GenerarCabeceraSalida(5, ALMACEN_TRUJILLO, clienteId ?? PROMOTOR_A);
            cabecera.Observacion = sello;
            if (fechaMov.HasValue) cabecera.FechaMovimiento = DateOnly.FromDateTime(fechaMov.Value);

            var prods = new List<VistaProductoGrid>();
            if (codigosIds.Any()) prods.Add(new VistaProductoGrid { ProductoId = productoId, Cantidad = codigosIds.Count, Detalle = new MovimientoDetalle { ProductoId = productoId, CantidadSalida = codigosIds.Count } });
            var cods = codigosIds.Select(id => new VistaCodigoGrid { ProductoId = productoId, MovCodigo = new MovimientoCodigo { CodigoCreadoId = id } }).ToList();

            await _salidaService.RegistrarSalidaCompletaAsync(cabecera, prods, cods, USUARIO_TEST, 1);
            return await EjecutarSqlEscalarAsync($"SELECT id FROM movimientos WHERE observacion = '{sello}'");
        }

        private Movimiento GenerarCabeceraEdicion(int id, int motivo) => new Movimiento
        {
            Id = id,
            FechaMovimiento = DateOnly.FromDateTime(DateTime.Today),
            MotivoProductoId = motivo,
            SerieDocumento = "0001",
            NumeroDocumento = "7" + new Random().Next(100000, 999999),
            AlmacenId = ALMACEN_TRUJILLO,
            AlmacenDestinoId = ALMACEN_TRUJILLO,
            UsuarioId = USUARIO_TEST
        };

        private Movimiento GenerarCabeceraSalida(int motivo, int almId, int? cliente, int id = 0) => new Movimiento
        {
            Id = id,
            FechaMovimiento = DateOnly.FromDateTime(DateTime.Today),
            MotivoProductoId = motivo,
            SerieDocumento = "0001",
            NumeroDocumento = "7" + new Random().Next(100000, 999999),
            AlmacenId = almId,
            AlmacenOrigenId = almId,
            PersonaComercialId = cliente,
            UsuarioId = USUARIO_TEST
        };

        private async Task<int> EjecutarSqlEscalarAsync(string sql)
        {
            using var conn = _database.GetConnection();
            var dbConn = (DbConnection)conn;
            await dbConn.OpenAsync();
            using var cmd = dbConn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(sql);
            var res = await cmd.ExecuteScalarAsync();
            return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
        }

        private async Task PrepararDependenciasBDAsync()
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta($@"
                    SET FOREIGN_KEY_CHECKS = 0;
                    INSERT IGNORE INTO almacenes (id, nombre, estado_id) VALUES ({ALMACEN_TRUJILLO}, 'TEST TRUJILLO', 1), ({ALMACEN_LIMA}, 'TEST LIMA', 1);
                    INSERT IGNORE INTO personas_comerciales (id, razon_social, estado_id) VALUES ({PROMOTOR_A}, 'PROMOTOR A TEST', 1), ({PROMOTOR_B}, 'PROMOTOR B TEST', 1);
                    INSERT IGNORE INTO colecciones (id, descripcion) VALUES (1, 'Coleccion Prueba');
                    SET FOREIGN_KEY_CHECKS = 1;");
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task LimpiarBasuraDePruebasAsync()
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();
                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(@"
                    SET FOREIGN_KEY_CHECKS = 0;
                    DELETE FROM movimiento_codigos WHERE movimiento_id IN (SELECT id FROM movimientos WHERE observacion LIKE 'PRUEBA_%');
                    DELETE FROM registro_rangos WHERE movimiento_detalle_id IN (SELECT id FROM movimiento_detalles WHERE movimiento_id IN (SELECT id FROM movimientos WHERE observacion LIKE 'PRUEBA_%'));
                    DELETE FROM movimiento_detalles WHERE movimiento_id IN (SELECT id FROM movimientos WHERE observacion LIKE 'PRUEBA_%');
                    DELETE FROM movimientos WHERE observacion LIKE 'PRUEBA_%';
                    DELETE FROM codigos_creados WHERE codigo LIKE 'T%-V-%' OR codigo LIKE 'ALFA-%' OR codigo LIKE 'BETA-%' OR codigo LIKE 'GAMMA-%' OR codigo LIKE 'MIX-%' OR codigo = 'T99-V-0009999';
                    DELETE FROM registro_codigos WHERE producto_id > 10000;
                    DELETE FROM stock_almacen WHERE producto_id > 10000;
                    DELETE FROM productos WHERE id > 10000;
                    SET FOREIGN_KEY_CHECKS = 1;");
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }
}