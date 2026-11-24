namespace Enrolamiento.EnvioCorreos.Api.Campanas;

/// <summary>
/// API de solicitudes del formulario de enrolamiento (sección "SolicitudesApi").
/// Usuario y Clave van en variables de entorno: SolicitudesApi__Usuario y SolicitudesApi__Clave.
/// </summary>
public sealed class SolicitudesApiOptions
{
    public const string Seccion = "SolicitudesApi";

    /// <summary>URL base HTTPS terminada en "/". Ej.: https://enrolamientodigital.awsfs.gfficohsa.hn/</summary>
    public string UrlBase { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";
    public string RutaToken { get; set; } = "solicitud/auth/token";
    public string RutaListar { get; set; } = "solicitud/listar";
    /// <summary>Solo se exportan las solicitudes con este estado.</summary>
    public string EstadoIncluido { get; set; } = "ENVIADA";
    public int TiempoEsperaSegundos { get; set; } = 60;

    public bool Configurada() =>
        Uri.TryCreate(UrlBase, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && new[] { Usuario, Clave }.All(v => !string.IsNullOrWhiteSpace(v) && !v.Contains("__"));
}
