using System;

namespace AplicativoDeAlmacen.Models.Documentos
{
    public class Empresa
    {
        public int Id { get; set; }
        public string Ruc { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string? NombreComercial { get; set; }
        public string? Direccion { get; set; }
        public bool EsActivo { get; set; } = true;
        public DateTime FechaRegistro { get; set; } = DateTime.Now;
    }
}