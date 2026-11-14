using Microsoft.AspNetCore.Mvc;
using MsNotificaciones.Aplicacion;

namespace MsNotificaciones.Api;

public static class NotificacionesEndpoints
{
    public static IEndpointRouteBuilder MapNotificaciones(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/v1/notificaciones")
            .AddEndpointFilter<RequiereUsuarioFilter>()
            .WithTags("Notificaciones");

        grupo.MapGet("", Listar)
            .Produces<Pagina<NotificacionDto>>()
            .Produces<ErrorRespuesta>(400)
            .Produces<ErrorRespuesta>(401);

        grupo.MapPost("", Crear)
            .Produces<NotificacionDto>(201)
            .Produces<ErrorRespuesta>(400);

        grupo.MapPatch("/{id}", MarcarLeida)
            .Produces<NotificacionDto>()
            .Produces<ErrorRespuesta>(400)
            .Produces<ErrorRespuesta>(404);

        grupo.MapDelete("/{id}", Eliminar)
            .Produces(204);

        grupo.MapPost("/negotiate", Negociar)
            .Produces<NegociacionDto>()
            .Produces<ErrorRespuesta>(503);

        return app;
    }

    private static async Task<IResult> Listar(
        HttpContext ctx, NotificacionService servicio,
        [FromQuery] int? page, [FromQuery] int? size, [FromQuery] bool? leida, CancellationToken ct)
    {
        // ?leida=false → solo sin leer (filtro "Sin leer"); los totales cuentan solo esas.
        var pagina = await servicio.ListarAsync(ctx.UsuarioId(), page ?? 0, size ?? 10, leida, ct);
        return Results.Ok(pagina.Map(NotificacionDto.Desde));
    }

    private static async Task<IResult> Crear(
        HttpContext ctx, NotificacionService servicio, [FromBody] CrearNotificacionRequest body, CancellationToken ct)
    {
        var n = await servicio.CrearAsync(ctx.UsuarioId(), body.Tipo, body.Titulo, body.Mensaje, body.Categoria, ct);
        return Results.Created($"/v1/notificaciones/{n.Id}", NotificacionDto.Desde(n));
    }

    private static async Task<IResult> MarcarLeida(
        HttpContext ctx, NotificacionService servicio, string id, [FromBody] MarcarLeidaRequest body, CancellationToken ct)
    {
        if (body.Leida is not true)
            return Errores.Resultado(ctx, 400, "Solo se admite {\"leida\": true}.");

        var n = await servicio.MarcarLeidaAsync(ctx.UsuarioId(), id, ct);
        return n is null
            ? Errores.Resultado(ctx, 404, "Notificación no encontrada.")
            : Results.Ok(NotificacionDto.Desde(n));
    }

    /// <summary>204 siempre: borrar algo que no existe (o ajeno) no es error ni revela si existe.</summary>
    private static async Task<IResult> Eliminar(HttpContext ctx, NotificacionService servicio, string id, CancellationToken ct)
    {
        await servicio.EliminarAsync(ctx.UsuarioId(), id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Negociar(HttpContext ctx, NotificacionService servicio, CancellationToken ct)
    {
        var r = await servicio.NegociarAsync(ctx.UsuarioId(), ct);
        return Results.Ok(new NegociacionDto(r.Url, r.AccessToken));
    }
}
