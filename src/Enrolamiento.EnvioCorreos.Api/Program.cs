using System.Threading.Channels;
using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Enrolamiento.EnvioCorreos.Api.Endpoints;
using Enrolamiento.EnvioCorreos.Api.Lotes;
using Enrolamiento.EnvioCorreos.Api.Seguridad;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
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

// Utilidad: pide la contraseña del portal (sin mostrarla) e imprime su hash para Login__ClaveHash.
//   dotnet run -- generar-clave
if (args.Length > 0 && args[0] == "generar-clave")
{
    AuthEndpoints.GenerarClaveDesdeConsola();
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Credenciales locales fuera de Git: appsettings.{Ambiente}.local.json (ignorado por Git y excluido de la publicación).
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: false);
// En local, el usuario y el hash del login pueden guardarse con "dotnet user-secrets" (Development ya los carga solo).
if (builder.Environment.IsEnvironment("Local")) builder.Configuration.AddUserSecrets<Program>(optional: true);
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

// Login: usuario y hash de contraseña desde variables de entorno (Login__Usuario, Login__ClaveHash).
s.AddOptions<LoginOptions>().Bind(builder.Configuration.GetSection(LoginOptions.Seccion))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Usuario), "Configure el usuario del portal en la variable Login__Usuario.")
    .Validate(o => HashClave.EsFormatoValido(o.ClaveHash),
        "Configure Login__ClaveHash con el hash generado por 'dotnet run -- generar-clave' (no la contraseña en texto).")
    .Validate(o => o.DuracionSesionHoras is > 0 and <= 24, "Login:DuracionSesionHoras debe estar entre 1 y 24.")
    .ValidateOnStart();
var login = builder.Configuration.GetSection(LoginOptions.Seccion).Get<LoginOptions>() ?? new LoginOptions();
s.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "Enrolamiento.Sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Secure automáticamente cuando el sitio usa HTTPS
    o.ExpireTimeSpan = TimeSpan.FromHours(login.DuracionSesionHoras);
    o.SlidingExpiration = true;
    // API: responder 401/403 en lugar de redirigir a una página de login.
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
s.AddAuthorization();
s.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.PoliticaIntentos, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, login.IntentosPorMinuto), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

s.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = builder.Configuration.GetValue("Carga:TamanoMaximoBytes", 5L * 1024 * 1024) + 64 * 1024);

s.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true }));
s.AddSingleton<AlmacenLotes>();
s.AddSingleton<LectorDestinatarios>();
s.AddSingleton<GeneradorPlantilla>();
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx, next) =>
{
    // Anti-CSRF: toda petición que modifica en /api debe traer la cabecera propia del portal.
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
        && !ctx.Request.Headers.ContainsKey(AuthEndpoints.CabeceraSolicitud))
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { title = "Solicitud no permitida." });
        return;
    }
    await next();
});
app.MapAuth();
app.MapLotes();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
