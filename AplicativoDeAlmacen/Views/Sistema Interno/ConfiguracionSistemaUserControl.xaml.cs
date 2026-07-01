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

        // Controla cuántas notificaciones Growl están "vivas" al mismo tiempo.
        // Si el usuario hace clic muy rápido muchas veces, a partir de la
        // cuarta notificación seguida simplemente no se muestran más hasta
        // que las anteriores se cierren (cada Growl dura unos segundos).
        private int _notificacionesActivas;
        private const int MAX_NOTIFICACIONES_SIMULTANEAS = 3;

        // "Fotografía" del último estado conocido y confirmado en BD para
        // cada categoría/módulo, tomada justo después de cargar los datos.
        // Cuando el DataGrid se re-dibuja (al cambiar de pestaña, volver a
        // la vista, etc.), WPF vuelve a disparar Checked/Unchecked aunque
        // el usuario no haya tocado nada. Comparando contra esta fotografía
        // sabemos si el cambio es real (el usuario hizo clic) o es un eco
        // del propio binding, y así evitamos UPDATEs y notificaciones falsas.
        private readonly Dictionary<int, bool> _estadoConocidoCategorias = new();
        private readonly Dictionary<int, bool> _estadoConocidoModulos = new();

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
            MostrarNotificacionControlada(mensaje, TipoNotificacion.Success);
        }

        private enum TipoNotificacion
        {
            Success,
            Warning,
            Error,
            Info
        }

        // Guarda, por texto de mensaje, la última vez que se mostró.
        // Si el mismo mensaje exacto se vuelve a pedir antes de que pase
        // el tiempo de "enfriamiento", se descarta. Esto es lo que evita
        // que 10 clics seguidos en el mismo botón (por ejemplo "Cambiar
        // Estado" sin seleccionar fila) generen 10 notificaciones idénticas
        // apiladas: solo se muestra la primera, las siguientes mientras
        // dura el enfriamiento se ignoran.
        private readonly Dictionary<string, DateTime> _ultimaVezMostrado = new();
        private static readonly TimeSpan EnfriamientoPorMensaje = TimeSpan.FromSeconds(2);

        // Punto único de salida para TODAS las notificaciones de esta pantalla.
        // Combina dos protecciones:
        // 1) Anti-rebote: el mismo mensaje no se repite antes de 2 segundos.
        // 2) Tope simultáneo: nunca hay más de 3 notificaciones visibles
        //    a la vez, sin importar de qué tipo o mensaje sean.
        private void MostrarNotificacionControlada(
            string mensaje,
            TipoNotificacion tipo,
            int duracionMs = 3000)
        {
            var ahora = DateTime.Now;

            if (_ultimaVezMostrado.TryGetValue(mensaje, out var ultimaVez)
                && (ahora - ultimaVez) < EnfriamientoPorMensaje)
            {
                return;
            }

            if (_notificacionesActivas >= MAX_NOTIFICACIONES_SIMULTANEAS)
                return;

            _ultimaVezMostrado[mensaje] = ahora;
            _notificacionesActivas++;

            switch (tipo)
            {
                case TipoNotificacion.Success:
                    Growl.Success(mensaje);
                    break;
                case TipoNotificacion.Warning:
                    Growl.Warning(mensaje);
                    break;
                case TipoNotificacion.Error:
                    Growl.Error(mensaje);
                    break;
                case TipoNotificacion.Info:
                    Growl.Info(mensaje);
                    break;
            }

            _ = LiberarNotificacionDespuesDeAsync(duracionMs);
        }

        private async Task LiberarNotificacionDespuesDeAsync(int duracionMs)
        {
            await Task.Delay(duracionMs);

            if (_notificacionesActivas > 0)
                _notificacionesActivas--;
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
                var categorias =
                    await _configService
                    .ObtenerCategoriasAsync();

                var modulos =
                    await _configService
                    .ObtenerModulosCompletosAsync();

                DgCategorias.ItemsSource = categorias;
                DgModulos.ItemsSource = modulos;

                // Tomamos la "fotografía": este es el estado real y
                // confirmado de cada fila justo después de traerla de la
                // base de datos. Cualquier evento Checked/Unchecked que
                // coincida con esta fotografía es un eco del re-render,
                // no un clic real del usuario.
                _estadoConocidoCategorias.Clear();
                foreach (var cat in categorias)
                    _estadoConocidoCategorias[cat.Id] = cat.Estado;

                _estadoConocidoModulos.Clear();
                foreach (var mod in modulos)
                    _estadoConocidoModulos[mod.Id] = mod.Estado;
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
                    MostrarNotificacionControlada(
                        "Todas las vistas ya están registradas.",
                        TipoNotificacion.Info);
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
                MostrarNotificacionControlada(
                    "Seleccione una vista",
                    TipoNotificacion.Warning);

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
                MostrarNotificacionControlada(
                    "Seleccione una categoría",
                    TipoNotificacion.Warning);

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

        private void BtnNuevoModulo_Click(object sender, RoutedEventArgs e)
        {
            // Pasamos un objeto vacío (null)
            var modal = new ModuloWindow(null);

            if (modal.ShowDialog() == true)
            {
                _ = CargarDatosAsync();
                EventBus.NotificarRolesPermisosChanged();
                MostrarNotificacionControlada("Nuevo módulo registrado", TipoNotificacion.Success);
            }
        }

        private void BtnEditarModulo_Click(object sender, RoutedEventArgs e)
        {
            if (DgModulos.SelectedItem is ModuloSistema mod)
            {
                var modal = new ModuloWindow(mod);
                if (modal.ShowDialog() == true)
                {
                    _ = CargarDatosAsync(); // Recarga la tabla
                    EventBus.NotificarRolesPermisosChanged(); // Refresca el menú lateral
                }
            }
        }

        private async void BtnEstadoModulo_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DgModulos.SelectedItem
                is not ModuloSistema mod)
            {
                MostrarNotificacionControlada(
                    "Seleccione un módulo",
                    TipoNotificacion.Warning);

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


        private async void ChkCategoriaEstado_Changed(object sender, RoutedEventArgs e)
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

                // Si el valor que llega es igual al último estado conocido
                // y confirmado en BD, este disparo es un eco del re-render
                // del DataGrid (por ejemplo al cambiar de pestaña), no un
                // clic real del usuario. Lo ignoramos por completo: nada
                // de UPDATE, nada de EventBus, nada de notificación.
                if (_estadoConocidoCategorias.TryGetValue(cat.Id, out bool estadoPrevio)
                    && estadoPrevio == nuevoEstado)
                {
                    return;
                }

                await _configService
                    .CambiarEstadoCategoriaAsync(
                        cat.Id,
                        nuevoEstado);

                // Actualizamos la fotografía con el nuevo estado confirmado.
                _estadoConocidoCategorias[cat.Id] = nuevoEstado;

                EventBus
                    .NotificarRolesPermisosChanged();

                if (nuevoEstado)
                {
                    MostrarNotificacionControlada(
                        $"Categoría '{cat.Nombre}' habilitada",
                        TipoNotificacion.Success);
                }
                else
                {
                    MostrarNotificacionControlada(
                        $"Categoría '{cat.Nombre}' deshabilitada",
                        TipoNotificacion.Warning);
                }
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
        }

        private async void ChkModuloEstado_Changed(object sender, RoutedEventArgs e)
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

                // Mismo principio que en categorías: si coincide con el
                // último estado confirmado, es un eco del re-render, no
                // un clic real.
                if (_estadoConocidoModulos.TryGetValue(mod.Id, out bool estadoPrevio)
                    && estadoPrevio == nuevoEstado)
                {
                    return;
                }

                await _configService
                    .CambiarEstadoModuloAsync(
                        mod.Id,
                        nuevoEstado);

                _estadoConocidoModulos[mod.Id] = nuevoEstado;

                EventBus
                    .NotificarRolesPermisosChanged();

                if (nuevoEstado)
                {
                    MostrarNotificacionControlada(
                        $"Módulo '{mod.NombreModulo}' habilitado",
                        TipoNotificacion.Success);
                }
                else
                {
                    MostrarNotificacionControlada(
                        $"Módulo '{mod.NombreModulo}' deshabilitado",
                        TipoNotificacion.Warning);
                }
            }
            catch (Exception ex)
            {
                Growl.Error(ex.Message);
            }
        }
    }
}