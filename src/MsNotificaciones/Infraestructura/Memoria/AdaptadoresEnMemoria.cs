using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Memoria;

/// <summary>Repositorio para desarrollo local sin Storage. Se pierde al reiniciar.</summary>
public sealed class RepositorioEnMemoria : INotificacionRepositorio
{
    private readonly Lock _lock = new();
    private readonly List<Notificacion> _datos = [];

    public Task<bool> GuardarAsync(Notificacion n, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_datos.Any(x => x.UsuarioId == n.UsuarioId && x.Id == n.Id)) return Task.FromResult(false);
            _datos.Add(n);
            return Task.FromResult(true);
        }
    }

    public Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, bool? leida, CancellationToken ct)
    {
        lock (_lock)
        {
            var delUsuario = _datos.Where(x => x.UsuarioId == usuarioId && (leida is null || x.Leida == leida))
                .OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Id).ToList();
            var contenido = delUsuario.Skip(page * size).Take(size).ToList();
            return Task.FromResult(Pagina<Notificacion>.Crear(contenido, page, size, delUsuario.Count));
        }
    }

    public Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct)
    {
        lock (_lock)
        {
            var i = _datos.FindIndex(x => x.UsuarioId == usuarioId && x.Id == id);
            if (i < 0) return Task.FromResult<Notificacion?>(null);
            _datos[i] = _datos[i] with { Leida = true };
            return Task.FromResult<Notificacion?>(_datos[i]);
        }
    }

    public Task<bool> EliminarAsync(string usuarioId, string id, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_datos.RemoveAll(x => x.UsuarioId == usuarioId && x.Id == id) > 0);
        }
    }
}

/// <summary>Suscripciones push para desarrollo local.</summary>
public sealed class SuscripcionesPushEnMemoria : ISuscripcionesPushRepositorio
{
    private readonly Lock _lock = new();
    private readonly List<SuscripcionPush> _datos = [];

    public Task GuardarAsync(SuscripcionPush s, CancellationToken ct)
    {
        lock (_lock)
        {
            _datos.RemoveAll(x => x.UsuarioId == s.UsuarioId && x.Endpoint == s.Endpoint);
            _datos.Add(s);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SuscripcionPush>> ListarAsync(string usuarioId, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<SuscripcionPush>>(_datos.Where(x => x.UsuarioId == usuarioId).ToList());
    }

    public Task EliminarAsync(string usuarioId, string endpoint, CancellationToken ct)
    {
        lock (_lock)
            _datos.RemoveAll(x => x.UsuarioId == usuarioId && x.Endpoint == endpoint);
        return Task.CompletedTask;
    }
}

/// <summary>Sin Azure SignalR configurado: solo deja constancia en el log.</summary>
public sealed class NotificadorSoloLog(ILogger<NotificadorSoloLog> logger) : INotificadorTiempoReal
{
    public bool Disponible => false;

    public Task EnviarAsync(Notificacion n, CancellationToken ct)
    {
        logger.LogInformation("[sin SignalR] Se habría enviado la notificación {Id} a {UsuarioId}", n.Id, n.UsuarioId);
        return Task.CompletedTask;
    }

    public Task<Negociacion> NegociarAsync(string usuarioId, CancellationToken ct) =>
        throw new TiempoRealNoDisponibleException();
}
