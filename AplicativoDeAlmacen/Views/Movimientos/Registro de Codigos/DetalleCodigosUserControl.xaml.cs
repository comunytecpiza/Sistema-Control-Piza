using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Core;

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

            _ = CargarCodigosAsync();
        }

        private async Task CargarCodigosAsync()
        {
            try
            {
                int miAlmacenId = SesionSistema.AlmacenActual?.Id ?? 1;
                const int ALMACEN_CENTRAL_ID = 1;

                // 🌟 REGLA POR SESIÓN ACTIVA (Sede activa vs Visión Global)
                int? filtroAlmacen = (miAlmacenId == ALMACEN_CENTRAL_ID) ? null : miAlmacenId;

                var codigos = await _service.ObtenerPorRegistroIdAsync(_lote.Id, filtroAlmacen);
                CodigosDataGrid.ItemsSource = codigos;

                // 🌟 VISIBILIDAD DE COLUMNA ALMACÉN:
                // La columna solo estará visible si el usuario tiene rol de Administrador.
                bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
                if (columnaAlmacen != null)
                {
                    columnaAlmacen.Visibility = esAdmin ? Visibility.Visible : Visibility.Collapsed;
                }

                // Contar Mermas / Dañados en la vista activa
                int mermas = codigos.Count(c => c.CondicionId == 2);
                TxtExcepciones.Text = $"{mermas} dañados";

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

        // 🌟 CAMBIO DE CONDICIÓN POR RANGO (CONVERTIR A MERMA)
        private async void BtnConvertirMermaRango_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarCambioCondicionRangoAsync(2, "DECLARAR MERMA");
        }

        private async void BtnRevertirOkRango_Click(object sender, RoutedEventArgs e)
        {
            await ProcesarCambioCondicionRangoAsync(1, "REVERTIR A OPERATIVO");
        }

        private async Task ProcesarCambioCondicionRangoAsync(int nuevaCondicionId, string operacionTexto)
        {
            if (!int.TryParse(TxtRangoDesde.Text.Trim(), out int desde) || !int.TryParse(TxtRangoHasta.Text.Trim(), out int hasta))
            {
                MessageBox.Show("Por favor, ingrese números válidos en los campos 'Desde' y 'Hasta'.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (desde > hasta)
            {
                MessageBox.Show("El correlativo 'Desde' no puede ser mayor que 'Hasta'.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"¿Está seguro de {operacionTexto} para los correlativos del {desde} al {hasta}?", "Confirmación", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
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
            }
        }

        // 🌟 MODAL DE AUDITORÍA AL HACER DOBLE CLICK (SOLO ADMINISTRADOR)
        private async void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            bool esAdmin = SesionSistema.UsuarioActual?.RolUsuarioId == 1;
            if (!esAdmin) return; // Si no es Admin, se ignora el doble clic

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
    }
}