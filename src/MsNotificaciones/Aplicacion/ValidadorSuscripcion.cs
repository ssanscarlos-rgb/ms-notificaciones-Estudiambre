using System.Buffers.Text;

namespace MsNotificaciones.Aplicacion;

/// <summary>
/// Valida lo que manda el navegador antes de guardarlo. El endpoint importa por seguridad: el
/// servicio le hace POST, así que si se aceptara cualquier URL, alguien podría usar el servicio para
/// llamar direcciones internas (SSRF). Solo se aceptan los servicios de push de los navegadores.
/// </summary>
public static class ValidadorSuscripcion
{
    // Dominios de los servicios de push: Chrome/Edge/Opera (Google), Firefox (Mozilla),
    // Edge en Windows (WNS) y Safari (Apple).
    private static readonly string[] Dominios =
    [
        "fcm.googleapis.com",
        "push.services.mozilla.com",
        "notify.windows.com",
        "push.apple.com",
    ];

    public static void Validar(string? endpoint, string? p256dh, string? auth, bool permitirLocales)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new SolicitudInvalidaException("'endpoint' debe ser una URL absoluta.");

        var local = permitirLocales && uri.IsLoopback && uri.Scheme is "http" or "https";
        var conocido = uri.Scheme == "https" && Dominios.Any(d =>
            uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        if (!local && !conocido)
            throw new SolicitudInvalidaException("'endpoint' no es de un servicio de push conocido.");

        if (Decodificar(p256dh) is not { Length: 65 } clave || clave[0] != 0x04)
            throw new SolicitudInvalidaException("'keys.p256dh' debe ser una clave P-256 sin comprimir (65 bytes, base64url).");
        if (Decodificar(auth) is not { Length: 16 })
            throw new SolicitudInvalidaException("'keys.auth' debe tener 16 bytes (base64url).");
    }

    private static byte[]? Decodificar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        try
        {
            return Base64Url.DecodeFromChars(valor.Trim().TrimEnd('='));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
