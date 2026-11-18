using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Enlaces;

/// <summary>Reglas de servidor para los cuatro campos. Se aplican al generar y otra vez al validar.</summary>
public sealed partial class ValidadorDatosEnrolamiento(IOptions<EnlacesOptions> opciones)
{
    private readonly EnlacesOptions _o = opciones.Value;

    public IReadOnlyList<ErrorCampo> Validar(DatosEnrolamiento d)
    {
        var errores = new List<ErrorCampo>();

        if (string.IsNullOrWhiteSpace(d.TipoFormulario))
            errores.Add(new("TipoFormulario", "Es obligatorio."));
        else if (!_o.TiposFormularioPermitidos.Contains(d.TipoFormulario, StringComparer.Ordinal))
            errores.Add(new("TipoFormulario", $"Tipo '{d.TipoFormulario}' no permitido. Debe ser el nombre completo: {string.Join(" | ", _o.TiposFormularioPermitidos)}."));

        Texto(errores, "NombreContratante", d.NombreContratante, _o.LongitudMaximaNombreContratante);

        if (Texto(errores, "NumeroPoliza", d.NumeroPoliza, _o.LongitudMaximaNumeroPoliza) && !PolizaRegex().IsMatch(d.NumeroPoliza))
            errores.Add(new("NumeroPoliza", "Solo se permiten letras, números, guion y barra."));

        if (Texto(errores, "Correo", d.Correo, _o.LongitudMaximaCorreo) && !EsCorreoValido(d.Correo))
            errores.Add(new("Correo", "Formato de correo no válido."));

        return errores;
    }

    /// <summary>
    /// Devuelve el nombre oficial del tipo si coincide sin importar mayúsculas ni espacios repetidos
    /// ("spn-f.gtp-61" → "SPN-F.GTP-61"); si no coincide, devuelve el texto tal cual para que se reporte el error.
    /// </summary>
    public string NormalizarTipo(string? tipo)
    {
        var t = Compactar(tipo);
        return _o.TiposFormularioPermitidos.FirstOrDefault(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase)) ?? t;
    }

    /// <summary>Quita espacios al inicio/fin y deja un solo espacio entre palabras.</summary>
    public static string Compactar(string? texto) =>
        string.Join(' ', (texto ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool EsCorreoValido(string correo) =>
        CorreoRegex().IsMatch(correo)
        && MailAddress.TryCreate(correo, out var m)
        && string.Equals(m.Address, correo, StringComparison.Ordinal);

    private static bool Texto(List<ErrorCampo> errores, string campo, string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor)) { errores.Add(new(campo, "Es obligatorio.")); return false; }
        if (valor.Length > max) { errores.Add(new(campo, $"Supera {max} caracteres.")); return false; }
        if (valor != valor.Trim()) { errores.Add(new(campo, "Tiene espacios al inicio o al final.")); return false; }
        if (valor.Any(char.IsControl)) { errores.Add(new(campo, "Contiene caracteres de control.")); return false; }
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9\-/]+$")]
    private static partial Regex PolizaRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-']+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex CorreoRegex();
}
