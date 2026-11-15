using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Envío Web Push con la criptografía de .NET, sin librerías externas.
/// <list type="number">
/// <item><b>Cifrado (RFC 8291 + RFC 8188, <c>aes128gcm</c>):</b> se genera un par ECDH P-256 efímero,
/// se combina con la clave pública del navegador (<c>p256dh</c>) y su secreto (<c>auth</c>) mediante
/// HKDF, y el mensaje se cifra con AES-128-GCM. Solo ese navegador puede leerlo; el servicio de push
/// (Google, Mozilla, Apple) solo ve bytes cifrados.</item>
/// <item><b>Identificación (RFC 8292, VAPID):</b> un JWT firmado con ECDSA P-256 (ES256) usando la clave
/// privada VAPID del servidor. El navegador se suscribió con la clave pública correspondiente, así que
/// el servicio de push solo acepta mensajes firmados por este servidor.</item>
/// </list>
/// </summary>
public sealed class WebPushEnviador : IEnviadorPush
{
    private const int TamanoRegistro = 4096;
    private static readonly TimeSpan VigenciaJwt = TimeSpan.FromHours(12);

    public const string ClienteHttp = "webpush";

    private readonly IHttpClientFactory _fabrica;
    private readonly ILogger<WebPushEnviador> _logger;
    private readonly TimeProvider _tiempo;
    private readonly string _sujeto;
    private readonly byte[]? _clavePublica;   // 65 bytes, punto sin comprimir
    private readonly ECParameters _parametrosPrivados;

    public WebPushEnviador(IHttpClientFactory fabrica, OpcionesServicio opciones, TimeProvider tiempo, ILogger<WebPushEnviador> logger)
    {
        _fabrica = fabrica;
        _logger = logger;
        _tiempo = tiempo;
        _sujeto = opciones.VapidSubject;

        if (opciones.VapidPublicKey is null || opciones.VapidPrivateKey is null)
        {
            _logger.LogInformation("Sin VAPID_PUBLIC_KEY/VAPID_PRIVATE_KEY: notificaciones push desactivadas");
            return;
        }
        try
        {
            var publica = Base64Url.DecodeFromChars(opciones.VapidPublicKey.TrimEnd('='));
            var privada = Base64Url.DecodeFromChars(opciones.VapidPrivateKey.TrimEnd('='));
            if (publica is not { Length: 65 } || publica[0] != 0x04 || privada.Length != 32)
                throw new CryptographicException("Tamaño de clave inválido");

            _parametrosPrivados = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privada,
                Q = new ECPoint { X = publica[1..33], Y = publica[33..65] },
            };
            using var prueba = ECDsa.Create(_parametrosPrivados); // valida que el par sea coherente
            _clavePublica = publica;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            _logger.LogError("Claves VAPID inválidas: el push queda desactivado ({Motivo})", ex.Message);
        }
    }

    public bool Disponible => _clavePublica is not null;

    public string? ClavePublica => _clavePublica is null ? null : Base64Url.EncodeToString(_clavePublica);

    public async Task<ResultadoPush> EnviarAsync(SuscripcionPush s, byte[] payload, CancellationToken ct)
    {
        if (!Disponible) return ResultadoPush.Fallido;

        byte[] cuerpo;
        try
        {
            cuerpo = Cifrar(payload,
                Base64Url.DecodeFromChars(s.P256dh.TrimEnd('=')),
                Base64Url.DecodeFromChars(s.Auth.TrimEnd('=')));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            _logger.LogWarning("Suscripción push con claves inválidas; se descarta ({Motivo})", ex.Message);
            return ResultadoPush.Expirada;
        }

        var destino = new Uri(s.Endpoint);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, destino)
        {
            Content = new ByteArrayContent(cuerpo),
        };
        peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        peticion.Content.Headers.ContentEncoding.Add("aes128gcm");
        peticion.Headers.Add("TTL", "86400");      // si el navegador está apagado, el push espera hasta 24 h
        peticion.Headers.Add("Urgency", "high");
        peticion.Headers.TryAddWithoutValidation("Authorization",
            $"vapid t={CrearJwtVapid(destino)}, k={ClavePublica}");

        using var respuesta = await _fabrica.CreateClient(ClienteHttp).SendAsync(peticion, ct);
        if (respuesta.IsSuccessStatusCode) return ResultadoPush.Enviado;
        if (respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return ResultadoPush.Expirada;

        var detalle = await respuesta.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("El servicio de push {Host} respondió {Status}: {Detalle}",
            destino.Host, (int)respuesta.StatusCode, detalle.Length > 300 ? detalle[..300] : detalle);
        return ResultadoPush.Fallido;
    }

    /// <summary>Cifrado <c>aes128gcm</c> de RFC 8291. Devuelve el cuerpo listo para enviar.</summary>
    public static byte[] Cifrar(byte[] payload, byte[] clavePublicaNavegador, byte[] secretoAuth)
    {
        if (clavePublicaNavegador is not { Length: 65 } || clavePublicaNavegador[0] != 0x04 || secretoAuth.Length != 16)
            throw new CryptographicException("Claves de la suscripción inválidas");
        if (payload.Length > TamanoRegistro - 16 - 1 - 86)
            throw new ArgumentException("El mensaje es demasiado grande para un push", nameof(payload));

        // 1. Par ECDH efímero del servidor y secreto compartido con el navegador.
        using var efimera = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = efimera.ExportParameters(false).Q;
        byte[] publicaServidor = [0x04, .. q.X!, .. q.Y!];

        using var navegador = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = clavePublicaNavegador[1..33], Y = clavePublicaNavegador[33..65] },
        });
        var secretoEcdh = efimera.DeriveRawSecretAgreement(navegador.PublicKey);

        // 2. Material de clave: mezcla el secreto ECDH con el secreto auth del navegador.
        byte[] infoClave = [.. "WebPush: info"u8, 0x00, .. clavePublicaNavegador, .. publicaServidor];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, secretoEcdh, 32, secretoAuth, infoClave);

        // 3. Clave de contenido y nonce, con un salt aleatorio por mensaje.
        var salt = RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        // 4. AES-128-GCM sobre payload + delimitador 0x02 (último y único registro).
        byte[] plano = [.. payload, 0x02];
        var cifrado = new byte[plano.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
            aes.Encrypt(nonce, plano, cifrado, tag);

        // 5. Cabecera aes128gcm: salt (16) | tamaño de registro (4) | largo de keyid (1) | keyid = clave pública efímera (65).
        var cuerpo = new byte[16 + 4 + 1 + 65 + cifrado.Length + tag.Length];
        salt.CopyTo(cuerpo, 0);
        BinaryPrimitives.WriteUInt32BigEndian(cuerpo.AsSpan(16, 4), TamanoRegistro);
        cuerpo[20] = 65;
        publicaServidor.CopyTo(cuerpo, 21);
        cifrado.CopyTo(cuerpo, 86);
        tag.CopyTo(cuerpo, 86 + cifrado.Length);
        return cuerpo;
    }

    /// <summary>JWT VAPID (ES256) para el origen del servicio de push.</summary>
    private string CrearJwtVapid(Uri destino)
    {
        var encabezado = Codificar(new { typ = "JWT", alg = "ES256" });
        var claims = Codificar(new
        {
            aud = destino.GetLeftPart(UriPartial.Authority),
            exp = _tiempo.GetUtcNow().Add(VigenciaJwt).ToUnixTimeSeconds(),
            sub = _sujeto,
        });
        var firmado = $"{encabezado}.{claims}";

        using var ecdsa = ECDsa.Create(_parametrosPrivados);
        // Formato IEEE P1363 (r||s, 64 bytes), el que exige JWS para ES256.
        var firma = ecdsa.SignData(Encoding.ASCII.GetBytes(firmado), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{firmado}.{Base64Url.EncodeToString(firma)}";
    }

    private static string Codificar(object valor) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(valor));
}
