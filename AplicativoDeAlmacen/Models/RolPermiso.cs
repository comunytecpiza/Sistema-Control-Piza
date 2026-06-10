using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Models
{
    public class RolPermiso
    {
        public int Id { get; set; }
        public int RolUsuarioId { get; set; }
        public int ModuloId { get; set; }

        // Propiedades extendidas mediante JOIN para la Vista (UI Binding)
        public string CodigoModulo { get; set; }
        public string NombreModulo { get; set; }

        // Matriz de permisos granulares
        public bool PuedeVer { get; set; }
        public bool PuedeCrear { get; set; }
        public bool PuedeEditar { get; set; }
        public bool PuedeEliminar { get; set; }
        public bool PuedeImprimir { get; set; }

        public string CategoriaModulo
        {
            get
            {
                if (CodigoModulo == "MOD_USUARIOS" || CodigoModulo == "MOD_LOCALIDADES" || CodigoModulo == "MOD_ZONAS" || CodigoModulo == "MOD_UBICACIONES" || CodigoModulo == "MOD_PRODUCTOS" || CodigoModulo == "MOD_UNIDADES" || CodigoModulo == "MOD_PERSONAS" || CodigoModulo == "MOD_COLECCIONES" || CodigoModulo == "MOD_TITULOS")
                    return "📁 CATÁLOGOS Y TABLAS MAESTRAS";

                if (CodigoModulo == "MOD_REG_CODIGOS" || CodigoModulo == "MOD_ING_PRODUCTOS" || CodigoModulo == "MOD_SAL_PRODUCTOS" || CodigoModulo == "MOD_REG_FACTURAS")
                    return "📦 MOVIMIENTOS Y OPERACIONES FÍSICAS";

                if (CodigoModulo == "MOD_VALORIZACION" || CodigoModulo == "MOD_IMP_CLIENTES" || CodigoModulo == "MOD_IMP_VENTAS")
                    return "⚙️ PROCESOS INTERNOS E IMPORTACIONES";

                return "📊 CONSULTAS, REPORTES Y AUDITORÍA"; // Por defecto, todo lo demás va aquí
            }
        }

        public bool Permanent { get; internal set; }
    }
}