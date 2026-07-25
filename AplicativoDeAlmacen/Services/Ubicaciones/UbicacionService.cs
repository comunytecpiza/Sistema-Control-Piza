using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Data;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Ubicaciones
{
    public class UbicacionService
    {
        private readonly DatabaseConnection _database;

        public UbicacionService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(IDbCommand cmd, string nombre, object? valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<Ubicacion>> ObtenerTodasAsync()
        {
            var lista = new List<Ubicacion>();

            string query = @"
            SELECT u.id, u.descripcion, u.direccion,
                   u.tipo_ubicacion_id, tu.nombre AS tipo_ubicacion_nombre,
                   u.localidad_id, l.nombre AS localidad_nombre,
                   u.estado_id, e.nombre AS estado_nombre,
                   u.departamento_id, d.nombre AS departamento_nombre,
                   u.provincia_id, p.nombre AS provincia_nombre,
                   u.distrito_id, di.nombre AS distrito_nombre
            FROM ubicaciones u
            LEFT JOIN tipo_ubicacion tu ON u.tipo_ubicacion_id = tu.id
            LEFT JOIN localidades l ON u.localidad_id = l.id
            LEFT JOIN departamentos d ON u.departamento_id = d.id
            LEFT JOIN provincias p ON u.provincia_id = p.id
            LEFT JOIN distritos di ON u.distrito_id = di.id
            LEFT JOIN estados e ON u.estado_id = e.id
            ORDER BY tu.id ASC, u.descripcion ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var u = new Ubicacion
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? string.Empty : reader.GetString(reader.GetOrdinal("descripcion")),
                    Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? string.Empty : reader.GetString(reader.GetOrdinal("direccion")),

                    TipoUbicacion = new TipoUbicacion
                    {
                        Id = reader.IsDBNull(reader.GetOrdinal("tipo_ubicacion_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("tipo_ubicacion_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("tipo_ubicacion_nombre")) ? "DESCONOCIDO" : reader.GetString(reader.GetOrdinal("tipo_ubicacion_nombre"))
                    },

                    Localidad = new Localidad
                    {
                        Id = reader.IsDBNull(reader.GetOrdinal("localidad_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("localidad_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("localidad_nombre")) ? "-" : reader.GetString(reader.GetOrdinal("localidad_nombre"))
                    },

                    Estado = new Estado
                    {
                        Id = reader.IsDBNull(reader.GetOrdinal("estado_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("estado_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("estado_nombre")) ? "DESCONOCIDO" : reader.GetString(reader.GetOrdinal("estado_nombre"))
                    }
                };

                if (!reader.IsDBNull(reader.GetOrdinal("departamento_id")))
                {
                    u.Departamento = new Departamento
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("departamento_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("departamento_nombre")) ? string.Empty : reader.GetString(reader.GetOrdinal("departamento_nombre"))
                    };
                }

                if (!reader.IsDBNull(reader.GetOrdinal("provincia_id")))
                {
                    u.Provincia = new Provincia
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("provincia_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("provincia_nombre")) ? string.Empty : reader.GetString(reader.GetOrdinal("provincia_nombre"))
                    };
                }

                if (!reader.IsDBNull(reader.GetOrdinal("distrito_id")))
                {
                    u.Distrito = new Distrito
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("distrito_id")),
                        Nombre = reader.IsDBNull(reader.GetOrdinal("distrito_nombre")) ? string.Empty : reader.GetString(reader.GetOrdinal("distrito_nombre"))
                    };
                }

                lista.Add(u);
            }

            return lista;
        }

        public async Task InsertarAsync(Ubicacion u)
        {
            string query = @"
            INSERT INTO ubicaciones
            (descripcion, tipo_ubicacion_id, localidad_id, direccion, departamento_id, provincia_id, distrito_id, estado_id)
            VALUES
            (@Descripcion, @TipoUbicacionId, @LocalidadId, @Direccion, @DepartamentoId, @ProvinciaId, @DistritoId, @EstadoId)";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            AgregarParametro(cmd, "@Descripcion", u.Descripcion?.Trim() ?? string.Empty);
            AgregarParametro(cmd, "@TipoUbicacionId", u.TipoUbicacion?.Id);
            AgregarParametro(cmd, "@LocalidadId", u.Localidad?.Id);
            AgregarParametro(cmd, "@Direccion", u.Direccion?.Trim() ?? string.Empty);
            AgregarParametro(cmd, "@DepartamentoId", u.Departamento?.Id);
            AgregarParametro(cmd, "@ProvinciaId", u.Provincia?.Id);
            AgregarParametro(cmd, "@DistritoId", u.Distrito?.Id);
            AgregarParametro(cmd, "@EstadoId", u.Estado?.Id ?? 1);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task ActualizarAsync(Ubicacion u)
        {
            string query = @"
            UPDATE ubicaciones
            SET descripcion = @Descripcion,
                tipo_ubicacion_id = @TipoUbicacionId,
                localidad_id = @LocalidadId,
                direccion = @Direccion,
                departamento_id = @DepartamentoId,
                provincia_id = @ProvinciaId,
                distrito_id = @DistritoId,
                estado_id = @EstadoId
            WHERE id = @Id";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            AgregarParametro(cmd, "@Id", u.Id);
            AgregarParametro(cmd, "@Descripcion", u.Descripcion?.Trim() ?? string.Empty);
            AgregarParametro(cmd, "@TipoUbicacionId", u.TipoUbicacion?.Id);
            AgregarParametro(cmd, "@LocalidadId", u.Localidad?.Id);
            AgregarParametro(cmd, "@Direccion", u.Direccion?.Trim() ?? string.Empty);
            AgregarParametro(cmd, "@DepartamentoId", u.Departamento?.Id);
            AgregarParametro(cmd, "@ProvinciaId", u.Provincia?.Id);
            AgregarParametro(cmd, "@DistritoId", u.Distrito?.Id);
            AgregarParametro(cmd, "@EstadoId", u.Estado?.Id ?? 1);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task EliminarAsync(int ubicacionId)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            // 🛡️ Integridad Referencial: Verificar si tiene movimientos registrados
            string checkQuery = "SELECT COUNT(*) FROM movimientos WHERE ubicacion_id = @UbicacionId";
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = QueryAdapter.FormatearConsulta(checkQuery);
                AgregarParametro(checkCmd, "@UbicacionId", ubicacionId);
                int movimientosRelacionados = Convert.ToInt32(await ((DbCommand)checkCmd).ExecuteScalarAsync());

                if (movimientosRelacionados > 0)
                {
                    throw new Exception($"No se puede eliminar la ubicación porque registra {movimientosRelacionados} movimiento(s) o transferencias de almacén asociadas.");
                }
            }

            string query = "DELETE FROM ubicaciones WHERE id = @UbicacionId";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@UbicacionId", ubicacionId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task<List<TipoUbicacion>> ObtenerTiposUbicacionAsync()
        {
            var lista = new List<TipoUbicacion>();
            string query = "SELECT id, nombre FROM tipo_ubicacion ORDER BY id ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new TipoUbicacion { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Localidad>> ObtenerLocalidadesAsync()
        {
            var lista = new List<Localidad>();
            string query = "SELECT id, nombre FROM localidades ORDER BY nombre ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Localidad { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Departamento>> ObtenerDepartamentosAsync()
        {
            var lista = new List<Departamento>();
            string query = "SELECT id, nombre FROM departamentos ORDER BY nombre ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Departamento { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Provincia>> ObtenerProvinciasAsync(int departamentoId)
        {
            var lista = new List<Provincia>();
            string query = "SELECT id, nombre FROM provincias WHERE departamento_id = @DepId ORDER BY nombre ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@DepId", departamentoId);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Provincia { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Distrito>> ObtenerDistritosAsync(int provinciaId)
        {
            var lista = new List<Distrito>();
            string query = "SELECT id, nombre FROM distritos WHERE provincia_id = @ProvId ORDER BY nombre ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);
            AgregarParametro(cmd, "@ProvId", provinciaId);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Distrito { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task<List<Estado>> ObtenerEstadosAsync()
        {
            var lista = new List<Estado>();
            string query = "SELECT id, nombre FROM estados ORDER BY nombre ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(query);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Estado { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        // Método auxiliar de compatibilidad para llamadas síncronas
        public List<Ubicacion> ObtenerTodas() => Task.Run(() => ObtenerTodasAsync()).Result;
        public List<Ubicacion> BuscarUbicaciones(string criterio)
        {
            var todas = ObtenerTodas();
            return todas.FindAll(x => x.Descripcion.Contains(criterio, StringComparison.OrdinalIgnoreCase));
        }

        public List<Ubicacion> BuscarUbicacionesPorNombre(string criterio)
        {
            var lista = new List<Ubicacion>();

            if (string.IsNullOrWhiteSpace(criterio))
                return lista;

            string query = @"
        SELECT id, descripcion, direccion 
        FROM ubicaciones 
        WHERE descripcion LIKE @criterio 
        ORDER BY descripcion ASC";

            try
            {
                using var conn = _database.GetConnection();
                var dbConn = (DbConnection)conn;
                if (dbConn.State != ConnectionState.Open) dbConn.Open();

                using var cmd = dbConn.CreateCommand();
                cmd.CommandText = QueryAdapter.FormatearConsulta(query);

                AgregarParametro(cmd, "@criterio", "%" + criterio.Trim() + "%");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lista.Add(new Ubicacion
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Descripcion = reader["descripcion"]?.ToString() ?? string.Empty,
                        Direccion = reader["direccion"]?.ToString() ?? string.Empty
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al buscar ubicaciones por nombre: {ex.Message}");
                return lista;
            }
        }
    }
}