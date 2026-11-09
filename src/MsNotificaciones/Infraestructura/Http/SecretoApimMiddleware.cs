using System.Security.Cryptography;
using System.Text;
using MsNotificaciones.Api;

namespace MsNotificaciones.Infraestructura.Http;

/// <summary>
/// Alternativa al certificado de cliente en planes que no lo soportan (F1): APIM agrega el header
/// <c>X-Apim-Secret</c> con un valor que solo APIM y este servicio conocen (<c>APIM_SHARED_SECRET</c>).
/// Una llamada directa al App Service sin ese header recibe 403. <c>/health</c> y <c>/swagger</c> quedan fuera.
/// Si <c>APIM_SHARED_SECRET</c> no está configurado, no se exige (con aviso en el log fuera de Development).
/// </summary>
public sealed class SecretoApimMiddleware(
    RequestDelegate next,
    OpcionesServicio opciones,
    IHostEnvironment entorno,
    ILogger<SecretoApimMiddleware> logger)
{
    public const string Header = "X-Apim-Secret";

    private readonly byte[]? _secreto = Preparar(opciones, entorno, logger);

    private static byte[]? Preparar(OpcionesServicio opciones, IHostEnvironment entorno, ILogger logger)
    {
        if (opciones.ApimSharedSecret is { } s) return Encoding.UTF8.GetBytes(s);
        if (!entorno.IsDevelopment())
            logger.LogWarning("APIM_SHARED_SECRET no está configurado: se aceptan llamadas directas al App Service");
        return null;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var ruta = ctx.Request.Path;
        if (_secreto is null
            || ruta.StartsWithSegments("/health")
            || ruta.StartsWithSegments("/swagger")
            || HttpMethods.IsOptions(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var recibido = Encoding.UTF8.GetBytes(ctx.Request.Headers[Header].ToString());
        // Comparación en tiempo constante: no revela cuántos caracteres coinciden.
        if (!CryptographicOperations.FixedTimeEquals(recibido, _secreto))
        {
            logger.LogWarning("Petición rechazada: falta o no coincide {Header}", Header);
            await Errores.EscribirAsync(ctx, StatusCodes.Status403Forbidden, "Acceso permitido solo a través de APIM.");
            return;
        }

        await next(ctx);
    }
}
