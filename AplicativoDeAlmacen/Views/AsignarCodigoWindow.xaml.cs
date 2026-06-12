using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Controls;

namespace AplicativoDeAlmacen.Views
{
    public partial class AsignarCodigoWindow : Window
    {
        public RangoCodigoItem RangoProcesado { get; set; }
        public bool FueConfirmado { get; set; } = false;

        private string _abreviaturaProducto;
        private int _categoriaActualId = 1; // 1 = Guía, 2 = Venta
        private int _productoId;

        private readonly string TuCadenaConexion = "Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        private System.Collections.IEnumerable _itemsEnGrilla;

        public AsignarCodigoWindow(System.Collections.IEnumerable itemsEnGrilla, string abreviaturaProducto, int productoId)
        {
            InitializeComponent();
            this._itemsEnGrilla = itemsEnGrilla;
            this._abreviaturaProducto = abreviaturaProducto;
            this._productoId = productoId;

            // Dejamos libre el campo para que el usuario ponga el tamaño de este paquete/lote (ej. 100, 50, etc.)
            txtSubCantidad.Text = "0"; // Valor sugerido por defecto
            txtSubCantidad.IsReadOnly = false;
            txtSubCantidad.Background = System.Windows.Media.Brushes.White;

            // Al cambiar el tamaño del paquete, se recalcula el "Hasta" en tiempo real
            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            RecalcularRangoAutomatico();
        }

        private void RecalcularRangoAutomatico()
        {
            if (string.IsNullOrEmpty(_abreviaturaProducto)) return;

            string prefijo = _categoriaActualId == 1 ? "G-" : "V-";
            lblPrefijoDesde.Text = prefijo;
            lblPrefijoHasta.Text = prefijo;

            // Leer cuántos códigos contiene este paquete específico
            if (!int.TryParse(txtSubCantidad.Text, out int tamanoPaquete) || tamanoPaquete <= 0)
            {
                txtDesde.Text = "";
                txtHasta.Text = "";
                return;
            }

            int proximoNumeroBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaProducto, _categoriaActualId);

            // Buscar el último número usado en la grilla local de esta sesión para continuar la secuencia
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

            // El "Desde" inicia en el número más alto disponible
            int desdeFinal = Math.Max(proximoNumeroBD, maxHastaLocal > 0 ? maxHastaLocal + 1 : 1);

            // LÓGICA SECUENCIAL CORRECTA: Desde + el tamaño del bloque consecutivo - 1
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
                using (SqlConnection conexion = new SqlConnection(TuCadenaConexion))
                {
                    string query = @"
                        SELECT ISNULL(MAX(hasta_num), 0) + 1 
                        FROM registro_rangos 
                        WHERE producto_id = @productoId
                          AND abreviatura_base = @baseLimpia 
                          AND categoria_producto_id = @categoriaId";

                    using (SqlCommand cmd = new SqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@productoId", this._productoId);
                        cmd.Parameters.AddWithValue("@baseLimpia", baseLimpia);
                        cmd.Parameters.AddWithValue("@categoriaId", categoriaId);

                        conexion.Open();
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

            // Buscamos con comodines flexibles para asegurar coincidencia
            string patronBusqueda = "%" + baseLimpia + "%";

            using (SqlConnection conexion = new SqlConnection(TuCadenaConexion))
            {
                // MODIFICACIÓN DE SEGURIDAD: 
                // Usamos REVERSE y SUBSTRING para capturar el número final sin importar si hay espacios (CHAR) o guiones.
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

                using (SqlCommand cmd = new SqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@productoId", productoId);
                    cmd.Parameters.AddWithValue("@patron", patronBusqueda);
                    cmd.Parameters.AddWithValue("@categoriaId", categoriaId);
                    cmd.Parameters.AddWithValue("@desde", desde);
                    cmd.Parameters.AddWithValue("@hasta", hasta);

                    conexion.Open();
                    totalEncontrados = Convert.ToInt32(cmd.ExecuteScalar());

                    // Cantidad esperada en el tramo inclusivo
                    int cantidadTeorica = (hasta - desde) + 1;

                    // Retorna true si los códigos encontrados en la BD cubren la solicitud
                    return totalEncontrados == cantidadTeorica;
                }
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

            // Validar solapamiento en memoria local de la grilla
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

            // Ejecutamos la validación blindada por Producto ID
            bool rangoEsValido = ValidarExistenciaRangoEnBD(this._productoId, abreviaturaOriginal, categoriaId, intDesde, intHasta, out totalFisicosEncontrados);
            int cantidadSolicitada = (intHasta - intDesde) + 1;

            if (!rangoEsValido)
            {
                MessageBox.Show($"Error de Inventario ❌\n\nEl rango requiere {cantidadSolicitada} códigos libres cargados en el sistema, pero la base de datos registra {totalFisicosEncontrados} códigos activos válidos de tipo '{tipoTexto}' para este producto.\n\nPor favor, verifique que los códigos físicos estén importados en la tabla 'codigos_creados'.", "Validación Fallida", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string sufijoVisual = categoriaId == 1 ? "-G" : "-V";
            string baseLimpia = abreviaturaOriginal.EndsWith("-V") || abreviaturaOriginal.EndsWith("-G") ? abreviaturaOriginal.Substring(0, abreviaturaOriginal.Length - 2) : abreviaturaOriginal;

            // Formateamos los strings visuales para la grilla principal de guardado
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

    // Extensión rápida para no romper la lectura de propiedades en la validación local
    public static class RangoExtension
    {
        public static int OriginalDesdeNum(this RangoCodigoItem item)
        {
            return item.DesdeNum;
        }
    }
}