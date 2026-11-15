using System.Globalization;

namespace MsNotificaciones.Dominio;

/// <summary>
/// Notificación de un usuario. <see cref="Fecha"/> va en Epoch milisegundos, UTC.
/// <see cref="Ruta"/> (opcional) es la pantalla de la app a la que lleva; si es null, el
/// frontend usa la de su tipo.
/// </summary>
public sealed record Notificacion(
    string Id,
    string UsuarioId,
    string Tipo,
    string Titulo,
    string Mensaje,
    bool Leida,
    long Fecha,
    string? Categoria = null,
    string? Ruta = null)
{
    /// <summary>Notificación a partir de un contenido del catálogo (bienvenida, recomendación, evento…).</summary>
    public static Notificacion Desde(Contenido c, string id, string usuarioId, string tipo, long fecha) =>
        new(id, usuarioId, tipo, c.Titulo, c.Mensaje, Leida: false, fecha, Categoria: null, c.Ruta);

    /// <summary>
    /// Arma la notificación que ve el usuario a partir del evento que publica Gastos y Plan.
    /// El <paramref name="id"/> se toma del mensaje de la cola para que reprocesar el mismo
    /// mensaje no cree una notificación repetida.
    /// </summary>
    public static Notificacion DesdeEvento(EventoPresupuesto evento, string id)
    {
        var porcentaje = (int)Math.Round(evento.Porcentaje, MidpointRounding.AwayFromZero);
        var cat = evento.Categoria;
        var (titulo, mensaje) = evento.Tipo switch
        {
            TiposEvento.Presupuesto80 => (
                $"Vas por el {porcentaje} % en {cat}",
                $"Ya usaste el {porcentaje} % del presupuesto de {cat}. Revisá tus gastos antes de que termine la quincena."),
            // Se pasó: el tipo sigue siendo PRESUPUESTO_100 (mismo ícono y botón al plan en el frontend).
            TiposEvento.Presupuesto100 when porcentaje > 100 => (
                "Te estás pasando del presupuesto",
                $"Vas por el {porcentaje} % en {cat}. ¿Querés ampliarlo? Con cuidado, ¡que no se vuelva costumbre!"),
            TiposEvento.Presupuesto100 => (
                $"Presupuesto de {cat} agotado",
                $"Llegaste al {porcentaje} % del presupuesto de {cat}. De aquí a la próxima quincena, ¡cuidá cada colón!"),
            _ => throw new ArgumentException($"Tipo de evento no soportado: {evento.Tipo}", nameof(evento)),
        };

        return new Notificacion(id, evento.UsuarioId, evento.Tipo, titulo, mensaje, Leida: false, evento.Fecha, cat);
    }

    /// <summary>Aviso de cambio de precio (evento que publicaría el microservicio de Precios).</summary>
    public static Notificacion DesdePrecio(EventoPrecio e, string id, string usuarioId)
    {
        var donde = string.IsNullOrWhiteSpace(e.Tienda) ? "" : $"En {e.Tienda} ";
        var (titulo, mensaje) = e.PrecioNuevo < e.PrecioAnterior
            ? ($"¡Bajó el precio de {e.Producto}!",
               $"{(donde == "" ? "Pasó" : donde + "pasó")} de {Colones(e.PrecioAnterior)} a {Colones(e.PrecioNuevo)}. ¡Aprovechá!")
            : ($"Subió el precio de {e.Producto}",
               $"{(donde == "" ? "Ahora" : donde + "ahora")} cuesta {Colones(e.PrecioNuevo)} (antes {Colones(e.PrecioAnterior)}). Antes de comprar, revisá el comparador.");
        if (titulo.Length > 120) titulo = titulo[..117] + "…";
        return new Notificacion(id, usuarioId, TiposNotificacion.Precio, titulo, mensaje.Trim(), Leida: false, e.Fecha,
            Categoria: null, Ruta: "/comparar");
    }

    /// <summary>Monto en colones como en el frontend: ₡30 388.</summary>
    public static string Colones(decimal monto) =>
        "₡" + Math.Round(monto, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '\u00A0'); // espacio que no se parte entre líneas
}
