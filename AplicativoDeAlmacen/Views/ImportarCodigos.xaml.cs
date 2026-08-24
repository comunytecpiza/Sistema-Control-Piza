using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public class ItemCodigoPreview
    {
        public int RowNumber { get; set; }
        public string CodigoRaw { get; set; } = string.Empty;
        public string CodigoNorm { get; set; } = string.Empty;
        public bool EstadoValido { get; set; }
        public int CategoriaId { get; set; } // 1 = GUÍA, 2 = VENTA
        public string CategoriaTexto => CategoriaId == 1 ? "📘 GUÍA" : "📙 VENTA";
        public string ObservacionAuditoria { get; set; } = string.Empty;
    }

    public class ProductoGroupPreview : INotifyPropertyChanged
    {
        private bool _isSelected;
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public bool TieneValidos { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string ResumenDiagnostico { get; set; } = string.Empty;
        public string BadgeTexto { get; set; } = string.Empty;
        public int? MovimientoIdActual { get; set; } = null;
        public Brush BackgroundColor { get; set; } = Brushes.Transparent;
        public Brush BorderColor { get; set; } = Brushes.Transparent;
        public Brush BadgeColor { get; set; } = Brushes.Black;

        public List<ItemCodigoPreview> CodigosInternos { get; set; } = new List<ItemCodigoPreview>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ImportarCodigos : Window
    {
        public int EstadoPermitido { get; set; } = 0;
        public List<string> CodigosImportados { get; set; } = new List<string>();
        public List<string> CodigosYaAgregadosEnMovimiento { get; set; } = new List<string>();
        public int? MovimientoIdActual { get; set; } = null;

        private readonly Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        private readonly IngresoMovimientoService _serviceMovimiento = new IngresoMovimientoService();
        private readonly DatabaseConnection _db = new DatabaseConnection();

        private List<ProductoGroupPreview> _gruposProductosMaster = new List<ProductoGroupPreview>();

        public ImportarCodigos()
        {
            InitializeComponent();
        }

        private SolidColorBrush CrearPincelSeguro(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        private string LimpiarCodigo(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string limpiado = Regex.Replace(input, @"[\u200B-\u200D\uFEFF\u00A0\t\r\n\0]", "");
            return limpiado.Trim().ToUpperInvariant();
        }

        private List<string> LeerExcelInteligente(string ruta)
        {
            var lista = new List<string>(100000);
            using var workbook = new XLWorkbook(ruta);
            var ws = workbook.Worksheet(1);
            var used = ws.RangeUsed();
            if (used == null) return lista;

            foreach (var row in used.Rows())
            {
                foreach (var cell in row.Cells())
                {
                    string cellStr = cell.GetString();
                    string limpio = LimpiarCodigo(cellStr);
                    if (!string.IsNullOrEmpty(limpio))
                    {
                        lista.Add(limpio);
                    }
                }
            }
            return lista;
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            openFileDialog.Filter = "Archivos Excel (*.xlsx; *.txt)|*.xlsx;*.txt";

            if (openFileDialog.ShowDialog() == true)
            {
                txtRutaArchivo.Text = openFileDialog.FileName;
                string extension = Path.GetExtension(openFileDialog.FileName).ToLower();

                List<string> rawList = new List<string>();
                _gruposProductosMaster.Clear();
                int totalErroresDetectados = 0;

                var loadingModal = new ProgressWindow("Auditoría por Producto", "Validando base de datos, condiciones y Kárdex...", async (progress) =>
                {
                    // 1. Lectura del archivo
                    if (extension == ".xlsx")
                        rawList = LeerExcelInteligente(openFileDialog.FileName);
                    else
                        rawList = File.ReadLines(openFileDialog.FileName).Select(LimpiarCodigo).Where(x => !string.IsNullOrEmpty(x)).ToList();

                    if (!rawList.Any()) return;

                    // 2. Códigos ya en el movimiento
                    var setYaAgregadosEnMovimiento = new HashSet<string>(
                        (CodigosYaAgregadosEnMovimiento ?? new List<string>()).Select(x => _serviceMovimiento.NormalizarCodigo(x)),
                        StringComparer.OrdinalIgnoreCase
                    );

                    // 3. Duplicados dentro del propio archivo Excel
                    var contadorOcurrenciasExcel = rawList
                        .GroupBy(x => _serviceMovimiento.NormalizarCodigo(x), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                    // 4. Búsqueda masiva en Base de Datos de códigos creados
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    // 5. Cargar catálogo de productos maestros completo para emparejar códigos no creados por prefijo
                    var listaCatalogoProductos = new List<(int Id, string Descripcion, string AbreviaturaLimpia)>();
                    var prodMap = new Dictionary<int, string>();

                    using (var conn = _db.GetConnection())
                    {
                        var dbConn = (DbConnection)conn;
                        await dbConn.OpenAsync();
                        using var cmdProdAll = dbConn.CreateCommand();
                        cmdProdAll.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion, abreviatura FROM productos WHERE estado_id = 1");
                        using var rdrProdAll = await cmdProdAll.ExecuteReaderAsync();
                        while (await rdrProdAll.ReadAsync())
                        {
                            int pId = rdrProdAll.GetInt32(0);
                            string pDesc = rdrProdAll.IsDBNull(1) ? "" : rdrProdAll.GetString(1);
                            string pAbrev = rdrProdAll.IsDBNull(2) ? "" : rdrProdAll.GetString(2);

                            prodMap[pId] = pDesc;

                            if (!string.IsNullOrWhiteSpace(pAbrev))
                            {
                                string abrevLimpia = pAbrev.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
                                listaCatalogoProductos.Add((pId, pDesc, abrevLimpia));
                            }
                        }
                    }

                    // 6. Evaluación de condiciones (permitir_salida)
                    var mapaCondiciones = new Dictionary<int, (bool PermitirSalida, string NombreCondicion)>();
                    using (var connCond = _db.GetConnection())
                    {
                        var dbConnCond = (DbConnection)connCond;
                        await dbConnCond.OpenAsync();

                        var codigosIdsProcesar = lookup.Values.Where(v => v.CodigoObj != null).Select(v => v.CodigoObj!.Id).Distinct().ToList();

                        if (codigosIdsProcesar.Any())
                        {
                            const int chunkSize = 500;
                            for (int cIdx = 0; cIdx < codigosIdsProcesar.Count; cIdx += chunkSize)
                            {
                                var subCodIds = codigosIdsProcesar.Skip(cIdx).Take(chunkSize).ToList();
                                var pNamesCond = subCodIds.Select((_, idx) => "@cond" + idx).ToList();

                                using var cmdCond = dbConnCond.CreateCommand();
                                for (int k = 0; k < subCodIds.Count; k++)
                                {
                                    var p = cmdCond.CreateParameter();
                                    p.ParameterName = pNamesCond[k];
                                    p.Value = subCodIds[k];
                                    cmdCond.Parameters.Add(p);
                                }

                                string qCond = QueryAdapter.EsMySQL
                                    ? $"SELECT cc.id, COALESCE(cond.permitir_salida, 0), COALESCE(cond.nombre, 'SIN CONDICIÓN') FROM codigos_creados cc LEFT JOIN condiciones_codigo cond ON cc.condicion_id = cond.id WHERE cc.id IN ({string.Join(",", pNamesCond)})"
                                    : $"SELECT cc.id, ISNULL(cond.permitir_salida, 0), ISNULL(cond.nombre, 'SIN CONDICIÓN') FROM codigos_creados cc WITH (NOLOCK) LEFT JOIN condiciones_codigo cond WITH (NOLOCK) ON cc.condicion_id = cond.id WHERE cc.id IN ({string.Join(",", pNamesCond)})";

                                cmdCond.CommandText = QueryAdapter.FormatearConsulta(qCond);
                                using var rdrCond = await cmdCond.ExecuteReaderAsync();
                                while (await rdrCond.ReadAsync())
                                {
                                    mapaCondiciones[rdrCond.GetInt32(0)] = (
                                        Convert.ToBoolean(rdrCond.GetValue(1)),
                                        rdrCond.GetString(2)
                                    );
                                }
                            }
                        }
                    }

                    // 7. Consulta de línea de tiempo futura si es edición
                    var mapaTieneFuturo = new Dictionary<int, bool>();
                    if (MovimientoIdActual.HasValue && MovimientoIdActual.Value > 0)
                    {
                        using var connFut = _db.GetConnection();
                        var dbConnFut = (DbConnection)connFut;
                        await dbConnFut.OpenAsync();

                        var codigosIdsFuturo = lookup.Values.Where(v => v.CodigoObj != null).Select(v => v.CodigoObj!.Id).Distinct().ToList();
                        foreach (var cId in codigosIdsFuturo)
                        {
                            bool tieneFuturo = await _serviceMovimiento.TieneMovimientosPosterioresAsync(
                                cId,
                                MovimientoIdActual.Value,
                                DateTime.Today,
                                dbConnFut,
                                null
                            );
                            mapaTieneFuturo[cId] = tieneFuturo;
                        }
                    }

                    // 8. Diagnóstico masivo y clasificación por producto
                    var dictTemp = new Dictionary<string, List<ItemCodigoPreview>>();
                    int total = rawList.Count;
                    int miAlmacenIdSesion = SesionSistema.AlmacenActual?.Id ?? 1;

                    for (int i = 0; i < total; i++)
                    {
                        string raw = rawList[i];
                        string norm = _serviceMovimiento.NormalizarCodigo(raw);

                        bool yaExisteEnMovimiento = setYaAgregadosEnMovimiento.Contains(norm);
                        bool duplicadoEnExcel = contadorOcurrenciasExcel.ContainsKey(norm) && contadorOcurrenciasExcel[norm] > 1;

                        lookup.TryGetValue(norm, out var coincidencia);
                        bool existeEnBD = coincidencia.CodigoObj != null;

                        string nombreProd = "❌ CÓDIGOS NO REGISTRADOS EN BD";
                        bool esValido = false;
                        string observacion = "❌ NO EXISTE EN BASE DE DATOS";
                        int categoriaId = (norm.Contains("-V-") || norm.Contains("'V'") || norm.Contains("-V'") || norm.Contains(" V ") || norm.Contains("VENTA")) ? 2 : 1;

                        // 🌟 SI ESTAMOS EDITANDO Y EL CÓDIGO YA PERTENECE AL MOVIMIENTO:
                        // Se marca como informativo/omitido para no bloquear los nuevos.
                        if (yaExisteEnMovimiento)
                        {
                            if (coincidencia.ProductoId.HasValue && prodMap.TryGetValue(coincidencia.ProductoId.Value, out string? pDescYa))
                                nombreProd = pDescYa;
                            else
                                nombreProd = "ℹ️ CÓDIGOS YA PRESENTES EN ESTE DOCUMENTO";

                            observacion = "ℹ️ YA GUARDADO EN ESTE MOVIMIENTO (SE MANTIENE)";
                            esValido = false; // No se re-inserta para no duplicar en grilla
                        }
                        else if (duplicadoEnExcel)
                        {
                            nombreProd = "⚠️ COLISIONES / DUPLICADOS EN EXCEL";
                            observacion = "❌ DUPLICADO EN EL MISMO EXCEL";
                            totalErroresDetectados++;
                        }
                        else if (!existeEnBD)
                        {
                            string codigoLimpioParaPrefijo = norm.Replace(" ", "").Replace("-", "").ToUpperInvariant();
                            var productoEmparejado = listaCatalogoProductos.FirstOrDefault(p => codigoLimpioParaPrefijo.StartsWith(p.AbreviaturaLimpia, StringComparison.OrdinalIgnoreCase));

                            if (!string.IsNullOrEmpty(productoEmparejado.Descripcion))
                            {
                                nombreProd = $"⚠️ Códigos no creados de: {productoEmparejado.Descripcion}";
                                observacion = "❌ EL PRODUCTO EXISTE PERO EL CÓDIGO NUNCA FUE CREADO";
                            }
                            else
                            {
                                nombreProd = "❌ CÓDIGOS CON FORMATO DESCONOCIDO";
                                observacion = "❌ NO COINCIDE CON NINGÚN PRODUCTO DEL CATÁLOGO";
                            }
                            totalErroresDetectados++;
                        }
                        else
                        {
                            int estadoCodigo = coincidencia.CodigoObj!.EstadoId;
                            int codigoId = coincidencia.CodigoObj.Id;
                            bool perteneceAMiAlmacen = coincidencia.CodigoObj.AlmacenId == miAlmacenIdSesion;

                            if (coincidencia.ProductoId.HasValue && prodMap.TryGetValue(coincidencia.ProductoId.Value, out string? pDesc))
                            {
                                nombreProd = pDesc;
                            }

                            bool tieneMovimientosFuturos = MovimientoIdActual.HasValue && mapaTieneFuturo.TryGetValue(codigoId, out bool tf) && tf;

                            if (tieneMovimientosFuturos)
                            {
                                nombreProd = "⚠️ CÓDIGOS CON HISTORIAL FUTURO";
                                observacion = "❌ TIENE MOVIMIENTOS POSTERIORES EN LA LÍNEA DE TIEMPO";
                                totalErroresDetectados++;
                            }
                            else if (estadoCodigo == 5)
                            {
                                nombreProd = "🚚 CÓDIGOS EN TRÁNSITO ENTRE ALMACENES";
                                observacion = "🚚 EN TRÁNSITO ENTRE ALMACENES (NO DISPONIBLE)";
                                totalErroresDetectados++;
                            }
                            else if (!perteneceAMiAlmacen && EstadoPermitido == 3)
                            {
                                nombreProd = "⚠️ CÓDIGOS DE OTRA SEDE";
                                observacion = "🏢 NO PERTENECE A TU ALMACÉN ACTUAL";
                                totalErroresDetectados++;
                            }
                            else
                            {
                                var infoCondicion = mapaCondiciones.TryGetValue(codigoId, out var condData)
                                    ? condData
                                    : (PermitirSalida: false, NombreCondicion: "DESCONOCIDO");

                                if (EstadoPermitido == 3 && !infoCondicion.PermitirSalida)
                                {
                                    nombreProd = "⚠️ CÓDIGOS CON CONDICIÓN RESTRICTIVA";
                                    observacion = $"🚫 {infoCondicion.NombreCondicion.ToUpper()} (NO PERMITE SALIDA)";
                                    totalErroresDetectados++;
                                }
                                else if (EstadoPermitido != 0 && estadoCodigo != EstadoPermitido)
                                {
                                    observacion = ObtenerDescripcionEstadoAmigable(estadoCodigo, EstadoPermitido);
                                    totalErroresDetectados++;
                                }
                                else
                                {
                                    esValido = true;
                                    observacion = "✅ LISTO PARA IMPORTAR";
                                }
                            }
                        }

                        if (!dictTemp.ContainsKey(nombreProd)) dictTemp[nombreProd] = new List<ItemCodigoPreview>();

                        dictTemp[nombreProd].Add(new ItemCodigoPreview
                        {
                            RowNumber = i + 1,
                            CodigoRaw = raw,
                            CodigoNorm = norm,
                            EstadoValido = esValido,
                            CategoriaId = categoriaId,
                            ObservacionAuditoria = observacion
                        });
                    }

                    // 9. Construcción de tarjetas
                    foreach (var kvp in dictTemp)
                    {
                        int cantValidos = kvp.Value.Count(x => x.EstadoValido);
                        int cantErrores = kvp.Value.Count(x => !x.EstadoValido);

                        bool tieneValidos = cantValidos > 0;
                        bool esTotalmenteValido = cantErrores == 0;

                        Brush bgColor, borderColor, badgeColor;
                        string badgeTexto;

                        if (esTotalmenteValido)
                        {
                            bgColor = CrearPincelSeguro("#ECFDF5");
                            borderColor = CrearPincelSeguro("#A7F3D0");
                            badgeColor = CrearPincelSeguro("#047857");
                            badgeTexto = "🟢 100% VÁLIDO";
                        }
                        else if (cantValidos > 0)
                        {
                            bgColor = CrearPincelSeguro("#FEF3C7");
                            borderColor = CrearPincelSeguro("#FDE68A");
                            badgeColor = CrearPincelSeguro("#B45309");
                            badgeTexto = $"🟡 PARCIAL ({cantValidos} Aptos / {cantErrores} Errores)";
                        }
                        else
                        {
                            bgColor = CrearPincelSeguro("#FEF2F2");
                            borderColor = CrearPincelSeguro("#FCA5A5");
                            badgeColor = CrearPincelSeguro("#B91C1C");
                            badgeTexto = "🔴 SIN CÓDIGOS VÁLIDOS";
                        }

                        var grupoMaestro = new ProductoGroupPreview
                        {
                            ProductoNombre = kvp.Key,
                            TieneValidos = tieneValidos,
                            IsSelected = esTotalmenteValido, // Solo marcado por defecto si es 100% válido
                            ResumenDiagnostico = $"Total en Lote: {kvp.Value.Count} | Aptos: {cantValidos} | Errores: {cantErrores}",
                            BadgeTexto = badgeTexto,
                            BackgroundColor = bgColor,
                            BorderColor = borderColor,
                            BadgeColor = badgeColor,
                            CodigosInternos = kvp.Value
                        };

                        _gruposProductosMaster.Add(grupoMaestro);
                    }

                    _gruposProductosMaster = _gruposProductosMaster
                        .OrderByDescending(x => x.TieneValidos)
                        .ThenBy(x => x.ProductoNombre)
                        .ToList();

                    await Task.Delay(30);
                });

                loadingModal.Owner = this;
                bool? resultadoModal = loadingModal.ShowDialog();

                if (resultadoModal == true)
                {
                    txtTotalCodigos.Text = rawList.Count.ToString();
                    FiltrarYMostrarDatos();

                    // 🚨 AVISO INMEDIATO SI EL ARCHIVO TIENE ERRORES
                    if (totalErroresDetectados > 0)
                    {
                        MessageBox.Show(
                            $"⚠️ AUDITORÍA RECHAZADA: SE ENCONTRARON {totalErroresDetectados:N0} ERROR(ES) EN EL ARCHIVO.\n\n" +
                            $"• No se puede importar lotes con inconsistencias.\n" +
                            $"• Revise las tarjetas en rojo/amarillo para ver qué códigos no existen o están dañados.\n" +
                            $"• Debe corregir su archivo Excel y volverlo a cargar para habilitar la importación.",
                            "Inconsistencias Detectadas",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else if (loadingModal.ErrorResult != null)
                {
                    MessageBox.Show($"Error al auditar en Base de Datos:\n\n{loadingModal.ErrorResult.Message}",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string ObtenerDescripcionEstadoAmigable(int estadoCodigo, int estadoPermitido)
        {
            return estadoCodigo switch
            {
                // 🔹 ESTADO 1: Creado sin ingreso al almacén
                1 => "❌ NO DISPONIBLE (ESTÁ CREADO PERO NO TIENE INGRESO)",

                // 🔹 ESTADO 3: Disponible en stock
                3 => estadoPermitido == 3
                        ? "❌ NO DISPONIBLE (STOCK YA REGISTRADO EN ALMACÉN)"
                        : "ℹ️ CÓDIGO DISPONIBLE EN ALMACÉN (NO APLICA PARA ESTA OPERACIÓN)",

                // 🔹 ESTADO 4: Ya despachado o vendido
                4 => "❌ NO DISPONIBLE (TIENE SALIDA / YA FUE VENDIDO)",

                // 🔹 ESTADO 5: Tránsito entre almacenes/sedes
                5 => "🚚 NO DISPONIBLE (EN TRÁNSITO ENTRE ALMACENES)",

                // 🔹 OTROS ESTADOS
                6 => "❌ NO DISPONIBLE (CÓDIGO ANULADO O DE BAJA)",
                _ => $"❌ NO DISPONIBLE (ESTADO NO RECONOCIDO: {estadoCodigo})"
            };
        }

        private void FiltrarYMostrarDatos()
        {
            if (_gruposProductosMaster == null || txtBuscarCodigo == null || lstProductosAgrupados == null)
                return;

            string busqueda = txtBuscarCodigo.Text?.Trim().ToLower() ?? string.Empty;

            int? categoriaFiltro = null;
            if (rbFiltroGuia != null && rbFiltroGuia.IsChecked == true)
                categoriaFiltro = 1;
            else if (rbFiltroVenta != null && rbFiltroVenta.IsChecked == true)
                categoriaFiltro = 2;

            var listaFiltrada = new List<ProductoGroupPreview>();

            foreach (var grupoMaster in _gruposProductosMaster)
            {
                if (grupoMaster == null || grupoMaster.CodigosInternos == null)
                    continue;

                var codigosFiltrados = grupoMaster.CodigosInternos.AsEnumerable();
                if (categoriaFiltro.HasValue)
                {
                    codigosFiltrados = codigosFiltrados.Where(c => c != null && c.CategoriaId == categoriaFiltro.Value);
                }

                if (!string.IsNullOrEmpty(busqueda))
                {
                    string prodNombre = grupoMaster.ProductoNombre ?? string.Empty;
                    bool coincideProducto = prodNombre.ToLower().Contains(busqueda);

                    if (!coincideProducto)
                    {
                        codigosFiltrados = codigosFiltrados.Where(c => c != null && c.CodigoRaw != null && c.CodigoRaw.ToLower().Contains(busqueda));
                    }
                }

                var codigosResultado = codigosFiltrados.ToList();

                if (codigosResultado.Any())
                {
                    var vistaCopia = new ProductoGroupPreview
                    {
                        ProductoId = grupoMaster.ProductoId,
                        ProductoNombre = grupoMaster.ProductoNombre ?? "Desconocido",
                        TieneValidos = grupoMaster.TieneValidos,
                        IsSelected = grupoMaster.IsSelected,
                        ResumenDiagnostico = grupoMaster.ResumenDiagnostico,
                        BadgeTexto = grupoMaster.BadgeTexto,
                        BackgroundColor = grupoMaster.BackgroundColor,
                        BorderColor = grupoMaster.BorderColor,
                        BadgeColor = grupoMaster.BadgeColor,
                        CodigosInternos = codigosResultado
                    };

                    vistaCopia.PropertyChanged += (s, ev) =>
                    {
                        if (ev.PropertyName == nameof(ProductoGroupPreview.IsSelected))
                        {
                            grupoMaster.IsSelected = vistaCopia.IsSelected;
                            RefrescarTotales();
                        }
                    };

                    listaFiltrada.Add(vistaCopia);
                }
            }

            lstProductosAgrupados.ItemsSource = null;
            lstProductosAgrupados.ItemsSource = listaFiltrada;

            RefrescarTotales();
        }

        private void RefrescarTotales()
        {
            if (_gruposProductosMaster == null || txtValidos == null || txtInvalidos == null || btnTransferir == null)
                return;

            int aptosSeleccionados = 0;
            int totalErroresEnArchivo = 0;

            foreach (var grupo in _gruposProductosMaster)
            {
                if (grupo == null || grupo.CodigosInternos == null) continue;

                // Errores globales en el archivo cargado
                totalErroresEnArchivo += grupo.CodigosInternos.Count(x => x == null || !x.EstadoValido);

                if (grupo.IsSelected)
                {
                    aptosSeleccionados += grupo.CodigosInternos.Count(x => x != null && x.EstadoValido);
                }
            }

            txtValidos.Text = aptosSeleccionados.ToString();
            txtInvalidos.Text = totalErroresEnArchivo.ToString();

            // 🌟 CANDADO 100% VERDE: Solo transfiere si NO hay ningún error en todo el archivo cargado
            bool todoEsValido = totalErroresEnArchivo == 0 && aptosSeleccionados > 0;

            btnTransferir.IsEnabled = todoEsValido;

            if (totalErroresEnArchivo > 0)
            {
                btnTransferir.Content = "⛔ Corrija los errores del archivo";
                btnTransferir.Background = CrearPincelSeguro("#94A3B8"); // Gris bloqueado
            }
            else if (aptosSeleccionados > 0)
            {
                btnTransferir.Content = $"🟢 Transferir ({aptosSeleccionados:N0} Códigos)";
                btnTransferir.Background = CrearPincelSeguro("#10B981"); // Verde listo
            }
            else
            {
                btnTransferir.Content = "Sin Códigos para Transferir";
                btnTransferir.Background = CrearPincelSeguro("#94A3B8");
            }

            if (brdHealth != null && txtHealth != null)
            {
                if (todoEsValido)
                {
                    brdHealth.Background = CrearPincelSeguro("#ECFDF5");
                    txtHealth.Text = "🟢 100% Válido para Importar";
                    txtHealth.Foreground = CrearPincelSeguro("#065F46");
                }
                else
                {
                    brdHealth.Background = CrearPincelSeguro("#FEF2F2");
                    txtHealth.Text = $"🔴 {totalErroresEnArchivo:N0} Inconsistencias Detectadas";
                    txtHealth.Foreground = CrearPincelSeguro("#991B1B");
                }
            }
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                scrollPrincipal.RaiseEvent(eventArgs);
            }
        }

        private void ChkSeleccionarTodos_Click(object sender, RoutedEventArgs e)
        {
            bool seleccionar = chkSeleccionarTodos.IsChecked == true;
            foreach (var grupo in _gruposProductosMaster)
            {
                if (grupo.TieneValidos)
                {
                    grupo.IsSelected = seleccionar;
                }
            }
            RefrescarTotales();
        }

        private void ChkProductoItem_Click(object sender, RoutedEventArgs e)
        {
            RefrescarTotales();
        }

        private void TxtBuscarCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            FiltrarYMostrarDatos();
        }

        private void FiltroCategoria_Changed(object sender, RoutedEventArgs e)
        {
            FiltrarYMostrarDatos();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            // 🌟 Extraer exclusivamente los códigos nuevos y válidos marcados
            CodigosImportados = _gruposProductosMaster
                .Where(g => g.IsSelected)
                .SelectMany(g => g.CodigosInternos)
                .Where(c => c.EstadoValido)
                .Select(c => c.CodigoRaw)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!CodigosImportados.Any())
            {
                MessageBox.Show(
                    "No hay códigos nuevos y válidos para agregar.\n\n" +
                    "Los códigos presentes en el Excel ya están guardados en este movimiento o contienen errores.",
                    "Sin Nuevos Códigos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}