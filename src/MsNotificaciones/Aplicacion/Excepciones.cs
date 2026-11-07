namespace MsNotificaciones.Aplicacion;

/// <summary>Datos de entrada inválidos en una petición HTTP. Se responde 400.</summary>
public sealed class SolicitudInvalidaException(string message) : Exception(message);

/// <summary>Mensaje de la cola que no se puede interpretar. Va a la cola de mensajes fallidos.</summary>
public sealed class EventoInvalidoException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>El tiempo real no está configurado. Se responde 503.</summary>
public sealed class TiempoRealNoDisponibleException()
    : Exception("El servicio de tiempo real no está configurado.");
