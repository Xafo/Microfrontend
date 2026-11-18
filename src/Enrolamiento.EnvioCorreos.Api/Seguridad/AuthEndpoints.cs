using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Seguridad;

public sealed record SolicitudLogin(string? Usuario, string? Clave);

public static class AuthEndpoints
{
    public const string PoliticaIntentos = "login";
    /// <summary>Cabecera obligatoria en peticiones que modifican (POST): un sitio externo no puede enviarla sin CORS.</summary>
    public const string CabeceraSolicitud = "X-Solicitud";

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        auth.MapPost("/login", async (SolicitudLogin s, IOptions<LoginOptions> opciones, HttpContext http, ILoggerFactory logs) =>
        {
            var o = opciones.Value;
            var usuario = (s.Usuario ?? "").Trim();
            // Se calcula el hash siempre (aunque el usuario no coincida) para no revelar por tiempo cuál dato falló.
            var claveOk = HashClave.Verificar(s.Clave ?? "", o.ClaveHash);
            var usuarioOk = usuario.Length > 0 && string.Equals(usuario, o.Usuario, StringComparison.OrdinalIgnoreCase);

            if (!(claveOk && usuarioOk))
            {
                logs.CreateLogger("Login").LogWarning("Inicio de sesión fallido desde {Ip}", http.Connection.RemoteIpAddress);
                return Results.Problem("Usuario o contraseña incorrectos.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var identidad = new ClaimsIdentity([new Claim(ClaimTypes.Name, o.Usuario)], CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidad));
            return Results.Ok(new { usuario = o.Usuario });
        }).RequireRateLimiting(PoliticaIntentos);

        auth.MapGet("/sesion", (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { usuario = http.User.Identity.Name })
                : Results.Problem("Sesión no iniciada.", statusCode: StatusCodes.Status401Unauthorized));

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    /// <summary>Lee la contraseña sin mostrarla en pantalla y imprime el hash para la variable Login__ClaveHash.</summary>
    public static void GenerarClaveDesdeConsola()
    {
        string Leer(string mensaje)
        {
            Console.Error.Write(mensaje);
            if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Error.Write("\b \b"); } continue; }
                if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Error.Write('*'); }
            }
            Console.Error.WriteLine();
            return sb.ToString();
        }

        var clave = Leer("Contraseña nueva: ");
        if (clave.Length < 10) { Console.Error.WriteLine("La contraseña debe tener al menos 10 caracteres."); Environment.Exit(1); }
        if (Leer("Repita la contraseña: ") != clave) { Console.Error.WriteLine("Las contraseñas no coinciden."); Environment.Exit(1); }
        Console.WriteLine(HashClave.Crear(clave));
    }
}
