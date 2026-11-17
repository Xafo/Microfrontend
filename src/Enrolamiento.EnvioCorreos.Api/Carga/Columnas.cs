using System.Globalization;
using System.Text;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

public static class Columnas
{
    public const string TipoFormulario = "Tipo de Formulario";
    public const string NombreContratante = "Nombre del Contratante";
    public const string NombreCliente = "Nombre del Cliente";
    public const string NumeroPoliza = "Numero de Poliza";
    public const string SumaAsegurada = "Suma Asegurada";
    public const string Correo = "Correo";
    public const string Enlace = "Enlace";

    public static readonly string[] Obligatorias = [TipoFormulario, NombreContratante, NombreCliente, NumeroPoliza, SumaAsegurada, Correo];

    /// <summary>Opcionales: se aceptan pero se ignoran (el enlace lo genera el servidor).</summary>
    public static readonly string[] Opcionales = [Enlace];

    /// <summary>"Número de Póliza " → "numero de poliza".</summary>
    public static string Normalizar(string? encabezado)
    {
        if (string.IsNullOrWhiteSpace(encabezado)) return "";
        var sb = new StringBuilder();
        foreach (var c in encabezado.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
