using System.Text.Json;
using Microsoft.Extensions.Logging;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

/// <summary>Casos de uso del microservicio. Solo conoce los puertos, no Azure.</summary>
public sealed class NotificacionService(
    INotificacionRepositorio repositorio,
    INotificadorTiempoReal notificador,
    ISuscripcionesPushRepositorio suscripciones,
    IEnviadorPush push,
    OpcionesPush opcionesPush,
    TimeProvider tiempo,
    ILogger<NotificacionService> logger)
{
    public const int TamanoMaximo = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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
    public async Task<Notificacion> CrearAsync(string usuarioId, string? tipo, string? titulo, string? mensaje, string? categoria, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(titulo)) throw new SolicitudInvalidaException("El campo 'titulo' es obligatorio.");
        if (string.IsNullOrWhiteSpace(mensaje)) throw new SolicitudInvalidaException("El campo 'mensaje' es obligatorio.");
        if (titulo.Length > 120) throw new SolicitudInvalidaException("'titulo' admite hasta 120 caracteres.");
        if (mensaje.Length > 500) throw new SolicitudInvalidaException("'mensaje' admite hasta 500 caracteres.");
        if (categoria?.Length > 60) throw new SolicitudInvalidaException("'categoria' admite hasta 60 caracteres.");

        var notificacion = new Notificacion(
            Guid.NewGuid().ToString("N"),
            usuarioId,
            string.IsNullOrWhiteSpace(tipo) ? "GENERAL" : tipo.Trim(),
            titulo.Trim(),
            mensaje.Trim(),
            Leida: false,
            AhoraMs(),
            string.IsNullOrWhiteSpace(categoria) ? null : categoria.Trim());

        await repositorio.GuardarAsync(notificacion, ct);
        await EmitirAsync(notificacion, ct);
        return notificacion;
    }

    public Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, bool? leida, CancellationToken ct)
    {
        if (page < 0) throw new SolicitudInvalidaException("'page' debe ser 0 o mayor.");
        if (size is < 1 or > TamanoMaximo) throw new SolicitudInvalidaException($"'size' debe estar entre 1 y {TamanoMaximo}.");
        return repositorio.ListarAsync(usuarioId, page, size, leida, ct);
    }

    public Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct) =>
        repositorio.MarcarLeidaAsync(usuarioId, id, ct);

    /// <summary>Idempotente: si no existe (o ya se borró), no es error.</summary>
    public async Task EliminarAsync(string usuarioId, string id, CancellationToken ct)
    {
        if (!await repositorio.EliminarAsync(usuarioId, id, ct))
            logger.LogInformation("DELETE de la notificación {Id}: no existía para este usuario", id);
    }

    public Task<Negociacion> NegociarAsync(string usuarioId, CancellationToken ct)
    {
        if (!notificador.Disponible) throw new TiempoRealNoDisponibleException();
        return notificador.NegociarAsync(usuarioId, ct);
    }

    // ---- Web Push ----

    public string ClavePublicaPush() =>
        push is { Disponible: true, ClavePublica: { } clave } ? clave : throw new PushNoDisponibleException();

    /// <summary>Guarda (o actualiza) la suscripción de este navegador para el usuario.</summary>
    public Task SuscribirPushAsync(string usuarioId, string? endpoint, string? p256dh, string? auth, CancellationToken ct)
    {
        if (!push.Disponible) throw new PushNoDisponibleException();
        ValidadorSuscripcion.Validar(endpoint, p256dh, auth, opcionesPush.PermitirEndpointsLocales);
        return suscripciones.GuardarAsync(
            new SuscripcionPush(usuarioId, endpoint!.Trim(), p256dh!.Trim().TrimEnd('='), auth!.Trim().TrimEnd('=')), ct);
    }

    /// <summary>Al desactivar los avisos o cerrar sesión. Idempotente.</summary>
    public Task DarDeBajaPushAsync(string usuarioId, string? endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new SolicitudInvalidaException("El campo 'endpoint' es obligatorio.");
        return suscripciones.EliminarAsync(usuarioId, endpoint.Trim(), ct);
    }

    // La notificación ya quedó guardada: si el envío en vivo o el push fallan, el usuario la ve al
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

        await EnviarPushAsync(notificacion, ct);
    }

    // Un push por cada navegador suscrito del usuario. Llega aunque la app esté cerrada.
    private async Task EnviarPushAsync(Notificacion n, CancellationToken ct)
    {
        if (!push.Disponible) return;
        try
        {
            var destinos = await suscripciones.ListarAsync(n.UsuarioId, ct);
            if (destinos.Count == 0) return;

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                n.Id, n.Tipo, n.Titulo, n.Mensaje, n.Categoria, n.Fecha,
            }, Json);

            foreach (var s in destinos)
            {
                var resultado = await push.EnviarAsync(s, payload, ct);
                if (resultado == ResultadoPush.Expirada)
                {
                    await suscripciones.EliminarAsync(s.UsuarioId, s.Endpoint, ct);
                    logger.LogInformation("Suscripción push expirada; se elimina");
                }
            }
            logger.LogInformation("Push de la notificación {Id} enviado a {Cantidad} navegador(es)", n.Id, destinos.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo enviar el push de la notificación {Id}", n.Id);
        }
    }
}
