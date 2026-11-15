namespace MsNotificaciones.Dominio;

/// <summary>
/// Suscripción Web Push de un navegador (lo que devuelve <c>PushSubscription.toJSON()</c>).
/// <see cref="Endpoint"/> es la URL del servicio de push del navegador (Google, Mozilla, Apple…);
/// <see cref="P256dh"/> (clave pública ECDH del navegador) y <see cref="Auth"/> (secreto) sirven
/// para cifrar el mensaje, que solo ese navegador puede leer. Todo en base64url.
/// </summary>
public sealed record SuscripcionPush(string UsuarioId, string Endpoint, string P256dh, string Auth);
