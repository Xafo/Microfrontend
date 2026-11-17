using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Correo;

public sealed record DatosPlantilla(string NombreCliente, string NombreContratante, string NumeroPoliza,
    string TipoFormulario, Uri Enlace, DateTimeOffset ExpiraEnUtc);

public sealed record CorreoRenderizado(string Asunto, bool AsuntoModificado, string Html, IReadOnlyList<string> Errores);

/// <summary>
/// Arma el HTML final: encabezado (imagen) + cuerpo editable con marcadores + pie (imagen).
/// pr_mail declara text/html; charset=iso-8859-1 y escribe con utl_smtp, por eso todo carácter no ASCII
/// se convierte en entidad HTML (&amp;#225;) y el asunto se envía sin tildes.
/// </summary>
public sealed partial class PlantillaCorreo(IOptions<CorreoOptions> opciones)
{
    public const string CuerpoPorDefecto =
        """
        <p>Estimado(a) {{NombreCliente}},</p>
        <p>Para continuar con el proceso de su póliza {{NumeroPoliza}}, le solicitamos completar el formulario de enrolamiento en el siguiente enlace:</p>
        <p style="margin:24px 0;"><a href="{{Enlace}}" style="background:#003DA5;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:4px;display:inline-block;font-weight:bold;">Completar formulario</a></p>
        <p>Saludos cordiales.</p>
        """;

    public static readonly string[] Marcadores =
        ["NombreCliente", "NombreContratante", "NumeroPoliza", "TipoFormulario", "Enlace", "FechaVencimiento"];

    private static readonly TimeZoneInfo Honduras = BuscarZona();
    private readonly CorreoOptions _o = opciones.Value;

    public string CuerpoInicial => string.IsNullOrWhiteSpace(_o.CuerpoPorDefecto) ? CuerpoPorDefecto : _o.CuerpoPorDefecto;

    /// <summary>Errores de la plantilla que no dependen de la fila.</summary>
    public List<string> ValidarPlantilla(string? asunto, string? cuerpo)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(asunto)) errores.Add("El asunto es obligatorio.");
        else if (asunto.Length > _o.LongitudMaximaAsunto) errores.Add($"El asunto supera {_o.LongitudMaximaAsunto} caracteres.");
        else if (asunto.Any(char.IsControl)) errores.Add("El asunto no puede tener saltos de línea.");
        if (string.IsNullOrWhiteSpace(cuerpo)) { errores.Add("El cuerpo es obligatorio."); return errores; }

        var desconocidos = MarcadorRegex().Matches(cuerpo + " " + asunto).Select(m => m.Groups[1].Value)
            .Where(m => !Marcadores.Contains(m)).Distinct().ToList();
        if (desconocidos.Count > 0) errores.Add($"Marcadores desconocidos: {string.Join(", ", desconocidos.Select(d => "{{" + d + "}}"))}.");
        if (!cuerpo.Contains("{{Enlace}}")) errores.Add("El cuerpo debe incluir el marcador {{Enlace}}.");
        if (asunto?.Contains("{{Enlace}}") == true) errores.Add("El enlace no puede ir en el asunto.");
        return errores;
    }

    public CorreoRenderizado Renderizar(string asunto, string cuerpo, DatosPlantilla d)
    {
        var errores = ValidarPlantilla(asunto, cuerpo);
        var vence = TimeZoneInfo.ConvertTime(d.ExpiraEnUtc, Honduras).ToString("dd/MM/yyyy hh:mm tt", new CultureInfo("es-HN"));
        var valores = new Dictionary<string, string>
        {
            ["NombreCliente"] = d.NombreCliente,
            ["NombreContratante"] = d.NombreContratante,
            ["NumeroPoliza"] = d.NumeroPoliza,
            ["TipoFormulario"] = d.TipoFormulario,
            ["Enlace"] = d.Enlace.AbsoluteUri,
            ["FechaVencimiento"] = vence,
        };

        var cuerpoFinal = MarcadorRegex().Replace(cuerpo, m =>
            valores.TryGetValue(m.Groups[1].Value, out var v) ? WebUtility.HtmlEncode(v) : m.Value);
        var html = AAsciiSeguro(Envolver(cuerpoFinal));

        var asuntoFinal = MarcadorRegex().Replace(asunto, m => valores.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        var asuntoAscii = SinTildes(asuntoFinal);

        if (html.Length > _o.LongitudMaximaMensaje)
            errores.Add($"El correo mide {html.Length} caracteres y supera el máximo de {_o.LongitudMaximaMensaje} de pr_mail.");
        return new CorreoRenderizado(asuntoAscii, asuntoAscii != asuntoFinal, html, errores);
    }

    private string Envolver(string cuerpo)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\"></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f4f6f9;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f6f9;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#1f2a37;\">");
        if (!string.IsNullOrWhiteSpace(_o.ImagenEncabezadoUrl))
            sb.Append($"<tr><td><img src=\"{WebUtility.HtmlEncode(_o.ImagenEncabezadoUrl)}\" width=\"600\" alt=\"Ficohsa Seguros\" style=\"display:block;width:100%;height:auto;border:0;\"></td></tr>");
        sb.Append("<tr><td style=\"padding:32px;\">").Append(cuerpo).Append("</td></tr>");
        if (!string.IsNullOrWhiteSpace(_o.ImagenPieUrl))
            sb.Append($"<tr><td><img src=\"{WebUtility.HtmlEncode(_o.ImagenPieUrl)}\" width=\"600\" alt=\"Ficohsa Seguros\" style=\"display:block;width:100%;height:auto;border:0;\"></td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    /// <summary>Todo carácter fuera de ASCII imprimible pasa a entidad numérica: el HTML llega igual sin importar la conversión de juego de caracteres.</summary>
    public static string AAsciiSeguro(string html)
    {
        var sb = new StringBuilder(html.Length + 64);
        foreach (var r in html.EnumerateRunes())
        {
            if (r.Value is 9 or 10 or 13 || (r.Value >= 32 && r.Value < 127)) sb.Append((char)r.Value);
            else sb.Append("&#").Append(r.Value).Append(';');
        }
        return sb.ToString();
    }

    public static string SinTildes(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { '¿' or '¡' => "", '“' or '”' => "\"", '‘' or '’' => "'", '–' or '—' => "-", _ when c < 127 => c.ToString(), _ => "" });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static TimeZoneInfo BuscarZona()
    {
        foreach (var id in new[] { "America/Tegucigalpa", "Central America Standard Time" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        return TimeZoneInfo.CreateCustomTimeZone("HN", TimeSpan.FromHours(-6), "Honduras", "Honduras");
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\s*\}\}")]
    private static partial Regex MarcadorRegex();
}
