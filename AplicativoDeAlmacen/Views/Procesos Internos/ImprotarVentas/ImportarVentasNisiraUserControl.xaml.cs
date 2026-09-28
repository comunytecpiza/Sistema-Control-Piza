using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Facturación;
using AplicativoDeAlmacen.Services.Importaciones;
using HandyControl.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Importaciones
{
    public partial class ImportarVentasNisiraUserControl : UserControl
    {
        private readonly ImportacionExcelService _importacionService = new ImportacionExcelService();
        private readonly ObservableCollection<ImportacionCabeceraDTO> _comprobantesProcesados = new ObservableCollection<ImportacionCabeceraDTO>();

        public ImportarVentasNisiraUserControl()
        {
            InitializeComponent();
            DgCabeceras.ItemsSource = _comprobantesProcesados;

            TxtResumenSede.Text = SesionSistema.AlmacenActual?.Nombre ?? "Almacén Principal";
        }

        private void BtnBuscarArchivo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                Title = "Seleccionar exportación de ventas NISIRA"
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
                Growl.Warning("Seleccione primero un archivo Excel de NISIRA.");
                return;
            }

            try
            {

                Cursor = Cursors.Wait;
                BtnEjecutar.IsEnabled = false;
                BtnTransferir.IsEnabled = false;

                _comprobantesProcesados.Clear();
                DgDetalles.ItemsSource = null;
                DgCodigos.ItemsSource = null;

                // 1. Leer y Agrupar

                var dataAgrupada = await _importacionService.LeerExcelVentasAgrupadoAsync(ruta);


                if (!dataAgrupada.Any())
                {
                    Growl.Info("El archivo no contiene registros de venta legibles.");
                    return;
                }

                // 2. Validar contra Catálogos y Kárdex
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                await _importacionService.ValidarDatosImportacionAsync(dataAgrupada, miAlmacenId);

                // 3. Pintar en pantalla
                foreach (var item in dataAgrupada)
                {
                    _comprobantesProcesados.Add(item);
                }

                DgCabeceras.Items.Refresh();

                // 4. Actualizar Tarjetas de Resumen
                int totalDocs = dataAgrupada.Count;
                decimal totalMonto = dataAgrupada.Sum(x => x.ImporteTotal);
                int totalErrores = dataAgrupada.Count(x => !x.EsValido);

                TxtResumenDocs.Text = $"{totalDocs} comprobantes";
                TxtResumenImporte.Text = $"S/ {totalMonto:N2}";
                TxtResumenErrores.Text = $"{totalErrores} con error";

                if (totalErrores > 0)
                {
                    Growl.Warning($"Se detectaron {totalErrores} comprobante(s) con inconsistencias. Revise las filas en rojo.");
                    BtnTransferir.IsEnabled = false;
                }
                else
                {
                    Growl.Success("¡Validación completada! Todos los comprobantes y códigos están listos.");
                    BtnTransferir.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Growl.Error($"Error al procesar NISIRA: {ex.Message}");
            }
            finally
            {
                BtnEjecutar.IsEnabled = true;
                Cursor = Cursors.Arrow;
            }
        }

        private async void BtnTransferir_Click(object sender, RoutedEventArgs e)
        {
            var confirmacion = System.Windows.MessageBox.Show(
                $"¿Está seguro de transferir los {_comprobantesProcesados.Count(c => c.EsValido)} comprobantes válidos a Facturación y rebajar el Kárdex a estado VENDIDO?",
                "Confirmar Inserción Masiva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes) return;

            try
            {
                Cursor = Cursors.Wait;
                BtnTransferir.IsEnabled = false;
                BtnEjecutar.IsEnabled = false;

                int idUsuarioActual = SesionSistema.UsuarioActual?.Id ?? 1;
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;

                // 🌟 Pasamos también miAlmacenId
                int insertados = await _importacionService.TransferirComprobantesValidosAsync(_comprobantesProcesados.ToList(), idUsuarioActual, miAlmacenId);

                Growl.Success($"¡Éxito! Se registraron {insertados} comprobantes en el sistema y se rebajó el stock físico.");

                // Quitar de la grilla los que ya se transfirieron
                var restantes = _comprobantesProcesados.Where(c => !c.EsValido).ToList();
                _comprobantesProcesados.Clear();
                foreach (var r in restantes) _comprobantesProcesados.Add(r);

                DgCabeceras.Items.Refresh();
                TxtResumenDocs.Text = $"{_comprobantesProcesados.Count} comprobantes";
            }
            catch (Exception ex)
            {
                Growl.Error($"Error en la transacción SQL: {ex.Message}");
            }
            finally
            {
                BtnEjecutar.IsEnabled = true;
                Cursor = Cursors.Arrow;
            }
        }

        private void DgCabeceras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgCabeceras.SelectedItem is ImportacionCabeceraDTO cab)
            {
                DgDetalles.ItemsSource = cab.Detalles;
                DgCodigos.ItemsSource = null;
                if (cab.Detalles.Any())
                {
                    DgDetalles.SelectedItem = cab.Detalles.First();
                }
            }
        }

        private void DgDetalles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgDetalles.SelectedItem is ImportacionDetalleDTO det)
            {
                DgCodigos.ItemsSource = det.Codigos;
                DgCodigos.Items.Refresh();
            }
        }
    }
}