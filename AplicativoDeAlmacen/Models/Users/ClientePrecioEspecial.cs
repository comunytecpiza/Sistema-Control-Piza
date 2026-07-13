using AplicativoDeAlmacen.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Users
{
    public partial class ClientePrecioEspecial
    {
        public int Id { get; set; }

        // IDs foráneos puros por si usas Dapper o consultas rápidas
        public int PersonaComercialId { get; set; }
        public int ProductoId { get; set; }

        // 🌟 Los campos de dinero
        public decimal PrecioUnitario { get; set; }
        public decimal PorcentajeBonificacion { get; set; }

        // Auditoría y estado
        public int? UsuarioId { get; set; }
        public int? EstadoId { get; set; } // 1: Activo, 2: Inactivo

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ==========================================
        // PROPIEDADES DE NAVEGACIÓN (Para Entity Framework o Mapeo)
        // ==========================================

        public PersonaComercial PersonaComercial { get; set; }

        // Asumiendo que tienes tu modelo Producto ya creado en tu proyecto
        // public Producto Producto { get; set; } 
    }
}
