using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Enrolamiento.EnvioCorreos.Api.Campanas;

namespace Enrolamiento.Tests;

/// <summary>Simula el API del formulario: POST solicitud/auth/token y GET solicitud/listar.</summary>
public sealed class ApiSolicitudesFalsa : HttpMessageHandler
{
    public const string Token = "token-de-prueba";
    public HttpStatusCode RespuestaToken { get; set; } = HttpStatusCode.OK;
    public List<string> Llamadas { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        Llamadas.Add($"{r.Method} {r.RequestUri!.AbsolutePath}");
        if (r.Method == HttpMethod.Post && r.RequestUri.AbsolutePath == "/solicitud/auth/token")
        {
            var cuerpo = await r.Content!.ReadFromJsonAsync<JsonElement>(ct);
            var ok = RespuestaToken == HttpStatusCode.OK && cuerpo.GetProperty("username").GetString() == "usuario-api"
                     && cuerpo.GetProperty("password").GetString() == "clave-api";
            return ok
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = Token, tokenType = "Bearer", expiresIn = 900 }) }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        if (r.Method == HttpMethod.Get && r.RequestUri.AbsolutePath == "/solicitud/listar")
        {
            if (r.Headers.Authorization?.ToString() != $"Bearer {Token}") return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Archivos", "solicitudes_prueba.json"), ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public class CampanasTests
{
    private static (Fabrica f, ApiSolicitudesFalsa api) Crear(bool configurada = true)
    {
        var api = new ApiSolicitudesFalsa();
        var config = configurada
            ? new Dictionary<string, string?> { ["SolicitudesApi:UrlBase"] = "https://api.example.test/", ["SolicitudesApi:Usuario"] = "usuario-api", ["SolicitudesApi:Clave"] = "clave-api" }
            : new Dictionary<string, string?> { ["SolicitudesApi:Usuario"] = "", ["SolicitudesApi:Clave"] = "" };
        var f = new Fabrica { Config = config };
        f.ServiciosExtra = s => s.AddHttpClient<ClienteSolicitudes>().ConfigurePrimaryHttpMessageHandler(() => api);
        return (f, api);
    }

    private static async Task<HttpResponseMessage> Descargar(HttpClient c, string agrupador) =>
        await c.GetAsync($"/api/campanas/excel?agrupador={Uri.EscapeDataString(agrupador)}");

    [Fact]
    public async Task Descarga_solo_las_enviadas_del_agrupador_con_las_columnas_amarillas()
    {
        var (f, api) = Crear();
        using var _ = f;
        var r = await Descargar(f.ClienteConSesion(), "CAMP-001");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("2", r.Headers.GetValues("X-Total-Solicitudes").Single());
        Assert.StartsWith("CARGA_MASIVA_CAMP-001_", r.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal(["POST /solicitud/auth/token", "GET /solicitud/listar"], api.Llamadas);

        using var libro = new XLWorkbook(await r.Content.ReadAsStreamAsync());
        var h = libro.Worksheet(1);
        int Col(string encabezado) => h.Row(1).CellsUsed().Single(c => c.GetString() == encabezado).Address.ColumnNumber;
        Assert.Equal(3, h.LastRowUsed()!.RowNumber());   // encabezado + 2 solicitudes

        Assert.Equal("PEREZ", h.Cell(2, Col("PRIMER APELLIDO")).GetString());
        Assert.Equal("LOPEZ", h.Cell(2, Col("SEGUNDO APELLIDO")).GetString());
        Assert.Equal("ANA", h.Cell(2, Col("PRIMER NOMBRE")).GetString());
        Assert.Equal("MARIA", h.Cell(2, Col("SEGUNDO NOMBRE")).GetString());
        Assert.Equal(1, h.Cell(2, Col("TIPODOC")).GetValue<int>());
        Assert.Equal("0801-1990-00001", h.Cell(2, Col("NUMIDENTIDAD")).GetString());
        Assert.Equal("HONDURAS", h.Cell(2, Col("PAISNAC")).GetString());
        Assert.Equal(new DateTime(1990, 3, 5), h.Cell(2, Col("FECNAC")).GetDateTime());
        Assert.Equal("F", h.Cell(2, Col("SEXO")).GetString());
        Assert.Equal("CONTADORA", h.Cell(2, Col("PROFESION")).GetString());
        Assert.Equal("99990001", h.Cell(2, Col("TELEFONO1")).GetString());
        Assert.Equal("22220001", h.Cell(2, Col("TELEFONO2")).GetString());
        Assert.Equal(new DateTime(2026, 11, 1), h.Cell(2, Col("FECHA INCLUSIÓN")).GetDateTime());

        // Segunda: agrupador con otra capitalización y espacio, ceros a la izquierda conservados, vacíos quedan vacíos.
        Assert.Equal("NUÑEZ", h.Cell(3, Col("PRIMER APELLIDO")).GetString());
        Assert.Equal("0001-2000-00099", h.Cell(3, Col("NUMIDENTIDAD")).GetString());
        Assert.True(h.Cell(3, Col("TELEFONO2")).IsEmpty());
        // Columnas no amarillas quedan vacías.
        Assert.True(h.Cell(2, Col("TIPOPROCESO")).IsEmpty());
        // Borradores y otras campañas no salen.
        Assert.DoesNotContain(h.CellsUsed(), c => c.GetString() is "BORRADOR" or "OTRA");
    }

    [Fact]
    public async Task Campana_sin_solicitudes_enviadas_responde_404()
    {
        var (f, _) = Crear();
        using var __ = f;
        var r = await Descargar(f.ClienteConSesion(), "NO-EXISTE");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("NO-EXISTE", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Credenciales_rechazadas_por_el_api_responde_502_claro()
    {
        var (f, api) = Crear();
        using var _ = f;
        api.RespuestaToken = HttpStatusCode.Unauthorized;
        var r = await Descargar(f.ClienteConSesion(), "CAMP-001");
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Contains("SolicitudesApi__Usuario", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sin_configurar_responde_503()
    {
        var (f, _) = Crear(configurada: false);
        using var __ = f;
        var c = f.ClienteConSesion();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Descargar(c, "CAMP-001")).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/configuracion")).GetProperty("descargaCampanaConfigurada").GetBoolean());
    }

    [Fact]
    public async Task Agrupador_vacio_400_y_sin_sesion_401()
    {
        var (f, _) = Crear();
        using var __ = f;
        Assert.Equal(HttpStatusCode.BadRequest, (await Descargar(f.ClienteConSesion(), "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Descargar(f.CreateClient(), "CAMP-001")).StatusCode);
    }
}
