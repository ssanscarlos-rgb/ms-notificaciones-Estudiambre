using Azure.Data.Tables;
using Azure.Storage.Queues;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Infraestructura.Adaptadores;
using MsNotificaciones.Infraestructura.Memoria;

namespace MsNotificaciones.Infraestructura;

/// <summary>Conecta cada puerto con su adaptador según la configuración.</summary>
public static class ConfiguracionServicios
{
    public static IServiceCollection AddInfraestructura(
        this IServiceCollection services, IConfiguration configuracion, IHostEnvironment entorno)
    {
        var opciones = OpcionesServicio.Desde(configuracion);
        services.AddSingleton(opciones);

        // Persistencia + cola (misma Storage Account).
        if (opciones.StorageConnectionString is { } storage)
        {
            services.AddSingleton(_ => new TableClient(storage, opciones.TableName));
            services.AddSingleton<INotificacionRepositorio, TableStorageNotificacionRepositorio>();

            // Mensajes en texto plano (por defecto en los SDK v12 de Java y .NET); el lector
            // también acepta Base64 por si el publicador lo cambia.
            services.AddSingleton(_ => new QueueServiceClient(storage,
                new QueueClientOptions { MessageEncoding = QueueMessageEncoding.None }));
            services.AddHostedService<ConsumidorColaWorker>();
        }
        else if (entorno.IsDevelopment())
        {
            services.AddSingleton<INotificacionRepositorio, RepositorioEnMemoria>();
        }
        else
        {
            throw new InvalidOperationException(
                "Falta STORAGE_CONNECTION_STRING. Configurala en los App settings del App Service.");
        }

        // Tiempo real. Si falta, el servicio arranca igual y /negotiate responde 503.
        if (opciones.SignalRConnectionString is not null)
        {
            services.AddSingleton<AzureSignalRNotificador>();
            services.AddSingleton<INotificadorTiempoReal>(sp => sp.GetRequiredService<AzureSignalRNotificador>());
        }
        else
        {
            services.AddSingleton<INotificadorTiempoReal, NotificadorSoloLog>();
        }

        return services;
    }
}
