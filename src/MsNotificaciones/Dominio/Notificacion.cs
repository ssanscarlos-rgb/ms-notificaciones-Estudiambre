namespace MsNotificaciones.Dominio;

/// <summary>
/// Notificación de un usuario. <see cref="Fecha"/> va en Epoch milisegundos, UTC.
/// </summary>
public sealed record Notificacion(
    string Id,
    string UsuarioId,
    string Tipo,
    string Titulo,
    string Mensaje,
    bool Leida,
    long Fecha)
{
    /// <summary>
    /// Arma la notificación que ve el usuario a partir del evento que publica Gastos y Plan.
    /// El <paramref name="id"/> se toma del mensaje de la cola para que reprocesar el mismo
    /// mensaje no cree una notificación repetida.
    /// </summary>
    public static Notificacion DesdeEvento(EventoPresupuesto evento, string id)
    {
        var porcentaje = (int)Math.Round(evento.Porcentaje, MidpointRounding.AwayFromZero);
        var (titulo, mensaje) = evento.Tipo switch
        {
            TiposEvento.Presupuesto80 => (
                $"Vas por el {porcentaje}% en {evento.Categoria}",
                $"Ya usaste el {porcentaje}% del presupuesto de {evento.Categoria}. Revisá tus gastos antes de que termine la quincena."),
            TiposEvento.Presupuesto100 => (
                $"Presupuesto de {evento.Categoria} agotado",
                $"Llegaste al {porcentaje}% del presupuesto de {evento.Categoria}."),
            _ => throw new ArgumentException($"Tipo de evento no soportado: {evento.Tipo}", nameof(evento)),
        };

        return new Notificacion(id, evento.UsuarioId, evento.Tipo, titulo, mensaje, Leida: false, evento.Fecha);
    }
}
