using System;
using System.Collections.Generic;

namespace AplicativoDeAlmacen.Models.Models
{
    // Representa una sola fila en el DataGrid de Kárdex Físico
    public class KardexFisicoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RazonSocialUbicacion { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;
        public decimal CostoUnitario { get; set; }

        // 🌟 Identificación de Almacén por Fila
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        // Columnas desglosadas originales (NO SE TOCAN NI ELIMINAN)
        public decimal IngresoNormal { get; set; }
        public decimal IngresoDevolucion { get; set; }
        public decimal SalidaNormal { get; set; }
        public decimal SalidaDevolucion { get; set; }

        public decimal SaldoFinal { get; set; }

        // Indica si el movimiento fue anulado
        public bool IsAnulado { get; set; }

        // 🌟 CAMPOS AÑADIDOS PARA EL KÁRDEX UNIFICADO (SIN BORRAR NADA)
        public decimal Ingreso { get; set; }
        public decimal Salida { get; set; }
        public decimal SaldoAcumulado { get; set; }

        // 🌟 CAMPOS DE AUDITORÍA DINÁMICOS
        public DateTime? CreatedAt { get; set; }
        public string UsuarioCreador { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        public string? UsuarioModificador { get; set; }
    }

    // Representa el reporte completo con sus totales
    public class KardexFisicoReporte
    {
        // 🌟 Información del Almacén Filtrado
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        public List<KardexFisicoItem> Detalles { get; set; } = new List<KardexFisicoItem>();
        public List<ConsultaCodigoItem> Codigos { get; set; } = new List<ConsultaCodigoItem>();

        // Totales originales de Entradas y Salidas
        public decimal TotalIngresos { get; set; }
        public decimal TotalDevIngresos { get; set; }

        public decimal TotalSalidas { get; set; }
        public decimal TotalDevSalidas { get; set; }
        public string NumeroRegistro { get; set; } = string.Empty;

        

        // 🌟 CAMPOS AÑADIDOS PARA LAS NUEVAS 5 TARJETAS CONTABLES
        public decimal StockInicial { get; set; }
        public decimal TotalDevoluciones { get; set; }
        public decimal SalidasFijas => TotalSalidas - TotalDevoluciones;
        public decimal StockFinal { get; set; }
    }

    // Item de Kárdex Valorizado (ORIGINAL CON CAMPOS DE ALMACÉN)
    public class KardexValorizadoItem
    {
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RazonSocialUbicacion { get; set; } = string.Empty;
        public string Guia { get; set; } = string.Empty;

        // 🌟 Identificación de Almacén por Fila
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        // Columnas Valorizadas
        public decimal CostoUnitario { get; set; }
        public decimal CostoPromedio { get; set; }

        // Columnas Físicas
        public decimal IngresoFisico { get; set; }
        public decimal SalidaFisico { get; set; }
        public decimal SaldoFisico { get; set; }

        // Totales Monetarios
        public decimal IngresoValorado { get; set; }
        public decimal SalidaValorado { get; set; }
        public decimal SaldoValorado { get; set; }
        public bool IsAnulado { get; set; }

        // 🌟 CAMPOS DE AUDITORÍA DINÁMICOS
        public DateTime? CreatedAt { get; set; }
        public string UsuarioCreador { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        public string? UsuarioModificador { get; set; }
    }

    // Reporte Kárdex Valorizado Completo (ORIGINAL)
    public class KardexValorizadoReporte
    {
        // 🌟 Información del Almacén Filtrado
        public int AlmacenId { get; set; }
        public string AlmacenNombre { get; set; } = string.Empty;

        public List<KardexValorizadoItem> Detalles { get; set; } = new List<KardexValorizadoItem>();

        // Totales Finales
        public decimal TotalIngresoFisico { get; set; }
        public decimal TotalSalidaFisico { get; set; }
        public decimal StockFinalFisico { get; set; }

        public decimal TotalIngresoValorado { get; set; }
        public decimal TotalSalidaValorado { get; set; }
        public decimal SaldoFinalValorado { get; set; }
    }

    public class MatrizKardexItemDTO
    {
        public int MovimientoId { get; set; }
        public int BloqueTipo { get; set; } // 1: Ingreso, 2: Salida, 3: Devolución
        public string OrdenDocumento { get; set; } = string.Empty; // Nº Registro
        public string NumeroGuia { get; set; } = string.Empty;     // Nº Guía
        public int OrigenAlmacenId { get; set; }                   // 👈 ID numérico de la sede de origen
        public string OrigenAlmacen { get; set; } = "Trujillo";    // Nombre visible
        public DateTime Fecha { get; set; }
        public int ProductoId { get; set; }
        public decimal Cantidad { get; set; }
    }

    public class ProductoColumnaDTO
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int NivelId { get; set; }
        public string NivelNombre { get; set; } = "INICIAL";
        public int GradoId { get; set; }
        public string GradoNombre { get; set; } = "";
        public string FamiliaNombre { get; set; } = "GENERAL";
        public string TipoEdicion { get; set; } = "G"; // "G" = Guía, "V" = Venta
    }

    public class UbicacionMatrizDTO
    {
        public int UbicacionId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int TipoUbicacionId { get; set; } // 1: Almacén, 2: Punto Venta, 3: Promotoría, 4: Distribuidor
        public string TipoUbicacionNombre { get; set; } = string.Empty;
        public List<MatrizKardexItemDTO> Movimientos { get; set; } = new List<MatrizKardexItemDTO>();
    }
}