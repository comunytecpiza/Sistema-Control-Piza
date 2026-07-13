using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Services.Importaciones;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Facturación.AplicativoDeAlmacen.Models.Facturación;

namespace AplicativoDeAlmacen.Views.Importaciones
{
    public partial class ImportarVentasNisiraUserControl : UserControl
    {
        private readonly ImportacionExcelService _importacionService;
        private ObservableCollection<ImportacionCabeceraDTO> _comprobantesProcesados;

        public ImportarVentasNisiraUserControl()
        {
            InitializeComponent();
            _importacionService = new ImportacionExcelService();
            _comprobantesProcesados = new ObservableCollection<ImportacionCabeceraDTO>();

            DgCabeceras.ItemsSource = _comprobantesProcesados;
        }

        private void BtnBuscarArchivo_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx|Archivos CSV (*.csv)|*.csv",
                Title = "Seleccionar exportación de ventas de Nisira"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtRutaArchivo.Text = openFileDialog.FileName;
            }
        }

        private async void BtnEjecutar_Click(object sender, RoutedEventArgs e)
        {
            string ruta = TxtRutaArchivo.Text.Trim();
            if (string.IsNullOrEmpty(ruta))
            {
                MessageBox.Show("Debe seleccionar un archivo Excel primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                BtnEjecutar.IsEnabled = false;
                BtnTransferir.IsEnabled = false;
                _comprobantesProcesados.Clear();
                DgDetalles.ItemsSource = null;
                DgCodigos.ItemsSource = null;

                var dataAgrupada = await _importacionService.LeerExcelVentasAgrupadoAsync(ruta);

                if (!dataAgrupada.Any())
                {
                    MessageBox.Show("El archivo seleccionado no contiene registros procesables.", "Importación", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 🌟 1. VALIDAMOS TODO ANTES DE MOSTRAR
                await _importacionService.ValidarDatosImportacionAsync(dataAgrupada);

                // 🌟 2. LLENAMOS LA INTERFAZ
                foreach (var item in dataAgrupada)
                {
                    _comprobantesProcesados.Add(item);
                }

                // 🌟 3. FORZAMOS A WPF A PINTAR LOS COLORES AHORA MISMO
                DgCabeceras.Items.Refresh();

                // 4. Evaluar transferencia
                bool hayErroresEnLote = dataAgrupada.Any(c => !c.EsValido);
                if (hayErroresEnLote)
                {
                    MessageBox.Show("El archivo contiene inconsistencias. Revise las líneas marcadas en ROJO.", "Errores de Validación", MessageBoxButton.OK, MessageBoxImage.Hand);
                    BtnTransferir.IsEnabled = false;
                }
                else
                {
                    MessageBox.Show("¡Todo correcto! Datos listos para transferir.", "Validación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    BtnTransferir.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error crítico al procesar el archivo:\n{ex.Message}", "Error de Motor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEjecutar.IsEnabled = true;
            }
        }

        private async void BtnTransferir_Click(object sender, RoutedEventArgs e)
        {
            var confirmacion = MessageBox.Show($"¿Está seguro que desea transferir los registros válidos al historial de facturación real?",
                                               "Confirmar Inserción Masiva", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes) return;

            try
            {
                BtnTransferir.IsEnabled = false;
                BtnEjecutar.IsEnabled = false;

                int idUsuarioActual = SesionSistema.UsuarioActual?.Id ?? 1;

                // Transferir registros a la base de datos de manera atómica
                int insertados = await _importacionService.TransferirComprobantesValidosAsync(_comprobantesProcesados.ToList(), idUsuarioActual);

                MessageBox.Show($"¡Proceso completado!\nSe registraron con éxito {insertados} comprobantes en el sistema.",
                                "Transferencia Masiva", MessageBoxButton.OK, MessageBoxImage.Information);

                // Refrescar grillas para verificar remanentes o errores individuales
                DgCabeceras.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en el lote transaccional: {ex.Message}", "Error de Servidor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEjecutar.IsEnabled = true;
            }
        }

        // Cascada 1: Selección de comprobante cambia artículos inferiores
        private void DgCabeceras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgCabeceras.SelectedItem is ImportacionCabeceraDTO cabeceraSeleccionada)
            {
                DgDetalles.ItemsSource = cabeceraSeleccionada.Detalles;
                DgCodigos.ItemsSource = null; // Limpiar subdetalle hasta que marquen un producto
            }
        }

        // Cascada 2: Selección de artículo cambia códigos derechos
        private void DgDetalles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgDetalles.SelectedItem is ImportacionDetalleDTO detalleSeleccionado)
            {
                DgCodigos.ItemsSource = detalleSeleccionado.Codigos;
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e)
        {
            var parentTab = this.Parent as ContentControl;
            if (parentTab != null)
            {
                // Si está en el shell dinámico de pestañas, busca el control contenedor y remueve este objeto.
                var tabControl = Window.GetWindow(this)?.FindName("MainTabControl") as TabControl;
                if (tabControl != null)
                {
                    var tabItem = tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Content == this);
                    if (tabItem != null) tabControl.Items.Remove(tabItem);
                }
            }
        }
    }
}