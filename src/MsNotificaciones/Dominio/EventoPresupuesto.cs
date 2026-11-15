namespace MsNotificaciones.Dominio;

/// <summary>
/// Evento que Gastos y Plan publica en la cola cuando una categoría llega al 80% o al 100%
/// de su presupuesto. Contrato (sección 4 del documento de contexto):
/// <c>{usuarioId, tipo, categoria, porcentaje, fecha, correlationId}</c>.
/// </summary>
public sealed record EventoPresupuesto(
    string UsuarioId,
    string Tipo,
    string Categoria,
    double Porcentaje,
    long Fecha,
    string CorrelationId);

/// <summary>
/// Evento de cambio de precio que publicaría el microservicio de Precios en la misma cola:
/// <c>{tipo: "PRECIO_CAMBIO", producto, tienda?, precioAnterior, precioNuevo, usuarioId?, fecha?, correlationId?}</c>.
/// Sin <c>usuarioId</c> va a todos los usuarios.
/// </summary>
public sealed record EventoPrecio(
    string? UsuarioId,
    string Producto,
    string? Tienda,
    decimal PrecioAnterior,
    decimal PrecioNuevo,
    long Fecha,
    string CorrelationId);

public static class TiposEvento
{
    public const string Presupuesto80 = "PRESUPUESTO_80";
    public const string Presupuesto100 = "PRESUPUESTO_100";
    public const string PrecioCambio = "PRECIO_CAMBIO";

    public static bool EsConocido(string tipo) => tipo is Presupuesto80 or Presupuesto100;
}
