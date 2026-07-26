using AplicativoDeAlmacen.Data;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using System.Windows.Threading;
using static AplicativoDeAlmacen.Data.DataConnection;

namespace AplicativoDeAlmacen.Services
{
    public class SincronizadorLeroService
    {
        private readonly DatabaseConnection _dbConnHelper;
        private readonly DispatcherTimer _timer;
        private long _ultimoMovimientoId = 0;
        private bool _isChecking = false;

        public SincronizadorLeroService()
        {
            _dbConnHelper = new DatabaseConnection();

            // Revisa la BD cada 10 segundos
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += async (s, e) => await VerificarCambiosAsync();
        }

        public void Iniciar() => _timer.Start();
        public void Detener() => _timer.Stop();

        private async Task VerificarCambiosAsync()
        {
            if (_isChecking) return;
            _isChecking = true;

            try
            {
                using var conn = _dbConnHelper.GetConnection();
                var dbConn = (DbConnection)conn;
                await dbConn.OpenAsync();

                using var cmd = dbConn.CreateCommand();
                // 🌟 Consulta ligera sobre la tabla que YA existe (Cero cambios en la BD)
                cmd.CommandText = QueryAdapter.FormatearConsulta("SELECT COALESCE(MAX(id), 0) FROM movimientos");

                object result = await cmd.ExecuteScalarAsync();
                long maxIdRemoto = (result != null && result != DBNull.Value) ? Convert.ToInt64(result) : 0;

                if (_ultimoMovimientoId == 0)
                {
                    _ultimoMovimientoId = maxIdRemoto;
                }
                else if (maxIdRemoto > _ultimoMovimientoId)
                {
                    _ultimoMovimientoId = maxIdRemoto;

                    // 📢 ¡Alguien en otra PC guardó un movimiento! Avisamos a las pantallas
                    EventBus.NotificarMovimientosChanged();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync Engine] Error: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }
    }
}