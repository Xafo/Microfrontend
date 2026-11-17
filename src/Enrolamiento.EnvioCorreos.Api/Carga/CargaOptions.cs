namespace Enrolamiento.EnvioCorreos.Api.Carga;

public sealed class CargaOptions
{
    public const string Seccion = "Carga";
    public long TamanoMaximoBytes { get; set; } = 5 * 1024 * 1024;
    public int FilasMaximas { get; set; } = 2000;
    /// <summary>Hoja preferida; si no existe se usa la primera hoja que tenga los encabezados esperados.</summary>
    public string NombreHoja { get; set; } = "Destinatarios";
    public int LongitudMaximaNombreCliente { get; set; } = 200;
}
