using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Enlaces;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el generador/validador de enlaces. Lo puede usar también el backend del formulario
    /// (mismo NombreAplicacion, misma variable de llaves y mismos propósitos) para validar sin llamada HTTP.
    /// </summary>
    public static IServiceCollection AddEnlacesEnrolamiento(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddOptions<EnlacesOptions>()
            .Bind(configuracion.GetSection(EnlacesOptions.Seccion))
            .PostConfigure(o =>
            {
                if (o.TiposFormularioPermitidos.Count == 0) o.TiposFormularioPermitidos.AddRange(EnlacesOptions.TiposPorDefecto);
                o.TiposFormularioPermitidos = o.TiposFormularioPermitidos.Select(ValidadorDatosEnrolamiento.Compactar)
                    .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            })
            .Validate(o => Uri.TryCreate(o.UrlPublicaBase, UriKind.Absolute, out var u)
                           && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment)
                           && u.AbsolutePath.EndsWith('/'),
                "Enlaces:UrlPublicaBase debe ser una URL HTTPS absoluta, terminada en '/', sin query ni fragmento.")
            .Validate(o => o.VigenciaHoras > 0 && o.VigenciaHoras <= o.VigenciaMaximaHoras,
                "Enlaces:VigenciaHoras debe ser positiva y no mayor que VigenciaMaximaHoras.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.NombreAplicacion), "Enlaces:NombreAplicacion es obligatorio.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.NombreParametro), "Enlaces:NombreParametro es obligatorio.")
            .ValidateOnStart();

        var nombreApp = configuracion[$"{EnlacesOptions.Seccion}:{nameof(EnlacesOptions.NombreAplicacion)}"] ?? new EnlacesOptions().NombreAplicacion;
        services.AddDataProtection().SetApplicationName(nombreApp).DisableAutomaticKeyGeneration();
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<EnlacesOptions>>((km, o) =>
            km.XmlRepository = new LlavesDesdeVariableEntorno(o.Value.VariableEntornoLlaves));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ValidadorDatosEnrolamiento>();
        services.TryAddSingleton<IEnlaceProtegidoService, EnlaceProtegidoService>();
        return services;
    }

    /// <summary>Prueba de ida y vuelta al arrancar: falla explícitamente si no hay llaves utilizables.</summary>
    public static void VerificarLlavesEnlaces(this IServiceProvider sp)
    {
        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Enrolamiento", "Verificacion");
        if (protector.Unprotect(protector.Protect("ok")) != "ok")
            throw new InvalidOperationException("La verificación de llaves de Data Protection falló.");
    }
}
