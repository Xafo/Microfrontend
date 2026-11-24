using System.Globalization;
using System.Text;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

public static class Columnas
{
    public const string TipoFormulario = "Tipo de Formulario";
    public const string NombreContratante = "Nombre del Contratante";
    public const string NombreCliente = "Nombre del Cliente";
    /// <summary>ID de la campaña (antes "Numero de Poliza"). Viaja en el campo numeroPoliza del enlace.</summary>
    public const string NumeroPoliza = "Agrupador";
    public const string Correo = "Correo";
    public const string Enlace = "Enlace";
    /// <summary>Ya no se usa. Se acepta en archivos anteriores y se ignora.</summary>
    public const string SumaAsegurada = "Suma Asegurada";

    public static readonly string[] Obligatorias = [TipoFormulario, NombreContratante, NombreCliente, NumeroPoliza, Correo];

    /// <summary>Opcionales: se aceptan pero se ignoran (el enlace lo genera el servidor; la suma ya no se usa).</summary>
    public static readonly string[] Opcionales = [Enlace, SumaAsegurada];

    /// <summary>Encabezados anteriores que se siguen aceptando (normalizado → columna actual).</summary>
    public static readonly Dictionary<string, string> Alias = new() { ["numero de poliza"] = NumeroPoliza };

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
