using System;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class ProductoVendidoClienteDTO
    {
        public int ProductoId { get; set; }
        public string CodigoProducto { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = "UND";
        public int Cantidad { get; set; }
        public decimal ImporteTotal { get; set; }
    }

    public class DetalleCodigoVentaClienteDTO
    {
        public int Cantidad { get; set; } = 1;
        public string Codigo { get; set; } = string.Empty;
        public string ColeccionTipo { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public string FechaTexto => FechaEmision.ToString("dd/MM/yyyy");
        public string TipoDocumento { get; set; } = string.Empty;
        public string SerieDocumento { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
        public string DocumentoFormateado => $"{SerieDocumento}-{NumeroDocumento}";
        public decimal Importe { get; set; }
        public int ProductoId { get; set; }
    }
}