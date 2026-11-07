using System.Text;
using System.Text.Json;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Aplicacion;

/// <summary>
/// Convierte el texto de un mensaje de la cola en un <see cref="EventoPresupuesto"/>.
/// Acepta JSON plano (lo que publica el SDK de Java por defecto) y también JSON en Base64,
/// por si el publicador codifica los mensajes.
/// </summary>
public static class LectorEventos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record EventoCrudo(
        string? UsuarioId, string? Tipo, string? Categoria, double? Porcentaje, long? Fecha, string? CorrelationId);

    public static EventoPresupuesto Leer(string texto, long ahoraMs)
    {
        if (string.IsNullOrWhiteSpace(texto))
            throw new EventoInvalidoException("Mensaje vacío.");

        var json = texto.Trim();
        if (!json.StartsWith('{'))
            json = DecodificarBase64(json);

        EventoCrudo? crudo;
        try
        {
            crudo = JsonSerializer.Deserialize<EventoCrudo>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new EventoInvalidoException("El mensaje no es JSON válido.", ex);
        }

        if (crudo is null) throw new EventoInvalidoException("Mensaje vacío.");
        if (string.IsNullOrWhiteSpace(crudo.UsuarioId)) throw new EventoInvalidoException("Falta usuarioId.");
        if (string.IsNullOrWhiteSpace(crudo.Categoria)) throw new EventoInvalidoException("Falta categoria.");
        if (crudo.Tipo is null || !TiposEvento.EsConocido(crudo.Tipo))
            throw new EventoInvalidoException($"Tipo desconocido: '{crudo.Tipo}'.");
        if (crudo.Porcentaje is null or < 0) throw new EventoInvalidoException("Falta porcentaje o es negativo.");

        // Fechas en Epoch ms. Igual que el frontend: un valor menor a 1e11 se toma como segundos.
        var fecha = crudo.Fecha is > 0 ? crudo.Fecha.Value : ahoraMs;
        if (fecha < 100_000_000_000L) fecha *= 1000;

        var correlationId = string.IsNullOrWhiteSpace(crudo.CorrelationId)
            ? Guid.NewGuid().ToString()
            : crudo.CorrelationId;

        return new EventoPresupuesto(
            crudo.UsuarioId, crudo.Tipo, crudo.Categoria.Trim(), crudo.Porcentaje.Value, fecha, correlationId);
    }

    private static string DecodificarBase64(string texto)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(texto)).Trim();
        }
        catch (FormatException ex)
        {
            throw new EventoInvalidoException("El mensaje no es JSON ni Base64.", ex);
        }
    }
}
