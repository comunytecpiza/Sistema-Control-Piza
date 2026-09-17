using System;
using System.Data;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Sistemas;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Sistemas
{
    public class AuditoriaService
    {
        private readonly DatabaseConnection _database = new DatabaseConnection();
        private readonly BackupService _notificacionService = new BackupService();

        private const int MAX_INTENTOS = 3;
        private const int MINUTOS_BLOQUEO = 5;

        // 1. Verificar si el usuario / PC está bloqueado antes de validar clave
        public async Task<EstadoValidacionAcceso> VerificarBloqueoAsync(string username, string nombrePc)
        {
            return await Task.Run(() =>
            {
                var estado = new EstadoValidacionAcceso { EstaBloqueado = false, IntentosRestantes = MAX_INTENTOS };

                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = @"SELECT intentos_consecutivos, bloqueado_hasta 
                                     FROM sys_intentos_login 
                                     WHERE username = @user AND nombre_pc = @pc LIMIT 1";

                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    var pUser = cmd.CreateParameter();
                    pUser.ParameterName = "@user";
                    pUser.Value = username.Trim();
                    cmd.Parameters.Add(pUser);

                    var pPc = cmd.CreateParameter();
                    pPc.ParameterName = "@pc";
                    pPc.Value = nombrePc;
                    cmd.Parameters.Add(pPc);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        int intentos = reader.GetInt32(0);
                        DateTime? bloqueadoHasta = reader.IsDBNull(1) ? null : (DateTime?)reader.GetDateTime(1);

                        if (bloqueadoHasta.HasValue && bloqueadoHasta.Value > DateTime.Now)
                        {
                            var diff = bloqueadoHasta.Value - DateTime.Now;
                            estado.EstaBloqueado = true;
                            estado.MinutosRestantesBloqueo = (int)Math.Ceiling(diff.TotalMinutes);
                            estado.Mensaje = $"Acceso bloqueado temporalmente por seguridad. Reintente en {estado.MinutosRestantesBloqueo} min.";
                            return estado;
                        }

                        estado.IntentosRestantes = Math.Max(0, MAX_INTENTOS - intentos);
                    }
                }
                catch { }

                return estado;
            });
        }

        // 2. Procesar fallo de contraseña (incrementa intentos y bloquea al 3ro)
        public async Task<EstadoValidacionAcceso> RegistrarIntentoFallidoAsync(string username, TelemetriaEquipo tel)
        {
            return await Task.Run(async () =>
            {
                var resultado = new EstadoValidacionAcceso { EstaBloqueado = false };

                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string selectQuery = @"SELECT id, intentos_consecutivos FROM sys_intentos_login 
                                           WHERE username = @user AND nombre_pc = @pc LIMIT 1";

                    using var cmdSelect = conexion.CreateCommand();
                    cmdSelect.CommandText = selectQuery;

                    var p1 = cmdSelect.CreateParameter(); p1.ParameterName = "@user"; p1.Value = username; cmdSelect.Parameters.Add(p1);
                    var p2 = cmdSelect.CreateParameter(); p2.ParameterName = "@pc"; p2.Value = tel.NombrePc; cmdSelect.Parameters.Add(p2);

                    int intentosActuales = 0;
                    int? registroId = null;

                    using (var reader = cmdSelect.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            registroId = reader.GetInt32(0);
                            intentosActuales = reader.GetInt32(1);
                        }
                    }

                    intentosActuales++;
                    DateTime? nuevoBloqueo = null;

                    if (intentosActuales >= MAX_INTENTOS)
                    {
                        nuevoBloqueo = DateTime.Now.AddMinutes(MINUTOS_BLOQUEO);
                        resultado.EstaBloqueado = true;
                        resultado.MinutosRestantesBloqueo = MINUTOS_BLOQUEO;
                        resultado.Mensaje = $"Has superado los {MAX_INTENTOS} intentos permitidos. Cuenta bloqueada por {MINUTOS_BLOQUEO} minutos.";

                        string msgPush = $"🚨 ALERTA DE SEGURIDAD (3 Intentos Fallidos)\n" +
                                         $"Cuenta: {username}\n" +
                                         $"PC: {tel.NombrePc} ({tel.UsuarioWindows})\n" +
                                         $"IP Local: {tel.IpLocal}\n" +
                                         $"IP Publica: {tel.IpPublica}\n" +
                                         $"Origen: {tel.TipoRed}\n" +
                                         $"Estado: Bloqueado por {MINUTOS_BLOQUEO} min.";

                        _ = _notificacionService.EnviarNotificacionNtfyAsync(msgPush, "Alerta de Seguridad TI");

                        RegistrarAuditoriaInterna(conexion, null, username, "BLOQUEO_3_INTENTOS", tel, $"Supero {MAX_INTENTOS} intentos de clave");
                    }
                    else
                    {
                        resultado.IntentosRestantes = MAX_INTENTOS - intentosActuales;
                        resultado.Mensaje = $"Contraseña incorrecta. Le quedan {resultado.IntentosRestantes} intento(s).";

                        RegistrarAuditoriaInterna(conexion, null, username, "LOGIN_FALLIDO", tel, $"Intento erroneo {intentosActuales}/{MAX_INTENTOS}");
                    }

                    using var cmdSave = conexion.CreateCommand();
                    if (registroId.HasValue)
                    {
                        cmdSave.CommandText = @"UPDATE sys_intentos_login 
                                               SET intentos_consecutivos = @intentos,
                                                   bloqueado_hasta = @bloqueo,
                                                   ip_local = @iplocal,
                                                   ip_publica = @ippublica,
                                                   ultimo_intento = NOW()
                                               WHERE id = @id";
                        var pId = cmdSave.CreateParameter(); pId.ParameterName = "@id"; pId.Value = registroId.Value; cmdSave.Parameters.Add(pId);
                    }
                    else
                    {
                        cmdSave.CommandText = @"INSERT INTO sys_intentos_login 
                                               (username, nombre_pc, ip_local, ip_publica, intentos_consecutivos, bloqueado_hasta)
                                               VALUES (@user, @pc, @iplocal, @ippublica, @intentos, @bloqueo)";
                        var pU = cmdSave.CreateParameter(); pU.ParameterName = "@user"; pU.Value = username; cmdSave.Parameters.Add(pU);
                        var pP = cmdSave.CreateParameter(); pP.ParameterName = "@pc"; pP.Value = tel.NombrePc; cmdSave.Parameters.Add(pP);
                    }

                    var pInt = cmdSave.CreateParameter(); pInt.ParameterName = "@intentos"; pInt.Value = intentosActuales; cmdSave.Parameters.Add(pInt);
                    var pBloq = cmdSave.CreateParameter(); pBloq.ParameterName = "@bloqueo"; pBloq.Value = (object)nuevoBloqueo ?? DBNull.Value; cmdSave.Parameters.Add(pBloq);
                    var pIpL = cmdSave.CreateParameter(); pIpL.ParameterName = "@iplocal"; pIpL.Value = tel.IpLocal; cmdSave.Parameters.Add(pIpL);
                    var pIpP = cmdSave.CreateParameter(); pIpP.ParameterName = "@ippublica"; pIpP.Value = tel.IpPublica; cmdSave.Parameters.Add(pIpP);

                    cmdSave.ExecuteNonQuery();
                }
                catch { }

                return resultado;
            });
        }

        // 3. Registrar Login Exitoso, limpiar intentos y registrar sesión activa
        // 3. Registrar Login Exitoso, limpiar intentos y registrar sesión activa
        public async Task<string> RegistrarLoginExitosoAsync(int usuarioId, string username, string rol, TelemetriaEquipo tel)
        {
            string tokenSesion = Guid.NewGuid().ToString("N");

            await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    // 1. Identificar si la IP pública coincide con una sede guardada en sys_sedes_ips
                    bool esSedeAutorizada = false;
                    string nombreSedeDetectada = "REMOTO_EXTERNO";

                    if (!string.IsNullOrWhiteSpace(tel.IpPublica) && tel.IpPublica != "No Disponible")
                    {
                        using var cmdSede = conexion.CreateCommand();
                        cmdSede.CommandText = "SELECT nombre_sede FROM sys_sedes_ips WHERE ip_publica = @ip AND activo = 1 LIMIT 1";

                        var pIp = cmdSede.CreateParameter();
                        pIp.ParameterName = "@ip";
                        pIp.Value = tel.IpPublica.Trim();
                        cmdSede.Parameters.Add(pIp);

                        var resSede = cmdSede.ExecuteScalar();
                        if (resSede != null && resSede != DBNull.Value)
                        {
                            esSedeAutorizada = true;
                            nombreSedeDetectada = resSede.ToString()?.Trim() ?? "OFICINA";
                        }
                    }

                    // Asignamos la sede detectada a la telemetría
                    tel.TipoRed = nombreSedeDetectada;

                    // 2. Limpiar intentos fallidos previos
                    using (var cmdClean = conexion.CreateCommand())
                    {
                        cmdClean.CommandText = "DELETE FROM sys_intentos_login WHERE username = @user AND nombre_pc = @pc";
                        var pU = cmdClean.CreateParameter(); pU.ParameterName = "@user"; pU.Value = username; cmdClean.Parameters.Add(pU);
                        var pP = cmdClean.CreateParameter(); pP.ParameterName = "@pc"; pP.Value = tel.NombrePc; cmdClean.Parameters.Add(pP);
                        cmdClean.ExecuteNonQuery();
                    }

                    // 3. 🛡️ EVITAR DUPLICADOS: Si este usuario ya tenía una sesión activa en esta PC, marcarla cerrada
                    using (var cmdInvalidar = conexion.CreateCommand())
                    {
                        cmdInvalidar.CommandText = @"UPDATE sys_sesiones_activas 
                                             SET estado = 'CERRADO_NORMAL' 
                                             WHERE username = @user AND nombre_pc = @pc AND estado = 'ACTIVO'";
                        var pU = cmdInvalidar.CreateParameter(); pU.ParameterName = "@user"; pU.Value = username; cmdInvalidar.Parameters.Add(pU);
                        var pP = cmdInvalidar.CreateParameter(); pP.ParameterName = "@pc"; pP.Value = tel.NombrePc; cmdInvalidar.Parameters.Add(pP);
                        cmdInvalidar.ExecuteNonQuery();
                    }

                    // 4. Insertar la nueva sesión activa con su sede real
                    string sesionQuery = @"INSERT INTO sys_sesiones_activas 
                                   (usuario_id, username, rol, token_sesion, nombre_pc, usuario_windows, ip_local, ip_publica, tipo_red)
                                   VALUES (@uid, @user, @rol, @token, @pc, @winuser, @iplocal, @ippublica, @tipored)";

                    using (var cmdSesion = conexion.CreateCommand())
                    {
                        cmdSesion.CommandText = sesionQuery;

                        var pUid = cmdSesion.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = usuarioId; cmdSesion.Parameters.Add(pUid);
                        var pUsr = cmdSesion.CreateParameter(); pUsr.ParameterName = "@user"; pUsr.Value = username; cmdSesion.Parameters.Add(pUsr);
                        var pRol = cmdSesion.CreateParameter(); pRol.ParameterName = "@rol"; pRol.Value = rol; cmdSesion.Parameters.Add(pRol);
                        var pTok = cmdSesion.CreateParameter(); pTok.ParameterName = "@token"; pTok.Value = tokenSesion; cmdSesion.Parameters.Add(pTok);
                        var pPc = cmdSesion.CreateParameter(); pPc.ParameterName = "@pc"; pPc.Value = tel.NombrePc; cmdSesion.Parameters.Add(pPc);
                        var pWin = cmdSesion.CreateParameter(); pWin.ParameterName = "@winuser"; pWin.Value = tel.UsuarioWindows; cmdSesion.Parameters.Add(pWin);
                        var pIpl = cmdSesion.CreateParameter(); pIpl.ParameterName = "@iplocal"; pIpl.Value = tel.IpLocal; cmdSesion.Parameters.Add(pIpl);
                        var pIpp = cmdSesion.CreateParameter(); pIpp.ParameterName = "@ippublica"; pIpp.Value = tel.IpPublica; cmdSesion.Parameters.Add(pIpp);
                        var pRed = cmdSesion.CreateParameter(); pRed.ParameterName = "@tipored"; pRed.Value = nombreSedeDetectada; cmdSesion.Parameters.Add(pRed);

                        cmdSesion.ExecuteNonQuery();
                    }

                    // 5. Registrar en bitácora histórica
                    RegistrarAuditoriaInterna(conexion, usuarioId, username, "LOGIN_EXITOSO", tel, $"Ingreso desde {nombreSedeDetectada}");

                    // 6. Alerta Push a iPhone: solo si es Admin o si es una IP externa no autorizada
                    bool esAdmin = rol.Equals("Administrador", StringComparison.OrdinalIgnoreCase) ||
                                   username.Equals("admin", StringComparison.OrdinalIgnoreCase);

                    if (esAdmin || !esSedeAutorizada)
                    {
                        string etiqueta = !esSedeAutorizada ? "⚠️ Acceso IP Externa" : "🔑 Acceso Administrador";
                        string msgLogin = $"{etiqueta}\n" +
                                          $"Usuario: {username} ({rol})\n" +
                                          $"Sede: {nombreSedeDetectada}\n" +
                                          $"Equipo: {tel.NombrePc} ({tel.UsuarioWindows})\n" +
                                          $"IP: {tel.IpLocal} / {tel.IpPublica}\n" +
                                          $"Hora: {DateTime.Now:HH:mm:ss}";

                        _ = _notificacionService.EnviarNotificacionNtfyAsync(msgLogin, "Acceso al Sistema");
                    }
                }
                catch { }
            });

            return tokenSesion;
        }

        // 4. Actualizar Latido (Heartbeat cada 2 min desde la ventana principal)
        public async Task ActualizarLatidoAsync(string tokenSesion)
        {
            if (string.IsNullOrWhiteSpace(tokenSesion)) return;

            await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "UPDATE sys_sesiones_activas SET ultimo_latido = NOW() WHERE token_sesion = @tok AND estado = 'ACTIVO'";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    var p = cmd.CreateParameter(); p.ParameterName = "@tok"; p.Value = tokenSesion; cmd.Parameters.Add(p);

                    cmd.ExecuteNonQuery();
                }
                catch { }
            });
        }

        // 5. Cerrar Sesión (Logout voluntario)
        public async Task CerrarSesionAsync(string tokenSesion, string username, TelemetriaEquipo tel)
        {
            if (string.IsNullOrWhiteSpace(tokenSesion)) return;

            await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "UPDATE sys_sesiones_activas SET estado = 'CERRADO_NORMAL' WHERE token_sesion = @tok";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    var p = cmd.CreateParameter(); p.ParameterName = "@tok"; p.Value = tokenSesion; cmd.Parameters.Add(p);
                    cmd.ExecuteNonQuery();

                    RegistrarAuditoriaInterna(conexion, null, username, "LOGOUT", tel, "Cierre voluntario de sesion");
                }
                catch { }
            });
        }

        // Método auxiliar de auditoría sobre conexión abierta
        private void RegistrarAuditoriaInterna(IDbConnection conexion, int? usuarioId, string username, string evento, TelemetriaEquipo tel, string detalles)
        {
            try
            {
                string query = @"INSERT INTO sys_auditoria_accesos 
                                 (usuario_id, username, evento, nombre_pc, usuario_windows, ip_local, ip_publica, tipo_red, detalles)
                                 VALUES (@uid, @usr, @evt, @pc, @win, @ipl, @ipp, @red, @det)";

                using var cmd = conexion.CreateCommand();
                cmd.CommandText = query;

                var pUid = cmd.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = (object)usuarioId ?? DBNull.Value; cmd.Parameters.Add(pUid);
                var pUsr = cmd.CreateParameter(); pUsr.ParameterName = "@usr"; pUsr.Value = username; cmd.Parameters.Add(pUsr);
                var pEvt = cmd.CreateParameter(); pEvt.ParameterName = "@evt"; pEvt.Value = evento; cmd.Parameters.Add(pEvt);
                var pPc = cmd.CreateParameter(); pPc.ParameterName = "@pc"; pPc.Value = tel.NombrePc; cmd.Parameters.Add(pPc);
                var pWin = cmd.CreateParameter(); pWin.ParameterName = "@win"; pWin.Value = tel.UsuarioWindows; cmd.Parameters.Add(pWin);
                var pIpl = cmd.CreateParameter(); pIpl.ParameterName = "@ipl"; pIpl.Value = tel.IpLocal; cmd.Parameters.Add(pIpl);
                var pIpp = cmd.CreateParameter(); pIpp.ParameterName = "@ipp"; pIpp.Value = tel.IpPublica; cmd.Parameters.Add(pIpp);
                var pRed = cmd.CreateParameter(); pRed.ParameterName = "@red"; pRed.Value = tel.TipoRed; cmd.Parameters.Add(pRed);
                var pDet = cmd.CreateParameter(); pDet.ParameterName = "@det"; pDet.Value = detalles; cmd.Parameters.Add(pDet);

                cmd.ExecuteNonQuery();
            }
            catch { }
        }


        // 1. Identificar si la IP pública pertenece a una sede conocida
        public async Task<(bool EsConocida, string NombreSede)> IdentificarSedePorIpAsync(string ipPublica)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(ipPublica)) return (false, "Desconocida");

                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "SELECT nombre_sede FROM sys_sedes_ips WHERE ip_publica = @ip AND activo = 1 LIMIT 1";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    var p = cmd.CreateParameter(); p.ParameterName = "@ip"; p.Value = ipPublica.Trim(); cmd.Parameters.Add(p);

                    var resultado = cmd.ExecuteScalar();
                    if (resultado != null && resultado != DBNull.Value)
                    {
                        return (true, resultado.ToString() ?? "Sede Registrada");
                    }
                }
                catch { }

                return (false, "Ubicación Externa");
            });
        }

        // 2. Obtener lista de sedes/IPs registradas
        public async Task<List<SedeIpModel>> ObtenerSedesIpsAsync()
        {
            return await Task.Run(() =>
            {
                var lista = new List<SedeIpModel>();
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "SELECT id, nombre_sede, ip_publica, descripcion, activo, fecha_registro FROM sys_sedes_ips ORDER BY nombre_sede ASC";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        lista.Add(new SedeIpModel
                        {
                            Id = reader.GetInt32(0),
                            NombreSede = reader.GetString(1),
                            IpPublica = reader.GetString(2),
                            Descripcion = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Activo = reader.GetBoolean(4),
                            FechaRegistro = reader.GetDateTime(5)
                        });
                    }
                }
                catch { }
                return lista;
            });
        }

        // 3. Registrar o actualizar una Sede/IP
        public async Task<bool> GuardarSedeIpAsync(string nombreSede, string ipPublica, string descripcion)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = @"INSERT INTO sys_sedes_ips (nombre_sede, ip_publica, descripcion, activo) 
                             VALUES (@nom, @ip, @desc, 1)
                             ON DUPLICATE KEY UPDATE nombre_sede = @nom, descripcion = @desc, activo = 1";

                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    var p1 = cmd.CreateParameter(); p1.ParameterName = "@nom"; p1.Value = nombreSede.Trim(); cmd.Parameters.Add(p1);
                    var p2 = cmd.CreateParameter(); p2.ParameterName = "@ip"; p2.Value = ipPublica.Trim(); cmd.Parameters.Add(p2);
                    var p3 = cmd.CreateParameter(); p3.ParameterName = "@desc"; p3.Value = descripcion.Trim(); cmd.Parameters.Add(p3);

                    cmd.ExecuteNonQuery();
                    return true;
                }
                catch { return false; }
            });
        }

        // 4. Eliminar Sede/IP
        public async Task<(bool Exito, string Mensaje)> EliminarSedeIpAsync(int id)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    // Verificar si es la sede protegida del sistema
                    string checkQuery = "SELECT es_sistema, nombre_sede FROM sys_sedes_ips WHERE id = @id LIMIT 1";
                    using var cmdCheck = conexion.CreateCommand();
                    cmdCheck.CommandText = checkQuery;
                    var pIdCheck = cmdCheck.CreateParameter(); pIdCheck.ParameterName = "@id"; pIdCheck.Value = id; cmdCheck.Parameters.Add(pIdCheck);

                    using var reader = cmdCheck.ExecuteReader();
                    if (reader.Read())
                    {
                        bool esSistema = !reader.IsDBNull(0) && Convert.ToBoolean(reader.GetInt32(0));
                        string nombre = reader.GetString(1);

                        if (esSistema)
                        {
                            return (false, $"La sede '{nombre}' es la sede raíz del sistema y está protegida contra eliminación.");
                        }
                    }
                    reader.Close();

                    // Si no es del sistema, proceder a eliminar
                    string deleteQuery = "DELETE FROM sys_sedes_ips WHERE id = @id AND es_sistema = 0";
                    using var cmdDelete = conexion.CreateCommand();
                    cmdDelete.CommandText = deleteQuery;
                    var pId = cmdDelete.CreateParameter(); pId.ParameterName = "@id"; pId.Value = id; cmdDelete.Parameters.Add(pId);

                    int filas = cmdDelete.ExecuteNonQuery();
                    return (filas > 0, filas > 0 ? "Sede eliminada con éxito." : "No se pudo eliminar la sede.");
                }
                catch (Exception ex)
                {
                    return (false, $"Error al eliminar: {ex.Message}");
                }
            });
        }

        // 5. Cierre Remoto de Sesión (Expulsar usuario desde el panel)
        // En: Services/Sistemas/AuditoriaService.cs
        public async Task<(bool Exito, string Mensaje)> ForzarCierreSesionRemotoAsync(string tokenObjetivo, string tokenSolicitante, string usernameObjetivo)
        {
            return await Task.Run(() =>
            {
                // 🛡️ REGLA: El Administrador General no puede ser expulsado jamás
                if (usernameObjetivo.Equals("USR01", StringComparison.OrdinalIgnoreCase) ||
                    usernameObjetivo.Equals("admin", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Acceso denegado: La cuenta de Administrador General no puede ser desconectada remotamente.");
                }

                // Tampoco te puedes auto-expulsar a ti mismo
                if (tokenObjetivo == tokenSolicitante)
                {
                    return (false, "Operación cancelada: No puede expulsar su propia sesión activa.");
                }

                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "UPDATE sys_sesiones_activas SET estado = 'FORZADO_TI' WHERE token_sesion = @tok AND estado = 'ACTIVO'";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    var p = cmd.CreateParameter(); p.ParameterName = "@tok"; p.Value = tokenObjetivo; cmd.Parameters.Add(p);

                    int filas = cmd.ExecuteNonQuery();
                    return (filas > 0, filas > 0 ? "Comando de desconexión emitido." : "La sesión ya no está activa.");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        // 6. Consultar estado de sesión durante el Heartbeat (¿Nos expulsaron?)
        public async Task<string> VerificarEstadoSesionAsync(string tokenSesion)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(tokenSesion)) return "CERRADO";

                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "SELECT estado FROM sys_sesiones_activas WHERE token_sesion = @tok LIMIT 1";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    var p = cmd.CreateParameter(); p.ParameterName = "@tok"; p.Value = tokenSesion; cmd.Parameters.Add(p);

                    var resultado = cmd.ExecuteScalar();
                    return resultado?.ToString() ?? "CERRADO";
                }
                catch { return "ACTIVO"; } // Ante falla temporal de red, no desconectar inmediatamente
            });
        }

        // 7. Obtener todas las sesiones activas en tiempo real
        public async Task<List<SesionActivaModel>> ObtenerSesionesActivasEnVivoAsync()
        {
            return await Task.Run(() =>
            {
                var lista = new List<SesionActivaModel>();
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    // Considerar activas solo las que enviaron latido en los últimos 4 minutos
                    string query = @"SELECT id, usuario_id, username, rol, token_sesion, nombre_pc, 
                                    usuario_windows, ip_local, ip_publica, tipo_red, inicio_sesion, ultimo_latido, estado
                             FROM sys_sesiones_activas 
                             WHERE estado = 'ACTIVO' AND ultimo_latido >= DATE_SUB(NOW(), INTERVAL 4 MINUTE)
                             ORDER BY inicio_sesion DESC";

                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        lista.Add(new SesionActivaModel
                        {
                            Id = reader.GetInt32(0),
                            UsuarioId = reader.GetInt32(1),
                            Username = reader.GetString(2),
                            Rol = reader.GetString(3),
                            TokenSesion = reader.GetString(4),
                            NombrePc = reader.GetString(5),
                            UsuarioWindows = reader.GetString(6),
                            IpLocal = reader.GetString(7),
                            IpPublica = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            TipoRed = reader.GetString(9),
                            InicioSesion = reader.GetDateTime(10),
                            UltimoLatido = reader.GetDateTime(11),
                            Estado = reader.GetString(12)
                        });
                    }
                }
                catch { }
                return lista;
            });
        }


        // Obtener historial de auditoría
        public async Task<List<AuditoriaAccesoModel>> ObtenerHistorialAccesosAsync(int limite = 100)
        {
            return await Task.Run(() =>
            {
                var lista = new List<AuditoriaAccesoModel>();
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = $@"SELECT id, usuario_id, username, evento, nombre_pc, usuario_windows, 
                                     ip_local, ip_publica, tipo_red, detalles, fecha_registro 
                              FROM sys_auditoria_accesos 
                              ORDER BY id DESC LIMIT {limite}";

                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        lista.Add(new AuditoriaAccesoModel
                        {
                            Id = reader.GetInt64(0),
                            UsuarioId = reader.IsDBNull(1) ? null : (int?)reader.GetInt32(1),
                            Username = reader.GetString(2),
                            Evento = reader.GetString(3),
                            NombrePc = reader.GetString(4),
                            UsuarioWindows = reader.GetString(5),
                            IpLocal = reader.GetString(6),
                            IpPublica = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            TipoRed = reader.GetString(8),
                            Detalles = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            FechaRegistro = reader.GetDateTime(10)
                        });
                    }
                }
                catch { }
                return lista;
            });
        }

        // Contar bloqueos actualmente vigentes
        public async Task<int> ContarBloqueosActivosAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "SELECT COUNT(*) FROM sys_intentos_login WHERE bloqueado_hasta > NOW()";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;

                    var res = cmd.ExecuteScalar();
                    return Convert.ToInt32(res);
                }
                catch { return 0; }
            });
        }

        // Limpiar bloqueos manualmente
        public async Task LimpiarTodosLosBloqueosAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    using var conexion = _database.GetConnection();
                    conexion.Open();

                    string query = "TRUNCATE TABLE sys_intentos_login";
                    using var cmd = conexion.CreateCommand();
                    cmd.CommandText = query;
                    cmd.ExecuteNonQuery();
                }
                catch { }
            });
        }
    }
}