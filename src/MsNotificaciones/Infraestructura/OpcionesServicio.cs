namespace MsNotificaciones.Infraestructura;

/// <summary>
/// Configuración leída de variables de entorno / App settings (mismos nombres que en GitHub).
/// </summary>
public sealed record OpcionesServicio
{
    public string? StorageConnectionString { get; init; }
    public string QueueName { get; init; } = "eventos-presupuesto";
    public string TableName { get; init; } = "notificaciones";
    public string? SignalRConnectionString { get; init; }
    public string SignalRHub { get; init; } = "notificaciones";
    public string? ClientCertThumbprint { get; init; }

    /// <summary>
    /// <c>false</c> solo cuando el App Service está en un plan sin certificados de cliente (F1/D1):
    /// ahí App Service nunca manda <c>X-ARR-ClientCert</c> y el filtro rechazaría todo.
    /// </summary>
    public bool RequerirCertificadoCliente { get; init; } = true;

    /// <summary>Claves VAPID de Web Push (base64url). Sin ellas, el push queda desactivado.</summary>
    public string? VapidPublicKey { get; init; }
    public string? VapidPrivateKey { get; init; }

    /// <summary>Contacto del servidor para los servicios de push (<c>mailto:</c> o <c>https:</c>).</summary>
    public string VapidSubject { get; init; } = "mailto:notificaciones@estudiambre.app";

    public string PushTableName { get; init; } = "suscripcionespush";
    public string ActividadTableName { get; init; } = "actividad";

    /// <summary>Secreto que APIM manda en <c>X-Apim-Secret</c>. Si es null, no se exige.</summary>
    public string? ApimSharedSecret { get; init; }

    /// <summary>Intentos antes de mandar un mensaje a la cola <c>{QueueName}-poison</c>.</summary>
    public int MaxIntentos { get; init; } = 5;

    public static OpcionesServicio Desde(IConfiguration c) => new()
    {
        StorageConnectionString = Valor(c, "STORAGE_CONNECTION_STRING"),
        QueueName = Valor(c, "QUEUE_NAME") ?? "eventos-presupuesto",
        TableName = Valor(c, "TABLE_NAME") ?? "notificaciones",
        SignalRConnectionString = Valor(c, "AZURE_SIGNALR_CONNECTION_STRING"),
        SignalRHub = Valor(c, "SIGNALR_HUB") ?? "notificaciones",
        ClientCertThumbprint = Valor(c, "CLIENT_CERT_THUMBPRINT"),
        ApimSharedSecret = Valor(c, "APIM_SHARED_SECRET"),
        VapidPublicKey = Valor(c, "VAPID_PUBLIC_KEY"),
        VapidPrivateKey = Valor(c, "VAPID_PRIVATE_KEY"),
        VapidSubject = Valor(c, "VAPID_SUBJECT") ?? "mailto:notificaciones@estudiambre.app",
        PushTableName = Valor(c, "PUSH_TABLE_NAME") ?? "suscripcionespush",
        ActividadTableName = Valor(c, "ACTIVIDAD_TABLE_NAME") ?? "actividad",
        RequerirCertificadoCliente =!string.Equals(Valor(c, "CLIENT_CERT_REQUIRED"), "false", StringComparison.OrdinalIgnoreCase),
        MaxIntentos = int.TryParse(Valor(c, "QUEUE_MAX_INTENTOS"), out var n) && n > 0 ? n : 5,
    };

    private static string? Valor(IConfiguration c, string clave) =>
        string.IsNullOrWhiteSpace(c[clave]) ? null : c[clave]!.Trim();
}
