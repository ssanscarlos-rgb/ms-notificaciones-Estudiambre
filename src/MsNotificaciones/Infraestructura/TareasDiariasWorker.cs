using MsNotificaciones.Aplicacion;

namespace MsNotificaciones.Infraestructura;

/// <summary>
/// Respaldo del cron: mientras el App Service está despierto, cada 15 minutos revisa si las tareas
/// de hoy ya corrieron en este proceso y, si no, las corre. Como las tareas son idempotentes, que
/// corran aquí y también por el cron no duplica nada. En el plan F1 la app se duerme sin tráfico,
/// por eso el disparo principal es el cron de GitHub Actions (que además la despierta).
/// </summary>
public sealed class TareasDiariasWorker(IServiceProvider servicios, ILogger<TareasDiariasWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);
    private DateOnly? _ultimaFecha;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tareas = servicios.GetRequiredService<TareasService>();
                var hoy = tareas.HoyEnCostaRica();
                if (_ultimaFecha != hoy)
                {
                    await tareas.EjecutarDiariasAsync(ct);
                    _ultimaFecha = hoy;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló la ejecución de las tareas diarias; se reintenta en {Minutos} min", Intervalo.TotalMinutes);
            }

            try { await Task.Delay(Intervalo, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
