using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using AplicativoDeAlmacen.Models.Models;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Services;

namespace AplicativoDeAlmacen.Views
{
    public partial class ConfiguracionSistemaUserControl : UserControl
    {
        public ConfiguracionSistemaUserControl()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Aquí luego cargaremos los Tab 1 y Tab 2 desde la base de datos
        }

        private async void BtnEscanear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. CONEXIÓN REAL A LA BASE DE DATOS
                List<string> vistasRegistradasEnBD = new List<string>();

                await Task.Run(() =>
                {
                    using (System.Data.IDbConnection conn = new Data.DataConnection.DatabaseConnection().GetConnection())
                    {
                        conn.Open();
                        using (System.Data.IDbCommand cmd = conn.CreateCommand())
                        {
                            // Traemos todos los controles que ya están registrados
                            cmd.CommandText = "SELECT control_wpf FROM modulos_sistema WHERE control_wpf IS NOT NULL";
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    vistasRegistradasEnBD.Add(reader.GetString(0));
                                }
                            }
                        }
                    }
                });

                // 2. MAGIA DE REFLEXIÓN: Buscamos en todo el proyecto
                var todasLasVistas = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(UserControl)) &&
                                t.Namespace != null &&
                                t.Namespace.StartsWith("AplicativoDeAlmacen.Views"))
                    .ToList();

                // 3. Filtramos: Solo mostramos las que NO están en la base de datos
                var vistasNuevas = todasLasVistas
                    .Where(t => !vistasRegistradasEnBD.Contains(t.Name))
                    .Select(t =>
                    {
                        // Limpiamos el Namespace para que parezca una ruta de carpetas
                        string rutaLimpia = (t.Namespace ?? "Raíz")
                                            .Replace("AplicativoDeAlmacen.Views", "Views")
                                            .Replace(".", " / ");

                        // Corrección estética si la vista estaba en la raíz
                        if (rutaLimpia == "Views") rutaLimpia = "Views / (Raíz)";

                        return new VistaDetectada
                        {
                            NombreVista = t.Name,
                            RutaCompleta = rutaLimpia,
                            Seleccionada = false
                        };
                    })
                    .OrderBy(v => v.NombreVista)
                    .ToList();

                DgVistasDetectadas.ItemsSource = vistasNuevas;

                if (!vistasNuevas.Any())
                {
                    MessageBox.Show("El sistema está actualizado. Todas las vistas existentes ya están en la Base de Datos.", "Escaneo Completo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al escanear el proyecto: " + ex.Message, "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRegistrar_Click(object sender, RoutedEventArgs e)
        {
            var vistasEnGrilla = DgVistasDetectadas.ItemsSource as System.Collections.Generic.List<VistaDetectada>;
            if (vistasEnGrilla == null) return;

            var seleccionadas = vistasEnGrilla.Where(v => v.Seleccionada).ToList();

            if (!seleccionadas.Any())
            {
                MessageBox.Show("Seleccione al menos una vista nueva para registrar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool cambiosRealizados = false;

            // Abrimos el modal uno por uno para que el usuario le ponga Código, Nombre y Categoría
            foreach (var vista in seleccionadas)
            {
                var modal = new RegistrarModuloWindow(vista.NombreVista);
                // Usamos ShowDialog() para que espere a que termine de registrar uno antes de pasar al siguiente
                if (modal.ShowDialog() == true)
                {
                    cambiosRealizados = true;
                }
            }

            if (cambiosRealizados)
            {
                // Volvemos a escanear automáticamente para que las registradas desaparezcan de la tabla
                BtnEscanear_Click(null, null);

                // 🌟 AQUÍ AGREGAS EL LLAMADO AL EVENTBUS
                EventBus.NotificarRolesPermisosChanged();
            }
        }
    }
}