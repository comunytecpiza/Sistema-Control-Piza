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

        public static string ObtenerMotor()
        {
            var config = LeerArchivo();
            return config.ContainsKey("Motor") ? config["Motor"] : "SQL Server (Actual)";
        }

        public static string ObtenerCadenaConexion()
        {
            if (!ExisteConfiguracion()) throw new FileNotFoundException("ARCHIVO_NO_ENCONTRADO");

            var diccionario = LeerArchivo();
            string server = diccionario.ContainsKey("Server") ? diccionario["Server"].Trim() : "";
            string database = diccionario.ContainsKey("DataBase") ? diccionario["DataBase"].Trim() : "";
            string user = diccionario.ContainsKey("Usuario") ? diccionario["Usuario"].Trim() : "";
            string pass = diccionario.ContainsKey("Password") ? diccionario["Password"].Trim() : "";
            string motor = diccionario.ContainsKey("Motor") ? diccionario["Motor"].Trim() : "";

            if (motor.Contains("MySQL"))
            {
                string puerto = "3306";
                if (server.Contains(",")) { var p = server.Split(','); server = p[0].Trim(); puerto = p[1].Trim(); }
                else if (server.Contains(":")) { var p = server.Split(':'); server = p[0].Trim(); puerto = p[1].Trim(); }

                // 🌟 Se elimina SslMode para evitar conflictos con la versión del conector
                return $"Server={server};Port={puerto};Database={database};Uid={user};Password={pass};Convert Zero Datetime=True;";
            }

            return $"Server={server};Database={database};User Id={user};Password={pass};TrustServerCertificate=True;";
        }

        public static void GuardarConfiguracion(string motor, string server, string database, string user, string password)
        {
            if (!Directory.Exists(CarpetaConfig)) Directory.CreateDirectory(CarpetaConfig);

            // Limpiamos cualquier salto de línea o espacio fantasma antes de guardarlo en texto plano
            string contenido = $"Motor={motor?.Trim()}\nServer={server?.Trim()}\nDataBase={database?.Trim()}\nUsuario={user?.Trim()}\nPassword={password?.Trim()}";
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
                // .Trim() elimina espacios o retornos de carro '\r' invisibles al final de la contraseña
                diccionario[partes[0].Trim()] = partes[1].Trim();
            }
            return diccionario;
        }
    }
}