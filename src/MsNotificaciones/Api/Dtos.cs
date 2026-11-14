using System.Text.Json.Serialization;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Api;

/// <summary>
/// Notificación tal como la ve el frontend (REST y SignalR):
/// <c>{id, tipo, titulo, mensaje, leida, fecha[, categoria]}</c>. <c>categoria</c> viene en las que
/// nacen de un evento de presupuesto; si es null no se envía.
/// </summary>
public sealed record NotificacionDto(
    string Id, string Tipo, string Titulo, string Mensaje, bool Leida, long Fecha,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Categoria)
{
    public static NotificacionDto Desde(Notificacion n) =>
        new(n.Id, n.Tipo, n.Titulo, n.Mensaje, n.Leida, n.Fecha, n.Categoria);
}

public sealed record CrearNotificacionRequest(string? Tipo, string? Titulo, string? Mensaje, string? Categoria);

/// <summary>PATCH parcial. Solo se admite <c>{"leida": true}</c>; cualquier otro campo es 400.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarcarLeidaRequest(bool? Leida);

public sealed record NegociacionDto(string Url, string AccessToken);
