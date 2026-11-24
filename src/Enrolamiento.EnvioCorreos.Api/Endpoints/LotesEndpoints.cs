using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Enrolamiento.EnvioCorreos.Api.Lotes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Endpoints;


public static class LotesEndpoints
{
    public static void MapLotes(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/configuracion", (IOptions<EnlacesOptions> enl, IOptions<CorreoOptions> cor, IOptions<CargaOptions> car,
            PlantillaCorreo plantilla, IOptions<Campanas.SolicitudesApiOptions> sol, IHostEnvironment env) => new
        {
            ambiente = env.EnvironmentName,
            urlPublicaBase = enl.Value.UrlPublicaBase,
            vigenciaHoras = enl.Value.VigenciaHoras,
            tiposFormulario = enl.Value.TiposFormularioPermitidos,
            columnas = Columnas.Obligatorias,
            extensiones = LectorDestinatarios.ExtensionesPermitidas,
            tamanoMaximoBytes = car.Value.TamanoMaximoBytes,
            filasMaximas = car.Value.FilasMaximas,
            modoEnvio = cor.Value.Modo.ToString(),
            remitente = cor.Value.Remitente,
            asunto = cor.Value.Asunto,
            imagenesCorreoConfiguradas = plantilla.ImagenesConfiguradas,
            descargaCampanaConfigurada = sol.Value.Configurada(),
        });

        api.MapGet("/plantilla", (GeneradorPlantilla plantillaExcel) =>
            Results.File(plantillaExcel.Generar(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Enrolamiento_EnvioCorreos.xlsx"));

        api.MapPost("/lotes", async (IFormFile? archivo, ServicioLotes servicio, IOptions<CargaOptions> carga, CancellationToken ct) =>
        {
            if (archivo is null || archivo.Length == 0) return Problema(400, "Seleccione un archivo.");
            if (archivo.Length > carga.Value.TamanoMaximoBytes)
                return Problema(400, $"El archivo supera {carga.Value.TamanoMaximoBytes / 1024 / 1024} MB.");
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms, ct);
            try { return Results.Ok(LoteDto.Completo(servicio.Preparar(archivo.FileName, ms.ToArray()))); }
            catch (FormatoArchivoException ex) { return Problema(400, ex.Message); }
        }).DisableAntiforgery();

        api.MapGet("/lotes/{id:guid}", (Guid id, AlmacenLotes almacen) =>
            almacen.Obtener(id) is { } l ? Results.Ok(LoteDto.Completo(l)) : NoEncontrado());

        api.MapGet("/lotes/{id:guid}/estado", (Guid id, AlmacenLotes almacen) =>
            almacen.Obtener(id) is { } l ? Results.Ok(LoteDto.Estado(l)) : NoEncontrado());

        // GET y sin cuerpo: el correo es fijo, la pantalla solo elige la fila a previsualizar.
        api.MapGet("/lotes/{id:guid}/vista-previa", (Guid id, int? numeroFila, HttpContext http, AlmacenLotes almacen,
            ServicioLotes servicio, PlantillaCorreo plantilla) =>
            Ejecutar(almacen, id, l =>
            {
                // Sin URL pública configurada, la vista previa usa las imágenes del propio portal.
                var baseImagenes = plantilla.ImagenesConfiguradas ? null : http.Request.PathBase.ToUriComponent() + "/correo/";
                var r = servicio.VistaPrevia(l, numeroFila, baseImagenes);
                var advertencias = plantilla.ValidarEnvio();
                return Results.Ok(new { asunto = r.Asunto, html = r.Html, errores = r.Errores, advertencias });
            }));

        api.MapPost("/lotes/{id:guid}/envio", (Guid id, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.Enviar(l); return Results.Accepted(null, LoteDto.Estado(l)); }));

        api.MapPost("/lotes/{id:guid}/reintentar", (Guid id, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.ReintentarFallidos(l); return Results.Accepted(null, LoteDto.Estado(l)); }));

        api.MapPost("/lotes/{id:guid}/cancelar", (Guid id, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.Cancelar(l); return Results.Ok(LoteDto.Estado(l)); }));
    }

    private static IResult Ejecutar(AlmacenLotes almacen, Guid id, Func<Lote, IResult> accion)
    {
        if (almacen.Obtener(id) is not { } lote) return NoEncontrado();
        try { return accion(lote); }
        catch (PlantillaInvalidaException ex) { return Problema(400, "El correo no está listo para enviarse.", ex.Errores); }
        catch (ConflictoLoteException ex) { return Problema(409, ex.Message); }
    }

    private static IResult NoEncontrado() =>
        Problema(404, "El lote no existe o expiró. Cargue el archivo de nuevo.");

    private static IResult Problema(int estado, string detalle, IReadOnlyList<string>? errores = null) =>
        Results.Problem(detail: detalle, statusCode: estado,
            extensions: errores is null ? null : new Dictionary<string, object?> { ["errores"] = errores });
}
