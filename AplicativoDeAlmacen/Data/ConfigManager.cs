using System;
using System.Collections.Generic;
using System.IO;

namespace AplicativoDeAlmacen.Data
{
    public static class ConfigManager
    {
        private static readonly string CarpetaConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdicionesPiza");
        private static readonly string RutaArchivo = Path.Combine(CarpetaConfig, "ControlConfig.txt");

        public static bool ExisteConfiguracion() => File.Exists(RutaArchivo);

        // Extrae el motor seleccionado
        public static string ObtenerMotor()
        {
            var config = LeerArchivo();
            return config.ContainsKey("Motor") ? config["Motor"] : "SQL Server (Actual)";
        }

        public static string ObtenerCadenaConexion()
        {
            if (!ExisteConfiguracion()) throw new FileNotFoundException("ARCHIVO_NO_ENCONTRADO");

            var diccionario = LeerArchivo();
            string server = diccionario.ContainsKey("Server") ? diccionario["Server"] : "";
            string database = diccionario.ContainsKey("DataBase") ? diccionario["DataBase"] : "";
            string user = diccionario.ContainsKey("Usuario") ? diccionario["Usuario"] : "";
            string pass = diccionario.ContainsKey("Password") ? diccionario["Password"] : "";
            string motor = diccionario.ContainsKey("Motor") ? diccionario["Motor"] : "";

            // Si el archivo dice MySQL, armamos la estructura que entiende MySQL
            if (motor.Contains("MySQL"))
            {
                // Si el usuario no puso puerto en la IP, le asignamos el 3306 por defecto
                string puerto = "3306";
                if (server.Contains(",")) { var p = server.Split(','); server = p[0]; puerto = p[1]; }
                else if (server.Contains(":")) { var p = server.Split(':'); server = p[0]; puerto = p[1]; }

                return $"Server={server};Port={puerto};Database={database};Uid={user};Pwd={pass};Convert Zero Datetime=True;";
            }

            // Si no, devolvemos la cadena estándar de SQL Server
            return $"Server={server};Database={database};User Id={user};Password={pass};TrustServerCertificate=True;";
        }

        public static void GuardarConfiguracion(string motor, string server, string database, string user, string password)
        {
            if (!Directory.Exists(CarpetaConfig)) Directory.CreateDirectory(CarpetaConfig);
            string contenido = $"Motor={motor}\nServer={server}\nDataBase={database}\nUsuario={user}\nPassword={password}";
            File.WriteAllText(RutaArchivo, contenido);
        }

        private static Dictionary<string, string> LeerArchivo()
        {
            var diccionario = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!ExisteConfiguracion()) return diccionario;

            var lineas = File.ReadAllLines(RutaArchivo);
            foreach (var linea in lineas)
            {
                if (string.IsNullOrWhiteSpace(linea) || !linea.Contains("=")) continue;
                var partes = linea.Split('=', 2);
                diccionario[partes[0].Trim()] = partes[1].Trim();
            }
            return diccionario;
        }
    }
}