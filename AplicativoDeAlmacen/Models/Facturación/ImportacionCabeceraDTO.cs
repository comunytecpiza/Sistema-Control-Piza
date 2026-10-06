using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Facturación
{
    /// <summary>
    /// DTO para la Grilla Superior: Cabecera del comprobante agrupado por Serie + Número
    /// </summary>
    public class ImportacionCabeceraDTO
    {
        // 🌟 1. EMPRESA EMISORA (PRIMER CAMPO PARA LA GRILLA)
        public int? InstitucionId { get; set; }
        public int? EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "[ SIN ASIGNAR ]";
        public int PuntoVentaId { get; set; }
        // 🌟 2. DATOS DEL COMPROBANTE
        public string DocumentoExcel { get; set; } = string.Empty; // BOLETA / FACTURA
        public string Serie { get; set; } = string.Empty;          // B001, BA01, FA01
        public string Numero { get; set; } = string.Empty;         // 0001739 (D7)
        public DateTime Fecha { get; set; } = DateTime.Today;

        // 🌟 3. CLIENTE COMODÍN (Se descartan los nombres particulares del Excel)
        public int? CompradorId { get; set; }
        public string RazonSocialExcel { get; set; } = string.Empty;
        public string RazonSocialSistema { get; set; } = "CLIENTES VARIOS"; // CLIENTES VARIOS o CLIENTES VARIOS FACTURACION
        public string ClienteNumeroDoc { get; set; } = "00000000";

        // Colegio / Institución referencial
        public string ClienteExcel { get; set; } = string.Empty;
        public int? ColegioSistemaId { get; set; }
        public string ClienteSistema { get; set; } = string.Empty;

        // 🌟 4. MONEDA, CONDICIÓN PRINCIPAL Y TOTALES
        public int MonedaId { get; set; } = 1;
        public string Moneda { get; set; } = "SOLES";
        public int? CondicionPagoId { get; set; }
        public string CondicionPagoNombre { get; set; } = "EFECTIVO";
        public decimal ImporteTotal => Total;

        public decimal Afecto { get; set; }
        public decimal Exonerado { get; set; }
        public decimal IGV { get; set; }
        public decimal MontoDelivery { get; set; }
        public decimal Total { get; set; }

        // 🌟 5. SEMÁFORO DE VALIDACIÓN
        public bool EsValido { get; set; } = true;
        public string MensajeError { get; set; } = string.Empty;

        // 🌟 6. COLECCIONES HIJAS
        public List<ImportacionDetalleDTO> Detalles { get; set; } = new();
        public List<ImportacionPagoDetalleDTO> PagosDesglosados { get; set; } = new();
    }

    /// <summary>
    /// DTO para la Grilla Inferior Izquierda: Artículos agrupados por comprobante
    /// </summary>
    public class ImportacionDetalleDTO
    {
        public int Linea { get; set; }
        public string DescripcionExcel { get; set; } = string.Empty;

        public int? ProductoSistemaId { get; set; }
        public string DescripcionSistema { get; set; } = string.Empty;
        public string AbreviaturaOficial { get; set; } = string.Empty; // Ej. "LMA4 C26-V-V"

        public string UnidadMedida { get; set; } = "UND";
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Importe { get; set; }

        public bool EsValido { get; set; } = true;
        public string MensajeError { get; set; } = string.Empty;

        public List<ImportacionCodigoDTO> Codigos { get; set; } = new();
    }

    /// <summary>
    /// DTO para la Grilla Inferior Derecha: Trazabilidad individual de códigos de Kárdex
    /// </summary>
    public class ImportacionCodigoDTO
    {
        // Código crudo tal como vino en el Excel (Ej. "LMA4-19983", "MAT6-1955")
        public string CodigoExcel { get; set; } = string.Empty;

        // Correlativo numérico limpio extraído (Ej. 19983, 1955)
        public int CorrelativoExtraido { get; set; }

        // Código oficial reconstruido con la abreviatura de tu sistema (Ej. "LMA4 C26-V-V-0019983")
        public string CodigoSistema { get; set; } = string.Empty;
        public string CodigoMostrar => !string.IsNullOrWhiteSpace(CodigoSistema) ? CodigoSistema : CodigoExcel;

        public int Cantidad { get; set; } = 1;
        public int? CodigoCreadoId { get; set; }
        public int? MovimientoKardexId { get; set; }

        public bool EsValido { get; set; } = true;
        public string MensajeValidacion { get; set; } = "PENDIENTE";
    }

    /// <summary>
    /// DTO para el Desglose Multicanal de Pagos (Yape, Efectivo, BCP, BBVA, etc.)
    /// </summary>
    public class ImportacionPagoDetalleDTO
    {
        public int MedioPagoId { get; set; }
        public string MedioPagoNombre { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string? NumeroOperacion { get; set; }
    }
}