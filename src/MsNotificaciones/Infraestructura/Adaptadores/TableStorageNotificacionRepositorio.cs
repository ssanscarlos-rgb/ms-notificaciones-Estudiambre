using Azure;
using Azure.Data.Tables;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Entidad en Table Storage.
/// <c>PartitionKey</c> = <c>sub</c> del usuario (todas sus notificaciones juntas).
/// <c>RowKey</c> = <c>(long.MaxValue - fecha)</c> con 19 dígitos + <c>_</c> + id, así la tabla las
/// devuelve ya ordenadas de la más nueva a la más vieja, sin ordenar en memoria.
/// </summary>
public sealed class NotificacionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Id { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public bool Leida { get; set; }
    public long Fecha { get; set; }

    public static string CrearRowKey(long fecha, string id) => $"{long.MaxValue - fecha:D19}_{id}";

    public static NotificacionEntity Desde(Notificacion n) => new()
    {
        PartitionKey = n.UsuarioId,
        RowKey = CrearRowKey(n.Fecha, n.Id),
        Id = n.Id,
        Tipo = n.Tipo,
        Titulo = n.Titulo,
        Mensaje = n.Mensaje,
        Leida = n.Leida,
        Fecha = n.Fecha,
    };

    public Notificacion ADominio() => new(Id, PartitionKey, Tipo, Titulo, Mensaje, Leida, Fecha);
}

public sealed class TableStorageNotificacionRepositorio(TableClient tabla) : INotificacionRepositorio
{
    private readonly TableClient _tabla = tabla;
    private volatile bool _tablaLista;

    // Crea la tabla la primera vez. Si falla (red, Storage caído) se vuelve a intentar en la
    // siguiente llamada en vez de quedar roto hasta reiniciar.
    private async Task InicializarAsync(CancellationToken ct)
    {
        if (_tablaLista) return;
        await _tabla.CreateIfNotExistsAsync(ct);
        _tablaLista = true;
    }

    public async Task<bool> GuardarAsync(Notificacion notificacion, CancellationToken ct)
    {
        await InicializarAsync(ct);
        try
        {
            await _tabla.AddEntityAsync(NotificacionEntity.Desde(notificacion), ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false; // Mismo RowKey: el mensaje ya se había procesado.
        }
    }

    /// <summary>
    /// Recorre la partición del usuario (ya ordenada) para contar el total y tomar la página.
    /// Con el volumen del proyecto (decenas de notificaciones por usuario) es lo más simple que
    /// cumple el formato <c>totalElements/totalPages</c>. Para volúmenes grandes habría que pasar
    /// a tokens de continuación y dejar de devolver el total exacto.
    /// </summary>
    public async Task<Pagina<Notificacion>> ListarAsync(string usuarioId, int page, int size, CancellationToken ct)
    {
        await InicializarAsync(ct);
        var filtro = TableClient.CreateQueryFilter($"PartitionKey eq {usuarioId}");
        var desde = (long)page * size;
        var contenido = new List<Notificacion>(size);
        long total = 0;

        await foreach (var e in _tabla.QueryAsync<NotificacionEntity>(filtro, maxPerPage: 1000, cancellationToken: ct))
        {
            if (total >= desde && contenido.Count < size) contenido.Add(e.ADominio());
            total++;
        }

        return Pagina<Notificacion>.Crear(contenido, page, size, total);
    }

    public async Task<Notificacion?> MarcarLeidaAsync(string usuarioId, string id, CancellationToken ct)
    {
        await InicializarAsync(ct);
        // La PartitionKey limita la búsqueda a las notificaciones de este usuario.
        var filtro = TableClient.CreateQueryFilter($"PartitionKey eq {usuarioId} and Id eq {id}");

        await foreach (var e in _tabla.QueryAsync<NotificacionEntity>(filtro, maxPerPage: 1, cancellationToken: ct))
        {
            if (!e.Leida)
            {
                // Merge: solo cambia la columna Leida, el resto de la entidad queda igual.
                var cambio = new TableEntity(e.PartitionKey, e.RowKey) { ["Leida"] = true };
                await _tabla.UpdateEntityAsync(cambio, ETag.All, TableUpdateMode.Merge, ct);
                e.Leida = true;
            }
            return e.ADominio();
        }

        return null;
    }
}
