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

/// <summary>Reglas del Excel de carga masiva (sección "CargaMasiva"). Ver docs: reglas del área de suscripción.</summary>
public sealed class CargaMasivaOptions
{
    public const string Seccion = "CargaMasiva";

    /// <summary>
    /// Valor de datos.tipoDocumento del formulario → clase de documento (DNI, CARNET o PASAPORTE).
    /// DNI → TIPOPERSONA L / TIPODOC 1; CARNET → E / 2; PASAPORTE → E / 4.
    /// </summary>
    public Dictionary<string, string> TiposDocumento { get; set; } = [];

    /// <summary>Tipos de formulario con continuidad de cobertura: CONTINUIDAD_COBERTURA = S (el resto N).</summary>
    public List<string> FormulariosContinuidad { get; set; } = [];

    public static readonly Dictionary<string, string> TiposDocumentoPorDefecto = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "DNI", ["DNI"] = "DNI",
        ["2"] = "CARNET", ["CARNET"] = "CARNET", ["CARNET DE RESIDENCIA"] = "CARNET", ["CARNET DE RESIDENTE"] = "CARNET",
        ["3"] = "PASAPORTE", ["4"] = "PASAPORTE", ["PASAPORTE"] = "PASAPORTE",
    };

    public static readonly string[] FormulariosContinuidadPorDefecto = ["SPN-F.GTP-101", "101"];
}
