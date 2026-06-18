using System;

namespace AplicativoDeAlmacen.Services
{
    public static class EventBus
    {
        // ====================================================================
        // 📡 CANALES DE EVENTOS POR MÓDULO/ENTIDAD
        // ====================================================================

        // Módulos Principales (Almacén y Movimientos)
        public static event Action? OnProductosChanged;
        public static event Action? OnRegistroCodigosChanged;
        public static event Action? OnMovimientosChanged; // Kardex, Entradas, Salidas

        // Módulos de Catálogos y Entidades (Según tu estructura)
        public static event Action? OnPersonasComercialesChanged; // Clientes, Proveedores
        public static event Action? OnColeccionesChanged;
        public static event Action? OnTitulosChanged;
        public static event Action? OnAcademicoChanged; // Niveles, Grados, Cursos

        // Módulos de Ubicación y Geografía
        public static event Action? OnLocalidadesChanged; // Regiones, Provincias, Distritos
        public static event Action? OnUbicacionesChanged;
        public static event Action? OnZonasChanged;

        // Módulos de Configuración y Seguridad
        public static event Action? OnUsuariosChanged;
        public static event Action? OnRolesPermisosChanged;

        // Módulos Generales
        public static event Action? OnUnidadesMedidaChanged;
        public static event Action? OnCatalogosGeneralesChanged; // Para tipos de documento, estados, etc.

        // ====================================================================
        // 📢 DISPARADORES UNIVERSALES (Llamar a estos métodos para notificar)
        // ====================================================================

        public static void NotificarProductosChanged() => OnProductosChanged?.Invoke();
        public static void NotificarRegistroCodigosChanged() => OnRegistroCodigosChanged?.Invoke();
        public static void NotificarMovimientosChanged() => OnMovimientosChanged?.Invoke();

        public static void NotificarPersonasComercialesChanged() => OnPersonasComercialesChanged?.Invoke();
        public static void NotificarColeccionesChanged() => OnColeccionesChanged?.Invoke();
        public static void NotificarTitulosChanged() => OnTitulosChanged?.Invoke();
        public static void NotificarAcademicoChanged() => OnAcademicoChanged?.Invoke();

        public static void NotificarLocalidadesChanged() => OnLocalidadesChanged?.Invoke();
        public static void NotificarUbicacionesChanged() => OnUbicacionesChanged?.Invoke();
        public static void NotificarZonasChanged() => OnZonasChanged?.Invoke();

        public static void NotificarUsuariosChanged() => OnUsuariosChanged?.Invoke();
        public static void NotificarRolesPermisosChanged() => OnRolesPermisosChanged?.Invoke();

        public static void NotificarUnidadesMedidaChanged() => OnUnidadesMedidaChanged?.Invoke();
        public static void NotificarCatalogosGeneralesChanged() => OnCatalogosGeneralesChanged?.Invoke();
    }
}