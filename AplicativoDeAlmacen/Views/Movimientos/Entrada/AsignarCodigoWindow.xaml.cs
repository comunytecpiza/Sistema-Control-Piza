using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Data.Common;
using System.Diagnostics;
using System.Windows;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class AsignarCodigoWindow : Window
    {
        public int EstadoPermitido { get; set; } = 1; // 1 = COMPRA, 4 = DEVOLUCIÓN
        public RangoCodigoItem RangoProcesado { get; set; }
        public bool EsModoEdicion { get; set; } = false;
        private RangoCodigoItem _itemEnEdicion = null;

        public bool FueConfirmado { get; set; } = false;

        private string _abreviaturaProducto;
        private int _categoriaActualId = 1;
        private int _productoId;

        private readonly DatabaseConnection _database;
        private System.Collections.IEnumerable _itemsEnGrilla;

        // =======================================================================
        // CONSTRUCTOR PARA MODO EDICIÓN (Doble clic en grilla)
        // =======================================================================
        public AsignarCodigoWindow(System.Collections.IEnumerable itemsEnGrilla, RangoCodigoItem itemAEditar, int cantidadFaltantePorAsignar)
        {
            InitializeComponent();
            _database = new DatabaseConnection();

            this._itemsEnGrilla = itemsEnGrilla;
            this._itemEnEdicion = itemAEditar;
            this.RangoProcesado = itemAEditar;
            this.EsModoEdicion = true;
            this._abreviaturaProducto = itemAEditar.AbreviaturaBase;
            this._productoId = itemAEditar.productoId;
            this._categoriaActualId = itemAEditar.CategoriaProductoId;

            int cantidadItemActual = int.TryParse(itemAEditar.Cantidad, out int cant) ? cant : 1;

            txtSubCantidad.Text = cantidadItemActual.ToString();
            txtSubCantidad.IsReadOnly = false;

            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();
            txtDesde.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            txtDesde.Text = itemAEditar.DesdeNum.ToString();
            txtHasta.Text = itemAEditar.HastaNum.ToString();

            if (_categoriaActualId == 1) rbLibroGuia.IsChecked = true; else rbLibroVenta.IsChecked = true;

            // 🌟 BLOQUEO AUTOMÁTICO SEGÚN EL TIPO DE PRODUCTO
            ConfigurarRestriccionCategoriaProducto();

            RecalcularRangoAutomatico();
        }

        // =======================================================================
        // CONSTRUCTOR PARA NUEVO RANGO
        // =======================================================================
        public AsignarCodigoWindow(List<RangoCodigoItem> itemsEnGrilla, string abreviaturaProducto, int productoId, int cantidadFaltantePorAsignar)
        {
            InitializeComponent();
            _database = new DatabaseConnection();

            this.EsModoEdicion = false;
            this._itemsEnGrilla = itemsEnGrilla;
            this._abreviaturaProducto = abreviaturaProducto;
            this._productoId = productoId;

            txtSubCantidad.Text = cantidadFaltantePorAsignar.ToString();
            txtSubCantidad.IsReadOnly = false;

            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();
            txtDesde.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            // 🌟 BLOQUEO AUTOMÁTICO SEGÚN EL TIPO DE PRODUCTO
            ConfigurarRestriccionCategoriaProducto();

            int sugeridoBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaProducto, _categoriaActualId);
            txtDesde.Text = sugeridoBD.ToString();

            RecalcularRangoAutomatico();
        }

        // 🌟 MÉTODO NUEVO: Detecta si el producto es Venta (V) o Guía (G) y bloquea el RadioButton opuesto
        private void ConfigurarRestriccionCategoriaProducto()
        {
            if (string.IsNullOrWhiteSpace(_abreviaturaProducto)) return;

            string abrevUpper = _abreviaturaProducto.Trim().ToUpperInvariant();

            bool esProductoVenta = abrevUpper.EndsWith("-V") || abrevUpper.EndsWith(" V") || abrevUpper.EndsWith("VENTA");
            bool esProductoGuia = abrevUpper.EndsWith("-G") || abrevUpper.EndsWith(" G") || abrevUpper.EndsWith("GUIA") || abrevUpper.EndsWith("GUÍA");

            if (esProductoVenta)
            {
                _categoriaActualId = 2; // Forzar Libro Venta
                rbLibroVenta.IsChecked = true;
                rbLibroGuia.IsEnabled = false; // 🚫 Inhabilita la opción de Guía
            }
            else if (esProductoGuia)
            {
                _categoriaActualId = 1; // Forzar Libro Guía
                rbLibroGuia.IsChecked = true;
                rbLibroVenta.IsEnabled = false; // 🚫 Inhabilita la opción de Venta
            }
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private void RecalcularRangoAutomatico()
        {
            if (string.IsNullOrEmpty(_abreviaturaProducto)) return;

            string prefijo = _categoriaActualId == 1 ? "G-" : "V-";
            lblPrefijoDesde.Text = prefijo;
            lblPrefijoHasta.Text = prefijo;

            if (!int.TryParse(txtSubCantidad.Text, out int tamanoPaquete) || tamanoPaquete <= 0)
            {
                txtHasta.Text = "";
                return;
            }

            if (int.TryParse(txtDesde.Text, out int desdeDigitado) && desdeDigitado > 0)
            {
                int hastaCalculado = desdeDigitado + tamanoPaquete - 1;
                txtHasta.Text = hastaCalculado.ToString();
            }
            else
            {
                txtHasta.Text = "";
            }

            txtDesde.IsReadOnly = false;
            txtHasta.IsReadOnly = true;
        }

        private int ObtenerSiguienteNumeroDesdeBD(string abreviaturaOriginal, int categoriaId)
        {
            if (string.IsNullOrWhiteSpace(abreviaturaOriginal)) return 1;

            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G")
                ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2)
                : abreviaturaOriginal;

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                dbConn.Open();

                string query = @"
                    SELECT COALESCE(MAX(hasta_num), 0) + 1 
                    FROM registro_rangos 
                    WHERE producto_id = @productoId
                      AND abreviatura_base = @baseLimpia 
                      AND categoria_producto_id = @categoriaId";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@productoId", this._productoId);
                AgregarParametro(cmd, "@baseLimpia", baseLimpia);
                AgregarParametro(cmd, "@categoriaId", categoriaId);

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch
            {
                return 1;
            }
        }

        public bool ValidarExistenciaRangoEnBD(int productoId, string abreviaturaRaw, int categoriaId, int desde, int hasta, out int totalEncontrados, int estadoPermitido = 1)
        {
            totalEncontrados = 0;

            if (this.EsModoEdicion)
            {
                totalEncontrados = (hasta - desde + 1);
                return true;
            }

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                string baseLimpia = abreviaturaRaw.Trim();
                string separador = baseLimpia.EndsWith("-V") || baseLimpia.EndsWith("-G") ? "-" : (categoriaId == 1 ? "-G-" : "-V-");
                string prefijoBuscado = baseLimpia.EndsWith("-") ? baseLimpia : $"{baseLimpia}-";

                // 🌟 AGREGAMOS cc.almacen_id = @almacenId PARA AISLAR LA BÚSQUEDA POR SEDE
                string query = @"
            SELECT COUNT(*)
            FROM codigos_creados cc WITH (NOLOCK)
            INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
            WHERE rc.producto_id = @productoId
              AND cc.almacen_id = @almacenId -- 👈 FILTRO POR ALMACÉN ACTIVO
              AND cc.estado_id = @estadoPermitido
              AND rc.categoria_producto_id = @categoriaId
              AND cc.codigo LIKE @prefijoPattern
              AND ISNUMERIC(RIGHT(cc.codigo, 7)) = 1
              AND CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@productoId", productoId);
                AgregarParametro(cmd, "@almacenId", miAlmacenId);
                AgregarParametro(cmd, "@estadoPermitido", estadoPermitido);
                AgregarParametro(cmd, "@categoriaId", categoriaId);
                AgregarParametro(cmd, "@prefijoPattern", prefijoBuscado + "%");
                AgregarParametro(cmd, "@desde", desde);
                AgregarParametro(cmd, "@hasta", hasta);

                object res = cmd.ExecuteScalar();
                totalEncontrados = (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;

                int cantidadEsperada = (hasta - desde + 1);
                return totalEncontrados >= cantidadEsperada;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al validar existencia de rango en BD: {ex.Message}");
                totalEncontrados = (hasta - desde + 1);
                return true;
            }
        }

        private void BtnGrabarCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtSubCantidad.Text, out int subCantidad) || subCantidad <= 0)
            {
                MessageBox.Show("Por favor, ingrese un número de unidades válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtDesde.Text, out int intDesde) || !int.TryParse(txtHasta.Text, out int intHasta))
            {
                MessageBox.Show("Verifique que los números del rango sean válidos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (intHasta < intDesde)
            {
                MessageBox.Show("El rango final no puede ser menor al inicial.", "Rango Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int categoriaId = rbLibroGuia.IsChecked == true ? 1 : 2;
            string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";

            // 🌟 VALIDACIÓN DE COINCIDENCIA DE CATEGORÍA
            string abrevUpper = _abreviaturaProducto?.Trim().ToUpperInvariant() ?? "";
            bool esProductoVenta = abrevUpper.EndsWith("-V") || abrevUpper.EndsWith(" V") || abrevUpper.EndsWith("VENTA");
            bool esProductoGuia = abrevUpper.EndsWith("-G") || abrevUpper.EndsWith(" G") || abrevUpper.EndsWith("GUIA") || abrevUpper.EndsWith("GUÍA");

            if (esProductoVenta && categoriaId == 1)
            {
                MessageBox.Show(
                    $"⚠️ Incompatibilidad de Categoría:\n\n" +
                    $"El producto seleccionado es 'LIBRO VENTA' (Alumno).\n" +
                    $"No se puede asignar un rango como 'LIBRO GUÍA' (Docente).\n\n" +
                    $"Seleccione 'Libro Venta (Alumno)' para continuar.",
                    "Categoría Incorrecta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (esProductoGuia && categoriaId == 2)
            {
                MessageBox.Show(
                    $"⚠️ Incompatibilidad de Categoría:\n\n" +
                    $"El producto seleccionado es 'LIBRO GUÍA' (Docente).\n" +
                    $"No se puede asignar un rango como 'LIBRO VENTA' (Alumno).\n\n" +
                    $"Seleccione 'Libro Guía (Docente)' para continuar.",
                    "Categoría Incorrecta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 🌟 VALIDACIÓN DE CATEGORÍA CONTRA LA BASE DE DATOS (MERCADERÍA EN STOCK)
            if (!this.EsModoEdicion)
            {
                bool existeRangoEnBD = ValidarExistenciaRangoEnBD(
                    this._productoId,
                    this._abreviaturaProducto,
                    categoriaId,
                    intDesde,
                    intHasta,
                    out int totalEncontrados,
                    this.EstadoPermitido);

                int cantidadEsperada = (intHasta - intDesde + 1);

                if (!existeRangoEnBD || totalEncontrados < cantidadEsperada)
                {
                    MessageBox.Show(
                        $"⚠️ Incompatibilidad de Lote o Serie Inexistente:\n\n" +
                        $"No se encontraron {cantidadEsperada} códigos disponibles de tipo '{tipoTexto}' para este producto en su stock actual.\n\n" +
                        $"• Códigos solicitados: {cantidadEsperada} unidades ({intDesde} al {intHasta}).\n" +
                        $"• Encontrados en stock como {tipoTexto}: {totalEncontrados} unidades.\n\n" +
                        $"Verifique si el producto seleccionado realmente cuenta con series registradas como '{tipoTexto}' o cambie el tipo de categoría.",
                        "Categoría / Serie Inexistente",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);

                    return;
                }
            }

            // 🛡️ CANDADO ÚNICO: Prevenir solapamiento entre los rangos
            if (_itemsEnGrilla != null)
            {
                foreach (var itemObj in _itemsEnGrilla)
                {
                    if (itemObj is RangoCodigoItem rangoExistente)
                    {
                        if (this.EsModoEdicion && _itemEnEdicion != null && _itemEnEdicion == rangoExistente)
                            continue;

                        if (rangoExistente.CategoriaProductoId == categoriaId)
                        {
                            int exDesde = rangoExistente.DesdeNum;
                            int exHasta = rangoExistente.HastaNum;

                            bool hayCruce = (intDesde <= exHasta) && (intHasta >= exDesde);

                            if (hayCruce)
                            {
                                MessageBox.Show(
                                    $"⚠️ Rango Duplicado o Solapado:\n\n" +
                                    $"El rango ingresado ({intDesde} al {intHasta}) se cruza con una serie que ya agregó previamente a la lista:\n" +
                                    $"• Serie existente: del {exDesde} al {exHasta}\n\n" +
                                    $"Por favor, ingrese un rango que no contenga códigos repetidos.",
                                    "Serie de Códigos Duplicada",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                return;
                            }
                        }
                    }
                }
            }

            string baseLimpia = _abreviaturaProducto.Trim();
            string separador = baseLimpia.EndsWith("-V") || baseLimpia.EndsWith("-G") ? "-" : (categoriaId == 1 ? "-G-" : "-V-");
            string desdeFormatted = $"{baseLimpia}{separador}{intDesde:D7}";
            string hastaFormatted = $"{baseLimpia}{separador}{intHasta:D7}";

            int cantidadSolicitada = (intHasta - intDesde) + 1;

            string anoColeccionBD = ObtenerAnoColeccionRealBD(this._productoId, baseLimpia, categoriaId, intDesde);
            string coleccionTipoDinamica = $"C{anoColeccionBD} / {tipoTexto}";

            if (this.EsModoEdicion && this.RangoProcesado != null)
            {
                this.RangoProcesado.Cantidad = cantidadSolicitada.ToString();
                this.RangoProcesado.Desde = desdeFormatted;
                this.RangoProcesado.Hasta = hastaFormatted;
                this.RangoProcesado.ColeccionTipo = coleccionTipoDinamica;
                this.RangoProcesado.DesdeNum = intDesde;
                this.RangoProcesado.HastaNum = intHasta;
                this.RangoProcesado.CategoriaProductoId = categoriaId;
                this.RangoProcesado.AbreviaturaBase = baseLimpia;
            }
            else
            {
                this.RangoProcesado = new RangoCodigoItem
                {
                    Cantidad = cantidadSolicitada.ToString(),
                    Desde = desdeFormatted,
                    Hasta = hastaFormatted,
                    ColeccionTipo = coleccionTipoDinamica,
                    DesdeNum = intDesde,
                    HastaNum = intHasta,
                    CategoriaProductoId = categoriaId,
                    AbreviaturaBase = baseLimpia,
                    productoId = this._productoId
                };
            }

            this.FueConfirmado = true;
            this.DialogResult = true;
        }

        private string ObtenerAnoColeccionRealBD(int productoId, string abreviaturaRaw, int categoriaId, int desde)
        {
            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                string baseLimpia = abreviaturaRaw.Trim();
                string separador = baseLimpia.EndsWith("-V") || baseLimpia.EndsWith("-G") ? "-" : (categoriaId == 1 ? "-G-" : "-V-");
                string codigoBuscado = $"{baseLimpia}{separador}{desde:D7}";

                string query;

                if (QueryAdapter.EsMySQL)
                {
                    // 🟢 Versión limpia para MySQL (Sin WITH NOLOCK y con LIMIT 1 al final)
                    query = @"
                SELECT c.ano
                FROM codigos_creados cc
                INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                INNER JOIN colecciones c ON rc.coleccion_id = c.id
                WHERE rc.producto_id = @productoId
                  AND (cc.codigo = @codigoBuscado OR cc.codigo LIKE @prefijoPattern)
                LIMIT 1;";
                }
                else
                {
                    // 🛡️ Versión original para SQL Server
                    query = @"
                SELECT TOP 1 c.ano
                FROM codigos_creados cc WITH (NOLOCK)
                INNER JOIN registro_codigos rc WITH (NOLOCK) ON cc.registro_codigo_id = rc.id
                INNER JOIN colecciones c WITH (NOLOCK) ON rc.coleccion_id = c.id
                WHERE rc.producto_id = @productoId
                  AND (cc.codigo = @codigoBuscado OR cc.codigo LIKE @prefijoPattern)";
                }

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                AgregarParametro(cmd, "@productoId", productoId);
                AgregarParametro(cmd, "@codigoBuscado", codigoBuscado);
                AgregarParametro(cmd, "@prefijoPattern", $"{baseLimpia}{separador}%");

                object res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    return res.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo año de colección: {ex.Message}");
            }

            return DateTime.Now.Year.ToString();
        }

        private void BtnCancelarRango_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}