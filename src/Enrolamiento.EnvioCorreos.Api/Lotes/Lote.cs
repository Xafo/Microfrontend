using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;

namespace Enrolamiento.EnvioCorreos.Api.Lotes;

public enum EstadoLote { Preparado, Enviando, Finalizado }

public enum EstadoFila { Invalida, Lista, Pendiente, Enviando, Enviada, Fallida, Cancelada }

public sealed class FilaLote(FilaLeida datos)
{
    public FilaLeida Datos { get; } = datos;
    public EnlaceGenerado? Enlace { get; set; }
    public Uri? EnlaceReferencia { get; set; }
    public EstadoFila Estado { get; set; } = EstadoFila.Invalida;
    public string? Mensaje { get; set; }
    public DateTimeOffset? EnviadaUtc { get; set; }
}

/// <summary>Lote en memoria. Todo acceso a su estado se hace bajo <see cref="Bloqueo"/>.</summary>
public sealed class Lote
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string NombreArchivo { get; init; }
    public required string? Hoja { get; init; }
    public required DateTimeOffset CreadoUtc { get; init; }
    public required IReadOnlyList<FilaLote> Filas { get; init; }
    public required IReadOnlyList<string> ErroresArchivo { get; init; }
    public EstadoLote Estado { get; set; } = EstadoLote.Preparado;
    public string? Asunto { get; set; }
    public string? Cuerpo { get; set; }
    public bool CancelacionSolicitada { get; set; }
    public object Bloqueo { get; } = new();
}
