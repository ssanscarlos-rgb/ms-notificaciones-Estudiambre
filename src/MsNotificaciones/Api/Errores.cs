using MsNotificaciones.Infraestructura.Http;

namespace MsNotificaciones.Api;

/// <summary>Formato de error común del proyecto: <c>{status, message, correlationId}</c>.</summary>
public sealed record ErrorRespuesta(int Status, string Message, string CorrelationId);

public static class Errores
{
    public static IResult Resultado(HttpContext ctx, int status, string message) =>
        Results.Json(new ErrorRespuesta(status, message, CorrelationIdMiddleware.Obtener(ctx)), statusCode: status);

    public static Task EscribirAsync(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new ErrorRespuesta(status, message, CorrelationIdMiddleware.Obtener(ctx)));
    }

    public static string MensajePorDefecto(int status) => status switch
    {
        400 => "Solicitud inválida.",
        401 => "No autenticado.",
        403 => "Acceso denegado.",
        404 => "Recurso no encontrado.",
        405 => "Método no permitido.",
        503 => "Servicio no disponible.",
        _ => "Error.",
    };
}
