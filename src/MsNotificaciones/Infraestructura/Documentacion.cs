namespace MsNotificaciones.Infraestructura;

/// <summary>Swagger UI en <c>/swagger</c> (público, igual que <c>/health</c>).</summary>
public static class Documentacion
{
    public static IServiceCollection AddDocumentacion(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }

    public static WebApplication UseDocumentacion(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
