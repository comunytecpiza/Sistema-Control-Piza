using System;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class ReporteVentaItemDTO
    {
        public int Id { get; set; }
        public DateTime FechaEmision { get; set; }
        public string FechaTexto => FechaEmision.ToString("dd/MM/yyyy");

        public string TipoDocumento { get; set; } = string.Empty; // "01", "02", etc.
        public string SerieDocumento { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;

        // Formato mostrado en la grilla: "FAC-F001-0000213" o "BOL-B001-0002356"
        public string DocumentoFormateado
        {
            get
            {
                string prefijo = TipoDocumento switch
                {
                    "01" => "FAC",
                    "02" => "BOL",
                    "03" => "REC",
                    _ => "DOC"
                };
                return $"{prefijo}-{SerieDocumento}-{NumeroDocumento}";
            }
        }

        public string Cliente { get; set; } = string.Empty;
        public string DocumentoIdentidad { get; set; } = string.Empty; // RUC o DNI

        public decimal Gravado { get; set; }
        public decimal Exonerado { get; set; }
        public decimal Inafecto { get; set; }
        public decimal IGV { get; set; }
        public decimal Total { get; set; }

        public bool EstadoRegistro { get; set; } = true;
    }
}