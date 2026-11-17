using Enrolamiento.Enlaces;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

/// <summary>Fila tal como vino en el archivo, ya normalizada (trim) y con sus errores de lectura/validación.</summary>
public sealed class FilaLeida
{
    public required int NumeroFila { get; init; }
    public required string TipoFormulario { get; init; }
    public required string NombreContratante { get; init; }
    public required string NombreCliente { get; init; }
    public required string NumeroPoliza { get; init; }
    public required string SumaAseguradaTexto { get; init; }
    public decimal? SumaAsegurada { get; init; }
    public required string Correo { get; init; }
    public List<ErrorCampo> Errores { get; } = [];

    public DatosEnrolamiento ADatos() => new(TipoFormulario, NombreContratante, NumeroPoliza, SumaAsegurada ?? 0, Correo);
}

public sealed record ResultadoLectura(IReadOnlyList<FilaLeida> Filas, IReadOnlyList<string> ErroresArchivo, string? Hoja);
