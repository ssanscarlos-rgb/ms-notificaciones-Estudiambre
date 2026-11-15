using Microsoft.Extensions.Logging;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

public sealed record ResumenTareas(string Fecha, int Usuarios, int Recomendaciones, int Inactividad, int Eventos);

/// <summary>
/// Notificaciones que no nacen de un evento sino del calendario: la recomendación del día, los
/// recordatorios por inactividad y los eventos con fecha. Las dispara un cron a medianoche (hora de
/// Costa Rica) o un temporizador interno. Todo es <b>idempotente</b>: los ids son fijos por usuario y
/// día, así que si la tarea corre dos veces el mismo día, la segunda no envía nada.
/// </summary>
public sealed class TareasService(
    NotificacionService notificaciones,
    IActividadRepositorio actividad,
    TimeProvider tiempo,
    ILogger<TareasService> logger)
{
    /// <summary>Costa Rica: UTC−6 todo el año (sin horario de verano).</summary>
    public static readonly TimeSpan ZonaCostaRica = TimeSpan.FromHours(-6);

    private const long MsPorDia = 86_400_000L;

    public DateOnly HoyEnCostaRica() => DateOnly.FromDateTime(tiempo.GetUtcNow().ToOffset(ZonaCostaRica).DateTime);

    public async Task<ResumenTareas> EjecutarDiariasAsync(CancellationToken ct)
    {
        var ahora = tiempo.GetUtcNow();
        var hoy = HoyEnCostaRica();
        var medianoche = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), ZonaCostaRica).ToUnixTimeMilliseconds();
        var usuarios = await actividad.ListarAsync(ct);
        var eventosDeHoy = Eventos.Todos.Where(e => e.Fecha == hoy).ToList();
        int recomendaciones = 0, inactivos = 0, eventos = 0;

        foreach (var u in usuarios)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // 1. Recomendación del día (una por usuario y día).
                var consejo = Recomendaciones.DelDia(u.UsuarioId, hoy);
                if (await notificaciones.EmitirSiNuevaAsync(Notificacion.Desde(consejo, $"rec-{hoy:yyyyMMdd}", u.UsuarioId,
                        TiposNotificacion.Recomendacion, medianoche), ct))
                    recomendaciones++;

                // 2. Inactividad: solo el nivel nuevo más alto (1 día, 1 semana, 1 mes), una vez por racha.
                var dias = (ahora.ToUnixTimeMilliseconds() - u.UltimaActividad) / (double)MsPorDia;
                var nivel = Inactividad.NivelPara(dias);
                if (nivel > u.NivelInactividad)
                {
                    var (umbral, texto) = Inactividad.Niveles[nivel - 1];
                    var n = Notificacion.Desde(texto, $"inactividad-{nivel}-{u.UltimaActividad}", u.UsuarioId,
                        TiposNotificacion.Inactividad, u.UltimaActividad + umbral * MsPorDia);
                    if (await notificaciones.EmitirSiNuevaAsync(n, ct)) inactivos++;
                    await actividad.MarcarNivelInactividadAsync(u.UsuarioId, nivel, ct);
                }

                // 3. Eventos de hoy (estrenos, Black Friday…).
                foreach (var e in eventosDeHoy)
                    if (await notificaciones.EmitirSiNuevaAsync(Notificacion.Desde(e.Texto, $"evento-{e.Codigo}", u.UsuarioId,
                            e.Tipo, medianoche), ct))
                        eventos++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un usuario con problemas no frena al resto.
                logger.LogWarning(ex, "Tareas diarias: falló un usuario");
            }
        }

        var resumen = new ResumenTareas(hoy.ToString("yyyy-MM-dd"), usuarios.Count, recomendaciones, inactivos, eventos);
        logger.LogInformation("Tareas diarias {Fecha}: {Usuarios} usuarios, {Recomendaciones} recomendaciones, {Inactividad} de inactividad, {Eventos} de eventos",
            resumen.Fecha, resumen.Usuarios, resumen.Recomendaciones, resumen.Inactividad, resumen.Eventos);
        return resumen;
    }

    /// <summary>
    /// Envía un evento del catálogo ya mismo a todos los usuarios (para la demo o para eventos sin
    /// fecha, como la entrada a clases del TEC). No es idempotente: cada llamada envía de nuevo.
    /// </summary>
    public async Task<int> EnviarEventoAhoraAsync(string codigo, CancellationToken ct)
    {
        var e = Eventos.Buscar(codigo) ?? throw new EventoNoEncontradoException(codigo);
        var usuarios = await actividad.ListarAsync(ct);
        var id = $"evento-{e.Codigo}-{Guid.NewGuid().ToString("N")[..12]}";
        var enviadas = 0;
        foreach (var u in usuarios)
            if (await notificaciones.EmitirSiNuevaAsync(Notificacion.Desde(e.Texto, id, u.UsuarioId, e.Tipo,
                    notificaciones.AhoraMs()), ct))
                enviadas++;
        logger.LogInformation("Evento {Codigo} enviado a mano a {Cantidad} usuarios", e.Codigo, enviadas);
        return enviadas;
    }
}
