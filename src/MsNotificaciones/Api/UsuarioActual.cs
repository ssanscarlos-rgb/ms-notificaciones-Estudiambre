using System.Buffers.Text;
using System.Text.Json;

namespace MsNotificaciones.Api;

/// <summary>
/// Lee el claim <c>sub</c> del JWT de Google. No valida la firma: eso lo hace APIM con
/// <c>validate-jwt</c>, y solo APIM puede llegar al backend (certificado cliente).
/// </summary>
public static class UsuarioActual
{
    private const string ClaveItems = "usuario.sub";

    public static string? LeerSub(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var partes = header["Bearer ".Length..].Trim().Split('.');
        if (partes.Length != 3) return null;

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(partes[1]));
            return payload.RootElement.TryGetProperty("sub", out var sub)
                   && sub.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(sub.GetString())
                ? sub.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary><c>sub</c> del usuario de la petición; lo deja listo <see cref="RequiereUsuarioFilter"/>.</summary>
    public static string UsuarioId(this HttpContext ctx) =>
        ctx.Items[ClaveItems] as string
        ?? throw new InvalidOperationException("Falta RequiereUsuarioFilter en este endpoint.");

    internal static void Guardar(HttpContext ctx, string sub) => ctx.Items[ClaveItems] = sub;
}

/// <summary>Responde 401 si la petición no trae un JWT con <c>sub</c>.</summary>
public sealed class RequiereUsuarioFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var sub = UsuarioActual.LeerSub(context.HttpContext.Request);
        if (sub is null)
            return Errores.Resultado(context.HttpContext, StatusCodes.Status401Unauthorized,
                "Falta el token de usuario (Authorization: Bearer <JWT con sub>).");

        UsuarioActual.Guardar(context.HttpContext, sub);
        return await next(context);
    }
}
