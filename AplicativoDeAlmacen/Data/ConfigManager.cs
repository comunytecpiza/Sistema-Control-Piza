using System;
using System.Collections.Generic;
using System.IO;

namespace AplicativoDeAlmacen.Data
{
    public static class ConfigManager
    {
        // Usamos AppData para que nunca haya problemas de permisos con el disco C
        private static readonly string CarpetaConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdicionesPiza");
        private static readonly string RutaArchivo = Path.Combine(CarpetaConfig, "ControlConfig.txt");

        public static bool ExisteConfiguracion()
        {
            return File.Exists(RutaArchivo);
        }

        public static string ObtenerCadenaConexion()
        {
            if (!ExisteConfiguracion()) throw new FileNotFoundException("ARCHIVO_NO_ENCONTRADO");

            var lineas = File.ReadAllLines(RutaArchivo);
            var diccionario = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var linea in lineas)
            {
                if (string.IsNullOrWhiteSpace(linea) || !linea.Contains("=")) continue;
                var partes = linea.Split('=', 2);
                diccionario[partes[0].Trim()] = partes[1].Trim();
            }

            string server = diccionario.ContainsKey("Server") ? diccionario["Server"] : "";
            string database = diccionario.ContainsKey("DataBase") ? diccionario["DataBase"] : "";
            string user = diccionario.ContainsKey("Usuario") ? diccionario["Usuario"] : "";
            string pass = diccionario.ContainsKey("Password") ? diccionario["Password"] : "";

            // El tipo de BD está guardado para el futuro (MySQL, Oracle, etc.)
            // string dbType = diccionario.ContainsKey("Motor") ? diccionario["Motor"] : "SQL Server";

            return $"Server={server};Database={database};User Id={user};Password={pass};TrustServerCertificate=True;";
        }

        public static void GuardarConfiguracion(string motor, string server, string database, string user, string password)
        {
            if (!Directory.Exists(CarpetaConfig)) Directory.CreateDirectory(CarpetaConfig);
            string contenido = $"Motor={motor}\nServer={server}\nDataBase={database}\nUsuario={user}\nPassword={password}";
            File.WriteAllText(RutaArchivo, contenido);
        }
    }
}