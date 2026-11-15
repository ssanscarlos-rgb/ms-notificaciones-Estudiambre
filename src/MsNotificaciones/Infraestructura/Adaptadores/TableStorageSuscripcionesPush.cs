using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Tabla <c>suscripcionespush</c>. <c>PartitionKey</c> = <c>sub</c> del usuario (al emitir se leen
/// todas sus suscripciones de una partición). <c>RowKey</c> = SHA-256 del endpoint en hexadecimal:
/// el endpoint es una URL con caracteres que <c>RowKey</c> no admite (<c>/</c>), y el hash da una
/// clave fija por navegador, así que volver a suscribirse actualiza en vez de duplicar.
/// </summary>
public sealed class SuscripcionPushEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";

    public static string Clave(string endpoint) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}

public sealed class TableStorageSuscripcionesPush(TableClient tabla) : ISuscripcionesPushRepositorio
{
    private volatile bool _tablaLista;

    private async Task InicializarAsync(CancellationToken ct)
    {
        if (_tablaLista) return;
        await tabla.CreateIfNotExistsAsync(ct);
        _tablaLista = true;
    }

    public async Task GuardarAsync(SuscripcionPush s, CancellationToken ct)
    {
        await InicializarAsync(ct);
        await tabla.UpsertEntityAsync(new SuscripcionPushEntity
        {
            PartitionKey = s.UsuarioId,
            RowKey = SuscripcionPushEntity.Clave(s.Endpoint),
            Endpoint = s.Endpoint,
            P256dh = s.P256dh,
            Auth = s.Auth,
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<SuscripcionPush>> ListarAsync(string usuarioId, CancellationToken ct)
    {
        await InicializarAsync(ct);
        var filtro = TableClient.CreateQueryFilter($"PartitionKey eq {usuarioId}");
        var lista = new List<SuscripcionPush>();
        await foreach (var e in tabla.QueryAsync<SuscripcionPushEntity>(filtro, cancellationToken: ct))
            lista.Add(new SuscripcionPush(e.PartitionKey, e.Endpoint, e.P256dh, e.Auth));
        return lista;
    }

    public async Task EliminarAsync(string usuarioId, string endpoint, CancellationToken ct)
    {
        await InicializarAsync(ct);
        try
        {
            await tabla.DeleteEntityAsync(usuarioId, SuscripcionPushEntity.Clave(endpoint), ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Ya no estaba: dar de baja es idempotente.
        }
    }
}
