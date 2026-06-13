using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data; // Usar DatabaseConnection
using System;
using System.Data.Common; // Multi-Motor
using System.Windows;
using System.Windows.Controls;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class AsignarCodigoWindow : Window
    {
        public RangoCodigoItem RangoProcesado { get; set; }
        public bool FueConfirmado { get; set; } = false;

        private string _abreviaturaProducto;
        private int _categoriaActualId = 1; // 1 = Guía, 2 = Venta
        private int _productoId;
        private int _cantidadMaximaPermitida; // Guarda el saldo faltante del producto

        private readonly DatabaseConnection _database;
        private System.Collections.IEnumerable _itemsEnGrilla;

        // 🛠️ VARIABLE NUEVA: Guarda una referencia al ítem que estamos editando (si aplica)
        private RangoCodigoItem _itemEnEdicion = null;

        // =======================================================================
        // 1️⃣ CONSTRUCTOR ORIGINAL: Se usa para AGREGAR un rango nuevo
        // =======================================================================

        public AsignarCodigoWindow(System.Collections.IEnumerable itemsEnGrilla, string abreviaturaProducto, int productoId, int cantidadFaltantePorAsignar)
        {
            InitializeComponent();
            this._itemsEnGrilla = itemsEnGrilla;
            this._abreviaturaProducto = abreviaturaProducto;
            this._productoId = productoId;
            this._cantidadMaximaPermitida = cantidadFaltantePorAsignar;

            _database = new DatabaseConnection();

            ConfigurarControlesEInicializar(cantidadFaltantePorAsignar.ToString(), true);
        }

        // =======================================================================
        // 2️⃣ CONSTRUCTOR NUEVO: Se usa exclusivamente para MODIFICAR un rango existente
        // =======================================================================
        public AsignarCodigoWindow(System.Collections.IEnumerable itemsEnGrilla, RangoCodigoItem itemAEditar, int cantidadFaltantePorAsignar)
        {
            InitializeComponent();
            this._itemsEnGrilla = itemsEnGrilla;
            this._itemEnEdicion = itemAEditar; // Guardamos el ítem que se está modificando
            this._abreviaturaProducto = itemAEditar.AbreviaturaBase;
            this._productoId = itemAEditar.productoId;
            this._categoriaActualId = itemAEditar.CategoriaProductoId;

            // El saldo máximo permitido en modo edición es el saldo faltante actual MÁS la cantidad que ya tenía este ítem guardada
            int cantidadItemActual = int.TryParse(itemAEditar.Cantidad, out int cant) ? cant : 0;
            this._cantidadMaximaPermitida = cantidadFaltantePorAsignar + cantidadItemActual;

            _database = new DatabaseConnection();

            ConfigurarControlesEInicializar(itemAEditar.Cantidad, false);

            // Reestablecemos el estado exacto en los controles de la UI
            txtDesde.Text = itemAEditar.DesdeNum.ToString();
            txtHasta.Text = itemAEditar.HastaNum.ToString();
            if (_categoriaActualId == 1) rbLibroGuia.IsChecked = true; else rbLibroVenta.IsChecked = true;
        }

        // Método auxiliar para no duplicar la configuración inicial de los eventos
        private void ConfigurarControlesEInicializar(string cantidadTexto, bool esNuevo)
        {
            txtSubCantidad.Text = cantidadTexto;
            txtSubCantidad.IsReadOnly = false;
            txtSubCantidad.Background = System.Windows.Media.Brushes.White;

            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();
            txtDesde.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            if (esNuevo)
            {
                int sugeridoBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaProducto, _categoriaActualId);
                txtDesde.Text = sugeridoBD.ToString();
            }

            RecalcularRangoAutomatico();
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

            int proximoNumeroBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaProducto, _categoriaActualId);
            int ultimoNumeroBD = proximoNumeroBD > 1 ? proximoNumeroBD - 1 : 0;

            int maxHastaLocal = 0;
            foreach (var item in _itemsEnGrilla)
            {
                // 🔥 CORRECCIÓN: Al calcular el máximo local, ignoramos el ítem que estamos editando actualmente
                if (item is RangoCodigoItem rango && rango != _itemEnEdicion && rango.CategoriaProductoId == _categoriaActualId)
                {
                    if (rango.HastaNum > maxHastaLocal)
                    {
                        maxHastaLocal = rango.HastaNum;
                    }
                }
            }

            string txtBD = ultimoNumeroBD > 0 ? ultimoNumeroBD.ToString() : "Ninguno";
            string txtGrilla = maxHastaLocal > 0 ? maxHastaLocal.ToString() : "Ninguno";

            if (lblInfoUltimoRango != null)
            {
                lblInfoUltimoRango.Text = $"💡 Guía: Último en BD: [{txtBD}] | Último en grilla actual: [{txtGrilla}]";
            }

            if (string.IsNullOrEmpty(txtDesde.Text.Trim()))
            {
                if (maxHastaLocal > 0)
                {
                    txtDesde.Text = (maxHastaLocal + 1).ToString();
                }
                else
                {
                    txtDesde.Text = proximoNumeroBD.ToString();
                }
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
            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G")
                ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2)
                : abreviaturaOriginal;

            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    dbConn.Open();

                    string query = @"
                        SELECT COALESCE(MAX(hasta_num), 0) + 1 
                        FROM registro_rangos 
                        WHERE producto_id = @productoId
                          AND abreviatura_base = @baseLimpia 
                          AND categoria_producto_id = @categoriaId";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        AgregarParametro(cmd, "@productoId", this._productoId);
                        AgregarParametro(cmd, "@baseLimpia", baseLimpia);
                        AgregarParametro(cmd, "@categoriaId", categoriaId);

                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception)
            {
                return 1;
            }
        }

        public bool ValidarExistenciaRangoEnBD(int productoId, string abreviaturaOriginal, int categoriaId, int desde, int hasta, out int totalEncontrados)
        {
            totalEncontrados = 0;
            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G")
                ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2)
                : abreviaturaOriginal;

            string patronBusqueda = "%" + baseLimpia + "%";

            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    dbConn.Open();

                    string query = @"
                        SELECT COUNT(*) 
                        FROM codigos_creados cc
                        INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                        WHERE rc.producto_id = @productoId
                          AND rc.categoria_producto_id = @categoriaId
                          AND LTRIM(RTRIM(cc.codigo)) LIKE @patron
                          AND cc.estado_id = 1
                          AND TRY_CAST(
                              REVERSE(
                                  SUBSTRING(
                                      REVERSE(LTRIM(RTRIM(cc.codigo))), 
                                      1, 
                                      CHARINDEX('-', REVERSE(LTRIM(RTRIM(cc.codigo)))) - 1
                                  )
                              ) AS INT
                            ) BETWEEN @desde AND @hasta";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        AgregarParametro(cmd, "@productoId", productoId);
                        AgregarParametro(cmd, "@patron", patronBusqueda);
                        AgregarParametro(cmd, "@categoriaId", categoriaId);
                        AgregarParametro(cmd, "@desde", desde);
                        AgregarParametro(cmd, "@hasta", hasta);

                        totalEncontrados = Convert.ToInt32(cmd.ExecuteScalar());

                        int cantidadExpectedEnTramo = (hasta - desde) + 1;
                        return totalEncontrados == cantidadExpectedEnTramo;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error al validar los códigos en la base de datos.\n\nDetalle técnico: {ex.Message}",
                                "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void BtnGrabarCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtSubCantidad.Text, out int subCantidad) || subCantidad <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida de códigos para este paquete.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (subCantidad > _cantidadMaximaPermitida)
            {
                MessageBox.Show($"La cantidad ingresada ({subCantidad}) es mayor al saldo disponible que queda por registrar ({_cantidadMaximaPermitida}).\nPor favor disminuya la cantidad.", "Cantidad Excedida 🛑", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtDesde.Text, out int intDesde) || !int.TryParse(txtHasta.Text, out int intHasta))
            {
                MessageBox.Show("Por favor, verifique el rango numérico.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (intHasta < intDesde)
            {
                MessageBox.Show("El rango final no puede ser menor que el inicial.", "Rango Inválido", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int categoriaId = rbLibroGuia.IsChecked == true ? 1 : 2;
            string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";

            // 1. Control de solapamiento local
            foreach (var item in _itemsEnGrilla)
            {
                // 🔥 CORRECCIÓN CRUCIAL: Si estamos editando un ítem, ignoramos la comparación contra sí mismo para evitar el falso error de concurrencia
                if (item is RangoCodigoItem rangoExistente && rangoExistente != _itemEnEdicion && rangoExistente.CategoriaProductoId == categoriaId)
                {
                    if (intDesde <= rangoExistente.HastaNum && intHasta >= rangoExistente.OriginalDesdeNum())
                    {
                        MessageBox.Show($"¡Error de Concurrencia! El rango [{intDesde} - {intHasta}] se cruza con un lote que ya listó en la grilla superior.", "Rango Duplicado ❌", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            // Obtenemos los datos limpios de la abreviatura
            string baseLimpia = _abreviaturaProducto;

            // =======================================================================
            // 🔥 NUEVA VALIDACIÓN: BLOQUEAR SI EL RANGO YA EXISTE EN EL HISTORIAL DE LA BD
            // =======================================================================
            if (VerificarSiRangoYaFueUsadoEnBD(this._productoId, baseLimpia, categoriaId, intDesde, intHasta))
            {
                MessageBox.Show($"❌ ¡ERROR DE VALIDACIÓN!\n\nEl rango digitado [{intDesde} - {intHasta}] contiene números que ya fueron registrados anteriormente en la base de datos.\n\nPor favor, respete el correlativo automático sugerido por el sistema.",
                                "Rango Ya Registrado", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            // 2. Control de existencia física
            int totalFisicosEncontrados = 0;
            string abreviaturaConSufijoOriginal = baseLimpia + (categoriaId == 1 ? "-G" : "-V");
            bool rangoEsValido = ValidarExistenciaRangoEnBD(this._productoId, abreviaturaConSufijoOriginal, categoriaId, intDesde, intHasta, out totalFisicosEncontrados);
            int cantidadSolicitada = (intHasta - intDesde) + 1;

            if (!rangoEsValido)
            {
                if (totalFisicosEncontrados > 0 || cantidadSolicitada > 0)
                {
                    MessageBox.Show($"Error de Inventario ❌\n\nEl rango requiere {cantidadSolicitada} códigos libres cargados en el sistema, pero la base de datos registra {totalFisicosEncontrados} códigos activos válidos de tipo '{tipoTexto}' para este rango.\n\nPor favor, verifique que los códigos físicos estén importados en la tabla 'codigos_creados'.", "Validación Fallida", MessageBoxButton.OK, MessageBoxImage.Stop);
                }
                return;
            }

            string sufijoVisual = categoriaId == 1 ? "-G" : "-V";

            this.RangoProcesado = new RangoCodigoItem
            {
                Cantidad = cantidadSolicitada.ToString(),
                Desde = $"{baseLimpia}{sufijoVisual}-{intDesde.ToString("D7")}",
                Hasta = $"{baseLimpia}{sufijoVisual}-{intHasta.ToString("D7")}",
                ColeccionTipo = $"C2026 / {tipoTexto}",
                DesdeNum = intDesde,
                HastaNum = intHasta,
                CategoriaProductoId = categoriaId,
                AbreviaturaBase = baseLimpia,
                productoId = this._productoId
            };

            this.FueConfirmado = true;
            this.DialogResult = true;
        }

        private bool VerificarSiRangoYaFueUsadoEnBD(int productoId, string baseLimpia, int categoriaId, int desde, int hasta)
        {
            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                    string query = @"
                        SELECT COUNT(*) 
                        FROM registro_rangos 
                        WHERE producto_id = @productoId 
                          AND abreviatura_base = @baseLimpia 
                          AND categoria_producto_id = @categoriaId
                          AND (@desde <= hasta_num AND @hasta >= desde_num)";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        AgregarParametro(cmd, "@productoId", productoId);
                        AgregarParametro(cmd, "@baseLimpia", baseLimpia);
                        AgregarParametro(cmd, "@categoriaId", categoriaId);
                        AgregarParametro(cmd, "@desde", desde);
                        AgregarParametro(cmd, "@hasta", hasta);

                        int registrosChocantes = Convert.ToInt32(cmd.ExecuteScalar());
                        return registrosChocantes > 0;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void BtnCancelarRango_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public static class RangoExtension
    {
        public static int OriginalDesdeNum(this RangoCodigoItem item)
        {
            return item.DesdeNum;
        }
    }
}