using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Enlaces;

/// <summary>Reglas de servidor para los cinco campos. Se aplican al generar y otra vez al validar.</summary>
public sealed partial class ValidadorDatosEnrolamiento(IOptions<EnlacesOptions> opciones)
{
    private readonly EnlacesOptions _o = opciones.Value;

    public IReadOnlyList<ErrorCampo> Validar(DatosEnrolamiento d)
    {
        var errores = new List<ErrorCampo>();

        if (string.IsNullOrWhiteSpace(d.TipoFormulario))
            errores.Add(new("TipoFormulario", "Es obligatorio."));
        else if (!_o.TiposFormularioPermitidos.Contains(d.TipoFormulario))
            errores.Add(new("TipoFormulario", $"Tipo '{d.TipoFormulario}' no permitido. Permitidos: {string.Join(", ", _o.TiposFormularioPermitidos)}."));

        Texto(errores, "NombreContratante", d.NombreContratante, _o.LongitudMaximaNombreContratante);

        if (Texto(errores, "NumeroPoliza", d.NumeroPoliza, _o.LongitudMaximaNumeroPoliza) && !PolizaRegex().IsMatch(d.NumeroPoliza))
            errores.Add(new("NumeroPoliza", "Solo se permiten letras, números, guion y barra."));

        if (d.SumaAsegurada <= 0)
            errores.Add(new("SumaAsegurada", "Debe ser mayor que cero."));
        else if (d.SumaAsegurada > _o.SumaAseguradaMaxima)
            errores.Add(new("SumaAsegurada", $"Supera el máximo permitido ({_o.SumaAseguradaMaxima:N2})."));
        else if (d.SumaAsegurada.Scale > _o.DecimalesMaximosSuma && decimal.Round(d.SumaAsegurada, _o.DecimalesMaximosSuma) != d.SumaAsegurada)
            errores.Add(new("SumaAsegurada", $"Máximo {_o.DecimalesMaximosSuma} decimales; no se redondea."));

        if (Texto(errores, "Correo", d.Correo, _o.LongitudMaximaCorreo) && !EsCorreoValido(d.Correo))
            errores.Add(new("Correo", "Formato de correo no válido."));

        return errores;
    }

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
