#nullable enable
using System;
using System.Collections.Generic;
using AplicativoDeAlmacen.Models.Models;

namespace AplicativoDeAlmacen.Models.Facturación
{
    public class HistorialVentaCodigoDTO
    {
        // Panel Lateral Superior (Datos Generales del Comprobante)
        public int FacturacionCabeceraId { get; set; }
        public string TipoDocumento { get; set; } = string.Empty;
        public string SerieNumero { get; set; } = string.Empty;
        public DateTime? FechaEmision { get; set; }
        public string ClienteNombre { get; set; } = "CLIENTES VARIOS";
        public string ClienteNumeroDoc { get; set; } = string.Empty;
        public string InstitucionColegio { get; set; } = "SIN COLEGIO";
        public string PuntoVentaNombre { get; set; } = "SIN SEDE";
        public string CanalesPago { get; set; } = "EFECTIVO";
        public decimal ImporteTotalComprobante { get; set; }
        public bool ComprobanteActivo { get; set; }

        // Pestaña 1: Historial Físico de Kárdex
        public List<KardexFisicoItem> MovimientosKardex { get; set; } = new();

        // Pestaña 2: Detalle Fiscal / Canales de Pago del Comprobante
        public List<PagoComprobanteItemDTO> DetallePagosFiscales { get; set; } = new();
    }

    public class PagoComprobanteItemDTO
    {
        public string MedioPago { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string NumeroOperacion { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
    }
}