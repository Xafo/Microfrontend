namespace Enrolamiento.EnvioCorreos.Api.Seguridad;

/// <summary>
/// Credenciales del portal. Se leen de variables de entorno (Login__Usuario, Login__ClaveHash)
/// o, en local, de dotnet user-secrets. Nunca en appsettings ni en Git.
/// </summary>
public sealed class LoginOptions
{
    public const string Seccion = "Login";
    public string Usuario { get; set; } = "";
    /// <summary>Hash generado con: dotnet run -- generar-clave</summary>
    public string ClaveHash { get; set; } = "";
    public int DuracionSesionHoras { get; set; } = 8;
    public int IntentosPorMinuto { get; set; } = 5;
}
