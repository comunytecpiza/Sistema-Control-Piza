using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Windows;
using System.Windows.Controls;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views
{
    public partial class AgregarRangoModal : Window
    {
        public RangoCodigoItem RangoProcesado { get; private set; }
        public bool FueConfirmado { get; private set; } = false;

        private readonly ObservableCollection<RangoCodigoItem> _rangosExistentes;
        private readonly string _abreviaturaBase;
        private readonly int _productoId;
        private readonly int _saldoPermitido;
        private int _categoriaActualId = 1; // 1 = Guía, 2 = Venta

        private readonly DatabaseConnection _database;

        public AgregarRangoModal(ObservableCollection<RangoCodigoItem> rangosExistentes, string abreviaturaBase, int productoId, int saldoPermitido)
        {
            InitializeComponent();

            _rangosExistentes = rangosExistentes;
            _abreviaturaBase = abreviaturaBase;
            _productoId = productoId;
            _saldoPermitido = saldoPermitido;
            _database = new DatabaseConnection();

            // Configuración inicial de la UI
            txtSubCantidad.Text = _saldoPermitido.ToString();

            // Enlazar eventos reactivos para el cálculo al vuelo
            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();
            txtDesde.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            // Cargar el correlativo inicial sugerido desde la BD
            int sugeridoBD = ObtenerSiguienteNumeroDesdeBD(_abreviaturaBase, _categoriaActualId);
            txtDesde.Text = sugeridoBD.ToString();

            RecalcularRangoAutomatico();
        }

        private void RecalcularRangoAutomatico()
        {
            if (string.IsNullOrEmpty(_abreviaturaBase)) return;

            string prefijo = _categoriaActualId == 1 ? "G-" : "V-";
            lblPrefijoDesde.Text = prefijo;
            lblPrefijoHasta.Text = prefijo;

            if (!int.TryParse(txtSubCantidad.Text, out int cantidad) || cantidad <= 0)
            {
                txtHasta.Text = "";
                return;
            }

            if (int.TryParse(txtDesde.Text, out int desdeNum) && desdeNum > 0)
            {
                int hastaCalculado = desdeNum + cantidad - 1;
                txtHasta.Text = hastaCalculado.ToString();
            }
            else
            {
                txtHasta.Text = "";
            }
        }

        private int ObtenerSiguienteNumeroDesdeBD(string abreviatura, int categoriaId)
        {
            try
            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                    string query = @"
                        SELECT COALESCE(MAX(hasta_num), 0) + 1 
                        FROM registro_rangos 
                        WHERE producto_id = @productoId
                          AND abreviatura_base = @abreviatura 
                          AND categoria_producto_id = @categoriaId";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        AgregarParametro(cmd, "@productoId", _productoId);
                        cmd.Parameters.Add(new System.Data.SqlClient.SqlParameter("@abreviatura", abreviatura));
                        AgregarParametro(cmd, "@categoriaId", categoriaId);

                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch
            {
                return 1;
            }
        }

        private bool VerificarSiRangoYaFueUsadoEnBD(int desde, int hasta)
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
                          AND abreviatura_base = @abreviatura 
                          AND categoria_producto_id = @categoriaId
                          AND (@desde <= hasta_num AND @hasta >= desde_num)";

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        AgregarParametro(cmd, "@productoId", _productoId);
                        cmd.Parameters.Add(new System.Data.SqlClient.SqlParameter("@abreviatura", _abreviaturaBase));
                        AgregarParametro(cmd, "@categoriaId", _categoriaActualId);
                        AgregarParametro(cmd, "@desde", desde);
                        AgregarParametro(cmd, "@hasta", hasta);

                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private void BtnGrabarCodigo_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtSubCantidad.Text, out int cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingrese una cantidad válida.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cantidad > _saldoPermitido)
            {
                MessageBox.Show($"La cantidad supera el saldo permitido ({_saldoPermitido} u.).", "Exceso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtDesde.Text, out int desdeNum) || !int.TryParse(txtHasta.Text, out int hastaNum))
            {
                MessageBox.Show("Verifique los rangos numéricos.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Control de solapamiento en la colección local temporal
            foreach (var rango in _rangosExistentes)
            {
                if (rango.CategoriaProductoId == _categoriaActualId)
                {
                    if (desdeNum <= rango.HastaNum && hastaNum >= rango.DesdeNum)
                    {
                        MessageBox.Show($"¡Error de Concurrencia! El rango [{desdeNum} - {hastaNum}] se cruza con un lote ya listado.", "Duplicado ❌", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            // 2. Control contra el historial real de la Base de Datos
            if (VerificarSiRangoYaFueUsadoEnBD(desdeNum, hastaNum))
            {
                MessageBox.Show("El rango seleccionado contiene números que ya fueron usados previamente en el sistema.", "Rango Usado 🛑", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string prefijo = _categoriaActualId == 1 ? "G-" : "V-";
            string tipoTexto = _categoriaActualId == 1 ? "LIBRO GUÍA" : "LIBRO VENTA";

            RangoProcesado = new RangoCodigoItem
            {
                productoId = _productoId,
                Cantidad = cantidad.ToString(),
                Desde = $"{_abreviaturaBase}-{prefijo}{desdeNum:D7}",
                Hasta = $"{_abreviaturaBase}-{prefijo}{hastaNum:D7}",
                ColeccionTipo = $"C{DateTime.Now.Year} / {tipoTexto}",
                DesdeNum = desdeNum,
                HastaNum = hastaNum,
                AbreviaturaBase = _abreviaturaBase,
                CategoriaProductoId = _categoriaActualId
            };

            FueConfirmado = true;
            this.DialogResult = true;
            this.Close();
        }

        private void AgregarParametro(DbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private void BtnCancelarRango_Click(object sender, RoutedEventArgs e)
        {
            FueConfirmado = false;
            this.DialogResult = false;
            this.Close();
        }
    }
}