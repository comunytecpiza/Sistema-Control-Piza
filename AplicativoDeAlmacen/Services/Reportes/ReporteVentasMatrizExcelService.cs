#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services.Facturacion;
using ClosedXML.Excel;

namespace AplicativoDeAlmacen.Services.Reportes
{
    public class ReporteVentasMatrizExcelService
    {
        private const string HojaIndice = "ÍNDICE";
        private const string HojaResumenGlobal = "RESUMEN GENERAL SEDES";
        private const string FormatoNumero = "#,##0";

        private enum ModoHoja
        {
            SedeReal,   // Ingresos + Salidas + Devoluciones
            Comercial,  // Salidas + Devoluciones (con colegio)
            Otros       // Solo salidas (sin colegio)
        }

        /// <summary>Posición de las columnas de productos dentro de una hoja.</summary>
        private sealed class LayoutColumnas
        {
            public Dictionary<int, int> MapProducto { get; } = new Dictionary<int, int>();
            public List<(int ColSubtotal, int ColInicio, int ColFin)> Subtotales { get; } = new List<(int, int, int)>();
            public int UltimaColumna { get; set; }
        }

        /// <summary>Estado compartido mientras se arma una hoja de resumen.</summary>
        private sealed class ContextoResumen
        {
            public ContextoResumen(IXLWorksheet ws, LayoutColumnas lay, Dictionary<int, string> mapNombres, bool esGlobal, int filtroAlmacenId)
            {
                Ws = ws;
                Lay = lay;
                MapNombres = mapNombres;
                EsGlobal = esGlobal;
                FiltroAlmacenId = filtroAlmacenId;
            }

            public IXLWorksheet Ws { get; }
            public LayoutColumnas Lay { get; }
            public Dictionary<int, string> MapNombres { get; }
            public bool EsGlobal { get; }
            public int FiltroAlmacenId { get; }
            public int Fila { get; set; } = 6;
            public List<int> FilasTotales { get; } = new List<int>();
        }

        // =========================================================================
        // PUNTO DE ENTRADA
        // =========================================================================
        public void GenerarExcelMatrizVentasCompleto(
            string campana,
            List<ProductoColumnaDTO> catalogoProductos,
            List<UbicacionMatrizDTO> ubicacionesComerciales,
            List<UbicacionMatrizDTO> almacenesReales,
            List<Almacen> almacenesRegistrados,
            Dictionary<int, decimal> ingresosCentral,
            int almacenSesionId,
            string nombreAlmacenSesion)
        {
            using var wb = new XLWorkbook();

            wb.Properties.Title = $"Matriz de Ventas {campana}";
            wb.Properties.Author = "Sistema de Control de Almacén";
            wb.Properties.Company = "Ediciones Piza";

            // 🛑 Reporte exclusivo de libros / productos con código (familia OTROS descartada)
            var columnasProductos = catalogoProductos
                .Where(p => !string.IsNullOrWhiteSpace(p.Codigo) && p.NivelId != 99)
                .ToList();

            // Nombres de hojas únicos (Excel no permite repetidos ni > 31 caracteres)
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HojaIndice, HojaResumenGlobal };
            var mapNombresHojas = new Dictionary<int, string>();

            string nombrePestanaSede = NombreUnico($"RESUMEN {nombreAlmacenSesion.ToUpper()}", usados);

            foreach (var alm in almacenesReales)
                mapNombresHojas[alm.UbicacionId + 100000] = NombreUnico(alm.Nombre.ToUpper(), usados);

            foreach (var ub in ubicacionesComerciales)
                mapNombresHojas[ub.UbicacionId] = NombreUnico(ub.Nombre.ToUpper(), usados);

            var listaIndice = new List<(string Tipo, string Nombre, string Hoja)>
            {
                ("RESUMEN", $"Resumen {nombreAlmacenSesion}", nombrePestanaSede),
                ("RESUMEN", "Resumen general sedes", HojaResumenGlobal)
            };

            // 1. RESUMEN SEDE ACTUAL
            var wsResumenSede = wb.Worksheets.Add(nombrePestanaSede);
            wsResumenSede.TabColor = XLColor.FromHtml("#0D9488");
            ConstruirHojaResumen(wsResumenSede, campana, ubicacionesComerciales, almacenesReales,
                columnasProductos, mapNombresHojas, ingresosCentral, almacenSesionId, nombreAlmacenSesion, false);

            // 2. RESUMEN GENERAL (CONSOLIDADO)
            var wsResumenGlobal = wb.Worksheets.Add(HojaResumenGlobal);
            wsResumenGlobal.TabColor = XLColor.FromHtml("#047857");
            ConstruirHojaResumen(wsResumenGlobal, campana, ubicacionesComerciales, almacenesReales,
                columnasProductos, mapNombresHojas, ingresosCentral, 0, "TODAS LAS SEDES", true);

            // 3. HOJAS DE SEDES REALES
            foreach (var alm in almacenesReales)
            {
                string nombreHoja = mapNombresHojas[alm.UbicacionId + 100000];
                var ws = wb.Worksheets.Add(nombreHoja);
                ws.TabColor = XLColor.FromHtml("#1E293B");
                ConstruirHojaMatriz(ws, ModoHoja.SedeReal, alm.Nombre, campana, alm.Movimientos, columnasProductos);
                listaIndice.Add(("SEDE REAL", alm.Nombre, nombreHoja));
            }

            // 4. HOJAS COMERCIALES (puntos, ferias, consignación, otros)
            foreach (var ub in ubicacionesComerciales)
            {
                string nombreHoja = mapNombresHojas[ub.UbicacionId];
                var ws = wb.Worksheets.Add(nombreHoja);

                if (ub.Nombre.Equals("OTROS", StringComparison.OrdinalIgnoreCase))
                {
                    ws.TabColor = XLColor.FromHtml("#7C3AED"); // Púrpura (Otros)
                    ConstruirHojaMatriz(ws, ModoHoja.Otros, ub.Nombre, campana, ub.Movimientos, columnasProductos);
                }
                else if (ub.TipoUbicacionId == 1)
                {
                    ws.TabColor = XLColor.FromHtml("#94A3B8"); // Gris (Alm. Referencial)
                    ConstruirHojaMatriz(ws, ModoHoja.Comercial, ub.Nombre, campana, ub.Movimientos, columnasProductos);
                }
                else if (ub.Nombre.Equals("FERIAS", StringComparison.OrdinalIgnoreCase))
                {
                    ws.TabColor = XLColor.FromHtml("#E11D48"); // Rojo / Fucsia (Ferias)
                    ConstruirHojaMatriz(ws, ModoHoja.Comercial, ub.Nombre, campana, ub.Movimientos, columnasProductos);
                }
                else if (ub.TipoUbicacionId == 5 || ub.Nombre.Equals("CONSIGNACION", StringComparison.OrdinalIgnoreCase))
                {
                    ws.TabColor = XLColor.FromHtml("#3B82F6"); // Azul Claro (Puntos Externos / Consignación)
                    ConstruirHojaMatriz(ws, ModoHoja.Comercial, ub.Nombre, campana, ub.Movimientos, columnasProductos);
                }
                else
                {
                    ws.TabColor = XLColor.FromHtml("#F59E0B"); // Amarillo / Naranja (Puntos de Venta)
                    ConstruirHojaMatriz(ws, ModoHoja.Comercial, ub.Nombre, campana, ub.Movimientos, columnasProductos);
                }

                listaIndice.Add(("COMERCIAL", ub.Nombre, nombreHoja));
            }

            // 5. ÍNDICE (primera pestaña)
            ConstruirHojaIndice(wb, campana, listaIndice);

            // Guardado
            string nombreArchivo = $"Matriz_Ventas_{campana}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(carpeta);
            string ruta = Path.Combine(carpeta, nombreArchivo);
            wb.SaveAs(ruta);

            try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); }
            catch { /* el archivo ya se guardó; no romper el flujo si no abre */ }
        }

        // =========================================================================
        // UTILIDADES
        // =========================================================================
        private static string SanitizarNombrePestana(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return "HOJA";
            string limpio = nombre.Trim();
            foreach (char c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                limpio = limpio.Replace(c, '_');
            limpio = limpio.Replace("'", "");
            if (limpio.Length == 0) limpio = "HOJA";
            return limpio.Length > 31 ? limpio.Substring(0, 31).Trim() : limpio;
        }

        private static string NombreUnico(string baseName, HashSet<string> usados)
        {
            string baseLimpia = SanitizarNombrePestana(baseName);
            string nombre = baseLimpia;
            int i = 2;
            while (usados.Contains(nombre))
            {
                string sufijo = $" ({i++})";
                nombre = baseLimpia.Substring(0, Math.Min(baseLimpia.Length, 31 - sufijo.Length)).Trim() + sufijo;
            }
            usados.Add(nombre);
            return nombre;
        }

        private static string Link(string hoja) => $"'{hoja}'!A1";

        private static string FormatearCodigoEnDosLineas(string codigoOriginal)
        {
            if (string.IsNullOrWhiteSpace(codigoOriginal)) return "";

            string c = codigoOriginal.Trim()
                .Replace("-V-V-", " V ")
                .Replace("-V-V", " V")
                .Replace("-V", " V")
                .Trim();

            if (c.Contains("\n")) return c;

            var partes = c.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length >= 3)
                return $"{partes[0]}\n{string.Join(" ", partes.Skip(1))}";
            if (partes.Length == 2)
                return $"{partes[0]}\n{partes[1]}";

            return c;
        }

        private static IXLRange Fusionar(IXLWorksheet ws, int f1, int c1, int f2, int c2, string? texto = null)
        {
            var r = ws.Range(f1, c1, f2, c2);
            r.Merge();
            if (texto != null) r.FirstCell().Value = texto;
            return r;
        }

        private static void CentrarRango(IXLRange r)
        {
            r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        private static void EstiloCabecera(IXLRange r, double size, XLBorderStyleValues borde)
        {
            r.Style.Font.Bold = true;
            r.Style.Font.FontSize = size;
            CentrarRango(r);
            r.Style.Border.OutsideBorder = borde;
        }

        private static void Boton(IXLWorksheet ws, int f1, int c1, int f2, int c2, string texto, string hojaDestino)
        {
            var r = Fusionar(ws, f1, c1, f2, c2, texto);
            r.FirstCell().CreateHyperlink().InternalAddress = Link(hojaDestino);
            r.Style.Font.Bold = true;
            r.Style.Font.FontSize = 9;
            r.Style.Font.FontColor = XLColor.White;
            r.Style.Font.Underline = XLFontUnderlineValues.None;
            r.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            CentrarRango(r);
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        private static void AplicarEstiloLink(IXLCell celda, string hojaDestino)
        {
            celda.CreateHyperlink().InternalAddress = Link(hojaDestino);
            celda.Style.Font.FontColor = XLColor.FromHtml("#1D4ED8");
            celda.Style.Font.Underline = XLFontUnderlineValues.Single;
        }

        private static void ConfigurarImpresion(IXLWorksheet ws, int repetirDesde, int repetirHasta)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.SetRowsToRepeatAtTop(repetirDesde, repetirHasta);
            ws.PageSetup.Margins.Left = 0.4;
            ws.PageSetup.Margins.Right = 0.4;
            ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber, XLHFOccurrence.AllPages);
        }

        // =========================================================================
        // ENCABEZADOS DE PRODUCTOS (nivel / familia / código) - compartido por todas las hojas
        // =========================================================================
        private static LayoutColumnas ConstruirEncabezadosProductos(
            IXLWorksheet ws,
            List<ProductoColumnaDTO> catalogo,
            int colInicial,
            int filaNivel,
            string colorTotalHex)
        {
            int filaTitulo = filaNivel + 1;
            int filaCodigo = filaNivel + 2;
            var lay = new LayoutColumnas();
            int col = colInicial;

            foreach (var grupoNivel in catalogo.GroupBy(p => p.NivelNombre))
            {
                int colIniNivel = col;

                foreach (var grupoFamilia in grupoNivel.GroupBy(p => p.FamiliaNombre))
                {
                    int colIniFamilia = col;

                    foreach (var prod in grupoFamilia)
                    {
                        lay.MapProducto[prod.ProductoId] = col;

                        var celda = ws.Cell(filaCodigo, col);
                        celda.Value = FormatearCodigoEnDosLineas(prod.Codigo);
                        celda.Style.Font.Bold = true;
                        celda.Style.Font.FontSize = 8;
                        celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        celda.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        celda.Style.Alignment.WrapText = true;
                        celda.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        col++;
                    }

                    int colSubtotal = col;
                    lay.Subtotales.Add((colSubtotal, colIniFamilia, colSubtotal - 1));

                    var celdaTotal = ws.Cell(filaCodigo, colSubtotal);
                    celdaTotal.Value = "TOTAL";
                    celdaTotal.Style.Font.Bold = true;
                    celdaTotal.Style.Font.FontSize = 8;
                    celdaTotal.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    celdaTotal.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    celdaTotal.Style.Fill.BackgroundColor = XLColor.FromHtml(colorTotalHex);
                    celdaTotal.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    col++;

                    var rTitulo = Fusionar(ws, filaTitulo, colIniFamilia, filaTitulo, colSubtotal, (grupoFamilia.Key ?? "").ToUpper());
                    EstiloCabecera(rTitulo, 8.5, XLBorderStyleValues.Thin);
                }

                var rNivel = Fusionar(ws, filaNivel, colIniNivel, filaNivel, col - 1, (grupoNivel.Key ?? "").ToUpper());
                EstiloCabecera(rNivel, 10, XLBorderStyleValues.Medium);
            }

            lay.UltimaColumna = col - 1;
            ws.Row(filaCodigo).Height = 28;
            return lay;
        }

        private static void EscribirSubtotalesHorizontales(
            IXLWorksheet ws, LayoutColumnas lay, int fila, string colorFuenteHex, string colorFondoHex)
        {
            foreach (var sub in lay.Subtotales)
            {
                string ini = XLHelper.GetColumnLetterFromNumber(sub.ColInicio);
                string fin = XLHelper.GetColumnLetterFromNumber(sub.ColFin);
                var celda = ws.Cell(fila, sub.ColSubtotal);
                celda.FormulaA1 = $"SUM({ini}{fila}:{fin}{fila})";
                celda.Style.NumberFormat.Format = FormatoNumero;
                celda.Style.Font.Bold = true;
                celda.Style.Font.FontColor = XLColor.FromHtml(colorFuenteHex);
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml(colorFondoHex);
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        // =========================================================================
        // HOJA ÍNDICE
        // =========================================================================
        private static void ConstruirHojaIndice(
            XLWorkbook wb, string campana, List<(string Tipo, string Nombre, string Hoja)> hojas)
        {
            var ws = wb.Worksheets.Add(HojaIndice, 1);
            ws.TabColor = XLColor.FromHtml("#0F172A");
            ws.ShowGridLines = false;

            var titulo = Fusionar(ws, 2, 2, 2, 4, $"MATRIZ DE VENTAS – CAMPAÑA {campana.ToUpper()}");
            titulo.Style.Font.Bold = true;
            titulo.Style.Font.FontSize = 16;
            titulo.Style.Font.FontColor = XLColor.White;
            titulo.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
            CentrarRango(titulo);
            ws.Row(2).Height = 30;

            var sub = Fusionar(ws, 3, 2, 3, 4, $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}");
            sub.Style.Font.Italic = true;
            sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(5, 2).Value = "N°";
            ws.Cell(5, 3).Value = "TIPO";
            ws.Cell(5, 4).Value = "HOJA";
            var enc = ws.Range(5, 2, 5, 4);
            enc.Style.Font.Bold = true;
            enc.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            enc.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            enc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int fila = 6;
            int n = 1;
            foreach (var (tipo, nombre, hoja) in hojas)
            {
                ws.Cell(fila, 2).Value = n++;
                ws.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(fila, 3).Value = tipo;
                ws.Cell(fila, 4).Value = nombre;
                AplicarEstiloLink(ws.Cell(fila, 4), hoja);
                ws.Range(fila, 2, fila, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (fila % 2 == 1)
                    ws.Range(fila, 2, fila, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                fila++;
            }

            ws.Column(1).Width = 2;
            ws.Column(2).Width = 6;
            ws.Column(3).Width = 22;
            ws.Column(4).Width = 42;
        }

        // =========================================================================
        // HOJA INDIVIDUAL (sede real, punto comercial u "OTROS")
        // =========================================================================
        private static void ConstruirHojaMatriz(
            IXLWorksheet ws,
            ModoHoja modo,
            string tituloUbicacion,
            string campanaTexto,
            List<MatrizKardexItemDTO> movimientos,
            List<ProductoColumnaDTO> catalogo)
        {
            bool conColegio = modo != ModoHoja.Otros;
            int colsEtiqueta = conColegio ? 5 : 4;
            string campUp = campanaTexto.ToUpper();

            ws.ShowGridLines = true;

            // Navegación
            Boton(ws, 1, 1, 1, 1, "📋 ÍNDICE", HojaIndice);
            Boton(ws, 1, 2, 1, 3, "🏠 RESUMEN", HojaResumenGlobal);

            ws.Cell("D1").Value = modo == ModoHoja.Otros
                ? $"LIBROS VENTA CAMPAÑA {campUp} - OTRAS SALIDAS"
                : $"LIBROS VENTA CAMPAÑA {campUp} - {tituloUbicacion.ToUpper()}";
            ws.Cell("D1").Style.Font.Bold = true;
            ws.Cell("D1").Style.Font.FontSize = 13;

            // Encabezados fijos
            var cabeceras = conColegio
                ? new[] { "ORDEN", "FECHA", "COLEGIO / UBICACIÓN", "EMPRESA", "N° GRE" }
                : new[] { "ORDEN", "FECHA", "EMPRESA", "N° GRE" };

            for (int i = 0; i < cabeceras.Length; i++)
            {
                var c = ws.Cell(4, i + 1);
                c.Value = cabeceras[i];
                c.Style.Font.Bold = true;
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                c.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }

            var lay = ConstruirEncabezadosProductos(ws, catalogo, colsEtiqueta + 1, 2, "#C6E0B4");
            ws.Row(4).Height = 28;

            var catalogoPorId = catalogo
                .GroupBy(p => p.ProductoId)
                .ToDictionary(g => g.Key, g => g.First());

            int fila = 5;
            int fTotIngresos = 0;
            int fTotDevoluciones = 0;

            if (modo == ModoHoja.SedeReal)
                fTotIngresos = RenderizarBloqueMatriz(ws, lay, catalogoPorId, movimientos, 1, "TOTAL INGRESOS", XLColor.FromHtml("#BFDBFE"), conColegio, colsEtiqueta, ref fila);

            int fTotSalidas = RenderizarBloqueMatriz(ws, lay, catalogoPorId, movimientos, 2, "TOTAL SALIDAS", XLColor.FromHtml("#FACC15"), conColegio, colsEtiqueta, ref fila);

            if (modo != ModoHoja.Otros)
                fTotDevoluciones = RenderizarBloqueMatriz(ws, lay, catalogoPorId, movimientos, 3, "TOTAL DEVOLUCIONES", XLColor.FromHtml("#FECACA"), conColegio, colsEtiqueta, ref fila);

            // Fila final: saldo de la campaña
            int filaSaldo = fila;
            var rEtiqueta = Fusionar(ws, filaSaldo, 1, filaSaldo, colsEtiqueta, $"TOTAL {campUp}");
            rEtiqueta.Style.Font.Bold = true;
            rEtiqueta.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDBA74");
            rEtiqueta.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int c = colsEtiqueta + 1; c <= lay.UltimaColumna; c++)
            {
                string L = XLHelper.GetColumnLetterFromNumber(c);
                var celda = ws.Cell(filaSaldo, c);

                if (modo == ModoHoja.SedeReal && fTotIngresos > 0)
                    celda.FormulaA1 = $"{L}{fTotIngresos}-{L}{fTotSalidas}+{L}{fTotDevoluciones}";
                else if (modo == ModoHoja.Otros)
                    celda.FormulaA1 = $"{L}{fTotSalidas}";
                else
                    celda.FormulaA1 = $"{L}{fTotSalidas}-{L}{fTotDevoluciones}";

                celda.Style.Font.Bold = true;
                celda.Style.NumberFormat.Format = FormatoNumero;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#FED7AA");
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            ws.Range(filaSaldo, 1, filaSaldo, lay.UltimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            // Anchos
            ws.Column(1).Width = 10.0;
            ws.Column(2).Width = 11.0;
            if (conColegio)
            {
                ws.Column(3).Width = 32.0;
                ws.Column(4).Width = 22.0;
                ws.Column(5).Width = 14.0;
            }
            else
            {
                ws.Column(3).Width = 24.0;
                ws.Column(4).Width = 14.0;
            }

            for (int c = colsEtiqueta + 1; c <= lay.UltimaColumna; c++)
            {
                bool esSubtotal = lay.Subtotales.Any(s => s.ColSubtotal == c);
                ws.Column(c).Width = esSubtotal ? 6.5 : 5.2;
            }

            // Presentación
            ws.SheetView.FreezeRows(4);
            ws.SheetView.FreezeColumns(colsEtiqueta);
            ws.SheetView.ZoomScale = 90;
            ConfigurarImpresion(ws, 1, 4);
        }

        private static int RenderizarBloqueMatriz(
            IXLWorksheet ws,
            LayoutColumnas lay,
            Dictionary<int, ProductoColumnaDTO> catalogoPorId,
            List<MatrizKardexItemDTO> movimientos,
            int tipoBloque,
            string tituloTotal,
            XLColor colorTotal,
            bool conColegio,
            int colsEtiqueta,
            ref int fila)
        {
            int filaInicio = fila;

            var grupos = movimientos
                .Where(m => m.BloqueTipo == tipoBloque)
                .GroupBy(m => new { m.MovimientoId, m.OrdenDocumento, m.NumeroGuia, m.OrigenAlmacen, m.Fecha })
                .OrderBy(g => g.Key.Fecha)
                .ThenBy(g => g.Key.MovimientoId)
                .ToList();

            int totalFilas = Math.Max(grupos.Count, 4);

            for (int i = 0; i < totalFilas; i++)
            {
                if (i < grupos.Count)
                {
                    var grupo = grupos[i];

                    // ORDEN
                    var celdaOrden = ws.Cell(fila, 1);
                    string orden = grupo.Key.OrdenDocumento ?? "";
                    if (orden.Contains("-"))
                    {
                        var partes = orden.Split('-');
                        if (partes.Length > 1 && int.TryParse(partes[1], out int num))
                            celdaOrden.Value = num;
                        else
                            celdaOrden.Value = orden;
                    }
                    else
                    {
                        celdaOrden.Value = orden;
                    }
                    celdaOrden.Style.Font.Bold = true;
                    celdaOrden.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // FECHA (fecha real, filtrable y ordenable)
                    var celdaFecha = ws.Cell(fila, 2);
                    celdaFecha.Value = grupo.Key.Fecha;
                    celdaFecha.Style.DateFormat.Format = "dd/MM/yyyy";
                    celdaFecha.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    int colEmpresa;
                    int colGre;
                    if (conColegio)
                    {
                        ws.Cell(fila, 3).Value = grupo.Key.OrigenAlmacen;
                        ws.Cell(fila, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        colEmpresa = 4;
                        colGre = 5;
                    }
                    else
                    {
                        colEmpresa = 3;
                        colGre = 4;
                    }

                    ws.Cell(fila, colEmpresa).Value = ClasificarEmpresa(grupo, catalogoPorId);
                    ws.Cell(fila, colEmpresa).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(fila, colGre).Value = grupo.Key.NumeroGuia;
                    ws.Cell(fila, colGre).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Cantidades (si un producto aparece 2 veces en el movimiento, se SUMA)
                    foreach (var porProducto in grupo.GroupBy(x => x.ProductoId))
                    {
                        if (!lay.MapProducto.TryGetValue(porProducto.Key, out int cIdx)) continue;
                        var celda = ws.Cell(fila, cIdx);
                        celda.Value = porProducto.Sum(x => x.Cantidad);
                        celda.Style.NumberFormat.Format = FormatoNumero;
                        celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                }

                EscribirSubtotalesHorizontales(ws, lay, fila, "#166534", "#DCFCE7");

                var rFila = ws.Range(fila, 1, fila, lay.UltimaColumna);
                rFila.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rFila.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                fila++;
            }

            // Fila de total del bloque
            int filaTotal = fila;
            var rEtiqueta = Fusionar(ws, filaTotal, 1, filaTotal, colsEtiqueta, tituloTotal);
            rEtiqueta.Style.Font.Bold = true;
            rEtiqueta.Style.Fill.BackgroundColor = colorTotal;
            rEtiqueta.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int c = colsEtiqueta + 1; c <= lay.UltimaColumna; c++)
            {
                string L = XLHelper.GetColumnLetterFromNumber(c);
                var celda = ws.Cell(filaTotal, c);
                celda.FormulaA1 = $"SUM({L}{filaInicio}:{L}{filaTotal - 1})";
                celda.Style.Font.Bold = true;
                celda.Style.NumberFormat.Format = FormatoNumero;
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            var rTotal = ws.Range(filaTotal, 1, filaTotal, lay.UltimaColumna);
            rTotal.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rTotal.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            fila += 2;
            return filaTotal;
        }

        private static string ClasificarEmpresa(
            IEnumerable<MatrizKardexItemDTO> items,
            Dictionary<int, ProductoColumnaDTO> catalogoPorId)
        {
            bool tieneInicial = false;
            bool tienePrimaria = false;

            foreach (var itm in items)
            {
                if (!catalogoPorId.TryGetValue(itm.ProductoId, out var prod)) continue;
                string n = (prod.NivelNombre ?? "").ToUpper();
                if (n.Contains("INICIAL")) tieneInicial = true;
                if (n.Contains("PRIMARI") || n.Contains("SECUNDARI")) tienePrimaria = true;
            }

            if (tieneInicial && tienePrimaria) return "AMBOS";
            if (tienePrimaria) return "PIZA EDITORES E.I.R.L.";
            return "EDICIONES PIZA S.A.C.";
        }

        // =========================================================================
        // HOJA RESUMEN (sede actual / consolidado global)
        // =========================================================================
        private static void ConstruirHojaResumen(
            IXLWorksheet ws,
            string campanaTexto,
            List<UbicacionMatrizDTO> ubicaciones,
            List<UbicacionMatrizDTO> almacenesReales,
            List<ProductoColumnaDTO> columnasProductos,
            Dictionary<int, string> mapNombresHojas,
            Dictionary<int, decimal> ingresosCentral,
            int filtroAlmacenId,
            string nombreSede,
            bool esConsolidadoGlobal)
        {
            ws.ShowGridLines = true;

            // Columnas de etiquetas: A (clasificación), B (tipo), C (zona / entidad)
            ws.Cell(3, 1).Value = "CLASIFICACIÓN";
            ws.Cell(3, 2).Value = "TIPO";
            ws.Cell(3, 3).Value = "ZONA / ENTIDAD";
            for (int c = 1; c <= 3; c++)
            {
                var r = ws.Range(3, c, 5, c);
                r.Merge();
                r.Style.Font.Bold = true;
            }
            var rEtiquetas = ws.Range(3, 1, 5, 3);
            CentrarRango(rEtiquetas);
            rEtiquetas.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rEtiquetas.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            var lay = ConstruirEncabezadosProductos(ws, columnasProductos, 4, 3, "#FDBA74");
            ws.Row(3).Height = 18;
            ws.Row(4).Height = 18;
            ws.Row(5).Height = 28;

            // Navegación + título
            Boton(ws, 1, 1, 1, 3, "📋 ÍNDICE", HojaIndice);

            string titulo = esConsolidadoGlobal
                ? $"RESUMEN GENERAL SEDES CONSOLIDADO - CAMPAÑA {campanaTexto.ToUpper()}"
                : $"RESUMEN GENERAL - {nombreSede.ToUpper()} - CAMPAÑA {campanaTexto.ToUpper()}";
            var rTitulo = Fusionar(ws, 1, 4, 1, Math.Max(lay.UltimaColumna, 12), titulo);
            rTitulo.Style.Font.Bold = true;
            rTitulo.Style.Font.FontSize = 13;
            rTitulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var ctx = new ContextoResumen(ws, lay, mapNombresHojas, esConsolidadoGlobal, filtroAlmacenId);

            // Bloques de ubicaciones
            int fIniUbicaciones = ctx.Fila;
            RenderizarBloqueResumen(ctx, "ALM. REFER", "TOTAL ALMACENES REFER", 1, false, "#94A3B8", "#CBD5E1", ubicaciones, "ALMACEN REFERENCIAL");
            RenderizarBloqueResumen(ctx, "PUNTOS DE VENTA", "TOTAL PUNTOS DE VENTA", 2, false, "#F59E0B", "#FDE047", ubicaciones, "PUNTO DE VENTA");
            RenderizarBloqueResumen(ctx, "PUNTOS EXT.", "TOTAL PUNTOS EXTERNOS", 5, false, "#3B82F6", "#93C5FD", ubicaciones, "PUNTO DE VENTA EXTERNO");
            int fFinUbicaciones = ctx.Fila - 1;

            if (fFinUbicaciones >= fIniUbicaciones)
            {
                var r = Fusionar(ws, fIniUbicaciones, 1, fFinUbicaciones, 1, "UBICACIONES");
                r.Style.Font.Bold = true;
                r.Style.Font.FontSize = 11;
                CentrarRango(r);
                r.Style.Alignment.TextRotation = 90;
                r.Style.Fill.BackgroundColor = XLColor.FromHtml("#94A3B8");
                r.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }

            // Bloque de sedes reales
            int fIniSedes = ctx.Fila;
            RenderizarBloqueResumen(ctx, "SEDES FÍSICAS", "TOTAL SEDES REALES", 1, true, "#1E293B", "#CBD5E1", almacenesReales, "SEDE REAL");
            int fFinSedes = ctx.Fila - 1;

            if (fFinSedes >= fIniSedes)
            {
                var r = Fusionar(ws, fIniSedes, 1, fFinSedes, 1, "SEDES REALES");
                r.Style.Font.Bold = true;
                r.Style.Font.FontSize = 11;
                CentrarRango(r);
                r.Style.Alignment.TextRotation = 90;
                r.Style.Fill.BackgroundColor = XLColor.FromHtml("#475569");
                r.Style.Font.FontColor = XLColor.White;
                r.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }

            // Total consolidado general
            int filaGranTotal = ctx.Fila;
            var rGran = Fusionar(ws, filaGranTotal, 1, filaGranTotal, 3, "TOTAL CONSOLIDADO GENERAL");
            rGran.Style.Font.Bold = true;
            rGran.Style.Fill.BackgroundColor = XLColor.FromHtml("#FACC15");
            rGran.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int c = 4; c <= lay.UltimaColumna; c++)
            {
                string L = XLHelper.GetColumnLetterFromNumber(c);
                var celda = ws.Cell(filaGranTotal, c);
                if (ctx.FilasTotales.Any())
                    celda.FormulaA1 = string.Join("+", ctx.FilasTotales.Select(f => $"{L}{f}"));
                else
                    celda.Value = 0;
                celda.Style.Font.Bold = true;
                celda.Style.NumberFormat.Format = FormatoNumero;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE047");
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            var rGranFila = ws.Range(filaGranTotal, 1, filaGranTotal, lay.UltimaColumna);
            rGranFila.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rGranFila.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ctx.Fila += 2;

            // Resumen final: saldo inicial / salidas netas / stock final
            int fInicioSedes = ctx.Fila;
            ws.Cell(ctx.Fila, 3).Value = "RESUMEN FINAL";
            ws.Cell(ctx.Fila, 3).Style.Font.Bold = true;
            ctx.Fila++;

            int fSaldoInicial = ctx.Fila;
            ws.Cell(ctx.Fila, 3).Value = "SALDO INICIAL";
            ws.Cell(ctx.Fila, 3).Style.Font.Bold = true;
            ctx.Fila++;

            int fSalidasNetas = ctx.Fila;
            ws.Cell(ctx.Fila, 3).Value = "SALIDAS NETAS";
            ws.Cell(ctx.Fila, 3).Style.Font.Bold = true;
            ctx.Fila++;

            int fStockFinal = ctx.Fila;
            ws.Cell(ctx.Fila, 3).Value = "STOCK FINAL";
            ws.Cell(ctx.Fila, 3).Style.Font.Bold = true;

            var colAProducto = lay.MapProducto
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.First().Key);

            for (int c = 4; c <= lay.UltimaColumna; c++)
            {
                string L = XLHelper.GetColumnLetterFromNumber(c);
                var celdaInicial = ws.Cell(fSaldoInicial, c);

                var sub = lay.Subtotales.FirstOrDefault(s => s.ColSubtotal == c);
                if (sub.ColSubtotal > 0)
                {
                    string ini = XLHelper.GetColumnLetterFromNumber(sub.ColInicio);
                    string fin = XLHelper.GetColumnLetterFromNumber(sub.ColFin);
                    celdaInicial.FormulaA1 = $"SUM({ini}{fSaldoInicial}:{fin}{fSaldoInicial})";
                }
                else
                {
                    decimal ingreso = 0;
                    if (colAProducto.TryGetValue(c, out int prodId))
                        ingresosCentral.TryGetValue(prodId, out ingreso);
                    celdaInicial.Value = ingreso;
                }

                celdaInicial.Style.NumberFormat.Format = FormatoNumero;
                celdaInicial.Style.Font.Bold = true;
                celdaInicial.Style.Font.FontColor = XLColor.FromHtml("#0E7490");

                var celdaSalidas = ws.Cell(fSalidasNetas, c);
                celdaSalidas.FormulaA1 = $"{L}{filaGranTotal}";
                celdaSalidas.Style.NumberFormat.Format = FormatoNumero;
                celdaSalidas.Style.Font.Bold = true;

                var celdaStock = ws.Cell(fStockFinal, c);
                celdaStock.FormulaA1 = $"{L}{fSaldoInicial}-{L}{fSalidasNetas}";
                celdaStock.Style.Font.Bold = true;
                celdaStock.Style.NumberFormat.Format = FormatoNumero;
                celdaStock.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF08A");
            }

            // Stock negativo en rojo
            if (lay.UltimaColumna >= 4)
            {
                var rangoStock = ws.Range(fStockFinal, 4, fStockFinal, lay.UltimaColumna);
                rangoStock.AddConditionalFormat().WhenLessThan(0)
                    .Font.SetFontColor(XLColor.Red)
                    .Font.SetBold()
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#FEE2E2"));
            }

            var rSedes = Fusionar(ws, fInicioSedes, 1, fStockFinal, 2, "SEDES");
            rSedes.Style.Font.Bold = true;
            rSedes.Style.Font.FontSize = 10;
            CentrarRango(rSedes);
            rSedes.Style.Alignment.TextRotation = 90;
            rSedes.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            rSedes.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            var rResumenFinal = ws.Range(fInicioSedes, 3, fStockFinal, lay.UltimaColumna);
            rResumenFinal.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rResumenFinal.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // Anchos
            ws.Column(1).Width = 4.0;
            ws.Column(2).Width = 4.0;
            ws.Column(3).Width = 27.0;
            for (int c = 4; c <= lay.UltimaColumna; c++)
            {
                bool esSubtotal = lay.Subtotales.Any(s => s.ColSubtotal == c);
                ws.Column(c).Width = esSubtotal ? 8.0 : 6.8;
            }

            // Presentación
            ws.SheetView.FreezeRows(5);
            ws.SheetView.FreezeColumns(3);
            ws.SheetView.ZoomScale = 90;
            ConfigurarImpresion(ws, 3, 5);
        }

        private static void RenderizarBloqueResumen(
            ContextoResumen ctx,
            string textoHijo,
            string textoTotal,
            int tipoUbicacionId,
            bool esSedeReal,
            string colorHijoHex,
            string colorTotalHex,
            List<UbicacionMatrizDTO> listaFuente,
            string filtroTipoNombre)
        {
            var ws = ctx.Ws;
            var lay = ctx.Lay;

            var lista = listaFuente.Where(u =>
            {
                if (esSedeReal) return u.TipoUbicacionId == 1;
                if (u.TipoUbicacionId == 1) return false;
                if (u.Nombre.Equals("FERIAS", StringComparison.OrdinalIgnoreCase)) return textoHijo.Contains("PUNTOS");
                if (u.Nombre.Equals("OTROS", StringComparison.OrdinalIgnoreCase)) return textoHijo.Contains("PUNTOS");
                if (u.Nombre.Equals("CONSIGNACION", StringComparison.OrdinalIgnoreCase)) return textoHijo.Contains("EXT");
                return u.TipoUbicacionId == tipoUbicacionId;
            }).ToList();

            if (!lista.Any()) return;

            int filaInicio = ctx.Fila;

            foreach (var ub in lista)
            {
                int fila = ctx.Fila;
                var celdaNombre = ws.Cell(fila, 3);
                celdaNombre.Value = ub.Nombre;

                int clave = esSedeReal ? ub.UbicacionId + 100000 : ub.UbicacionId;
                if (ctx.MapNombres.TryGetValue(clave, out string? hojaDestino))
                    AplicarEstiloLink(celdaNombre, hojaDestino);
                celdaNombre.Style.Font.Bold = true;

                if (!esSedeReal && tipoUbicacionId != 1)
                {
                    var consideradas = ctx.EsGlobal ? ub.Movimientos : ub.Movimientos.Where(m => ctx.FiltroAlmacenId == 0 || m.OrigenAlmacenId == ctx.FiltroAlmacenId).ToList();
                    var saldos = consideradas.GroupBy(m => m.ProductoId).ToDictionary(g => g.Key, g => g.Where(x => x.BloqueTipo == 2).Sum(x => x.Cantidad) - g.Where(x => x.BloqueTipo == 3).Sum(x => x.Cantidad));

                    foreach (var kvp in saldos)
                    {
                        if (kvp.Value == 0) continue;
                        if (!lay.MapProducto.TryGetValue(kvp.Key, out int cIdx)) continue;
                        var celda = ws.Cell(fila, cIdx);
                        celda.Value = kvp.Value;
                        celda.Style.NumberFormat.Format = FormatoNumero;
                        celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                }

                EscribirSubtotalesHorizontales(ws, lay, fila, "#991B1B", "#FFEDD5");
                ws.Range(fila, 3, fila, lay.UltimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(fila, 3, fila, lay.UltimaColumna).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                ctx.Fila++;
            }

            int filaFin = ctx.Fila - 1;
            var rHijo = Fusionar(ws, filaInicio, 2, filaFin, 2, textoHijo);
            rHijo.Style.Font.Bold = true; rHijo.Style.Font.FontSize = 9; CentrarRango(rHijo);
            rHijo.Style.Alignment.TextRotation = 90; rHijo.Style.Fill.BackgroundColor = XLColor.FromHtml(colorHijoHex);
            rHijo.Style.Font.FontColor = XLColor.White; rHijo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            int filaSubtotal = ctx.Fila;
            var colorTotal = XLColor.FromHtml(colorTotalHex);
            var rEtiq = Fusionar(ws, filaSubtotal, 2, filaSubtotal, 3, textoTotal);
            rEtiq.Style.Font.Bold = true; rEtiq.Style.Fill.BackgroundColor = colorTotal; CentrarRango(rEtiq);

            for (int c = 4; c <= lay.UltimaColumna; c++)
            {
                string L = XLHelper.GetColumnLetterFromNumber(c);
                var celda = ws.Cell(filaSubtotal, c);
                celda.FormulaA1 = $"SUM({L}{filaInicio}:{L}{filaFin})";
                celda.Style.Font.Bold = true; celda.Style.NumberFormat.Format = FormatoNumero;
                celda.Style.Fill.BackgroundColor = colorTotal; celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            ws.Range(filaSubtotal, 2, filaSubtotal, lay.UltimaColumna).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(filaSubtotal, 2, filaSubtotal, lay.UltimaColumna).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ctx.FilasTotales.Add(filaSubtotal);
            ctx.Fila++;
        }
    }
}