using MsNotificaciones.Api;
using MsNotificaciones.Aplicacion;
using MsNotificaciones.Infraestructura;
using MsNotificaciones.Infraestructura.Http;

var builder = WebApplication.CreateBuilder(args);

// Valores locales (cadenas de conexión de prueba). Está en .gitignore.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Logs en JSON con scopes: cada línea lleva el correlationId de su petición o mensaje.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificacionService>();
builder.Services.AddInfraestructura(builder.Configuration, builder.Environment);
builder.Services.AddDocumentacion();

// Errores de binding (body mal formado, campos de más en PATCH) → excepción → JSON de error común.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

// En Azure el CORS lo resuelve APIM. Esto es solo para probar en local con tools/cliente-signalr.html.
if (builder.Environment.IsDevelopment())
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(CorrelationIdMiddleware.Header)));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ManejoErroresMiddleware>();
app.UseStatusCodePages(async contexto =>
{
    var status = contexto.HttpContext.Response.StatusCode;
    await Errores.EscribirAsync(contexto.HttpContext, status, Errores.MensajePorDefecto(status));
});
if (app.Environment.IsDevelopment()) app.UseCors();
app.UseMiddleware<CertificadoClienteMiddleware>();
app.UseMiddleware<SecretoApimMiddleware>();
app.UseDocumentacion();

app.MapGet("/health", () => Results.Ok(new { status = "UP", servicio = "ms-notificaciones" }))
    .WithTags("Salud");
app.MapNotificaciones();

app.Run();

public partial class Program;
