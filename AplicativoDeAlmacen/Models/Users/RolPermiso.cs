using System;

namespace AplicativoDeAlmacen.Models.Models
{
    public class RolPermiso
    {
        public int Id { get; set; }
        public int RolUsuarioId { get; set; }
        public int ModuloId { get; set; }

        // Propiedades extendidas mediante JOIN para la Vista
        public string CodigoModulo { get; set; }
        public string NombreModulo { get; set; }

        // 🌟 NUEVAS COLUMNAS DESDE SQL
        
        public int Orden { get; set; }

        // Matriz de permisos granulares
        public bool PuedeVer { get; set; }
        public bool PuedeCrear { get; set; }
        public bool PuedeEditar { get; set; }
        public bool PuedeEliminar { get; set; }
        public bool PuedeImprimir { get; set; }

        public bool Permanent { get; internal set; }
        public string ControlWpf { get; set; }

        public int CategoriaId { get; set; }

        public string CategoriaNombre { get; set; }

        public string IconoCategoria { get; set; }

        public int OrdenCategoria { get; set; }

        public bool EstadoModulo { get; set; }

        public bool EstadoCategoria { get; set; }
        public string ColorCategoria { get; set; } = "#2563EB";
    }
}