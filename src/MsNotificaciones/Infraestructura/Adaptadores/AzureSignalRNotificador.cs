using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Management;
using MsNotificaciones.Api;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Azure SignalR en modo <b>Serverless</b> con el Management SDK: este servicio no hospeda un hub.
/// <list type="bullet">
/// <item><c>NegociarAsync</c> genera la URL y el token con los que el navegador se conecta directo a
/// Azure SignalR, identificado con el <c>sub</c> del usuario.</item>
/// <item><c>EnviarAsync</c> manda el evento <c>notificacion</c> a todas las conexiones de ese usuario.</item>
/// </list>
/// El recurso de Azure SignalR tiene que estar en modo Serverless.
/// </summary>
public sealed class AzureSignalRNotificador(
    OpcionesServicio opciones,
    ILoggerFactory loggerFactory) : INotificadorTiempoReal, IAsyncDisposable
{
    /// <summary>Nombre del evento que escucha el frontend: <c>connection.on("notificacion", ...)</c>.</summary>
    public const string EventoCliente = "notificacion";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ServiceManager? _manager;
    private ServiceHubContext? _hub;

    public bool Disponible => true;

    public async Task EnviarAsync(Notificacion n, CancellationToken ct)
    {
        var hub = await HubAsync(ct);
        await hub.Clients.User(n.UsuarioId).SendAsync(EventoCliente, NotificacionDto.Desde(n), ct);
    }

    public async Task<Negociacion> NegociarAsync(string usuarioId, CancellationToken ct)
    {
        var hub = await HubAsync(ct);
        var r = await hub.NegotiateAsync(
            new NegotiationOptions { UserId = usuarioId, TokenLifetime = TimeSpan.FromHours(1) }, ct);
        return new Negociacion(r.Url!, r.AccessToken!);
    }

    // Crear el ServiceHubContext es caro: se crea una vez y se reutiliza.
    private async Task<ServiceHubContext> HubAsync(CancellationToken ct)
    {
        if (_hub is not null) return _hub;
        await _lock.WaitAsync(ct);
        try
        {
            if (_hub is not null) return _hub;
            _manager = new ServiceManagerBuilder()
                .WithOptions(o =>
                {
                    o.ConnectionString = opciones.SignalRConnectionString;
                    o.ServiceTransportType = ServiceTransportType.Transient;
                    // camelCase, igual que la API REST: {id, tipo, titulo, mensaje, leida, fecha}.
                    o.UseJsonObjectSerializer(new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                })
                .WithLoggerFactory(loggerFactory)
                .BuildServiceManager();
            _hub = await _manager.CreateHubContextAsync(opciones.SignalRHub, ct);
            return _hub;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _manager?.Dispose();
        _lock.Dispose();
    }
}
