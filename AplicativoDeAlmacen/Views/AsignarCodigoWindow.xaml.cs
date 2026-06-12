using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data; // Agregado para usar DatabaseConnection
using System;
using System.Data.Common; // Cambiado para Multi-Motor
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

        // 1. ELIMINAMOS la cadena hardcodeada y usamos la conexión central
        private readonly DatabaseConnection _database;
        private System.Collections.IEnumerable _itemsEnGrilla;

        public AsignarCodigoWindow(System.Collections.IEnumerable itemsEnGrilla, string abreviaturaProducto, int productoId)
        {
            InitializeComponent();
            this._itemsEnGrilla = itemsEnGrilla;
            this._abreviaturaProducto = abreviaturaProducto;
            this._productoId = productoId;

            // Inicializamos la conexión global
            _database = new DatabaseConnection();

            txtSubCantidad.Text = "0";
            txtSubCantidad.IsReadOnly = false;
            txtSubCantidad.Background = System.Windows.Media.Brushes.White;

            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            RecalcularRangoAutomatico();
        }

        // =======================================================
        // HELPER PARA PARÁMETROS
        // =======================================================
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
                txtDesde.Text = "";
                txtHasta.Text = "";
                return;
            }

            int proximoNumeroBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaProducto, _categoriaActualId);

            int maxHastaLocal = 0;
            foreach (var item in _itemsEnGrilla)
            {
                if (item is RangoCodigoItem rango && rango.CategoriaProductoId == _categoriaActualId)
                {
                    if (rango.HastaNum > maxHastaLocal)
                    {
                        maxHastaLocal = rango.HastaNum;
                    }
                }
            }

            int desdeFinal = Math.Max(proximoNumeroBD, maxHastaLocal > 0 ? maxHastaLocal + 1 : 1);
            int hastaFinal = desdeFinal + tamanoPaquete - 1;

            txtDesde.Text = desdeFinal.ToString();
            txtHasta.Text = hastaFinal.ToString();

            txtDesde.IsReadOnly = true;
            txtHasta.IsReadOnly = true;
        }

        private int ObtenerSiguienteNumeroDesdeBD(string abreviaturaOriginal, int categoriaId)
        {
            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G")
                ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2)
                : abreviaturaOriginal;

            try
            {
                // Usamos la conexión central
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    dbConn.Open(); // Síncrono porque estamos en evento UI rápido

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

            // APLICAMOS TRY-CATCH PARA UX Y USAMOS CONEXIÓN CENTRAL
            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    dbConn.Open();

                    // Mantenemos tu consulta compleja pero la pasamos por el Formateador
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

                        int cantidadTeorica = (hasta - desde) + 1;
                        return totalEncontrados == cantidadTeorica;
                    }
                }
            }
            catch (Exception ex)
            {
                // Mejora de UX: Muestra el error real de la base de datos sin crashear la app
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

            if (!int.TryParse(txtDesde.Text, out int intDesde) || !int.TryParse(txtHasta.Text, out int intHasta))
            {
                MessageBox.Show("Por favor, verifique el rango numérico.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int categoriaId = rbLibroGuia.IsChecked == true ? 1 : 2;
            string tipoTexto = categoriaId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";

            foreach (var item in _itemsEnGrilla)
            {
                if (item is RangoCodigoItem rangoExistente && rangoExistente.CategoriaProductoId == categoriaId)
                {
                    if (intDesde <= rangoExistente.HastaNum && intHasta >= rangoExistente.OriginalDesdeNum())
                    {
                        MessageBox.Show($"¡Error de Concurrencia! El rango [{intDesde} - {intHasta}] se cruza con un lote ya listado.", "Rango Duplicado ❌", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            var ventanaPadre = (AgregarItemWindow)this.Owner;
            string abreviaturaOriginal = ventanaPadre._productoSeleccionado.Abreviatura ?? "";

            int totalFisicosEncontrados = 0;

            bool rangoEsValido = ValidarExistenciaRangoEnBD(this._productoId, abreviaturaOriginal, categoriaId, intDesde, intHasta, out totalFisicosEncontrados);
            int cantidadSolicitada = (intHasta - intDesde) + 1;

            if (!rangoEsValido)
            {
                // Si la validación devuelve 0 y falla, pero sabemos que hubo un Exception en el TryCatch, el usuario ya vio el mensaje.
                // Evitamos mostrar el mensaje de inventario si el error fue de conexión (totalFisicosEncontrados == 0 por error).
                if (totalFisicosEncontrados > 0 || cantidadSolicitada > 0)
                {
                    MessageBox.Show($"Error de Inventario ❌\n\nEl rango requiere {cantidadSolicitada} códigos libres cargados en el sistema, pero la base de datos registra {totalFisicosEncontrados} códigos activos válidos de tipo '{tipoTexto}' para este producto.\n\nPor favor, verifique que los códigos físicos estén importados en la tabla 'codigos_creados'.", "Validación Fallida", MessageBoxButton.OK, MessageBoxImage.Stop);
                }
                return;
            }

            string sufijoVisual = categoriaId == 1 ? "-G" : "-V";
            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G") ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2) : abreviaturaOriginal;

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