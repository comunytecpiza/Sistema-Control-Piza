using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AplicativoDeAlmacen.Models.Facturación
{
    // 1. DTO para la Grilla Superior (Cabecera)
    using System;
    using System.Collections.Generic;

    namespace AplicativoDeAlmacen.Models.Facturación
    {
        public class ImportacionCabeceraDTO
        {
            public string DocumentoExcel { get; set; } = string.Empty;
            public string Serie { get; set; } = string.Empty;
            public string Numero { get; set; } = string.Empty;
            public DateTime Fecha { get; set; }

            public string RazonSocialExcel { get; set; } = string.Empty;
            public int? PagadorSistemaId { get; set; }
            public string RazonSocialSistema { get; set; } = string.Empty;

            public string ClienteExcel { get; set; } = string.Empty; // Institucion
            public int? ColegioSistemaId { get; set; }
            public string ClienteSistema { get; set; } = string.Empty;

            public string Moneda { get; set; } = string.Empty;
            public decimal Afecto { get; set; }
            public decimal Exonerado { get; set; }
            public decimal IGV { get; set; }
            public decimal Total { get; set; }

            public bool EsValido { get; set; } = true;
            public string MensajeError { get; set; } = string.Empty;

            public List<ImportacionDetalleDTO> Detalles { get; set; } = new();
        }

        public class ImportacionDetalleDTO
        {
            public string DescripcionExcel { get; set; } = string.Empty;

            public int? ProductoSistemaId { get; set; }
            public string DescripcionSistema { get; set; } = string.Empty;

            public string UnidadMedida { get; set; } = "UND";
            public int Cantidad { get; set; }
            public decimal PrecioUnitario { get; set; }
            public decimal Importe { get; set; }

            public bool EsValido { get; set; } = true;

            public List<ImportacionCodigoDTO> Codigos { get; set; } = new();
        }

        public class ImportacionCodigoDTO
        {
            public string CodigoExcel { get; set; } = string.Empty;

            public int? CodigoCreadoId { get; set; }
            public int? MovimientoKardexId { get; set; }

            public bool EsValido { get; set; } = true;
            public string Error { get; set; } = string.Empty;
        }
    }
}
