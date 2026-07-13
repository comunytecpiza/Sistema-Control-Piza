using System;

namespace AplicativoDeAlmacen.Models.Models
{
    // ====================================================================
    // 1. EL DTO DE LA VISTA (Para el DataGrid del XAML)
    // ====================================================================
    // Este modelo "camaleón" permite que un solo DataGrid muestre cualquier tabla.
    // Si la tabla no tiene "InfoExtra", simplemente se queda en blanco.
    public class CatalogoViewItem
    {
        public int Id { get; set; }
        public string Descripcion { get; set; }
        public string InfoExtra { get; set; }   // Guarda 'ENTRADA'/'SALIDA' o abreviaturas
        public string TablaOrigen { get; set; } // Le avisa al botón "Guardar" a qué tabla de SQL/MySQL ir
    }

    // ====================================================================
    // 2. EL MODELO GENÉRICO DE BASE DE DATOS
    // ====================================================================
    // Sirve para mapear TODAS las tablas que solo tienen ID y NOMBRE 
    // (tipo_persona, categoria_producto, tipos_libro, etc.)
    public class CatalogoBasico
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }

        
}