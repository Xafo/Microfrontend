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
    public async Task Descarga_titulares_y_dependientes_con_las_reglas_de_suscripcion()
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
        int C(string encabezado) => h.Row(1).CellsUsed().First(c => c.GetString() == encabezado).Address.ColumnNumber;
        string T(int fila, string col) => h.Cell(fila, C(col)).GetString();
        // Ana (titular) + 2 dependientes + José (titular, sin dependientes: la fila vacía del formulario se descarta)
        Assert.Equal(5, h.LastRowUsed()!.RowNumber());

        // Fila 2: titular femenina con DNI
        Assert.Equal("INC", T(2, "TIPOPROCESO"));
        Assert.Equal(("PEREZ", "LOPEZ", "ANA", "MARIA"), (T(2, "PRIMER APELLIDO"), T(2, "SEGUNDO APELLIDO"), T(2, "PRIMER NOMBRE"), T(2, "SEGUNDO NOMBRE")));
        Assert.Equal("A", T(2, "TIPOCLIEN"));
        Assert.Equal(2, h.Cell(2, C("CODPARENT")).GetValue<int>());          // titular femenino
        Assert.Equal("L", T(2, "TIPOPERSONA"));
        Assert.Equal(1, h.Cell(2, C("TIPODOC")).GetValue<int>());
        Assert.Equal("0801-1990-00001", T(2, "NUMIDENTIDAD"));
        Assert.True(h.Cell(2, C("DIRECCION")).IsEmpty());
        Assert.Equal("HONDURAS", T(2, "PAISNAC"));
        Assert.Equal(new DateTime(1990, 3, 5), h.Cell(2, C("FECNAC")).GetDateTime());
        Assert.Equal("F", T(2, "SEXO"));
        Assert.Equal("CONTADORA", T(2, "PROFESION"));
        Assert.Equal("99990001", T(2, "TELEFONO1"));
        Assert.Equal("22220001", T(2, "TELEFONO2"));
        Assert.Equal("N", T(2, "CONTINUIDAD_COBERTURA"));
        Assert.Equal(15000m, h.Cell(2, C("SALARIO")).GetValue<decimal>());
        // Las columnas del área comercial quedan vacías.
        foreach (var col in new[] { "NUMCERTIFICADO", "COD. RESP. PAGO", "FECHA INCLUSIÓN", "FECMOD", "FECEXC", "CODPUNTOSERVICIO", "AFILIADA" })
            Assert.True(h.Cell(2, C(col)).IsEmpty(), col);

        // Filas 3-4: dependientes de Ana (TIPOCLIEN D, parentesco elegido, identidad del titular, L/1)
        Assert.Equal(("REYES", "EMMA", "D", 4), (T(3, "PRIMER APELLIDO"), T(3, "PRIMER NOMBRE"), T(3, "TIPOCLIEN"), h.Cell(3, C("CODPARENT")).GetValue<int>()));
        Assert.Equal(("L", 1, "0801-1990-00001", "F"), (T(3, "TIPOPERSONA"), h.Cell(3, C("TIPODOC")).GetValue<int>(), T(3, "NUMIDENTIDAD"), T(3, "SEXO")));
        Assert.Equal(new DateTime(2015, 2, 10), h.Cell(3, C("FECNAC")).GetDateTime());
        Assert.True(h.Cell(3, C("SALARIO")).IsEmpty());
        Assert.Equal(("CARLOS", 3, "M"), (T(4, "PRIMER NOMBRE"), h.Cell(4, C("CODPARENT")).GetValue<int>(), T(4, "SEXO")));

        // Fila 5: titular masculino con pasaporte y formulario con continuidad de cobertura
        Assert.Equal(("NUÑEZ", "A", 1), (T(5, "PRIMER APELLIDO"), T(5, "TIPOCLIEN"), h.Cell(5, C("CODPARENT")).GetValue<int>()));
        Assert.Equal(("E", 4, "P1234567"), (T(5, "TIPOPERSONA"), h.Cell(5, C("TIPODOC")).GetValue<int>(), T(5, "NUMIDENTIDAD")));
        Assert.Equal("S", T(5, "CONTINUIDAD_COBERTURA"));
        Assert.Equal(18092.82m, h.Cell(5, C("SALARIO")).GetValue<decimal>());
        Assert.True(h.Cell(5, C("TELEFONO2")).IsEmpty());

        // Borradores y otras campañas no salen.
        Assert.DoesNotContain(h.CellsUsed(), c => c.GetString() is "BORRADOR" or "OTRA");
    }

    [Theory]
    [InlineData("0003", 3)] [InlineData("0004", 4)] [InlineData("0030", 30)] [InlineData("CÓNYUGE", 3)] [InlineData("HIJASTRO/A", 30)] [InlineData("HIJO(A)", 4)]
    public void Codigos_de_parentesco(string valor, int esperado) => Assert.Equal(esperado, GeneradorCargaMasiva.CodigoParentesco(valor));

    [Theory]
    [InlineData("FEMENINO", "F")] [InlineData("f", "F")] [InlineData("MASCULINO", "M")] [InlineData("M", "M")]
    public void Sexo_normalizado(string valor, string esperado) => Assert.Equal(esperado, GeneradorCargaMasiva.Sexo(valor));

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
