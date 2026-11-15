using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

/// <summary>Puerto de persistencia. Adaptador real: Table Storage.</summary>
public interface INotificacionRepositorio
{
    /// <summary>Guarda la notificación. Devuelve <c>false</c> si ya existía (mensaje reprocesado).</summary>
    Task<bool> GuardarAsync(Notificacion notificacion, CancellationToken ct);

    /// <summary>
    /// Página <paramref name="page"/> (base 0) del usuario, de la más nueva a la más vieja.
    /// Con <paramref name="leida"/> filtra (y los totales cuentan solo las filtradas).
    /// </summary>
    Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, bool? leida, CancellationToken ct);

    /// <summary>Marca como leída. Devuelve <c>null</c> si no existe o es de otro usuario.</summary>
    Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct);

    /// <summary>Elimina. Devuelve <c>false</c> si no existía o es de otro usuario.</summary>
    Task<bool> EliminarAsync(string usuarioId, string id, CancellationToken ct);
}

/// <summary>Puerto de envío en tiempo real. Adaptador real: Azure SignalR (modo Serverless).</summary>
public interface INotificadorTiempoReal
{
    /// <summary><c>false</c> si no hay SignalR configurado (por ejemplo, en local).</summary>
    bool Disponible { get; }

    Task EnviarAsync(Notificacion notificacion, CancellationToken ct);

    /// <summary>Datos para que el navegador se conecte directo a Azure SignalR como este usuario.</summary>
    Task<Negociacion> NegociarAsync(string usuarioId, CancellationToken ct);
}

public sealed record Negociacion(string Url, string AccessToken);

/// <summary>Última vez que cada usuario abrió la app. Adaptador real: Table Storage.</summary>
public interface IActividadRepositorio
{
    /// <summary>Registra que el usuario entró ahora. Devuelve <c>true</c> si es la primera vez (usuario nuevo).</summary>
    Task<bool> RegistrarAsync(string usuarioId, long ahoraMs, CancellationToken ct);

    Task<IReadOnlyList<ActividadUsuario>> ListarAsync(CancellationToken ct);

    /// <summary>Guarda hasta qué nivel de inactividad ya se le avisó (para no repetir).</summary>
    Task MarcarNivelInactividadAsync(string usuarioId, int nivel, CancellationToken ct);
}

public sealed record ActividadUsuario(string UsuarioId, long UltimaActividad, int NivelInactividad);

/// <summary>Puerto de persistencia de suscripciones push. Adaptador real: Table Storage.</summary>
public interface ISuscripcionesPushRepositorio
{
    /// <summary>Alta o actualización (misma suscripción = mismo endpoint).</summary>
    Task GuardarAsync(SuscripcionPush suscripcion, CancellationToken ct);
    Task<IReadOnlyList<SuscripcionPush>> ListarAsync(string usuarioId, CancellationToken ct);
    Task EliminarAsync(string usuarioId, string endpoint, CancellationToken ct);
}

/// <summary>Puerto de envío Web Push (cifrado RFC 8291 + firma VAPID RFC 8292).</summary>
public interface IEnviadorPush
{
    /// <summary><c>false</c> si no hay claves VAPID configuradas.</summary>
    bool Disponible { get; }

    /// <summary>Clave pública VAPID (base64url) que el navegador usa al suscribirse.</summary>
    string? ClavePublica { get; }

    Task<ResultadoPush> EnviarAsync(SuscripcionPush suscripcion, byte[] payload, CancellationToken ct);
}

public enum ResultadoPush
{
    Enviado,
    /// <summary>El servicio de push respondió 404/410: la suscripción ya no existe y se borra.</summary>
    Expirada,
    Fallido,
}

/// <param name="PermitirEndpointsLocales">Solo en Development: acepta endpoints http://localhost para pruebas.</param>
public sealed record OpcionesPush(bool PermitirEndpointsLocales);

/// <summary>Formato de paginación del curso: página base 0.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Content, int Page, int Size, long TotalElements, int TotalPages)
{
    public static Pagina<T> Crear(IReadOnlyList<T> content, int page, int size, long total) =>
        new(content, page, size, total, (int)((total + size - 1) / size));

    public Pagina<TOut> Map<TOut>(Func<T, TOut> f) =>
        new(Content.Select(f).ToList(), Page, Size, TotalElements, TotalPages);
}
