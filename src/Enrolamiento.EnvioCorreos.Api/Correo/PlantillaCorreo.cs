using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Correo;

public sealed record DatosPlantilla(string NombreCliente, string NombreContratante, string Agrupador,
    string TipoFormulario, Uri Enlace, DateTimeOffset ExpiraEnUtc);

public sealed record CorreoRenderizado(string Asunto, bool AsuntoModificado, string Html, IReadOnlyList<string> Errores);

/// <summary>
/// Correo fijo (no editable) armado sobre el diseño "Completa el formulario y asegura tu mundo":
/// imágenes recortadas del diseño (encabezado, botón, logo y pie) + textos dinámicos en HTML
/// (nombre del cliente y del contratante). Las imágenes se sirven desde <see cref="CorreoOptions.ImagenesBaseUrl"/>.
///
/// pr_mail declara text/html; charset=iso-8859-1 y escribe con utl_smtp: todo carácter no ASCII del HTML
/// pasa a entidad (&amp;#225;) y el asunto se envía sin tildes.
/// </summary>
public sealed class PlantillaCorreo(IOptions<CorreoOptions> opciones)
{
    public static readonly string[] Imagenes = ["encabezado.jpg", "boton-enrolamiento.png", "logo-ficohsa-seguros.png", "pie.png"];

    private const string Azul = "#0158CB";
    private const string Texto = "#002870";
    private static readonly TimeZoneInfo Honduras = BuscarZona();
    private readonly CorreoOptions _o = opciones.Value;

    public bool ImagenesConfiguradas => !string.IsNullOrWhiteSpace(_o.ImagenesBaseUrl);

    /// <summary>Errores que impiden enviar, independientes de la fila.</summary>
    public List<string> ValidarEnvio()
    {
        var errores = new List<string>();
        // En modo Simulado (pruebas) no se exige: el correo no sale del portal.
        if (!ImagenesConfiguradas && _o.Modo == ModoEnvio.Oracle)
            errores.Add("Las imágenes del correo no están configuradas (Correo:ImagenesBaseUrl). Configure la URL pública antes de enviar.");
        if (string.IsNullOrWhiteSpace(_o.Asunto)) errores.Add("Falta el asunto del correo (Correo:Asunto).");
        return errores;
    }

    /// <param name="baseImagenes">URL de las imágenes; si es null se usa la configurada (obligatoria para enviar).</param>
    public CorreoRenderizado Renderizar(DatosPlantilla d, string? baseImagenes = null)
    {
        var errores = new List<string>();
        var img = baseImagenes ?? _o.ImagenesBaseUrl ?? "";
        if (img.Length > 0 && !img.EndsWith('/')) img += "/";

        string E(string v) => WebUtility.HtmlEncode(v);
        var enlace = E(d.Enlace.AbsoluteUri);
        var vence = TimeZoneInfo.ConvertTime(d.ExpiraEnUtc, Honduras).ToString("dd/MM/yyyy hh:mm tt", new CultureInfo("es-HN"));
        const string fuente = "font-family:Arial,Helvetica,sans-serif;";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          // Modo claro: evita que iOS/Android inviertan colores y pinten fondos detrás de las imágenes.
          .Append("<meta name=\"color-scheme\" content=\"light only\"><meta name=\"supported-color-schemes\" content=\"light\"></head>")
          .Append("<body style=\"margin:0;padding:0;background:#f2f5fa;\" bgcolor=\"#f2f5fa\">")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f2f5fa;\"><tr><td align=\"center\" style=\"padding:16px 8px;\">")
          .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" bgcolor=\"#ffffff\" style=\"width:600px;max-width:100%;background:#ffffff;\">")
          // Encabezado (foto + "Completa el formulario y asegura tu mundo")
          .Append($"<tr><td><img src=\"{img}encabezado.jpg\" width=\"600\" alt=\"Tu bienestar merece estar asegurado. Completa el formulario y asegura tu mundo\" style=\"display:block;width:100%;max-width:600px;height:auto;border:0;\"></td></tr>")
          // Saludo
          .Append($"<tr><td style=\"padding:32px 40px 0 40px;{fuente}font-size:20px;line-height:1.4;color:{Texto};\">")
          .Append($"Estimado(a) <strong>{E(d.NombreCliente)}</strong></td></tr>")
          // Mensaje con el contratante
          .Append($"<tr><td style=\"padding:20px 40px 0 40px;{fuente}font-size:17px;line-height:1.5;color:{Texto};\">")
          .Append("Completa el siguiente formulario para iniciar tu proceso de enrolamiento y acceder al programa de seguro contratado por ")
          .Append($"<strong>{E(d.NombreContratante)}</strong>.</td></tr>")
          // Botón (imagen del diseño enlazada) + logo
          // Botón y logo en celdas con ancho porcentual y separación: en celulares se achican sin pegarse.
          .Append("<tr><td style=\"padding:32px 24px 8px 24px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>")
          .Append($"<td width=\"55%\" align=\"left\" valign=\"middle\" bgcolor=\"#ffffff\"><a href=\"{enlace}\" target=\"_blank\" style=\"text-decoration:none;\">")
          .Append($"<img src=\"{img}boton-enrolamiento.png\" width=\"230\" alt=\"Inicia tu enrolamiento aquí\" style=\"display:block;width:100%;max-width:230px;height:auto;border:0;{fuente}font-size:16px;font-weight:bold;color:{Azul};\"></a></td>")
          .Append("<td width=\"10%\" style=\"font-size:0;line-height:0;\">&nbsp;</td>")
          .Append($"<td width=\"35%\" align=\"right\" valign=\"middle\" bgcolor=\"#ffffff\"><img src=\"{img}logo-ficohsa-seguros.png\" width=\"160\" alt=\"Ficohsa Seguros\" style=\"display:block;width:100%;max-width:160px;height:auto;border:0;\"></td>")
          .Append("</tr></table></td></tr>")
          // Enlace de respaldo y vencimiento
          .Append($"<tr><td style=\"padding:8px 40px 24px 40px;{fuente}font-size:12px;line-height:1.5;color:#5b6676;\">")
          .Append($"Si el botón no funciona, <a href=\"{enlace}\" target=\"_blank\" style=\"color:{Azul};\">haz clic aquí</a>. ")
          .Append($"Este enlace es personal y vence el {E(vence)}{(vence.EndsWith('.') ? "" : ".")}</td></tr>")
          // Pie (consultas, WhatsApp, teléfonos)
          .Append($"<tr><td bgcolor=\"#ffffff\" style=\"background:#ffffff;line-height:0;\"><img src=\"{img}pie.png\" width=\"600\" alt=\"¿Tienes consultas? Escríbenos por WhatsApp al 9494-3426, llámanos al 4747 o al 2276-3960, o visita nuestras oficinas de Ficohsa Seguros a nivel nacional.\" style=\"display:block;width:100%;max-width:600px;height:auto;border:0;\"></td></tr>")
          .Append("</table></td></tr></table></body></html>");

        var html = LimitarLineas(AAsciiSeguro(sb.ToString()));
        // El asunto admite {{NombreContratante}} (p. ej. "Programa de enrolamiento de seguro | {{NombreContratante}}").
        var asunto = (_o.Asunto ?? "").Replace("{{NombreContratante}}", d.NombreContratante, StringComparison.OrdinalIgnoreCase).Trim();
        var asuntoAscii = SinTildes(asunto);
        if (html.Length > _o.LongitudMaximaMensaje)
            errores.Add($"El correo mide {html.Length} caracteres y supera el máximo de {_o.LongitudMaximaMensaje} de pr_mail.");
        return new CorreoRenderizado(asuntoAscii, asuntoAscii != asunto, html, errores);
    }

    /// <summary>
    /// SMTP (RFC 5321) admite como máximo 998 caracteres por línea: un HTML en una sola línea larga puede ser
    /// rechazado o descartado por el servidor de correo (y pr_mail no reporta el error). Se parte el HTML en
    /// líneas de hasta <paramref name="maximo"/> caracteres cortando en espacios o después de "&gt;", lugares donde
    /// un salto de línea no cambia cómo se ve. Ninguna línea queda empezando con "." (punto de fin de datos en SMTP).
    /// </summary>
    public static string LimitarLineas(string html, int maximo = 900)
    {
        var sb = new StringBuilder(html.Length + html.Length / maximo * 2 + 16);
        var linea = 0;
        var ultimoCorte = -1;      // posición en sb donde se puede cortar (después de un espacio o de '>')
        for (int i = 0; i < html.Length; i++)
        {
            var c = html[i];
            sb.Append(c);
            if (c == '\n') { linea = 0; ultimoCorte = -1; continue; }
            linea++;
            var siguiente = i + 1 < html.Length ? html[i + 1] : '\0';
            if ((c == ' ' || c == '>') && siguiente != '.' && siguiente != '\r' && siguiente != '\n') ultimoCorte = sb.Length;
            if (linea >= maximo && ultimoCorte > 0)
            {
                sb.Insert(ultimoCorte, "\r\n");
                linea = sb.Length - (ultimoCorte + 2);
                ultimoCorte = -1;
            }
        }
        return sb.ToString();
    }

    /// <summary>Todo carácter fuera de ASCII imprimible pasa a entidad numérica.</summary>
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
}
