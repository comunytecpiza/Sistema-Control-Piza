#nullable enable
using System.Collections.Generic;
using AplicativoDeAlmacen.Models.Almacen;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class GrupoVentaMatrizDTO
    {
        public int Id { get; set; }
        public string TituloPestana { get; set; } = string.Empty;
        public int TipoUbicacionId { get; set; } = 2; // 1: Referencial, 2: Punto Venta/Feria, 5: Punto Externo
        public string TipoUbicacionNombre { get; set; } = "PUNTOS DE VENTA";
        public List<MatrizKardexItemDTO> Movimientos { get; set; } = new();

    }
}