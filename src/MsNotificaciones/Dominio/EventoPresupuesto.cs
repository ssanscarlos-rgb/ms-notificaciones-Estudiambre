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

public static class TiposEvento
{
    public const string Presupuesto80 = "PRESUPUESTO_80";
    public const string Presupuesto100 = "PRESUPUESTO_100";

    public static bool EsConocido(string tipo) => tipo is Presupuesto80 or Presupuesto100;
}
