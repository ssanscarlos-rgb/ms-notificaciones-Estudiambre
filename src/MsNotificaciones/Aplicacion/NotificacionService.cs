using Microsoft.Extensions.Logging;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

/// <summary>Casos de uso del microservicio. Solo conoce los puertos, no Azure.</summary>
public sealed class NotificacionService(
    INotificacionRepositorio repositorio,
    INotificadorTiempoReal notificador,
    TimeProvider tiempo,
    ILogger<NotificacionService> logger)
{
    public const int TamanoMaximo = 50;

    public bool TiempoRealDisponible => notificador.Disponible;

    public long AhoraMs() => tiempo.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Evento de la cola → notificación guardada → envío en vivo.</summary>
    public async Task<Notificacion> ProcesarEventoAsync(EventoPresupuesto evento, string idMensaje, CancellationToken ct)
    {
        var notificacion = Notificacion.DesdeEvento(evento, idMensaje);
        var esNueva = await repositorio.GuardarAsync(notificacion, ct);
        if (esNueva)
            await EmitirAsync(notificacion, ct);
        else
            logger.LogInformation("Mensaje {IdMensaje} ya procesado; no se repite la notificación", idMensaje);
        return notificacion;
    }

    /// <summary>Crea una notificación a mano (POST /notificaciones, útil para Postman y pruebas).</summary>
    public async Task<Notificacion> CrearAsync(string usuarioId, string? tipo, string? titulo, string? mensaje, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(titulo)) throw new SolicitudInvalidaException("El campo 'titulo' es obligatorio.");
        if (string.IsNullOrWhiteSpace(mensaje)) throw new SolicitudInvalidaException("El campo 'mensaje' es obligatorio.");
        if (titulo.Length > 120) throw new SolicitudInvalidaException("'titulo' admite hasta 120 caracteres.");
        if (mensaje.Length > 500) throw new SolicitudInvalidaException("'mensaje' admite hasta 500 caracteres.");

        var notificacion = new Notificacion(
            Guid.NewGuid().ToString("N"),
            usuarioId,
            string.IsNullOrWhiteSpace(tipo) ? "GENERAL" : tipo.Trim(),
            titulo.Trim(),
            mensaje.Trim(),
            Leida: false,
            AhoraMs());

        await repositorio.GuardarAsync(notificacion, ct);
        await EmitirAsync(notificacion, ct);
        return notificacion;
    }

    public Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, CancellationToken ct)
    {
        if (page < 0) throw new SolicitudInvalidaException("'page' debe ser 0 o mayor.");
        if (size is < 1 or > TamanoMaximo) throw new SolicitudInvalidaException($"'size' debe estar entre 1 y {TamanoMaximo}.");
        return repositorio.ListarAsync(usuarioId, page, size, ct);
    }

    public Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct) =>
        repositorio.MarcarLeidaAsync(usuarioId, id, ct);

    public Task<Negociacion> NegociarAsync(string usuarioId, CancellationToken ct)
    {
        if (!notificador.Disponible) throw new TiempoRealNoDisponibleException();
        return notificador.NegociarAsync(usuarioId, ct);
    }

    // La notificación ya quedó guardada: si el envío en vivo falla, el usuario la ve al
    // recargar la campana. Por eso no se propaga el error (ni se reintenta el mensaje).
    private async Task EmitirAsync(Notificacion notificacion, CancellationToken ct)
    {
        try
        {
            await notificador.EnviarAsync(notificacion, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo enviar en vivo la notificación {Id}", notificacion.Id);
        }
    }
}
