using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;
using MsNotificaciones.Infraestructura;

namespace MsNotificaciones.Api;

/// <summary>
/// Tareas del sistema, sin usuario: las llama el cron de GitHub Actions directo al App Service, con el
/// mismo header secreto que pone APIM (<c>X-Apim-Secret</c>, lo valida <c>SecretoApimMiddleware</c>).
/// <b>No se publican en APIM</b>: si estuvieran ahí, cualquier usuario con sesión podría dispararlas.
/// </summary>
public static class TareasEndpoints
{
    public static IEndpointRouteBuilder MapTareas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/v1/tareas")
            .AddEndpointFilter<RequiereSecretoFilter>()
            .WithTags("Tareas programadas");

        grupo.MapPost("/diarias", (TareasService t, CancellationToken ct) => t.EjecutarDiariasAsync(ct))
            .Produces<ResumenTareas>();

        grupo.MapGet("/eventos", () => new
        {
            eventos = Eventos.Todos.Select(e => new
            {
                e.Codigo, Fecha = e.Fecha?.ToString("yyyy-MM-dd"), e.Tipo, e.Texto.Titulo, e.Texto.Mensaje,
            }),
            demo = TareasService.CodigosDemo,
        });

        grupo.MapPost("/eventos/{codigo}", async (string codigo, TareasService t, CancellationToken ct) =>
                Results.Ok(new { codigo, enviadas = await t.EnviarEventoAhoraAsync(codigo, ct) }))
            .Produces<ErrorRespuesta>(404);

        return app;
    }
}

/// <summary>
/// Fuera de Development, las tareas exigen que exista <c>APIM_SHARED_SECRET</c> (y por lo tanto que la
/// petición lo traiga). Sin secreto configurado quedarían abiertas a cualquiera: se rechazan.
/// </summary>
public sealed class RequiereSecretoFilter(OpcionesServicio opciones, IHostEnvironment entorno) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (opciones.ApimSharedSecret is null && !entorno.IsDevelopment())
            return Errores.Resultado(context.HttpContext, 403, "Las tareas requieren APIM_SHARED_SECRET configurado.");
        return await next(context);
    }
}
