# ms-notificaciones

Microservicio de notificaciones de **EstudiAmbre** (Diseño de Software, Fase II).
.NET 10 · Azure Queue Storage · Azure Table Storage · Azure SignalR (modo Serverless).

```
ms-gastos-plan ──evento──► Queue "eventos-presupuesto" ──► ConsumidorColaWorker
                                                             │
                                       Table Storage ◄───────┤ guarda
                                       Azure SignalR ◄───────┘ envía al usuario (sub)
                                             │
React (campana) ◄──WebSocket directo──────────┘
React ──APIM──► GET/PATCH /v1/notificaciones, POST /v1/notificaciones/negotiate
```

## Endpoints

Todos bajo `/v1`, con `Authorization: Bearer <Google JWT>` (APIM valida la firma; aquí solo se lee `sub`).

| Método | Ruta | Respuesta |
|---|---|---|
| GET | `/notificaciones?page=0&size=10` | `{content, page, size, totalElements, totalPages}`, de la más nueva a la más vieja. `size` 1–50 |
| POST | `/notificaciones` | Body `{tipo?, titulo, mensaje}` → 201 con la notificación. También la envía en vivo (útil para Postman) |
| PATCH | `/notificaciones/{id}` | Body `{"leida": true}` → 200 con la notificación. Otro campo → 400. De otro usuario → 404 |
| POST | `/notificaciones/negotiate` | `{url, accessToken}` para conectarse a Azure SignalR. 503 si no hay SignalR configurado |
| GET | `/health`, `/swagger` | Sin autenticación ni certificado |

Notificación: `{id, tipo, titulo, mensaje, leida, fecha}` (fecha en Epoch ms, UTC).
Errores: `{status, message, correlationId}`. Toda respuesta trae `X-Correlation-Id`.

## Contrato con Gastos y Plan (evento en la cola)

Cola `eventos-presupuesto` en la misma Storage Account. Mensaje en **JSON plano** (se acepta también Base64):

```json
{"usuarioId":"<sub>","tipo":"PRESUPUESTO_80","categoria":"Comida","porcentaje":82.5,"fecha":1794700000000,"correlationId":"..."}
```

- `tipo`: `PRESUPUESTO_80` o `PRESUPUESTO_100`. Otro valor → el mensaje va a `eventos-presupuesto-poison`.
- `porcentaje`: número de 0 a 100+ (no fracción). `fecha`: Epoch ms (si viene en segundos, se convierte).
- `usuarioId`: el `sub` del JWT de Google, el mismo que usa el frontend al negociar.

## Contrato con el frontend (tiempo real)

1. `POST /v1/notificaciones/negotiate` por APIM → `{url, accessToken}`.
2. Conectar directo a Azure SignalR (APIM Consumption no soporta WebSocket):
   ```js
   const c = new signalR.HubConnectionBuilder()
     .withUrl(url, { accessTokenFactory: () => accessToken })
     .withAutomaticReconnect().build();
   c.on('notificacion', n => prepend(n)); // n = {id, tipo, titulo, mensaje, leida, fecha}
   await c.start();
   ```
3. El token dura 1 hora: si la conexión se cierra después de eso, volver a negociar.

`tools/cliente-signalr.html` hace exactamente esto, para probar sin el frontend.

## Estructura (puertos y adaptadores)

```
src/MsNotificaciones/
  Dominio/          Notificacion, EventoPresupuesto (sin dependencias)
  Aplicacion/       NotificacionService (casos de uso), Puertos (interfaces), LectorEventos
  Api/              Endpoints, DTOs, lectura del sub, formato de error
  Infraestructura/
    Http/           Middlewares: X-Correlation-Id, errores, certificado cliente
    Adaptadores/    Table Storage, consumidor de la cola, Azure SignalR
    Memoria/        Adaptadores en memoria para correr en local sin Azure
```

## Correr en local

Requiere el SDK de .NET 10.

**Sin nada de Azure** (repositorio en memoria, sin cola ni SignalR):
```bash
cd src/MsNotificaciones && dotnet run      # http://localhost:5080/swagger
```

**Con cola y tablas locales** ([Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)):
```bash
npm i -g azurite && azurite --silent --location .azurite
cp src/MsNotificaciones/appsettings.Local.example.json src/MsNotificaciones/appsettings.Local.json
# dejá STORAGE_CONNECTION_STRING = "UseDevelopmentStorage=true"
```
Para meter un evento a mano: Azure Storage Explorer (Emulator → Queues → `eventos-presupuesto` → Add Message,
pegando `tools/evento-ejemplo.json`) o `az storage message put --queue-name eventos-presupuesto --content "$(cat tools/evento-ejemplo.json)" --connection-string "<cadena>"`.

**Con Azure SignalR real:** poné `AZURE_SIGNALR_CONNECTION_STRING` en `appsettings.Local.json`, corré el servicio y abrí
`tools/cliente-signalr.html` (botón "Generar JWT falso" con el `sub` de `evento-ejemplo.json`, después "Negociar y conectar").
Al meter el evento en la cola, la notificación aparece en la página.

En Development sin `CLIENT_CERT_THUMBPRINT` no se exige certificado y CORS queda abierto. En cualquier otro ambiente sin huella, todo responde 403.

## Variables

| Nombre | Tipo | Valor por defecto |
|---|---|---|
| `STORAGE_CONNECTION_STRING` | secret | — (obligatoria fuera de Development) |
| `AZURE_SIGNALR_CONNECTION_STRING` | secret | — (sin ella, `negotiate` responde 503) |
| `CLIENT_CERT_THUMBPRINT` | secret | — (SHA-1; con o sin `:`) |
| `QUEUE_NAME` | variable | `eventos-presupuesto` |
| `TABLE_NAME` | variable | `notificaciones` |
| `SIGNALR_HUB` | variable | `notificaciones` |
| `QUEUE_MAX_INTENTOS` | variable | `5` |

Para el workflow (Environment `test` de GitHub): secret `AZURE_WEBAPP_PUBLISH_PROFILE` y variable `AZURE_WEBAPP_NAME`.

## Checklist de Azure

- App Service Linux, pila **.NET 10** (.NET 8 deja de tener soporte el 10 nov 2026).
- **Always On** activado (plan B1 o superior). En el plan Free el App Service se duerme y el consumidor de la cola deja de leer hasta que llegue una petición HTTP.
- *Incoming client certificates* = **Require**, con *exclusion paths* `/health;/swagger`.
- App settings con las variables de arriba.
- Azure SignalR (plan Free) con **Service mode = Serverless**.
- En APIM, operaciones para los cuatro endpoints, incluido `POST /notificaciones/negotiate`.

## Decisiones (para la defensa)

- **Table Storage** para persistir: el acceso siempre es por usuario y por fecha. `PartitionKey = sub`, `RowKey = (long.MaxValue − fecha)` con 19 dígitos + `_` + id, así la consulta devuelve las notificaciones ya ordenadas de la más nueva a la más vieja. Marcar como leída es un *merge* de una sola columna. Barato y sin esquema, y comparte Storage Account con la cola.
- **Idempotencia**: una notificación que viene de la cola usa el `MessageId` del mensaje como id. Si el mensaje se reprocesa (por ejemplo, el servicio se cayó antes de borrarlo), el `RowKey` choca (409) y no se duplica ni se reenvía.
- **Mensajes fallidos**: tras 5 intentos, o si el JSON es inválido, el mensaje pasa a `eventos-presupuesto-poison` en vez de reintentarse para siempre.
- **SignalR Serverless + `Clients.User(sub)`**: no hace falta hospedar un hub ni manejar grupos; Azure SignalR asocia cada conexión al `sub` que se puso en el token de `negotiate`, y un usuario con varias pestañas las recibe todas.
- **Paginación**: se recorre la partición del usuario para dar `totalElements` exacto. Con decenas de notificaciones por usuario es suficiente; con volúmenes grandes habría que pasar a tokens de continuación.
