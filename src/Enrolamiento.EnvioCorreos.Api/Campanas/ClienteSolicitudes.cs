using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Campanas;

/// <summary>Dependiente declarado en la pantalla "Dependientes" del formulario.</summary>
public sealed record DependienteCampana(
    string PrimerNombre, string SegundoNombre, string PrimerApellido, string SegundoApellido,
    string Parentesco, string Genero, string FechaNacimiento);

/// <summary>Datos de una solicitud que se exportan a la carga masiva (titular + dependientes).</summary>
public sealed record SolicitudCampana(
    string SolicitudId, string Estado, string Agrupador, string TipoFormulario,
    string PrimerApellido, string SegundoApellido, string PrimerNombre, string SegundoNombre,
    string TipoDocumento, string NumeroDocumento, string Nacionalidad, string FechaNacimiento, string Sexo,
    string Profesion, string Celular, string TelefonoFijo, string FechaIngresoPoliza, string SueldoMensual,
    IReadOnlyList<DependienteCampana> Dependientes);

public sealed class SolicitudesApiException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Consume el API del formulario: POST solicitud/auth/token y luego GET solicitud/listar con Bearer.</summary>
public sealed class ClienteSolicitudes(HttpClient http, IOptions<SolicitudesApiOptions> opciones, ILogger<ClienteSolicitudes> log)
{
    private readonly SolicitudesApiOptions _o = opciones.Value;

    public async Task<IReadOnlyList<SolicitudCampana>> ListarAsync(CancellationToken ct)
    {
        var token = await ObtenerTokenAsync(ct);

        using var pedido = new HttpRequestMessage(HttpMethod.Get, Url(_o.RutaListar));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var r = await Enviar(pedido, "consultar las solicitudes", ct);

        using var doc = await LeerJson(r, "la lista de solicitudes", ct);
        var raiz = doc.RootElement;
        var lista = raiz.ValueKind == JsonValueKind.Array ? raiz
            : Prop(raiz, "data") is { ValueKind: JsonValueKind.Array } d ? d
            : throw new SolicitudesApiException("El API de solicitudes devolvió un formato inesperado (se esperaba una lista).");

        var res = new List<SolicitudCampana>();
        foreach (var item in lista.EnumerateArray())
        {
            var s = Prop(item, "solicitud") ?? item;           // { "solicitud": {...} } o directamente {...}
            var datos = Prop(s, "datos") ?? default;
            var tipo = Texto(datos, "tipoFormulario") is { Length: > 0 } t ? t : Texto(s, "tipoFormulario");
            var dependientes = new List<DependienteCampana>();
            if (Prop(Prop(s, "beneficiarios") ?? default, "dependientes") is { ValueKind: JsonValueKind.Array } deps)
                foreach (var dj in deps.EnumerateArray())
                {
                    var dep = new DependienteCampana(Texto(dj, "primerNombre"), Texto(dj, "segundoNombre"), Texto(dj, "primerApellido"),
                        Texto(dj, "segundoApellido"), Texto(dj, "parentesco"), Texto(dj, "genero"), Texto(dj, "fecha"));
                    // El formulario envía filas vacías cuando no hay dependientes: se descartan.
                    if (dep.PrimerNombre.Length > 0 || dep.PrimerApellido.Length > 0) dependientes.Add(dep);
                }
            res.Add(new SolicitudCampana(
                Texto(s, "solicitudId"), Texto(s, "estado"), Texto(datos, "numeroPoliza"), tipo,
                Texto(datos, "primerApellido"), Texto(datos, "segundoApellido"), Texto(datos, "primerNombre"), Texto(datos, "segundoNombre"),
                Texto(datos, "tipoDocumento"), Texto(datos, "numeroDocumento"), Texto(datos, "nacionalidad"), Texto(datos, "fechaNacimiento"),
                Texto(datos, "sexo"), Texto(datos, "profesionOficio"), Texto(datos, "celular"), Texto(datos, "telefonoFijo"),
                Texto(datos, "fechaIngresoPoliza"), Texto(datos, "sueldoMensual"), dependientes));
        }
        log.LogInformation("API de solicitudes: {Total} solicitudes recibidas", res.Count);
        return res;
    }

    private async Task<string> ObtenerTokenAsync(CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Url(_o.RutaToken))
        {
            Content = JsonContent.Create(new { username = _o.Usuario, password = _o.Clave }),
        };
        using var r = await Enviar(pedido, "autenticarse", ct);
        using var doc = await LeerJson(r, "el token", ct);
        var token = Texto(doc.RootElement, "accessToken");
        if (token.Length == 0) throw new SolicitudesApiException("El API de solicitudes no devolvió un token de acceso.");
        return token;
    }

    private async Task<HttpResponseMessage> Enviar(HttpRequestMessage pedido, string accion, CancellationToken ct)
    {
        HttpResponseMessage r;
        try { r = await http.SendAsync(pedido, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "No se pudo conectar con el API de solicitudes para {Accion}", accion);
            throw new SolicitudesApiException($"No se pudo conectar con el API de solicitudes para {accion}.", ex);
        }
        if (!r.IsSuccessStatusCode)
        {
            var codigo = (int)r.StatusCode;
            r.Dispose();
            log.LogWarning("API de solicitudes respondió {Codigo} al {Accion}", codigo, accion);
            throw new SolicitudesApiException(codigo is 401 or 403
                ? $"El API de solicitudes rechazó las credenciales al {accion} (HTTP {codigo}). Revise SolicitudesApi__Usuario y SolicitudesApi__Clave."
                : $"El API de solicitudes respondió HTTP {codigo} al {accion}.");
        }
        return r;
    }

    private static async Task<JsonDocument> LeerJson(HttpResponseMessage r, string que, CancellationToken ct)
    {
        try { return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct); }
        catch (JsonException ex) { throw new SolicitudesApiException($"El API de solicitudes devolvió {que} en un formato no válido.", ex); }
    }

    private Uri Url(string ruta) => new(new Uri(_o.UrlBase.EndsWith('/') ? _o.UrlBase : _o.UrlBase + "/"), ruta.TrimStart('/'));

    private static JsonElement? Prop(JsonElement e, string nombre)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string Texto(JsonElement e, string nombre) => Prop(e, nombre) switch
    {
        { ValueKind: JsonValueKind.String } v => v.GetString()!.Trim(),
        { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        _ => "",
    };
}
