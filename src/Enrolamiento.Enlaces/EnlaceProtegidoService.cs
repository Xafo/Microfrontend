using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Enlaces;

public interface IEnlaceProtegidoService
{
    /// <summary>Valida los datos, los cifra con vencimiento y arma el enlace sobre la URL pública configurada.</summary>
    /// <exception cref="DatosEnrolamientoInvalidosException">Si algún campo no es válido.</exception>
    EnlaceGenerado GenerarEnlace(DatosEnrolamiento datos);

    /// <summary>Descifra y valida el valor del parámetro "datos". Nunca lanza por contenido inválido.</summary>
    ResultadoValidacion ValidarContenido(string? contenidoProtegido);

    /// <summary>Enlace equivalente con los datos en texto claro. SOLO para mostrar en pantalla; nunca se envía.</summary>
    Uri ConstruirEnlaceReferenciaSinCifrar(DatosEnrolamiento datos);
}

public sealed class EnlaceProtegidoService : IEnlaceProtegidoService
{
    public const int VersionContrato = 1;

    /// <summary>Cadena de propósitos versionada. Cambiarla invalida todos los enlaces emitidos.</summary>
    public static readonly string[] Propositos = ["Enrolamiento", "EnlaceCorreo", "v1"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Sin escapar acentos/Ñ como \uXXXX: reduce el tamaño del enlace. El JSON nunca se inserta en HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ITimeLimitedDataProtector _protector;
    private readonly ValidadorDatosEnrolamiento _validador;
    private readonly EnlacesOptions _o;
    private readonly TimeProvider _reloj;
    private readonly Uri _base;

    public EnlaceProtegidoService(IDataProtectionProvider proveedor, ValidadorDatosEnrolamiento validador,
        IOptions<EnlacesOptions> opciones, TimeProvider reloj)
    {
        _protector = proveedor.CreateProtector(Propositos[0], Propositos[1..]).ToTimeLimitedDataProtector();
        _validador = validador;
        _o = opciones.Value;
        _reloj = reloj;
        _base = new Uri(_o.UrlPublicaBase, UriKind.Absolute);
    }

    public EnlaceGenerado GenerarEnlace(DatosEnrolamiento datos)
    {
        var errores = _validador.Validar(datos);
        if (errores.Count > 0) throw new DatosEnrolamientoInvalidosException(errores);

        var expira = _reloj.GetUtcNow().AddHours(_o.VigenciaHoras);
        var contenido = new ContenidoV1(VersionContrato, datos.TipoFormulario, datos.NombreContratante,
            datos.NumeroPoliza, datos.Correo);
        var protegido = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(contenido, Json), expira);
        var url = new Uri(QueryHelpers.AddQueryString(_base.AbsoluteUri, _o.NombreParametro, WebEncoders.Base64UrlEncode(protegido)));

        if (url.AbsoluteUri.Length > _o.LongitudMaximaUrl)
            throw new DatosEnrolamientoInvalidosException(
                [new("Enlace", $"El enlace mide {url.AbsoluteUri.Length} caracteres y supera el máximo de {_o.LongitudMaximaUrl}. Acorte el nombre.")]);

        return new EnlaceGenerado(url, expira, VersionContrato);
    }

    public ResultadoValidacion ValidarContenido(string? contenidoProtegido)
    {
        if (string.IsNullOrWhiteSpace(contenidoProtegido) || contenidoProtegido.Length > _o.LongitudMaximaUrl)
            return ResultadoValidacion.Rechazo(MotivoRechazo.Malformado);

        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(contenidoProtegido); }
        catch (FormatException) { return ResultadoValidacion.Rechazo(MotivoRechazo.Malformado); }

        try
        {
            var plano = _protector.Unprotect(bytes, out var expira);
            var c = JsonSerializer.Deserialize<ContenidoV1>(plano, Json);
            if (c is null || c.Version != VersionContrato) return ResultadoValidacion.Rechazo(MotivoRechazo.InvalidoOVencido);

            var datos = new DatosEnrolamiento(c.TipoFormulario, c.NombreContratante, c.NumeroPoliza, c.Correo);
            if (_validador.Validar(datos).Count > 0) return ResultadoValidacion.Rechazo(MotivoRechazo.InvalidoOVencido);

            return ResultadoValidacion.Ok(datos, expira, c.Version);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return ResultadoValidacion.Rechazo(MotivoRechazo.InvalidoOVencido);
        }
    }

    public Uri ConstruirEnlaceReferenciaSinCifrar(DatosEnrolamiento d) =>
        new(QueryHelpers.AddQueryString(_base.AbsoluteUri, new Dictionary<string, string?>
        {
            ["tipoFormulario"] = d.TipoFormulario,
            ["nombreContratante"] = d.NombreContratante,
            ["numeroPoliza"] = d.NumeroPoliza,
            ["correo"] = d.Correo,
        }));

    /// <summary>DTO explícito que viaja cifrado. Agregar campos exige una versión nueva.</summary>
    private sealed record ContenidoV1(
        [property: JsonRequired] int Version,
        [property: JsonRequired] string TipoFormulario,
        [property: JsonRequired] string NombreContratante,
        [property: JsonRequired] string NumeroPoliza,
        [property: JsonRequired] string Correo);
}
