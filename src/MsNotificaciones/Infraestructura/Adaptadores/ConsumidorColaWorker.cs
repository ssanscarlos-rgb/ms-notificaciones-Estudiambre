using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Dominio;

namespace MsNotificaciones.Infraestructura.Adaptadores;

/// <summary>
/// Consumidor de la cola de eventos de Gastos y Plan (proceso en segundo plano del mismo App Service).
/// Por cada mensaje: interpreta el evento → guarda en Table Storage → envía por SignalR → borra el mensaje.
/// Si algo falla, el mensaje vuelve a ser visible y se reintenta; tras <c>MaxIntentos</c> (o si el
/// mensaje es inválido) se mueve a <c>{cola}-poison</c> para revisarlo a mano.
/// </summary>
public sealed class ConsumidorColaWorker(
    QueueServiceClient colas,
    OpcionesServicio opciones,
    NotificacionService servicio,
    ILogger<ConsumidorColaWorker> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EsperaTrasError = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Visibilidad = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cola = colas.GetQueueClient(opciones.QueueName);
        var poison = colas.GetQueueClient($"{opciones.QueueName}-poison");
        var listo = false;
        var espera = EsperaMinima;

        logger.LogInformation("Consumidor de la cola '{Cola}' iniciado", opciones.QueueName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!listo)
                {
                    await cola.CreateIfNotExistsAsync(cancellationToken: ct);
                    await poison.CreateIfNotExistsAsync(cancellationToken: ct);
                    listo = true;
                }

                QueueMessage[] mensajes = (await cola.ReceiveMessagesAsync(16, Visibilidad, ct)).Value;
                if (mensajes.Length == 0)
                {
                    await Task.Delay(espera, ct);
                    espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, EsperaMaxima.Ticks));
                    continue;
                }

                espera = EsperaMinima;
                foreach (var mensaje in mensajes)
                    await ProcesarAsync(cola, poison, mensaje, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Storage caído o mal configurado: no tumbar el servicio HTTP, reintentar luego.
                logger.LogError(ex, "Error leyendo la cola '{Cola}'; se reintenta en {Segundos} s",
                    opciones.QueueName, EsperaTrasError.TotalSeconds);
                try { await Task.Delay(EsperaTrasError, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ProcesarAsync(QueueClient cola, QueueClient poison, QueueMessage mensaje, CancellationToken ct)
    {
        var texto = mensaje.Body.ToString();
        EventoPresupuesto evento;
        try
        {
            evento = LectorEventos.Leer(texto, servicio.AhoraMs());
        }
        catch (EventoInvalidoException ex)
        {
            logger.LogWarning("Mensaje {IdMensaje} inválido ({Motivo}); se mueve a la cola poison",
                mensaje.MessageId, ex.Message);
            await MoverAPoisonAsync(cola, poison, mensaje, texto, ct);
            return;
        }

        using var _ = logger.BeginScope("correlationId:{correlationId}", evento.CorrelationId);
        try
        {
            var n = await servicio.ProcesarEventoAsync(evento, mensaje.MessageId, ct);
            await cola.DeleteMessageAsync(mensaje.MessageId, mensaje.PopReceipt, ct);
            logger.LogInformation("Evento {Tipo} de {UsuarioId} procesado como notificación {Id}",
                evento.Tipo, evento.UsuarioId, n.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (mensaje.DequeueCount >= opciones.MaxIntentos)
            {
                logger.LogError(ex, "Mensaje {IdMensaje} falló {Intentos} veces; se mueve a la cola poison",
                    mensaje.MessageId, mensaje.DequeueCount);
                await MoverAPoisonAsync(cola, poison, mensaje, texto, ct);
            }
            else
            {
                logger.LogWarning(ex, "Mensaje {IdMensaje} falló (intento {Intento}); se reintentará",
                    mensaje.MessageId, mensaje.DequeueCount);
            }
        }
    }

    private static async Task MoverAPoisonAsync(
        QueueClient cola, QueueClient poison, QueueMessage mensaje, string texto, CancellationToken ct)
    {
        await poison.SendMessageAsync(texto, ct);
        await cola.DeleteMessageAsync(mensaje.MessageId, mensaje.PopReceipt, ct);
    }
}
