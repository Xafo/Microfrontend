using System.Globalization;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

public static class ConversorValores
{
    /// <summary>Texto de una celda: números enteros sin notación científica ni ".0".</summary>
    public static string ATexto(object? valor) => valor switch
    {
        null or DBNull => "",
        string s => s.Trim(),
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => d.ToString("0", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        int or long => Convert.ToString(valor, CultureInfo.InvariantCulture)!,
        DateTime f => f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "VERDADERO" : "FALSO",
        _ => Convert.ToString(valor, CultureInfo.InvariantCulture)?.Trim() ?? "",
    };
}
