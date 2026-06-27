using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using HandyControl.Controls;
using System.Collections.Generic;
using AplicativoDeAlmacen.Models.Models;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Services;
using AplicativoDeAlmacen.Views.Sistema_Interno;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConfiguracionSistemaUserControl : UserControl
    {
        private readonly ConfiguracionService _configService;

        // Bandera de protección: mientras es true, los eventos Checked/Unchecked
        // disparados por la carga programática del DataGrid se ignoran.
        // Esto evita el bucle de recargas que ralentizaba toda la pantalla.
        private bool _cargandoDatos;

        public ConfiguracionSistemaUserControl()
        {
            InitializeComponent();

            _configService =
                new ConfiguracionService();
        }

        private async void UserControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await CargarDatosAsync();
        }

        private void MostrarNotificacion(
            string mensaje)
        {
            Growl.Success(mensaje);
        }

        private void BtnNuevaCategoria_Click(
            object sender,
            RoutedEventArgs e)
        {
            var modal =
                new CategoriaWindow();

            if (modal.ShowDialog() == true)
            {
                _ = CargarDatosAsync();

                MostrarNotificacion(
                    "Categoría registrada");
            }
        }

        private void BtnEditarCategoria_Click(
            object sender,
            RoutedEventArgs e)
        {
            var catSeleccionada =
                DgCategorias.SelectedItem
                as CategoriaModulo;

            if (catSeleccionada == null)
                return;

            var modal =
                new CategoriaWindow(
                    catSeleccionada);

            if (modal.ShowDialog() == true)
            {
                _ = CargarDatosAsync();

                MostrarNotificacion(
                    "Categoría actualizada");
            }
        }

        private async Task CargarDatosAsync()
        {
            _cargandoDatos = true;

            try
            {
                DgCategorias.ItemsSource =
                    await _configService
                    .ObtenerCategoriasAsync();

                DgModulos.ItemsSource =
                    await _configService
                    .ObtenerModulosCompletosAsync();
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
            finally
            {
                // Se libera al final, incluso si algo falla,
                // para no dejar la pantalla bloqueada permanentemente.
                _cargandoDatos = false;
            }
        }

        private async void BtnEscanear_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                List<string> vistasRegistradasEnBD =
                    new List<string>();

                await Task.Run(() =>
                {
                    using (System.Data.IDbConnection conn =
                        new Data.DataConnection
                        .DatabaseConnection()
                        .GetConnection())
                    {
                        conn.Open();

                        using (var cmd =
                            conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT control_wpf " +
                                "FROM modulos_sistema " +
                                "WHERE control_wpf IS NOT NULL";

                            using (var reader =
                                cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    vistasRegistradasEnBD
                                        .Add(
                                        reader.GetString(0));
                                }
                            }
                        }
                    }
                });

                var todasLasVistas =
                    Assembly
                    .GetExecutingAssembly()
                    .GetTypes()

                    .Where(t =>
                        t.IsSubclassOf(
                            typeof(UserControl))

                        &&

                        t.Namespace != null

                        &&

                        t.Namespace.StartsWith(
                            "AplicativoDeAlmacen.Views"))

                    .ToList();

                var vistasNuevas =
                    todasLasVistas

                    .Where(t =>
                        !vistasRegistradasEnBD
                        .Contains(t.Name))

                    .Select(t =>
                    {
                        string rutaLimpia =
                            (t.Namespace ?? "Raíz")

                            .Replace(
                                "AplicativoDeAlmacen.Views",
                                "Views")

                            .Replace(
                                ".",
                                " / ");

                        if (rutaLimpia == "Views")
                        {
                            rutaLimpia =
                                "Views / (Raíz)";
                        }

                        return new VistaDetectada
                        {
                            NombreVista =
                                t.Name,

                            RutaCompleta =
                                rutaLimpia,

                            Seleccionada =
                                false
                        };

                    })

                    .OrderBy(v =>
                        v.NombreVista)

                    .ToList();

                DgVistasDetectadas.ItemsSource =
                    vistasNuevas;

                if (!vistasNuevas.Any())
                {
                    Growl.Info(
                        "Todas las vistas ya están registradas.");
                }
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
        }

        private void BtnRegistrar_Click(
            object sender,
            RoutedEventArgs e)
        {
            var vistasEnGrilla =
                DgVistasDetectadas.ItemsSource
                as List<VistaDetectada>;

            if (vistasEnGrilla == null)
                return;

            var seleccionadas =
                vistasEnGrilla

                .Where(v =>
                    v.Seleccionada)

                .ToList();

            if (!seleccionadas.Any())
            {
                Growl.Warning(

                    "Seleccione una vista");

                return;
            }

            bool cambiosRealizados =
                false;

            foreach (var vista in seleccionadas)
            {
                var modal =
                    new RegistrarModuloWindow(

                        vista.NombreVista);

                if (modal.ShowDialog() == true)
                {
                    cambiosRealizados =
                        true;
                }
            }

            if (cambiosRealizados)
            {
                BtnEscanear_Click(
                    null,
                    null);

                EventBus
                    .NotificarRolesPermisosChanged();

                MostrarNotificacion(

                    "Módulos registrados");
            }
        }

        private async void BtnEstadoCategoria_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DgCategorias.SelectedItem
                is not CategoriaModulo cat)
            {
                Growl.Warning(

                    "Seleccione una categoría");

                return;
            }

            bool nuevoEstado =
                !cat.Estado;

            await _configService

                .CambiarEstadoCategoriaAsync(

                    cat.Id,

                    nuevoEstado);

            await CargarDatosAsync();

            EventBus
                .NotificarRolesPermisosChanged();

            MostrarNotificacion(

                "Estado actualizado");
        }

        private void BtnNuevoModulo_Click(
            object sender,
            RoutedEventArgs e)
        {
            Growl.Info(

                "Pendiente ModuloWindow");
        }

        private void BtnEditarModulo_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DgModulos.SelectedItem == null)
            {
                Growl.Warning(

                    "Seleccione un módulo");

                return;
            }

            Growl.Info(

                "Pendiente ModuloWindow");
        }

        private async void BtnEstadoModulo_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DgModulos.SelectedItem
                is not ModuloSistema mod)
            {
                Growl.Warning(

                    "Seleccione un módulo");

                return;
            }

            bool nuevoEstado =
                !mod.Estado;

            await _configService

                .CambiarEstadoModuloAsync(

                    mod.Id,

                    nuevoEstado);

            await CargarDatosAsync();

            EventBus
                .NotificarRolesPermisosChanged();

            MostrarNotificacion(

                "Módulo actualizado");
        }


        private async void ChkCategoriaEstado_Changed( object sender,  RoutedEventArgs e)
        {
            // Si el evento se disparó porque estamos poblando el grid
            // (no porque el usuario hizo clic), lo ignoramos.
            if (_cargandoDatos)
                return;

            try
            {
                if (sender is not CheckBox chk)
                    return;

                if (chk.DataContext is not CategoriaModulo cat)
                    return;

                bool nuevoEstado =
                    chk.IsChecked == true;

                await _configService
                    .CambiarEstadoCategoriaAsync(
                        cat.Id,
                        nuevoEstado);

                // El binding ya actualizó cat.Estado en memoria,
                // así que no es necesario volver a consultar toda la BD.
                EventBus
                    .NotificarRolesPermisosChanged();

                if (nuevoEstado)
                {
                    Growl.Success(
                        $"Categoría '{cat.Nombre}' habilitada");
                }
                else
                {
                    Growl.Warning(
                        $"Categoría '{cat.Nombre}' deshabilitada");
                }
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
        }

        private async void ChkModuloEstado_Changed( object sender, RoutedEventArgs e)
        {
            // Si el evento se disparó porque estamos poblando el grid
            // (no porque el usuario hizo clic), lo ignoramos.
            if (_cargandoDatos)
                return;

            try
            {
                if (sender is not CheckBox chk)
                    return;

                if (chk.DataContext is not ModuloSistema mod)
                    return;

                bool nuevoEstado =
                    chk.IsChecked == true;

                await _configService
                    .CambiarEstadoModuloAsync(
                        mod.Id,
                        nuevoEstado);

                // El binding ya actualizó mod.Estado en memoria,
                // así que no es necesario volver a consultar toda la BD.
                EventBus
                    .NotificarRolesPermisosChanged();

                if (nuevoEstado)
                {
                    Growl.Success(
                        $"Módulo '{mod.NombreModulo}' habilitado");
                }
                else
                {
                    Growl.Warning(
                        $"Módulo '{mod.NombreModulo}' deshabilitado");
                }
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
        }
    }
}