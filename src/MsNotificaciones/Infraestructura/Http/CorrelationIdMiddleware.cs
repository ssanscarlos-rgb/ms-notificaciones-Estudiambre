using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MsNotificaciones.Infraestructura.Http;

/// <summary>
/// Toma <c>X-Correlation-Id</c> (lo genera APIM) o crea uno, lo devuelve en la respuesta y lo
/// mete en el scope de los logs, así cada línea JSON de esta petición lleva <c>correlationId</c>.
/// También deja una línea de log por petición con método, ruta, estado y duración.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";
    private const string ClaveItems = "correlationId";

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$")]
    private static partial Regex FormatoValido();

    public static string Obtener(HttpContext ctx) => ctx.Items[ClaveItems] as string ?? ctx.TraceIdentifier;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var recibido = ctx.Request.Headers[Header].ToString();
        var correlationId = FormatoValido().IsMatch(recibido) ? recibido : Guid.NewGuid().ToString();

        ctx.Items[ClaveItems] = correlationId;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope("correlationId:{correlationId}", correlationId))
        {
            var reloj = Stopwatch.StartNew();
            try
            {
                await next(ctx);
            }
            finally
            {
                if (!ctx.Request.Path.StartsWithSegments("/health"))
                    logger.LogInformation("{Method} {Path} -> {StatusCode} en {ElapsedMs} ms",
                        ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, reloj.ElapsedMilliseconds);
            }
        }
    }
}
