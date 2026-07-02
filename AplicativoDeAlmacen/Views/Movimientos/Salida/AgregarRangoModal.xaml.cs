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

        // Cambia esto:

        // private int _categoriaActualId = 1; 

        // Por esto (inicializado en el constructor):

        private int _categoriaActualId;
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
            _categoriaActualId = (rbLibroGuia.IsChecked == true) ? 1 : 2;


            // Enlazar eventos reactivos para el cálculo al vuelo

            txtSubCantidad.TextChanged += (s, e) => RecalcularRangoAutomatico();
            txtDesde.TextChanged += (s, e) => RecalcularRangoAutomatico();

            rbLibroGuia.Checked += (s, e) => { _categoriaActualId = 1; RecalcularRangoAutomatico(); };
            rbLibroVenta.Checked += (s, e) => { _categoriaActualId = 2; RecalcularRangoAutomatico(); };

            // Cargar el correlativo inicial sugerido desde la BD

            RecalcularRangoAutomatico();

        }


        private void RecalcularRangoAutomatico()
        {
            if (string.IsNullOrEmpty(_abreviaturaBase)) return;
            // YA NO ESCRIMOS "G-" O "V-" A MANO. 
            // Mostramos la abreviatura base tal cual viene de la BD

            lblPrefijoDesde.Text = _abreviaturaBase;
            lblPrefijoHasta.Text = _abreviaturaBase;

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

        private void BtnGrabarCodigo_Click(object sender, RoutedEventArgs e)

        {
            // 1. Validaciones básicas
            if (!int.TryParse(txtDesde.Text, out int desde) || !int.TryParse(txtHasta.Text, out int hasta)) return;
            // --- LIMPIEZA DE ESPACIOS AQUÍ ---
            // Quitamos cualquier espacio que pueda traer la abreviatura base
            string abreviaturaLimpia = _abreviaturaBase.Replace(" ", "");
            for (int i = desde; i <= hasta; i++)

            {
                // Usamos la versión limpia para armar el código
                string codigoGenerado = $"{abreviaturaLimpia}-{i:D7}";
                // Validamos...
                if (!EsCodigoDisponibleParaSalida(codigoGenerado, _categoriaActualId))
                {
                    MessageBox.Show($"El código {codigoGenerado} NO está disponible o no pertenece a esta categoría.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            ///AQUI ME QUEDO
            // 2. SEGUNDO: Validamos duplicados locales

            foreach (var r in _rangosExistentes)
            {
                if (desde <= r.HastaNum && hasta >= r.DesdeNum && r.CategoriaProductoId == _categoriaActualId)
                {
                    MessageBox.Show("Este rango ya fue agregado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 3. TERCERO: Si todo pasó, creamos el objeto y cerramos
            string tipoTexto = (_categoriaActualId == 1) ? "LIBRO GUÍA" : "LIBRO VENTA";


            RangoProcesado = new RangoCodigoItem
            {
              
                Cantidad = txtSubCantidad.Text,
                Desde = $"{_abreviaturaBase}-{desde:D7}",
                Hasta = $"{_abreviaturaBase}-{hasta:D7}",
                // --- ESTO ES LO QUE FALTA ---
                DesdeNum = desde,
                HastaNum = hasta,
                AbreviaturaBase = _abreviaturaBase,
                ColeccionTipo = $"C{DateTime.Now.Year} / {tipoTexto}"
            };//aqui me quedo

            FueConfirmado = true;
            this.DialogResult = true;
            this.Close();

        }

        private bool EsCodigoDisponibleParaSalida(string codigoBuscado, int categoriaId)

        {
            try

            {
                using (var conn = _database.GetConnection())
                {
                    var dbConn = (DbConnection)conn;
                    if (dbConn.State != System.Data.ConnectionState.Open) dbConn.Open();

                    // Usamos REPLACE(..., ' ', '') para quitar todos los espacios en la comparación

                    string query = @"SELECT cc.estado_id 
                                    FROM codigos_creados cc
                                    INNER JOIN registro_codigos rc ON cc.registro_codigo_id = rc.id
                                    WHERE REPLACE(cc.codigo, ' ', '') = REPLACE(@codigo, ' ', '')
                                    AND rc.categoria_producto_id = @categoriaId";



                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        // También limpiamos el parámetro por seguridad
                        AgregarParametro(cmd, "@codigo", codigoBuscado.Replace(" ", ""));
                        AgregarParametro(cmd, "@categoriaId", categoriaId);
                        object resultado = cmd.ExecuteScalar();
                        //LMA3C26-V-0000002
                        if (resultado == null) return false;
                        return Convert.ToInt32(resultado) == 3;

                    }
                }
            }

            catch (Exception ex)

            {
                return false;
            }

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