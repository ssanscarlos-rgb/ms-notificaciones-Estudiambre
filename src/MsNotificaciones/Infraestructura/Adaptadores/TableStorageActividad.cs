using Azure;
using Azure.Data.Tables;
using MsNotificaciones.Aplicacion;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Tabla <c>actividad</c>: una fila por usuario. <c>PartitionKey</c> = <c>"usuarios"</c> (una sola
/// partición: las tareas diarias leen a todos de una vez; el volumen es chico) y <c>RowKey</c> = <c>sub</c>.
/// </summary>
public sealed class ActividadEntity : ITableEntity
{
    public const string Particion = "usuarios";

    public string PartitionKey { get; set; } = Particion;
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public long UltimaActividad { get; set; }
    public int NivelInactividad { get; set; }
}

public sealed class TableStorageActividad(TableClient tabla) : IActividadRepositorio
{
    private volatile bool _tablaLista;

    private async Task InicializarAsync(CancellationToken ct)
    {
        if (_tablaLista) return;
        await tabla.CreateIfNotExistsAsync(ct);
        _tablaLista = true;
    }

    public async Task<bool> RegistrarAsync(string usuarioId, long ahoraMs, CancellationToken ct)
    {
        await InicializarAsync(ct);
        try
        {
            // El alta es la "cerradura": si dos peticiones del mismo usuario llegan juntas, solo una
            // la crea (la otra recibe 409), así que la bienvenida sale una sola vez.
            await tabla.AddEntityAsync(new ActividadEntity { RowKey = usuarioId, UltimaActividad = ahoraMs }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var cambio = new TableEntity(ActividadEntity.Particion, usuarioId)
            {
                ["UltimaActividad"] = ahoraMs,
                ["NivelInactividad"] = 0,
            };
            await tabla.UpdateEntityAsync(cambio, ETag.All, TableUpdateMode.Merge, ct);
            return false;
        }
    }

    public async Task<IReadOnlyList<ActividadUsuario>> ListarAsync(CancellationToken ct)
    {
        await InicializarAsync(ct);
        var filtro = TableClient.CreateQueryFilter($"PartitionKey eq {ActividadEntity.Particion}");
        var lista = new List<ActividadUsuario>();
        await foreach (var e in tabla.QueryAsync<ActividadEntity>(filtro, cancellationToken: ct))
            lista.Add(new ActividadUsuario(e.RowKey, e.UltimaActividad, e.NivelInactividad));
        return lista;
    }

    public async Task MarcarNivelInactividadAsync(string usuarioId, int nivel, CancellationToken ct)
    {
        await InicializarAsync(ct);
        var cambio = new TableEntity(ActividadEntity.Particion, usuarioId) { ["NivelInactividad"] = nivel };
        await tabla.UpdateEntityAsync(cambio, ETag.All, TableUpdateMode.Merge, ct);
    }
}
