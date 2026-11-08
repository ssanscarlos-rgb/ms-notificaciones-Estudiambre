using System.Security.Cryptography.X509Certificates;
using MsNotificaciones.Api;

namespace MsNotificaciones.Infraestructura.Http;

/// <summary>
/// Mismo patrón del Lab 5: App Service (con <i>Incoming client certificates = Require</i>) pasa el
/// certificado que presentó APIM en el header <c>X-ARR-ClientCert</c> (Base64 del DER). Se compara
/// su huella SHA-1 con <c>CLIENT_CERT_THUMBPRINT</c>. <c>/health</c> y <c>/swagger</c> quedan fuera.
/// En Development, si no hay huella configurada, deja pasar todo; en cualquier otro ambiente
/// responde 403 (igual que los servicios Node).
/// </summary>
public sealed class CertificadoClienteMiddleware(
    RequestDelegate next,
    OpcionesServicio opciones,
    IHostEnvironment entorno,
    TimeProvider tiempo,
    ILogger<CertificadoClienteMiddleware> logger)
{
    public const string Header = "X-ARR-ClientCert";

    private readonly string? _huellaEsperada = Normalizar(opciones.ClientCertThumbprint);
    private readonly bool _desactivado = DesactivarConAviso(opciones, logger);

    private static bool DesactivarConAviso(OpcionesServicio opciones, ILogger logger)
    {
        if (opciones.RequerirCertificadoCliente) return false;
        logger.LogWarning("CLIENT_CERT_REQUIRED=false: no se valida el certificado de cliente (plan sin mTLS)");
        return true;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (_desactivado || EsRutaPublica(ctx.Request.Path) || HttpMethods.IsOptions(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        if (_huellaEsperada is null)
        {
            if (entorno.IsDevelopment())
            {
                await next(ctx);
                return;
            }
            await Rechazar(ctx, "CLIENT_CERT_THUMBPRINT no está configurado.");
            return;
        }

        var header = ctx.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            await Rechazar(ctx, "Falta el certificado de cliente.");
            return;
        }

        X509Certificate2 certificado;
        try
        {
            certificado = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(LimpiarPem(header)));
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            await Rechazar(ctx, "Certificado de cliente inválido.");
            return;
        }

        using (certificado)
        {
            var ahora = tiempo.GetUtcNow().UtcDateTime;
            if (ahora < certificado.NotBefore.ToUniversalTime() || ahora > certificado.NotAfter.ToUniversalTime())
            {
                await Rechazar(ctx, "Certificado de cliente vencido o todavía no válido.");
                return;
            }

            if (!string.Equals(certificado.Thumbprint, _huellaEsperada, StringComparison.OrdinalIgnoreCase))
            {
                await Rechazar(ctx, "Certificado de cliente no autorizado.");
                return;
            }
        }

        await next(ctx);
    }

    private static bool EsRutaPublica(PathString ruta) =>
        ruta.StartsWithSegments("/health") || ruta.StartsWithSegments("/swagger");

    private Task Rechazar(HttpContext ctx, string motivo)
    {
        logger.LogWarning("Petición rechazada por certificado: {Motivo}", motivo);
        return Errores.EscribirAsync(ctx, StatusCodes.Status403Forbidden, motivo);
    }

    /// <summary>Quita los <c>:</c> y espacios que trae la huella si se copió de OpenSSL o del portal.</summary>
    internal static string? Normalizar(string? huella)
    {
        if (string.IsNullOrWhiteSpace(huella)) return null;
        return new string(huella.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    private static string LimpiarPem(string valor) =>
        string.Concat(valor
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Where(c => !char.IsWhiteSpace(c)));
}
