using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using AplicativoDeAlmacen.Core;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class DetalleCodigosUserControl : UserControl
    {
        private readonly CodigoCreadoService _service;
        private readonly RegistroCodigo _lote;

        public DetalleCodigosUserControl(RegistroCodigo lote)
        {
            InitializeComponent();
            _service = new CodigoCreadoService();
            _lote = lote;

            this.Loaded += DetalleCodigosUserControl_Loaded;
        }

        private void DetalleCodigosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TxtProducto.Text = !string.IsNullOrWhiteSpace(_lote.Producto?.Descripcion) ? _lote.Producto.Descripcion : "Sin Producto";
            TxtCategoria.Text = !string.IsNullOrWhiteSpace(_lote.CategoriaProducto?.Nombre) ? _lote.CategoriaProducto.Nombre : "Sin Categoría";
            TxtRango.Text = $"De {_lote.Desde} a {_lote.Hasta}";
            TxtTotal.Text = $"{_lote.Cantidad} uds.";

            // 🌟 PRIVILEGIO ADMINISTRADOR: Solo Administrador ve el panel de Registro Manual
            bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
            if (PanelRegistroManual != null)
            {
                PanelRegistroManual.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
            }

            _ = CargarCodigosAsync();
        }

        private async Task CargarCodigosAsync()
        {
            try
            {
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                int? filtroAlmacen = (miAlmacenId == ALMACEN_CENTRAL_ID) ? null : miAlmacenId;

                var codigos = await _service.ObtenerPorRegistroIdAsync(_lote.Id, filtroAlmacen);
                CodigosDataGrid.ItemsSource = codigos;

                bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
                if (columnaAlmacen != null)
                {
                    columnaAlmacen.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
                }

                // Resumen de Mermas y Perdidos
                int dañados = codigos.Count(c => c.CondicionId == 2);
                int perdidos = codigos.Count(c => c.CondicionId == 3);
                TxtExcepciones.Text = $"{dañados} dañados / {perdidos} perdidos";

                CalcularSiguienteCodigo(codigos);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar códigos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CalcularSiguienteCodigo(List<CodigoCreado> codigos)
        {
            if (codigos == null || codigos.Count == 0)
            {
                TxtNuevoCodigo.Text = "1";
                return;
            }

            int maxNumero = 0;
            foreach (var cod in codigos)
            {
                if (cod.Codigo != null && cod.Codigo.Contains("-"))
                {
                    string numeroStr = cod.Codigo.Substring(cod.Codigo.LastIndexOf('-') + 1);
                    if (int.TryParse(numeroStr, out int num))
                    {
                        if (num > maxNumero) maxNumero = num;
                    }
                }
            }

            TxtNuevoCodigo.Text = (maxNumero + 1).ToString();
        }

        private async void BtnRegistrarManual_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtNuevoCodigo.Text.Trim();

            if (!int.TryParse(input, out int numero))
            {
                MessageBox.Show("Por favor, ingrese solo el número correlativo.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string prefijo = _lote.Producto?.Abreviatura ?? "COD";
            string codigoCompleto = $"{prefijo}-{numero:D7}";

            try
            {
                bool existe = await _service.ExisteCodigoAsync(_lote.Id, codigoCompleto);
                if (existe)
                {
                    MessageBox.Show($"El código {codigoCompleto} YA EXISTE. No se permiten duplicados.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                int usrId = SesionSistema.UsuarioActual?.Id ?? 1;
                int almId = SesionSistema.AlmacenActual?.Id ?? 1;

                await _service.RegistrarManualAsync(_lote.Id, codigoCompleto, usrId, almId);
                await CargarCodigosAsync();
                MessageBox.Show($"Código {codigoCompleto} registrado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                EventBus.NotificarRegistroCodigosChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al registrar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 🌟 CAMBIOS DE CONDICIÓN (1: OPERATIVO, 2: DAÑADO, 3: PERDIDO)
        private async void BtnConvertirMermaRango_Click(object sender, RoutedEventArgs e)
        {
            if (CboNuevaCondicion.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int condicionId))
            {
                string textoOperacion = item.Content.ToString() ?? "NUEVA CONDICIÓN";
                var filasSeleccionadas = CodigosDataGrid.SelectedItems.OfType<CodigoCreado>().ToList();

                // 🟢 CASO A: EL USUARIO SELECCIONÓ FILAS EN LA GRILLA (CTRL / SHIFT + CLIC)
                if (filasSeleccionadas.Any())
                {
                    var confirmacion = MessageBox.Show(
                        $"¿Desea cambiar la condición a '{textoOperacion.ToUpper()}' para los {filasSeleccionadas.Count} código(s) seleccionado(s) en la tabla?",
                        "Confirmar Cambio Masivo",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmacion == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            int usuarioId = SesionSistema.UsuarioActual?.Id ?? 1;
                            var idsAProcesar = filasSeleccionadas.Select(c => c.Id).ToList();

                            int actualizados = await _service.CambiarCondicionPorListaIdsAsync(idsAProcesar, condicionId, usuarioId);

                            MessageBox.Show($"Se actualizaron correctamente {actualizados} código(s).", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                            await CargarCodigosAsync();
                            EventBus.NotificarRegistroCodigosChanged();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error al cambiar la condición: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Arrow;
                        }
                    }
                    return;
                }

                // 🔵 CASO B: NO HAY SELECCIÓN EN GRILLA, PROCESA POR RANGO (DESDE / HASTA)
                await ProcesarCambioCondicionRangoAsync(condicionId, textoOperacion.ToUpper());
            }
        }

        private async Task ProcesarCambioCondicionRangoAsync(int nuevaCondicionId, string operacionTexto)
        {
            if (!int.TryParse(TxtRangoDesde.Text.Trim(), out int desde) || !int.TryParse(TxtRangoHasta.Text.Trim(), out int hasta))
            {
                MessageBox.Show("Por favor, ingrese números válidos en los campos 'Desde' y 'Hasta' o seleccione las filas directamente en la tabla.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (desde > hasta)
            {
                MessageBox.Show("El correlativo 'Desde' no puede ser mayor que 'Hasta'.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"¿Está seguro de cambiar la condición a '{operacionTexto}' para los correlativos del {desde} al {hasta}?", "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    this.Cursor = Cursors.Wait;
                    int usuarioId = SesionSistema.UsuarioActual?.Id ?? 1;
                    int actualizados = await _service.CambiarCondicionPorRangoAsync(_lote.Id, desde, hasta, nuevaCondicionId, usuarioId);

                    MessageBox.Show($"Operación completada. Se actualizaron {actualizados} códigos.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    await CargarCodigosAsync();
                    EventBus.NotificarRegistroCodigosChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al actualizar la condición: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                }
            }
        }

        // 🌟 MODAL DE AUDITORÍA CON NOMBRE DEL USUARIO CREADOR AL HACER DOBLE CLICK (SOLO ADMIN)
        private async void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
            if (!esAdmin) return;

            if (sender is DataGridRow row && row.Item is CodigoCreado codigoItem)
            {
                var audit = await _service.ObtenerAuditoriaCompletaAsync(codigoItem.Id);
                if (audit != null)
                {
                    AudCodigo.Text = audit.Codigo;
                    AudCondicion.Text = audit.CondicionNombre;
                    AudAlmacen.Text = audit.AlmacenNombre;
                    AudUsuario.Text = audit.UsuarioCreador; // 👈 Muestra el NOMBRE DEL USUARIO
                    AudOrigen.Text = audit.OrigenCreacion;
                    AudFecha.Text = audit.FechaCreacion.ToString("dd/MM/yyyy HH:mm:ss");

                    ModalAuditoria.Visibility = Visibility.Visible;
                }
            }
        }

        private void BtnCerrarAuditoria_Click(object sender, RoutedEventArgs e)
        {
            ModalAuditoria.Visibility = Visibility.Collapsed;
        }

        // 🌟 MANEJADOR PARA CAMBIAR CONDICIÓN A LAS FILAS SELECCIONADAS CON EL MOUSE (CTRL/SHIFT + CLIC Y CLIC DERECHO)
        private async void MenuItemCondicion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && int.TryParse(menuItem.Tag?.ToString(), out int nuevaCondicionId))
            {
                var seleccionados = CodigosDataGrid.SelectedItems.OfType<CodigoCreado>().ToList();

                if (!seleccionados.Any())
                {
                    MessageBox.Show("Seleccione primero una o varias filas (use Ctrl + Clic para selecciones intercaladas).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string textoCondicion = nuevaCondicionId switch
                {
                    1 => "OK / OPERATIVO",
                    2 => "DAÑADO / DEFECTUOSO",
                    3 => "PERDIDO / EXTRAVIADO",
                    _ => "NUEVA CONDICIÓN"
                };

                var confirmacion = MessageBox.Show(
                    $"¿Desea cambiar la condición a '{textoCondicion}' para los {seleccionados.Count} código(s) seleccionado(s)?",
                    "Confirmar Cambio Masivo",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmacion == MessageBoxResult.Yes)
                {
                    try
                    {
                        this.Cursor = Cursors.Wait;
                        int usuarioId = SesionSistema.UsuarioActual?.Id ?? 1;
                        var idsAProcesar = seleccionados.Select(c => c.Id).ToList();

                        int actualizados = await _service.CambiarCondicionPorListaIdsAsync(idsAProcesar, nuevaCondicionId, usuarioId);

                        MessageBox.Show($"Se actualizaron correctamente {actualizados} código(s).", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                        await CargarCodigosAsync();
                        EventBus.NotificarRegistroCodigosChanged();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error al cambiar la condición: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        this.Cursor = Cursors.Arrow;
                    }
                }
            }


        }


        private void CodigosDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool haySeleccionEnGrilla = CodigosDataGrid.SelectedItems.Count > 0;

            if (TxtRangoDesde != null && TxtRangoHasta != null)
            {
                // Si seleccionó filas intercaladas o continuas con el ratón, deshabilitamos las cajas de texto
                TxtRangoDesde.IsEnabled = !haySeleccionEnGrilla;
                TxtRangoHasta.IsEnabled = !haySeleccionEnGrilla;

                if (haySeleccionEnGrilla)
                {
                    TxtRangoDesde.Clear();
                    TxtRangoHasta.Clear();
                }
            }
        }
    }
}