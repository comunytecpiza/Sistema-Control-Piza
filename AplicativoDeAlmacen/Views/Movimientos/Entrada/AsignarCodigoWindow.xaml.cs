using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data; // Usar DatabaseConnection
using System;
using System.Data.Common; // Multi-Motor
using System.Windows;
using System.Windows.Controls;
using static AplicativoDeAlmacen.Data.DataConnection;
using System.Diagnostics;

namespace AplicativoDeAlmacen.Views
{
    public partial class AsignarCodigoWindow : Window
    {
        // Permite indicar desde el flujo superior cuál es el estado permitido (1 = COMPRA, 4 = otros)
        public int EstadoPermitido { get; set; } = 1;
        public RangoCodigoItem RangoProcesado { get; set; }
        public bool EsModoEdicion { get; set; } = false;
        private RangoCodigoItem _itemEnEdicion = null; // Variable para la comparación

        public bool FueConfirmado { get; set; } = false;

        private string _abreviaturaProducto;
        private int _categoriaActualId = 1; // 1 = Guía, 2 = Venta
        private int _productoId;
        private int _cantidadMaximaPermitida; // Guarda el saldo faltante del producto

        private readonly DatabaseConnection _database;
        private System.Collections.IEnumerable _itemsEnGrilla;

       
        
        // =======================================================================
        // 1️⃣ CONSTRUCTOR ORIGINAL: Se usa para AGREGAR un rango nuevo
        // =======================================================================
        // Constructor para MODO EDICIÓN (simplificado). Inicializa el formulario
        // con el rango existente para permitir edición y guardado.
        public AsignarCodigoWindow(RangoCodigoItem rangoAEditar)
        {
            InitializeComponent();

            // Inicializar dependencias y contexto mínimo
            _database = new DatabaseConnection();
            this._itemEnEdicion = rangoAEditar;
            this.RangoProcesado = rangoAEditar;
            this._abreviaturaProducto = rangoAEditar.AbreviaturaBase;
            this._productoId = rangoAEditar.productoId;
            this._categoriaActualId = rangoAEditar.CategoriaProductoId;

            // Establecer la colección local como mínimo para las validaciones
            var lista = new System.Collections.Generic.List<RangoCodigoItem> { rangoAEditar };
            this._itemsEnGrilla = lista;

            // El saldo máximo permitido en modo edición incluye la cantidad actual del ítem
            int cantidadItemActual = int.TryParse(rangoAEditar.Cantidad, out int cant) ? cant : 0;
            this._cantidadMaximaPermitida = cantidadItemActual;

            // Configuramos controles y eventos tal como en el constructor de edición complejo
            ConfigurarControlesEInicializar(rangoAEditar.Cantidad, false);

            // Rellenamos los controles con los datos del rango pasado
            txtDesde.Text = rangoAEditar.DesdeNum.ToString();
            txtHasta.Text = rangoAEditar.HastaNum.ToString();
            txtSubCantidad.Text = rangoAEditar.Cantidad;
            if (_categoriaActualId == 1) rbLibroGuia.IsChecked = true; else rbLibroVenta.IsChecked = true;

            this.EsModoEdicion = true;
        }

        public AsignarCodigoWindow(List<RangoCodigoItem> itemsEnGrilla, string abreviaturaProducto, int productoId, int cantidadFaltantePorAsignar)
        {
            this.EsModoEdicion = false;
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
            // Marcar modo edición y trabajar sobre el mismo objeto para que los cambios
            // se reflejen correctamente en la colección enlazada del padre.
            this.RangoProcesado = itemAEditar;
            this.EsModoEdicion = true;
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
            if (string.IsNullOrWhiteSpace(abreviaturaOriginal))
            {
                // Si no disponemos de abreviatura válida, devolvemos 1 como valor seguro
                return 1;
            }

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

        // Ahora acepta un parámetro opcional `estadoPermitido` (por defecto = 1) para permitir
        // reutilizar este método cuando el estado esperado cambie según el motivo del movimiento.
        public bool ValidarExistenciaRangoEnBD(int productoId, string baseLimpia, int categoriaId, int desde, int hasta, out int totalEncontrados, int estadoPermitido = 1)
        {
            totalEncontrados = 0;
            string patronBusqueda = baseLimpia + "%";

            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    dbConn.Open(); // Aseguramos conexión abierta

                    // 🌟 CAMBIO: Buscamos por Producto y Prefijo, validando el estado
                    string query = @"SELECT COUNT(*)
                    FROM codigos_creados cc
                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                    WHERE rc.producto_id = @productoId
                      AND rc.categoria_producto_id = @categoriaId
                      AND cc.codigo LIKE @patron
                      AND cc.estado_id = @estadoPermitido
                      AND TRY_CAST(RIGHT(cc.codigo, 7) AS INT) BETWEEN @desde AND @hasta";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = QueryAdapter.FormatearConsulta(query);
                        AgregarParametro(cmd, "@categoriaId", categoriaId);
                        AgregarParametro(cmd, "@productoId", productoId);
                        AgregarParametro(cmd, "@patron", patronBusqueda);
                        AgregarParametro(cmd, "@estadoPermitido", estadoPermitido);
                        AgregarParametro(cmd, "@desde", desde);
                        AgregarParametro(cmd, "@hasta", hasta);

                        object res = cmd.ExecuteScalar();
                        totalEncontrados = (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;

                        // Retornamos true si encontramos al menos el conteo esperado
                        return totalEncontrados >= (hasta - desde + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error validando rango: {ex.Message}");
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
                if (item is RangoCodigoItem rangoExistente && rangoExistente != _itemEnEdicion && rangoExistente.CategoriaProductoId == categoriaId)
                {
                    // Ajuste: Usa las propiedades numéricas directamente para evitar errores de formato
                    if (intDesde <= rangoExistente.HastaNum && intHasta >= rangoExistente.DesdeNum)
                    {
                        MessageBox.Show($"¡Conflicto local! El rango [{intDesde} - {intHasta}] se cruza con un ítem ya agregado en esta misma sesión.",
                                        "Rango Duplicado ❌", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            // Obtenemos los datos limpios de la abreviatura
            string baseLimpia = _abreviaturaProducto.Trim();

            // =======================================================================
            // 🔥 NUEVA VALIDACIÓN: BLOQUEAR SI EL RANGO YA EXISTE EN EL HISTORIAL DE LA BD
            // Nota: Si el flujo indicó que el estado permitido NO es 1 (COMPRA), entonces
            // permitimos rangos que existan en el historial porque pueden corresponder a
            // códigos que están siendo retornados (estado 4). En ese caso la validación
            // de existencia física se realiza más abajo contra la tabla `codigos_creados`.
            // =======================================================================
            if (VerificarSiRangoYaFueUsadoEnBD(this._productoId, baseLimpia, categoriaId, intDesde, intHasta, this.EstadoPermitido))
            {
                MessageBox.Show($"❌ ¡Error! El rango digitado contiene códigos que ya fueron registrados anteriormente en la base de datos.",
                                "Rango Ya Registrado", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            // 2. Control de existencia física
            int totalFisicosEncontrados = 0;

            bool rangoEsValido = ValidarExistenciaRangoEnBD(this._productoId, baseLimpia, categoriaId, intDesde, intHasta, out totalFisicosEncontrados, this.EstadoPermitido);
            int cantidadSolicitada = (intHasta - intDesde) + 1;

            if (!rangoEsValido)
            {
                if (totalFisicosEncontrados > 0 || cantidadSolicitada > 0)
                {
                    MessageBox.Show($"Error de Inventario ❌\n\nEl rango requiere {cantidadSolicitada} códigos libres cargados en el sistema, pero la base de datos registra {totalFisicosEncontrados} códigos activos válidos de tipo '{tipoTexto}' para este rango.\n\nPor favor, verifique que los códigos físicos estén importados en la tabla 'codigos_creados'.", "Validación Fallida", MessageBoxButton.OK, MessageBoxImage.Stop);
                }
                return;
            }


            if (this.EsModoEdicion && this.RangoProcesado != null)
            {
                // Actualizamos el objeto que ya existe
                this.RangoProcesado.Cantidad = cantidadSolicitada.ToString();
                this.RangoProcesado.Desde = $"{baseLimpia}-{intDesde.ToString("D7")}";
                this.RangoProcesado.Hasta = $"{baseLimpia}-{intHasta.ToString("D7")}";
                this.RangoProcesado.ColeccionTipo = $"C2026 / {tipoTexto}";
                this.RangoProcesado.DesdeNum = intDesde;
                this.RangoProcesado.HastaNum = intHasta;
                this.RangoProcesado.CategoriaProductoId = categoriaId;
                this.RangoProcesado.AbreviaturaBase = baseLimpia;
                // productoId ya debería estar correcto
            }
            else
            {
                // Creamos uno nuevo solo si no estamos editando
                this.RangoProcesado = new RangoCodigoItem
                {
                    Cantidad = cantidadSolicitada.ToString(),
                    Desde = $"{baseLimpia}-{intDesde.ToString("D7")}",
                    Hasta = $"{baseLimpia}-{intHasta.ToString("D7")}",
                    ColeccionTipo = $"C2026 / {tipoTexto}",
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

        // Ahora acepta estadoPermitido para decidir el comportamiento.
        // Si estadoPermitido != 1 asumimos que se trata de un ingreso distinto a COMPRA y
        // permitimos reutilizar rangos ya registrados (la comprobación de existencia
        // física se realiza por separado). Esto evita bloquear DEVOLUCIÓN/PROMOCIÓN/TRANSFER.
        private bool VerificarSiRangoYaFueUsadoEnBD(int productoId, string baseLimpia, int categoriaId, int desde, int hasta, int estadoPermitido = 1)
        {
            try
            {
                // Si no estamos en el caso COMPRA (estadoPermitido != 1), no bloqueamos por historial
                if (estadoPermitido != 1)
                {
                    return false;
                }
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