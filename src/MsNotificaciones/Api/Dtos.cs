using System.Text.Json.Serialization;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Api;

/// <summary>Notificación tal como la ve el frontend: <c>{id, tipo, titulo, mensaje, leida, fecha}</c>.</summary>
public sealed record NotificacionDto(string Id, string Tipo, string Titulo, string Mensaje, bool Leida, long Fecha)
{
    public static NotificacionDto Desde(Notificacion n) => new(n.Id, n.Tipo, n.Titulo, n.Mensaje, n.Leida, n.Fecha);
}

public sealed record CrearNotificacionRequest(string? Tipo, string? Titulo, string? Mensaje);

/// <summary>PATCH parcial. Solo se admite <c>{"leida": true}</c>; cualquier otro campo es 400.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarcarLeidaRequest(bool? Leida);

public sealed record NegociacionDto(string Url, string AccessToken);
