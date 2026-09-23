using AplicativoDeAlmacen.Models.Models;
using System;

namespace AplicativoDeAlmacen.Models.Documentos
{
    public class SerieDocumento
    {
        public int Id { get; set; }

        public int UbicacionId { get; set; }
        public Ubicacion? Ubicacion { get; set; }

        // 🌟 RELACIÓN CON EMPRESA
        public int? EmpresaId { get; set; }
        public Empresa? Empresa { get; set; }

        public string NumeroSerie { get; set; } = string.Empty; // num_seri (ej. "B001", "FA01")
        public string TipoSerie { get; set; } = "E";           // tip_seri: "E" (Electrónica), "M" (Manual)

        // Correlativos
        public int CorrelativoFactura { get; set; }
        public int CorrelativoBoleta { get; set; }
        public int CorrelativoRecibo { get; set; }

        public DateTime FechaRegistro { get; set; }
        public string CodigoUsuario { get; set; } = string.Empty;
        public int EstadoId { get; set; } // 1 = Activo, 0 = Inactivo
    }
}