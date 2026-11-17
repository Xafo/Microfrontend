using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Enrolamiento.EnvioCorreos.Api.Lotes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Endpoints;

public sealed record SolicitudCorreo(string? Asunto, string? Cuerpo, int? NumeroFila);

public static class LotesEndpoints
{
    public static void MapLotes(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/configuracion", (IOptions<EnlacesOptions> enl, IOptions<CorreoOptions> cor, IOptions<CargaOptions> car,
            PlantillaCorreo plantilla, IHostEnvironment env) => new
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
            asuntoPorDefecto = cor.Value.AsuntoPorDefecto,
            cuerpoPorDefecto = plantilla.CuerpoInicial,
            marcadores = PlantillaCorreo.Marcadores,
        });

        api.MapGet("/plantilla", (IWebHostEnvironment env) =>
            Results.File(Path.Combine(AppContext.BaseDirectory, "Plantillas", "Enrolamiento_EnvioCorreos.xlsx"),
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

        api.MapPost("/lotes/{id:guid}/vista-previa", (Guid id, SolicitudCorreo s, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l =>
            {
                var r = servicio.VistaPrevia(l, s.Asunto ?? "", s.Cuerpo ?? "", s.NumeroFila);
                return Results.Ok(new { asunto = r.Asunto, asuntoModificado = r.AsuntoModificado, html = r.Html, errores = r.Errores });
            }));

        api.MapPost("/lotes/{id:guid}/envio", (Guid id, SolicitudCorreo s, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.Enviar(l, s.Asunto ?? "", s.Cuerpo ?? ""); return Results.Accepted(null, LoteDto.Estado(l)); }));

        api.MapPost("/lotes/{id:guid}/reintentar", (Guid id, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.ReintentarFallidos(l); return Results.Accepted(null, LoteDto.Estado(l)); }));

        api.MapPost("/lotes/{id:guid}/cancelar", (Guid id, AlmacenLotes almacen, ServicioLotes servicio) =>
            Ejecutar(almacen, id, l => { servicio.Cancelar(l); return Results.Ok(LoteDto.Estado(l)); }));
    }

    private static IResult Ejecutar(AlmacenLotes almacen, Guid id, Func<Lote, IResult> accion)
    {
        if (almacen.Obtener(id) is not { } lote) return NoEncontrado();
        try { return accion(lote); }
        catch (PlantillaInvalidaException ex) { return Problema(400, "La plantilla del correo no es válida.", ex.Errores); }
        catch (ConflictoLoteException ex) { return Problema(409, ex.Message); }
    }

    private static IResult NoEncontrado() =>
        Problema(404, "El lote no existe o expiró. Cargue el archivo de nuevo.");

    private static IResult Problema(int estado, string detalle, IReadOnlyList<string>? errores = null) =>
        Results.Problem(detail: detalle, statusCode: estado,
            extensions: errores is null ? null : new Dictionary<string, object?> { ["errores"] = errores });
}
