using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using AplicativoDeAlmacen.Models.Models; // Aquí viven CatalogoBasico y MotivoProducto
using AplicativoDeAlmacen.Data;
using static AplicativoDeAlmacen.Data.DataConnection; // Para acceder a DatabaseConnection

namespace AplicativoDeAlmacen.Services
{
    public class CatalogoMaestroService
    {
        private readonly DatabaseConnection _database;

        public CatalogoMaestroService()
        {
            _database = new DatabaseConnection();
        }

        // =========================================================================
        // 1. MOTOR GENÉRICO PARA TABLAS SIMPLES (Id, Nombre/Descripción)
        // Sirve para: tipo_persona, categoria_producto, tipos_libro, tipo_ubicacion, etc.
        // =========================================================================

        public async Task<List<CatalogoBasico>> ObtenerCatalogoAsync(string tablaBd, string columnaTexto = "nombre")
        {
            var lista = new List<CatalogoBasico>();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                // Interceptamos el nombre de la tabla de forma segura
                string queryRaw = $"SELECT id, {columnaTexto} FROM {tablaBd} ORDER BY {columnaTexto} ASC";

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(new CatalogoBasico
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }

            return lista;
        }

        public async Task GuardarCatalogoAsync(string tablaBd, CatalogoBasico item, string columnaTexto = "nombre")
        {
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                bool esEdicion = item.Id > 0;
                string queryRaw = esEdicion
                    ? $"UPDATE {tablaBd} SET {columnaTexto} = @Nombre WHERE id = @Id"
                    : $"INSERT INTO {tablaBd} ({columnaTexto}) VALUES (@Nombre)";

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@Nombre", item.Nombre);
                    if (esEdicion) AgregarParametro(cmd, "@Id", item.Id);

                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
            }
        }

        public async Task EliminarCatalogoAsync(string tablaBd, int id)
        {
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta($"DELETE FROM {tablaBd} WHERE id = @Id");
                    AgregarParametro(cmd, "@Id", id);

                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
            }
        }

        // =========================================================================
        // 2. MÉTODOS ESPECÍFICOS PARA MOTIVOS DE MOVIMIENTO
        // (Tienen un campo extra llamado 'tipo_movimiento' crítico para el Kardex)
        // =========================================================================

        public async Task<List<MotivoProducto>> ObtenerMotivosAsync()
        {
            var lista = new List<MotivoProducto>();

            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT id, descripcion, tipo_movimiento FROM motivo_productos ORDER BY tipo_movimiento, descripcion");

                    using (IDataReader reader = await ((DbCommand)cmd).ExecuteReaderAsync())
                    {
                        while (await ((DbDataReader)reader).ReadAsync())
                        {
                            lista.Add(new MotivoProducto
                            {
                                Id = reader.GetInt32(0),
                                Descripcion = reader.GetString(1),
                                TipoMovimiento = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return lista;
        }

        public async Task GuardarMotivoAsync(MotivoProducto motivo)
        {
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                bool esEdicion = motivo.Id > 0;
                string queryRaw = esEdicion
                    ? "UPDATE motivo_productos SET descripcion = @Desc, tipo_movimiento = @Tipo WHERE id = @Id"
                    : "INSERT INTO motivo_productos (descripcion, tipo_movimiento) VALUES (@Desc, @Tipo)";

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta(queryRaw);

                    AgregarParametro(cmd, "@Desc", motivo.Descripcion);
                    AgregarParametro(cmd, "@Tipo", motivo.TipoMovimiento);
                    if (esEdicion) AgregarParametro(cmd, "@Id", motivo.Id);

                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
            }
        }

        public async Task EliminarMotivoAsync(int id)
        {
            using (IDbConnection conn = _database.GetConnection())
            {
                await ((DbConnection)conn).OpenAsync();

                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = QueryAdapter.FormatearConsulta("DELETE FROM motivo_productos WHERE id = @Id");
                    AgregarParametro(cmd, "@Id", id);

                    await ((DbCommand)cmd).ExecuteNonQueryAsync();
                }
            }
        }

        // =========================================================================
        // HELPER PARA INYECCIÓN DE PARÁMETROS LIMPIA Y SEGURA
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