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

    /// <summary>Códigos de demo que no son eventos del catálogo.</summary>
    public static readonly IReadOnlyList<string> CodigosDemo =
        ["recomendacion", .. Inactividad.Niveles.Select(n => $"inactividad-{n.Dias}")];

    /// <summary>
    /// Envía ya mismo a todos los usuarios un evento del catálogo (para la demo o para eventos sin
    /// fecha, como la entrada a clases del TEC), una recomendación al azar (<c>recomendacion</c>) o un
    /// aviso de inactividad (<c>inactividad-1</c>, <c>-7</c>, <c>-30</c>). No es idempotente: cada
    /// llamada envía de nuevo. No toca la recomendación del día ni el estado de inactividad real.
    /// </summary>
    public async Task<int> EnviarEventoAhoraAsync(string codigo, CancellationToken ct)
    {
        var (tipo, texto) = Resolver(codigo);
        var usuarios = await actividad.ListarAsync(ct);
        var id = $"demo-{codigo}-{Guid.NewGuid().ToString("N")[..12]}";
        var enviadas = 0;
        foreach (var u in usuarios)
        {
            var contenido = texto ?? Recomendaciones.Todas[Random.Shared.Next(Recomendaciones.Todas.Count)];
            if (await notificaciones.EmitirSiNuevaAsync(Notificacion.Desde(contenido, id, u.UsuarioId, tipo,
                    notificaciones.AhoraMs()), ct))
                enviadas++;
        }
        logger.LogInformation("{Codigo} enviado a mano a {Cantidad} usuarios", codigo, enviadas);
        return enviadas;
    }

    // texto null = una recomendación al azar distinta por usuario.
    private static (string Tipo, Contenido? Texto) Resolver(string codigo)
    {
        if (codigo == "recomendacion") return (TiposNotificacion.Recomendacion, null);
        foreach (var (dias, texto) in Inactividad.Niveles)
            if (codigo == $"inactividad-{dias}") return (TiposNotificacion.Inactividad, texto);
        var e = Eventos.Buscar(codigo) ?? throw new EventoNoEncontradoException(codigo);
        return (e.Tipo, e.Texto);
    }
}
