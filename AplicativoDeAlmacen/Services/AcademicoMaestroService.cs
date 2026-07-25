using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models;
using AplicativoDeAlmacen.Data;

namespace AplicativoDeAlmacen.Services
{
    // DTOs exclusivos para aplanar la vista del DataGrid Académico
    public class VistaGradoCurso
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public int NivelId { get; set; }
        public string NivelNombre { get; set; }
    }

    public class AcademicoMaestroService
    {
        private readonly DataConnection.DatabaseConnection _database;

        public AcademicoMaestroService()
        {
            _database = new DataConnection.DatabaseConnection();
        }

        // =========================================================================
        // 1. MANTENIMIENTO DE NIVELES (Tabla Padre)
        // =========================================================================
        public async Task<List<Nivel>> ObtenerNivelesAsync()
        {
            var lista = new List<Nivel>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, nombre FROM niveles ORDER BY id ASC");

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new Nivel { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
            }
            return lista;
        }

        public async Task GuardarNivelAsync(Nivel nivel)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            bool esEdicion = nivel.Id > 0;
            string queryRaw = esEdicion
                ? "UPDATE niveles SET nombre = @Nombre WHERE id = @Id"
                : "INSERT INTO niveles (nombre) VALUES (@Nombre)";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

            AgregarParametro(cmd, "@Nombre", nivel.Nombre);
            if (esEdicion) AgregarParametro(cmd, "@Id", nivel.Id);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task EliminarNivelAsync(int id)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM niveles WHERE id = @Id");
            AgregarParametro(cmd, "@Id", id);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        // =========================================================================
        // 2. MANTENIMIENTO DE GRADOS Y CURSOS (Tablas Hijas)
        // =========================================================================
        public async Task<List<VistaGradoCurso>> ObtenerHijosAsync(string tablaBd)
        {
            var lista = new List<VistaGradoCurso>();
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            // Usamos LEFT JOIN para traer el nombre del Nivel al que pertenecen
            string queryRaw = $@"
                SELECT h.id, h.nombre, h.nivel_id, n.nombre AS nivel_nombre 
                FROM {tablaBd} h
                LEFT JOIN niveles n ON h.nivel_id = n.id
                ORDER BY n.id ASC, h.nombre ASC";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

            using var reader = await ((DbCommand)cmd).ExecuteReaderAsync();
            while (await ((DbDataReader)reader).ReadAsync())
            {
                lista.Add(new VistaGradoCurso
                {
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    NivelId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    NivelNombre = reader.IsDBNull(3) ? "SIN NIVEL" : reader.GetString(3)
                });
            }
            return lista;
        }

        public async Task GuardarHijoAsync(string tablaBd, VistaGradoCurso item)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            bool esEdicion = item.Id > 0;
            string queryRaw = esEdicion
                ? $"UPDATE {tablaBd} SET nombre = @Nombre, nivel_id = @NivelId WHERE id = @Id"
                : $"INSERT INTO {tablaBd} (nombre, nivel_id) VALUES (@Nombre, @NivelId)";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

            AgregarParametro(cmd, "@Nombre", item.Nombre);
            AgregarParametro(cmd, "@NivelId", item.NivelId);
            if (esEdicion) AgregarParametro(cmd, "@Id", item.Id);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        public async Task EliminarHijoAsync(string tablaBd, int id)
        {
            using var conn = _database.GetConnection();
            await ((DbConnection)conn).OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = QueryAdapter.FormatearConsulta($"DELETE FROM {tablaBd} WHERE id = @Id");
            AgregarParametro(cmd, "@Id", id);

            await ((DbCommand)cmd).ExecuteNonQueryAsync();
        }

        // =========================================================================
        // HELPER
        // =========================================================================
        private void AgregarParametro(IDbCommand cmd, string nombre, object valor)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}