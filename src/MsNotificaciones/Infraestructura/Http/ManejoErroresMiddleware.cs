using MsNotificaciones.Api;
using MsNotificaciones.Aplicacion;

namespace MsNotificaciones.Infraestructura.Http;

/// <summary>Convierte cualquier excepción en el JSON de error común. Nunca filtra detalles internos.</summary>
public sealed class ManejoErroresMiddleware(RequestDelegate next, ILogger<ManejoErroresMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // El cliente cerró la conexión; no hay a quién responder.
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            var (status, mensaje) = ex switch
            {
                SolicitudInvalidaException e => (400, e.Message),
                BadHttpRequestException e => (e.StatusCode, "Solicitud inválida: revisá el formato del body y de los parámetros."),
                TiempoRealNoDisponibleException e => (503, e.Message),
                _ => (500, "Error interno del servidor."),
            };

            if (status >= 500 && ex is not TiempoRealNoDisponibleException)
                logger.LogError(ex, "Error no controlado");
            else
                logger.LogInformation("Solicitud rechazada ({Status}): {Motivo}", status, ex.Message);

            ctx.Response.Clear();
            await Errores.EscribirAsync(ctx, status, mensaje);
        }
    }
}
