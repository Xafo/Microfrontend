using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Lotes;

public sealed class LotesOptions
{
    public const string Seccion = "Lotes";
    /// <summary>Tiempo que un lote sin actividad permanece en memoria. No hay historial persistente: queda en pr_mail.</summary>
    public int DuracionHoras { get; set; } = 8;
}

public sealed class AlmacenLotes(IMemoryCache cache, IOptions<LotesOptions> opciones)
{
    public void Guardar(Lote lote) =>
        cache.Set(Clave(lote.Id), lote, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(opciones.Value.DuracionHoras) });

    public Lote? Obtener(Guid id) => cache.TryGetValue(Clave(id), out Lote? l) ? l : null;

    private static string Clave(Guid id) => "lote:" + id.ToString("N");
}
