namespace Enrolamiento.Enlaces;

/// <summary>
/// Configuración de los enlaces protegidos. Sección "Enlaces" de appsettings.{Ambiente}.json.
/// No contiene secretos: las llaves se leen de la variable de entorno indicada en <see cref="VariableEntornoLlaves"/>.
/// </summary>
public sealed class EnlacesOptions
{
    public const string Seccion = "Enlaces";

    /// <summary>URL pública base del formulario (HTTPS). Ej.: https://seguros.ficohsa.com/EnrolamientoHN/</summary>
    public string UrlPublicaBase { get; set; } = "";

    /// <summary>Nombre del parámetro de query que lleva el contenido protegido.</summary>
    public string NombreParametro { get; set; } = "datos";

    /// <summary>Vigencia del enlace en horas (aprobado: 48).</summary>
    public int VigenciaHoras { get; set; } = 48;

    /// <summary>Límite superior aceptado para la vigencia; se valida al arrancar.</summary>
    public int VigenciaMaximaHoras { get; set; } = 72;

    /// <summary>Largo máximo del enlace completo. IIS acepta 2048 caracteres de query por defecto.</summary>
    public int LongitudMaximaUrl { get; set; } = 2000;

    /// <summary>Nombre de aplicación estable de Data Protection. Debe ser igual en quien genera y quien valida.</summary>
    public string NombreAplicacion { get; set; } = "Ficohsa.EnrolamientoHN";

    /// <summary>Variable de entorno que contiene el key ring (XML o Base64 del XML).</summary>
    public string VariableEntornoLlaves { get; set; } = "ENROLAMIENTO_DP_LLAVES";

    public static readonly string[] TiposPorDefecto = ["61", "62", "63", "64", "101", "102", "161"];

    /// <summary>Vacío en configuración = <see cref="TiposPorDefecto"/>. (Inicia vacía: el binder agrega a la lista, no la reemplaza.)</summary>
    public List<string> TiposFormularioPermitidos { get; set; } = [];

    public int LongitudMaximaNombreContratante { get; set; } = 200;
    public int LongitudMaximaNumeroPoliza { get; set; } = 30;
    public int LongitudMaximaCorreo { get; set; } = 254;
    public decimal SumaAseguradaMaxima { get; set; } = 999_999_999_999.99m;
    public int DecimalesMaximosSuma { get; set; } = 2;
}
