namespace Enrolamiento.Enlaces;

/// <summary>Los cinco campos del contrato que viajan cifrados dentro del enlace.</summary>
public sealed record DatosEnrolamiento(
    string TipoFormulario,
    string NombreContratante,
    string NumeroPoliza,
    decimal SumaAsegurada,
    string Correo);

public sealed record ErrorCampo(string Campo, string Mensaje);

public sealed record EnlaceGenerado(Uri Url, DateTimeOffset ExpiraEnUtc, int Version);

public enum MotivoRechazo
{
    /// <summary>Vacío, demasiado largo o con caracteres no válidos.</summary>
    Malformado,
    /// <summary>Alterado, vencido, de otro propósito, de otra llave o de una versión no soportada. No se distingue a propósito.</summary>
    InvalidoOVencido,
}

public sealed record ResultadoValidacion(
    bool Valido,
    DatosEnrolamiento? Datos,
    DateTimeOffset? ExpiraEnUtc,
    int? Version,
    MotivoRechazo? Motivo)
{
    public static ResultadoValidacion Ok(DatosEnrolamiento d, DateTimeOffset expira, int version) => new(true, d, expira, version, null);
    public static ResultadoValidacion Rechazo(MotivoRechazo m) => new(false, null, null, null, m);
}

public sealed class DatosEnrolamientoInvalidosException(IReadOnlyList<ErrorCampo> errores)
    : Exception("Los datos de enrolamiento no son válidos.")
{
    public IReadOnlyList<ErrorCampo> Errores { get; } = errores;
}
