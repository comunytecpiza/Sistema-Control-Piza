using System;
using System.Collections.Generic;
using System.Linq;

namespace AplicativoDeAlmacen.Models.Models
{
    public class ConsultaMovimientoItem
    {
        public DateTime Fecha { get; set; }
        public string NumeroRegistro { get; set; }
        public string RazonSocialUbicacion { get; set; }
        public string NumeroGuia { get; set; }
        public decimal Ingreso { get; set; }
        public decimal Salida { get; set; }
    }

    public class ConsultaCodigoItem
    {
        public string Codigo { get; set; }
        public string ColeccionTipo { get; set; }
    }

    public class ConsultaMovimientoReporte
    {
        // Listas que llenarán las dos tablas de la vista
        public List<ConsultaMovimientoItem> Movimientos { get; set; } = new List<ConsultaMovimientoItem>();
        public List<ConsultaCodigoItem> Codigos { get; set; } = new List<ConsultaCodigoItem>();

        // Autocálculos matemáticos para los totales del pie de página
        public decimal TotalIngresos => Movimientos.Sum(m => m.Ingreso);
        public decimal TotalSalidas => Movimientos.Sum(m => m.Salida);
        public int TotalCodigos => Codigos.Count;
    }
}