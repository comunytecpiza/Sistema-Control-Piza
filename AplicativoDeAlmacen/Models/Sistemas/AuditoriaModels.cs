using System;

namespace AplicativoDeAlmacen.Models.Sistemas
{
    public class TelemetriaEquipo
    {
        public string NombrePc { get; set; } = string.Empty;
        public string UsuarioWindows { get; set; } = string.Empty;
        public string IpLocal { get; set; } = string.Empty;
        public string IpPublica { get; set; } = string.Empty;
        public string TipoRed { get; set; } = "REMOTO_EXTERNO"; // "OFICINA" o "REMOTO_EXTERNO"
    }

    public class SesionActivaModel
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public string TokenSesion { get; set; } = string.Empty;
        public string NombrePc { get; set; } = string.Empty;
        public string UsuarioWindows { get; set; } = string.Empty;
        public string IpLocal { get; set; } = string.Empty;
        public string IpPublica { get; set; } = string.Empty;
        public string TipoRed { get; set; } = "REMOTO_EXTERNO";
        public DateTime InicioSesion { get; set; }
        public DateTime UltimoLatido { get; set; }
        public string Estado { get; set; } = "ACTIVO";
    }

    public class IntentoLoginModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string NombrePc { get; set; } = string.Empty;
        public string IpLocal { get; set; } = string.Empty;
        public string IpPublica { get; set; } = string.Empty;
        public int IntentosConsecutivos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
        public DateTime UltimoIntento { get; set; }
    }

    public class AuditoriaAccesoModel
    {
        public long Id { get; set; }
        public int? UsuarioId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Evento { get; set; } = string.Empty; // LOGIN_EXITOSO, LOGIN_FALLIDO, BLOQUEO_3_INTENTOS, LOGOUT, CIERRE_FORZADO
        public string NombrePc { get; set; } = string.Empty;
        public string UsuarioWindows { get; set; } = string.Empty;
        public string IpLocal { get; set; } = string.Empty;
        public string IpPublica { get; set; } = string.Empty;
        public string TipoRed { get; set; } = "REMOTO_EXTERNO";
        public string Detalles { get; set; } = string.Empty;
        public DateTime FechaRegistro { get; set; }
    }

    public class EstadoValidacionAcceso
    {
        public bool EstaBloqueado { get; set; }
        public int IntentosRestantes { get; set; }
        public int MinutosRestantesBloqueo { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }

    public class SedeIpModel
    {
        public int Id { get; set; }
        public string NombreSede { get; set; } = string.Empty;
        public string IpPublica { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public DateTime FechaRegistro { get; set; }
    }
}