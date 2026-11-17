using System.Globalization;
using System.Text.RegularExpressions;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

public static partial class ConversorValores
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

    /// <summary>
    /// Suma asegurada desde una celda numérica o texto. Acepta "2500000", "2500000.50", "2,500,000.50",
    /// "2.500.000,50", "2500000,50", con o sin "L"/"HNL"/"$". No redondea: devuelve null si no se puede interpretar sin ambigüedad.
    /// </summary>
    public static decimal? ASuma(object? valor)
    {
        switch (valor)
        {
            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 7.9e27) return null;
                return decimal.Parse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            case decimal m: return m;
            case int or long: return Convert.ToDecimal(valor, CultureInfo.InvariantCulture);
            case string s: return DesdeTexto(s);
            default: return null;
        }
    }

    private static decimal? DesdeTexto(string s)
    {
        s = Moneda().Replace(s.Trim(), "").Replace(" ", "").Replace(" ", "");
        if (s.Length == 0 || !Permitidos().IsMatch(s)) return null;

        int puntos = s.Count(c => c == '.'), comas = s.Count(c => c == ',');
        string normal;
        if (puntos > 0 && comas > 0)
        {
            var decimalEsComa = s.LastIndexOf(',') > s.LastIndexOf('.');
            var miles = decimalEsComa ? '.' : ',';
            var dec = decimalEsComa ? ',' : '.';
            if (s.Count(c => c == dec) != 1) return null;
            var partes = s.Split(dec);
            if (!Miles(partes[0], miles)) return null;
            normal = partes[0].Replace(miles.ToString(), "") + "." + partes[1];
        }
        else if (puntos + comas == 0) normal = s;
        else
        {
            var sep = puntos > 0 ? '.' : ',';
            var n = Math.Max(puntos, comas);
            var tras = s.Length - s.LastIndexOf(sep) - 1;
            if (n > 1) { if (!Miles(s, sep)) return null; normal = s.Replace(sep.ToString(), ""); }
            else if (sep == ',' && tras == 3) normal = s.Replace(",", "");      // 1,500 → miles (formato HN)
            else normal = s.Replace(',', '.');                                  // 1500.5 / 1500,50 → decimal
        }
        return decimal.TryParse(normal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private static bool Miles(string entero, char sep)
    {
        var g = entero.Split(sep);
        return g[0].Length is >= 1 and <= 3 && g.Skip(1).All(x => x.Length == 3);
    }

    [GeneratedRegex(@"^(HNL|L\.?|\$|USD)\s*|\s*(HNL|USD)$", RegexOptions.IgnoreCase)]
    private static partial Regex Moneda();

    [GeneratedRegex(@"^[0-9][0-9.,]*$")]
    private static partial Regex Permitidos();
}
