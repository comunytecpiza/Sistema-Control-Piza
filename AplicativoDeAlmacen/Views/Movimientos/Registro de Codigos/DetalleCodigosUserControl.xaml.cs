using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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
        private List<CodigoCreado> _listaCodigosCompleta;

        public DetalleCodigosUserControl(RegistroCodigo lote)
        {
            InitializeComponent();
            _service = new CodigoCreadoService();
            _lote = lote;
            _listaCodigosCompleta = new List<CodigoCreado>();

            this.Loaded += DetalleCodigosUserControl_Loaded;
        }

        private void DetalleCodigosUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TxtProducto.Text = !string.IsNullOrWhiteSpace(_lote.Producto?.Descripcion) ? _lote.Producto.Descripcion : "Sin Producto";
            TxtCategoria.Text = !string.IsNullOrWhiteSpace(_lote.CategoriaProducto?.Nombre) ? _lote.CategoriaProducto.Nombre : "Sin Categoría";
            TxtRango.Text = $"De {_lote.Desde} a {_lote.Hasta}";
            TxtTotal.Text = $"{_lote.Cantidad} uds.";

            // 🌟 Verificamos por RolUsuarioId o Rol.Id (Admin: 1, Almacenero: 3, Almacenero OP: 4)
            int rolId = SesionSistema.UsuarioActual?.RolUsuarioId ?? SesionSistema.UsuarioActual?.Rol?.Id ?? 0;
            string rolNombre = SesionSistema.UsuarioActual?.Rol?.Nombre?.ToUpperInvariant() ?? "";

            bool tienePermisoRegistroManual = rolId == 1 || rolId == 3 || rolId == 4 ||
                                              rolNombre.Contains("ADMIN") ||
                                              rolNombre.Contains("ALMACEN");

            if (PanelRegistroManual != null)
            {
                PanelRegistroManual.Visibility = tienePermisoRegistroManual ? Visibility.Visible : Visibility.Collapsed;
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

                _listaCodigosCompleta = await _service.ObtenerPorRegistroIdAsync(_lote.Id, filtroAlmacen) ?? new List<CodigoCreado>();

                FiltrarYRefrescarGrilla();

                bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
                if (columnaAlmacen != null)
                {
                    columnaAlmacen.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
                }

                ActualizarResumenExcepciones(_listaCodigosCompleta);
                CalcularSiguienteCodigo(_listaCodigosCompleta);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar códigos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ActualizarResumenExcepciones(List<CodigoCreado> lista)
        {
            if (lista == null) return;
            int dañados = lista.Count(c => c.CondicionId == 2);
            int perdidos = lista.Count(c => c.CondicionId == 3);
            TxtExcepciones.Text = $"{dañados} dañados / {perdidos} perdidos";
        }

        // 🔍 FILTRADO EN TIEMPO REAL AL ESCRIBIR EN EL BUSCADOR (EN EL CENTRO)
        private void TxtBuscadorCodigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            FiltrarYRefrescarGrilla();
        }

        private void FiltrarYRefrescarGrilla()
        {
            if (_listaCodigosCompleta == null) return;

            string filtro = TxtBuscadorCodigo?.Text?.Trim()?.ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(filtro))
            {
                CodigosDataGrid.ItemsSource = null;
                CodigosDataGrid.ItemsSource = _listaCodigosCompleta;
            }
            else
            {
                // 🌟 Normalizamos el filtro (admitiendo guión o comillas simples si escanean con pistola)
                string filtroNormalizado = filtro.Replace("'", "-");

                var filtrados = _listaCodigosCompleta
                    .Where(c => (c.Codigo != null &&
                                (c.Codigo.ToLower().Contains(filtro) ||
                                 c.Codigo.ToLower().Replace("'", "-").Contains(filtroNormalizado))) ||
                                (c.AlmacenNombre != null && c.AlmacenNombre.ToLower().Contains(filtro)))
                    .ToList();

                CodigosDataGrid.ItemsSource = null;
                CodigosDataGrid.ItemsSource = filtrados;
            }
        }

        // 🌟 MARCAR O DESMARCAR TODOS LOS CHECKBOXES
        private void ChkSeleccionarTodos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chkMaster && CodigosDataGrid.ItemsSource is IEnumerable<CodigoCreado> listaVisible)
            {
                bool valor = chkMaster.IsChecked ?? false;
                foreach (var item in listaVisible)
                {
                    item.IsSeleccionado = valor;
                }
                CodigosDataGrid.Items.Refresh();
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

        // 🌟 CAMBIO DE CONDICIÓN (POR CHECKBOXES, FILAS SELECCIONADAS O POR RANGO)
        private async void BtnConvertirMermaRango_Click(object sender, RoutedEventArgs e)
        {
            if (CboNuevaCondicion.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int condicionId))
            {
                string textoOperacion = item.Content.ToString() ?? "NUEVA CONDICIÓN";

                // 🟢 CASO A: CHECKBOXES MARCADOS O FILAS SELECCIONADAS CON EL MOUSE
                var marcadosPorCheckbox = _listaCodigosCompleta.Where(c => c.IsSeleccionado).ToList();
                var marcadosPorFila = CodigosDataGrid.SelectedItems.OfType<CodigoCreado>().ToList();

                var listaAProcesar = marcadosPorCheckbox.Union(marcadosPorFila).Distinct().ToList();

                if (listaAProcesar.Any())
                {
                    var confirmacion = MessageBox.Show(
                        $"¿Desea cambiar la condición a '{textoOperacion.ToUpper()}' para los {listaAProcesar.Count} código(s) marcado(s)?",
                        "Confirmar Cambio Masivo",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmacion == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            int usuarioId = SesionSistema.UsuarioActual?.Id ?? 1;
                            var idsAProcesar = listaAProcesar.Select(c => c.Id).ToList();

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

                // 🔵 CASO B: PROCESA POR RANGO (DESDE / HASTA)
                await ProcesarCambioCondicionRangoAsync(condicionId, textoOperacion.ToUpper());
            }
        }

        private async Task ProcesarCambioCondicionRangoAsync(int nuevaCondicionId, string operacionTexto)
        {
            if (!int.TryParse(TxtRangoDesde.Text.Trim(), out int desde) || !int.TryParse(TxtRangoHasta.Text.Trim(), out int hasta))
            {
                MessageBox.Show("Por favor, marque los CheckBoxes de las filas, seleccione las filas directamente o ingrese un rango 'Desde / Hasta'.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // 🌟 MODAL DE AUDITORÍA (SOLO ADMIN AL HACER DOBLE CLIC)
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
                    AudUsuario.Text = audit.UsuarioCreador;
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

        // 🌟 MENÚ CONTEXTUAL AL HACER CLIC DERECHO
        private async void MenuItemCondicion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && int.TryParse(menuItem.Tag?.ToString(), out int nuevaCondicionId))
            {
                var marcadosPorCheckbox = _listaCodigosCompleta.Where(c => c.IsSeleccionado).ToList();
                var marcadosPorFila = CodigosDataGrid.SelectedItems.OfType<CodigoCreado>().ToList();

                var seleccionados = marcadosPorCheckbox.Union(marcadosPorFila).Distinct().ToList();

                if (!seleccionados.Any())
                {
                    MessageBox.Show("Seleccione o marque con el CheckBox las filas que desea modificar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    $"¿Desea cambiar la condición a '{textoCondicion}' para los {seleccionados.Count} código(s) marcado(s)?",
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
    }
}