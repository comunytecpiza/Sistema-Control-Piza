using AplicativoDeAlmacen.Data;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using System.Windows;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Views.Consultas_y_Reportes.Consulta
{
    public partial class ValorizacionProductosWindow : Window
    {
        private readonly DatabaseConnection _db = new DatabaseConnection();

        public ValorizacionProductosWindow()
        {
            InitializeComponent();
            CargarMesesYAnios();
        }

        private void CargarMesesYAnios()
        {
            var meses = new List<string>
            {
                "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
                "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"
            };

            CboMesDesde.ItemsSource = meses;
            CboMesHasta.ItemsSource = meses;

            int mesActual = DateTime.Now.Month - 1;
            CboMesDesde.SelectedIndex = mesActual;
            CboMesHasta.SelectedIndex = mesActual;

            string anioActual = DateTime.Now.Year.ToString();
            TxtAnioDesde.Text = anioActual;
            TxtAnioHasta.Text = anioActual;
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int mesDesde = CboMesDesde.SelectedIndex + 1;
                int mesHasta = CboMesHasta.SelectedIndex + 1;

                if (!int.TryParse(TxtAnioDesde.Text, out int anioDesde) || !int.TryParse(TxtAnioHasta.Text, out int anioHasta))
                {
                    MessageBox.Show("Ingrese años válidos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                this.Cursor = System.Windows.Input.Cursors.Wait;

                DateTime fechaInicio = new DateTime(anioDesde, mesDesde, 1);
                DateTime fechaFin = new DateTime(anioHasta, mesHasta, DateTime.DaysInMonth(anioHasta, mesHasta));

                using var conn = _db.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                // 🌟 SCRIPT BLINDADO: 
                // 1. Ubica estrictamente el costo unitario de la PRIMERA COMPRA real (Motivo Tipo 1 / Compra).
                // 2. Respeta ese costo y lo propaga a los demás movimientos del producto en el rango, 
                //    SIN SOBREESCRIBIR la compra inicial si esta ya tiene su costo digitado.
                string queryPropagar = QueryAdapter.EsMySQL
                    ? @"UPDATE movimiento_detalles md
                        JOIN movimientos m ON md.movimiento_id = m.id
                        SET md.costo_unitario = (
                            SELECT md_compra.costo_unitario 
                            FROM movimiento_detalles md_compra
                            JOIN movimientos m_compra ON md_compra.movimiento_id = m_compra.id
                            WHERE md_compra.producto_id = md.producto_id 
                              AND m_compra.motivo_producto_id = 1 
                              AND m_compra.estado_id = 1
                            ORDER BY m_compra.fecha_movimiento ASC, m_compra.id ASC
                            LIMIT 1
                        )
                        WHERE m.fecha_movimiento BETWEEN @fInicio AND @fFin 
                          AND m.estado_id = 1
                          AND m.motivo_producto_id != 1;" // 👈 Actualiza TODO menos la compra (Motivo 1)
                    : @"UPDATE md
                        SET md.costo_unitario = sub.costo_unitario
                        FROM movimiento_detalles md
                        INNER JOIN movimientos m ON md.movimiento_id = m.id
                        INNER JOIN (
                            SELECT md_compra.producto_id, md_compra.costo_unitario,
                                   ROW_NUMBER() OVER(PARTITION BY md_compra.producto_id ORDER BY m_compra.fecha_movimiento ASC, m_compra.id ASC) as rn
                            FROM movimiento_detalles md_compra
                            INNER JOIN movimientos m_compra ON md_compra.movimiento_id = m_compra.id
                            WHERE m_compra.motivo_producto_id = 1 AND m_compra.estado_id = 1
                        ) sub ON sub.producto_id = md.producto_id AND sub.rn = 1
                        WHERE m.fecha_movimiento BETWEEN @fInicio AND @fFin 
                          AND m.estado_id = 1 
                          AND m.motivo_producto_id != 1;";

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(queryPropagar);

                var p1 = cmd.CreateParameter(); p1.ParameterName = "@fInicio"; p1.Value = fechaInicio; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@fFin"; p2.Value = fechaFin; cmd.Parameters.Add(p2);

                int filasAfectadas = await cmd.ExecuteNonQueryAsync();

                MessageBox.Show($"¡Proceso de valorización completado con éxito!\n\nSe propagó el costo base de la compra a los movimientos del período.",
                                "Valorización Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al ejecutar la valorización: " + ex.Message, "Error de Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}