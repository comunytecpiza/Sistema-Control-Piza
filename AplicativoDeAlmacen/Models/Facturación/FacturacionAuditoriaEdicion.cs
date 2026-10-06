#nullable enable

using System;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class FacturacionAuditoriaEdicion
    {
        public int Id { get; set; }
        public int FacturacionCabeceraId { get; set; }
        public int UsuarioId { get; set; }
        public DateTime FechaEdicion { get; set; } = DateTime.Now;
        public string MotivoEdicion { get; set; } = string.Empty;
        public string? ObservacionPrevia { get; set; }
        public string? ObservacionNueva { get; set; }
        public int TotalItemsNuevos { get; set; }
        public decimal ImportePrevio { get; set; }
        public decimal ImporteNuevo { get; set; }
    }
}