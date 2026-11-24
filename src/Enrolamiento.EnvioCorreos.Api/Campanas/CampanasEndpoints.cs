using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Campanas;

public static partial class CampanasEndpoints
{
    public static void MapCampanas(this IEndpointRouteBuilder app)
    {
        // GET .../api/campanas/excel?agrupador=XXXX  → Excel de carga masiva con las solicitudes ENVIADAS de ese agrupador.
        app.MapGet("/api/campanas/excel", async (string? agrupador, IOptions<SolicitudesApiOptions> opciones,
            ClienteSolicitudes cliente, GeneradorCargaMasiva generador, HttpContext http, ILoggerFactory logs, CancellationToken ct) =>
        {
            var o = opciones.Value;
            if (!o.Configurada())
                return Problema(503, "La descarga de campañas no está configurada. Defina SolicitudesApi:UrlBase y las variables SolicitudesApi__Usuario y SolicitudesApi__Clave.");

            agrupador = (agrupador ?? "").Trim();
            if (agrupador.Length == 0) return Problema(400, "Ingrese el ID de la campaña (agrupador).");
            if (agrupador.Length > 60 || agrupador.Any(char.IsControl)) return Problema(400, "El ID de la campaña no es válido.");

            IReadOnlyList<SolicitudCampana> todas;
            try { todas = await cliente.ListarAsync(ct); }
            catch (SolicitudesApiException ex) { return Problema(502, ex.Message); }

            var filas = todas
                .Where(s => string.Equals(s.Estado, o.EstadoIncluido, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(s.Agrupador, agrupador, StringComparison.OrdinalIgnoreCase))
                .ToList();
            logs.CreateLogger("Campanas").LogInformation("Descarga de campaña: {Total} solicitudes {Estado}", filas.Count, o.EstadoIncluido);

            if (filas.Count == 0)
                return Problema(404, $"No hay solicitudes en estado {o.EstadoIncluido} para la campaña '{agrupador}'.");

            http.Response.Headers["X-Total-Solicitudes"] = filas.Count.ToString();
            var nombre = $"CARGA_MASIVA_{NombreSeguro().Replace(agrupador, "_")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return Results.File(generador.Generar(filas), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
        }).RequireAuthorization();
    }

    private static IResult Problema(int estado, string detalle) => Results.Problem(detail: detalle, statusCode: estado);

    [GeneratedRegex(@"[^A-Za-z0-9\-]+")]
    private static partial Regex NombreSeguro();
}
