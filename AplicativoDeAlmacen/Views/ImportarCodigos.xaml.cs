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
        public string CodigoRaw { get; set; }
        public string CodigoNorm { get; set; }
        public bool EstadoValido { get; set; }
        public int CategoriaId { get; set; } // 1 = GUÍA, 2 = VENTA
        public string CategoriaTexto => CategoriaId == 1 ? "📘 GUÍA" : "📙 VENTA";
        public string ObservacionAuditoria { get; set; }

    }


    public class ProductoGroupPreview : INotifyPropertyChanged
    {
        private bool _isSelected;
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; }
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

        public string ResumenDiagnostico { get; set; }
        public string BadgeTexto { get; set; }

        public Brush BackgroundColor { get; set; }
        public Brush BorderColor { get; set; }
        public Brush BadgeColor { get; set; }

        
        public List<ItemCodigoPreview> CodigosInternos { get; set; } = new List<ItemCodigoPreview>();
        public List<string> CodigosYaAgregadosEnMovimiento { get; set; } = new List<string>();

        public event PropertyChangedEventHandler PropertyChanged;
        // 🌟 PROPIEDAD PARA RECIBIR LOS CÓDIGOS QUE YA ESTÁN EN LA GRILLA DEL MOVIMIENTO
        
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }



    public partial class ImportarCodigos : Window
    {
        public int EstadoPermitido { get; set; } = 0;
        public List<string> CodigosImportados { get; set; } = new List<string>();
        // Lista pública de códigos que ya fueron agregados al movimiento activo.
        // Se añadió para que otras vistas puedan pasar/leer los códigos ya presentes
        // y evitar duplicados al importar desde Excel.
        public List<string> CodigosYaAgregadosEnMovimiento { get; set; } = new List<string>();

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

                var loadingModal = new ProgressWindow("Auditoría por Producto", "Validando base de datos y categorías...", async (progress) =>
                {
                    // 1. Lectura inteligente del archivo Excel o TXT
                    if (extension == ".xlsx")
                        rawList = LeerExcelInteligente(openFileDialog.FileName);
                    else
                        rawList = File.ReadLines(openFileDialog.FileName).Select(LimpiarCodigo).Where(x => !string.IsNullOrEmpty(x)).ToList();

                    if (!rawList.Any()) return;

                    // 2. Set para colisiones con códigos ya presentes en la grilla principal
                    var setYaAgregadosEnMovimiento = new HashSet<string>(
                        (CodigosYaAgregadosEnMovimiento ?? new List<string>()).Select(x => _serviceMovimiento.NormalizarCodigo(x)),
                        StringComparer.OrdinalIgnoreCase
                    );

                    // 3. Detección de duplicados dentro del archivo Excel
                    var contadorOcurrenciasExcel = rawList
                        .GroupBy(x => _serviceMovimiento.NormalizarCodigo(x), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                    // 4. Búsqueda masiva en la Base de Datos (1 solo viaje)
                    var lookup = await _serviceMovimiento.ObtenerCodigosPorListaAsync(rawList);

                    var prodIds = lookup.Values
                        .Where(v => v.ProductoId.HasValue)
                        .Select(v => v.ProductoId.Value)
                        .Distinct()
                        .ToList();

                    var prodMap = new Dictionary<int, string>();

                    if (prodIds.Any())
                    {
                        using var conn = _db.GetConnection();
                        var dbConn = (DbConnection)conn;
                        await dbConn.OpenAsync();

                        const int chunkProdSize = 200;
                        for (int pIdx = 0; pIdx < prodIds.Count; pIdx += chunkProdSize)
                        {
                            var subIds = prodIds.Skip(pIdx).Take(chunkProdSize).ToList();
                            var paramNames = new List<string>();

                            using var cmd = dbConn.CreateCommand();

                            for (int i = 0; i < subIds.Count; i++)
                            {
                                string pName = "@p" + i;
                                paramNames.Add(pName);
                                var p = cmd.CreateParameter();
                                p.ParameterName = pName;
                                p.Value = subIds[i];
                                cmd.Parameters.Add(p);
                            }

                            string q = $"SELECT id, descripcion FROM productos WHERE id IN ({string.Join(',', paramNames)})";
                            cmd.CommandText = QueryAdapter.FormatearConsulta(q);

                            using var rdr = await cmd.ExecuteReaderAsync();
                            while (await rdr.ReadAsync())
                            {
                                if (!rdr.IsDBNull(0)) prodMap[rdr.GetInt32(0)] = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                            }
                        }
                    }

                    // 5. Evaluación ultra-rápida EN MEMORIA (Sin consultas a la BD dentro del bucle)
                    var dictTemp = new Dictionary<string, List<ItemCodigoPreview>>();
                    int total = rawList.Count;

                    int miAlmacenIdSesion = AplicativoDeAlmacen.Core.SesionSistema.AlmacenActual?.Id ?? 1;

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

                        // Deducción de Categoría limpia en memoria
                        int categoriaId = (norm.Contains("-V-") || norm.Contains("'V'") || norm.Contains("-V'") || norm.Contains(" V ") || norm.Contains("VENTA")) ? 2 : 1;

                        if (yaExisteEnMovimiento)
                        {
                            nombreProd = "⚠️ CÓDIGOS YA ENLAZADOS EN ESTE MOVIMIENTO";
                            observacion = "❌ YA FUE AGREGADO EN EL MOVIMIENTO ACTUAL";
                        }
                        else if (duplicadoEnExcel)
                        {
                            nombreProd = "⚠️ COLISIONES / DUPLICADOS EN EXCEL";
                            observacion = "❌ DUPLICADO EN EXCEL";
                        }
                        else if (existeEnBD)
                        {
                            // 🔒 Validación por Almacén de Sesión
                            bool perteneceAMiAlmacen = coincidencia.CodigoObj.AlmacenId == miAlmacenIdSesion;

                            if (!perteneceAMiAlmacen)
                            {
                                nombreProd = "⚠️ CÓDIGOS NO DISPONIBLES EN SU STOCK";
                                observacion = "❌ NO ESTÁ EN TU STOCK ACTUAL"; // 👈 MENSAJE CONFIDENCIAL ACTUALIZADO
                            }
                            else
                            {
                                if (coincidencia.ProductoId.HasValue && prodMap.TryGetValue(coincidencia.ProductoId.Value, out string pDesc))
                                {
                                    nombreProd = pDesc;
                                }

                                if (EstadoPermitido != 0 && coincidencia.CodigoObj?.EstadoId != EstadoPermitido)
                                {
                                    observacion = $"❌ ESTADO INVÁLIDO (Estado: {coincidencia.CodigoObj?.EstadoId})";
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

                    // 6. Generación de tarjetas por Producto
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
                            badgeTexto = $"🟡 PARCIAL ({cantValidos} Aptos)";
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
                            IsSelected = tieneValidos,
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
                }
                else if (loadingModal.ErrorResult != null)
                {
                    MessageBox.Show($"Error al consultar MySQL en la auditoría:\n\n{loadingModal.ErrorResult.Message}",
                                    "Error de Base de Datos", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 🌟 FILTRADO DINÁMICO COMBINADO (CATEGORÍA + BÚSQUEDA BIVALENTE)
        private void FiltrarYMostrarDatos()
        {
            // 🛡️ CANDADO DE SEGURIDAD CONTRA INICIALIZACIÓN XAML INCOMPLETA
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

                // 1. Filtrar códigos por categoría si aplica
                var codigosFiltrados = grupoMaster.CodigosInternos.AsEnumerable();
                if (categoriaFiltro.HasValue)
                {
                    codigosFiltrados = codigosFiltrados.Where(c => c != null && c.CategoriaId == categoriaFiltro.Value);
                }

                // 2. Filtrar por búsqueda si hay texto
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

                // Si tiene códigos que coincidan con los filtros, mostramos el grupo
                if (codigosResultado.Any())
                {
                    // Creamos una vista filtrada (copia ligera) pero sincronizamos la selección con el grupo maestro
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

                    // Cuando el usuario cambie la selección en la vista filtrada, propagamos el cambio al grupo maestro
                    vistaCopia.PropertyChanged += (s, ev) =>
                    {
                        if (ev.PropertyName == nameof(ProductoGroupPreview.IsSelected))
                        {
                            grupoMaster.IsSelected = vistaCopia.IsSelected;
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
            int omitidosOErrores = 0;

            foreach (var grupo in _gruposProductosMaster)
            {
                if (grupo == null || grupo.CodigosInternos == null) continue;

                // 🌟 SI EL PRODUCTO ESTÁ SELECCIONADO:
                if (grupo.IsSelected)
                {
                    aptosSeleccionados += grupo.CodigosInternos.Count(x => x != null && x.EstadoValido);
                    omitidosOErrores += grupo.CodigosInternos.Count(x => x == null || !x.EstadoValido);
                }
                else // 🌟 SI EL PRODUCTO FUE DESMARCADO: TODOS SUS CÓDIGOS PASAN A OMITIDOS
                {
                    omitidosOErrores += grupo.CodigosInternos.Count;
                }
            }

            txtValidos.Text = aptosSeleccionados.ToString();
            txtInvalidos.Text = omitidosOErrores.ToString();

            btnTransferir.IsEnabled = aptosSeleccionados > 0;
            btnTransferir.Content = aptosSeleccionados > 0 ? $"Transferir ({aptosSeleccionados} Códigos Aptos)" : "Sin Selección Válida";

            if (brdHealth != null && txtHealth != null)
            {
                if (aptosSeleccionados > 0 && omitidosOErrores == 0)
                {
                    brdHealth.Background = CrearPincelSeguro("#ECFDF5");
                    txtHealth.Text = "🟢 Importación Íntegra";
                    txtHealth.Foreground = CrearPincelSeguro("#065F46");
                }
                else
                {
                    brdHealth.Background = CrearPincelSeguro("#FEF3C7");
                    txtHealth.Text = $"🟡 Parcial ({aptosSeleccionados} Aptos)";
                    txtHealth.Foreground = CrearPincelSeguro("#92400E");
                }
            }
        }

        // 🌟 INTERCEPTOR DE RUEDA DEL MOUSE PARA MANTENER EL SCROLL FLUIDO
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
            CodigosImportados = _gruposProductosMaster
                .Where(g => g.IsSelected)
                .SelectMany(g => g.CodigosInternos)
                .Where(c => c.EstadoValido)
                .Select(c => c.CodigoRaw)
                .ToList();

            if (!CodigosImportados.Any())
            {
                MessageBox.Show("No hay códigos válidos seleccionados para transferir.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        // 🟢 Detección inteligente respetando la abreviatura del Excel
        
    }
}