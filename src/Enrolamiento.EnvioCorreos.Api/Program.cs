using System.Threading.Channels;
using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Enrolamiento.EnvioCorreos.Api.Endpoints;
using Enrolamiento.EnvioCorreos.Api.Lotes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

// Utilidad: genera (o rota) el key ring para la variable de entorno ENROLAMIENTO_DP_LLAVES.
//   dotnet Enrolamiento.EnvioCorreos.Api.dll generar-llave [--rotar]
if (args.Length > 0 && args[0] == "generar-llave")
{
    var anterior = args.Contains("--rotar") ? Environment.GetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES") : null;
    Console.WriteLine(GeneradorLlaves.CrearBase64("Ficohsa.EnrolamientoHN", TimeSpan.FromDays(730), anterior));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var s = builder.Services;

s.AddProblemDetails();
s.AddMemoryCache();
s.AddEnlacesEnrolamiento(builder.Configuration);

s.AddOptions<CargaOptions>().Bind(builder.Configuration.GetSection(CargaOptions.Seccion));
s.AddOptions<LotesOptions>().Bind(builder.Configuration.GetSection(LotesOptions.Seccion));
s.AddOptions<OracleOptions>().Bind(builder.Configuration.GetSection(OracleOptions.Seccion));
s.AddOptions<CorreoOptions>().Bind(builder.Configuration.GetSection(CorreoOptions.Seccion))
    .Validate(o => o.Modo == ModoEnvio.Simulado || ValidadorDatosEnrolamiento.EsCorreoValido(o.Remitente),
        "Correo:Remitente debe ser un correo válido cuando Correo:Modo es Oracle.")
    .Validate(o => new[] { o.ImagenEncabezadoUrl, o.ImagenPieUrl }.All(u => string.IsNullOrWhiteSpace(u)
        || (Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == Uri.UriSchemeHttps)),
        "Las imágenes del correo deben ser URLs HTTPS públicas.")
    .ValidateOnStart();

s.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = builder.Configuration.GetValue("Carga:TamanoMaximoBytes", 5L * 1024 * 1024) + 64 * 1024);

s.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true }));
s.AddSingleton<AlmacenLotes>();
s.AddSingleton<LectorDestinatarios>();
s.AddSingleton<PlantillaCorreo>();
s.AddSingleton<ServicioLotes>();
s.AddHostedService<ProcesadorEnvios>();
s.AddSingleton<IEnviadorCorreo>(sp => sp.GetRequiredService<IOptions<CorreoOptions>>().Value.Modo switch
{
    ModoEnvio.Oracle => ActivatorUtilities.CreateInstance<EnviadorOracle>(sp),
    _ => ActivatorUtilities.CreateInstance<EnviadorSimulado>(sp),
});

var app = builder.Build();

// Fallar al arrancar si falta configuración crítica (sin modos silenciosos).
app.Services.VerificarLlavesEnlaces();
var correo = app.Services.GetRequiredService<IOptions<CorreoOptions>>().Value;
if (correo.Modo == ModoEnvio.Simulado && app.Environment.IsProduction())
    throw new InvalidOperationException("Correo:Modo=Simulado no está permitido en Production.");
if (correo.Modo == ModoEnvio.Oracle && !app.Services.GetRequiredService<IOptions<OracleOptions>>().Value.Completa())
    throw new InvalidOperationException("Configure Oracle:DataSource, Oracle:Usuario y Oracle:Clave (variables Oracle__Usuario / Oracle__Clave).");
app.Logger.LogInformation("Ambiente {Ambiente}. Envío: {Modo}. URL pública: {Url}", app.Environment.EnvironmentName, correo.Modo,
    app.Services.GetRequiredService<IOptions<EnlacesOptions>>().Value.UrlPublicaBase);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (ctx, next) =>
{
    // Respuestas con enlaces y datos personales: nunca en cachés compartidas.
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h.XFrameOptions = "DENY";
        return Task.CompletedTask;
    });
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapLotes();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
