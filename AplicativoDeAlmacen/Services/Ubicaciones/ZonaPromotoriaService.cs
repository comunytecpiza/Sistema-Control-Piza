using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services.Ubicaciones
{
    public class ZonaPromotoriaService
    {
        private readonly DatabaseConnection _database;

        public ZonaPromotoriaService()
        {
            _database = new DatabaseConnection();
        }

        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public async Task<List<Localidad>> ObtenerLocalidadesAsync()
        {
            var lista = new List<Localidad>();
            string query = "SELECT id, nombre FROM localidades ORDER BY nombre";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new Localidad
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Nombre = reader["nombre"]?.ToString() ?? string.Empty
                });
            }

            return lista;
        }

        public async Task<List<ZonaPromotoria>> ObtenerZonasPorLocalidadAsync(int localidadId)
        {
            var lista = new List<ZonaPromotoria>();
            string query = @"
                SELECT id, descripcion, localidad_id
                FROM zona_promotoria
                WHERE localidad_id = @LocalidadId
                ORDER BY descripcion ASC";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            AgregarParametro(cmd, "@LocalidadId", localidadId);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new ZonaPromotoria
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Descripcion = reader["descripcion"]?.ToString() ?? string.Empty,
                    LocalidadId = Convert.ToInt32(reader["localidad_id"])
                });
            }

            return lista;
        }

        public async Task RegistrarZonaAsync(string descripcion, int localidadId)
        {
            string query = @"
                INSERT INTO zona_promotoria (descripcion, localidad_id)
                VALUES (@Descripcion, @LocalidadId)";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            AgregarParametro(cmd, "@Descripcion", descripcion.Trim());
            AgregarParametro(cmd, "@LocalidadId", localidadId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task ActualizarZonaAsync(int zonaId, string nuevaDescripcion)
        {
            string query = @"
                UPDATE zona_promotoria 
                SET descripcion = @Descripcion 
                WHERE id = @ZonaId";

            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            AgregarParametro(cmd, "@Descripcion", nuevaDescripcion.Trim());
            AgregarParametro(cmd, "@ZonaId", zonaId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task EliminarZonaAsync(int zonaId)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            // 🛡️ Integridad Referencial: Verificar si está asociada a Clientes/Personas Comerciales
            string checkQuery = "SELECT COUNT(*) FROM personas_comerciales WHERE zona_promotoria_id = @ZonaId";
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = checkQuery;
                AgregarParametro(checkCmd, "@ZonaId", zonaId);
                int clientesUsando = Convert.ToInt32(await ((DbCommand)checkCmd).ExecuteScalarAsync());

                if (clientesUsando > 0)
                {
                    throw new Exception($"No se puede eliminar la zona porque se encuentra asignada a {clientesUsando} cliente(s) o personas comerciales.");
                }
            }

            string query = "DELETE FROM zona_promotoria WHERE id = @ZonaId";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            AgregarParametro(cmd, "@ZonaId", zonaId);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }
    }
}