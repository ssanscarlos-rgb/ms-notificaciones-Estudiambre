using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

/// <summary>Puerto de persistencia. Adaptador real: Table Storage.</summary>
public interface INotificacionRepositorio
{
    /// <summary>Guarda la notificación. Devuelve <c>false</c> si ya existía (mensaje reprocesado).</summary>
    Task<bool> GuardarAsync(Notificacion notificacion, CancellationToken ct);

    /// <summary>Página <paramref name="page"/> (base 0) del usuario, de la más nueva a la más vieja.</summary>
    Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, CancellationToken ct);

    /// <summary>Marca como leída. Devuelve <c>null</c> si no existe o es de otro usuario.</summary>
    Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct);
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

/// <summary>Formato de paginación del curso: página base 0.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Content, int Page, int Size, long TotalElements, int TotalPages)
{
    public static Pagina<T> Crear(IReadOnlyList<T> content, int page, int size, long total) =>
        new(content, page, size, total, (int)((total + size - 1) / size));

    public Pagina<TOut> Map<TOut>(Func<T, TOut> f) =>
        new(Content.Select(f).ToList(), Page, Size, TotalElements, TotalPages);
}
