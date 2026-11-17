using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Enrolamiento.Tests;

public sealed class EnviadorDePrueba : IEnviadorCorreo
{
    public ConcurrentQueue<MensajeCorreo> Enviados { get; } = new();
    public Task EnviarAsync(MensajeCorreo m, CancellationToken ct)
    {
        if (m.Destinatario.StartsWith("falla")) throw new InvalidOperationException("SMTP no disponible");
        Enviados.Enqueue(m);
        return Task.CompletedTask;
    }
}

public sealed class Fabrica : WebApplicationFactory<Program>
{
    public EnviadorDePrueba Enviador { get; } = new();
    public string Ambiente { get; init; } = "Local";
    public Dictionary<string, string?> Config { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        Environment.SetEnvironmentVariable(Utilidades.Variable, Utilidades.LlavePrueba);
        b.UseEnvironment(Ambiente);
        b.UseSetting("Enlaces:VariableEntornoLlaves", Utilidades.Variable);
        b.UseSetting("Correo:PausaEntreEnviosMs", "0");
        foreach (var kv in Config) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureTestServices(s => s.AddSingleton<IEnviadorCorreo>(Enviador));
    }
}

public class ApiTests : IClassFixture<Fabrica>
{
    private readonly Fabrica _f;
    private readonly HttpClient _c;
    public ApiTests(Fabrica f) { _f = f; _c = f.CreateClient(); }

    internal static async Task<JsonElement> Subir(HttpClient c, string nombre, byte[] bytes, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        using var form = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(archivo, "archivo", nombre);
        var r = await c.PostAsync("/api/lotes", form);
        Assert.Equal(esperado, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static byte[] Csv(params string[] filas) => System.Text.Encoding.UTF8.GetBytes(
        "Tipo de Formulario;Nombre del Contratante;Nombre del Cliente;Numero de Poliza;Suma Asegurada;Correo\n" + string.Join("\n", filas));

    internal static async Task<JsonElement> EsperarFin(HttpClient c, string id)
    {
        for (int i = 0; i < 200; i++)
        {
            var e = await c.GetFromJsonAsync<JsonElement>($"/api/lotes/{id}/estado");
            if (e.GetProperty("estado").GetString() == "Finalizado") return e;
            await Task.Delay(25);
        }
        throw new TimeoutException();
    }

    private async Task<JsonElement> Configuracion() => await _c.GetFromJsonAsync<JsonElement>("/api/configuracion");

    [Fact]
    public async Task Configuracion_expone_plantilla_por_defecto_y_url_del_ambiente()
    {
        var c = await Configuracion();
        Assert.Equal("Complete su formulario de enrolamiento", c.GetProperty("asuntoPorDefecto").GetString());
        Assert.Contains("{{NombreCliente}}", c.GetProperty("cuerpoPorDefecto").GetString());
        Assert.Equal("https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/", c.GetProperty("urlPublicaBase").GetString());
        Assert.Equal(48, c.GetProperty("vigenciaHoras").GetInt32());
        Assert.Equal(["61", "62", "63", "64", "101", "102", "161"],
            c.GetProperty("tiposFormulario").EnumerateArray().Select(t => t.GetString()!).ToArray());
    }

    [Fact]
    public async Task Descarga_plantilla_excel()
    {
        var r = await _c.GetAsync("/api/plantilla");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Carga_excel_cifra_enlaces_y_el_cifrado_se_puede_validar()
    {
        var lote = await Subir(_c, "destinatarios.xlsx", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Archivos", "destinatarios.xlsx")));
        var filas = lote.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal(4, filas.Count);
        Assert.Equal(2, lote.GetProperty("resumen").GetProperty("lista").GetInt32());
        Assert.Equal(2, lote.GetProperty("resumen").GetProperty("invalida").GetInt32());

        var enlace = filas[0].GetProperty("enlace");
        var cifrado = enlace.GetProperty("cifrado").GetString()!;
        Assert.StartsWith("https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/?datos=", cifrado);
        Assert.Contains("numeroPoliza=1000000332", enlace.GetProperty("referencia").GetString());
        Assert.Equal(JsonValueKind.Null, filas[2].GetProperty("enlace").ValueKind);

        // El backend del formulario (mismas llaves) recupera exactamente los datos.
        var svc = _f.Services.GetRequiredService<IEnlaceProtegidoService>();
        var r = svc.ValidarContenido(QueryHelpers.ParseQuery(new Uri(cifrado).Query)["datos"]);
        Assert.True(r.Valido);
        Assert.Equal(new DatosEnrolamiento("61", "CONTRATANTE DE PRUEBA", "1000000332", 2500000m, "cliente@example.test"), r.Datos);
    }

    [Fact]
    public async Task Vista_previa_reemplaza_marcadores_y_es_ascii()
    {
        var c = await Configuracion();
        var lote = await Subir(_c, "a.csv", Csv("61;Contratante;José <b>Pérez</b>;123;100;jose@example.test"));
        var r = await _c.PostAsJsonAsync($"/api/lotes/{lote.GetProperty("id")}/vista-previa",
            new { asunto = "Póliza {{NumeroPoliza}}", cuerpo = c.GetProperty("cuerpoPorDefecto").GetString() });
        var v = await r.Content.ReadFromJsonAsync<JsonElement>();
        var html = v.GetProperty("html").GetString()!;

        Assert.Equal("Poliza 123", v.GetProperty("asunto").GetString());
        Assert.True(v.GetProperty("asuntoModificado").GetBoolean());
        Assert.Contains("Jos&#233; &lt;b&gt;P&#233;rez&lt;/b&gt;", html);   // escapado y ASCII
        Assert.Contains("p&#243;liza 123", html);
        Assert.Contains("href=\"https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/?datos=", html);
        Assert.Contains("Completar formulario", html);
        Assert.All(html, ch => Assert.True(ch < 128));
    }

    [Fact]
    public async Task Envia_por_fila_y_no_permite_enviar_dos_veces()
    {
        var c = await Configuracion();
        var lote = await Subir(_c, "a.csv", Csv(
            "61;Contratante A;Cliente A;111;100;a@example.test",
            "62;Contratante B;Cliente B;222;200.50;b@example.test",
            "99;Inválida;X;333;1;c@example.test"));
        var id = lote.GetProperty("id").ToString();
        var cuerpo = c.GetProperty("cuerpoPorDefecto").GetString();

        var r = await _c.PostAsJsonAsync($"/api/lotes/{id}/envio", new { asunto = "Complete su formulario de enrolamiento", cuerpo });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var fin = await EsperarFin(_c, id);
        Assert.Equal(2, fin.GetProperty("resumen").GetProperty("enviada").GetInt32());
        Assert.Equal(1, fin.GetProperty("resumen").GetProperty("invalida").GetInt32());

        var enviados = _f.Enviador.Enviados.Where(m => m.Destinatario is "a@example.test" or "b@example.test").ToList();
        Assert.Equal(2, enviados.Count);
        Assert.All(enviados, m => Assert.Equal("no-responder@example.test", m.Remitente));
        Assert.Contains(enviados, m => m.Html.Contains("Cliente B") && m.Html.Contains("222"));

        var otra = await _c.PostAsJsonAsync($"/api/lotes/{id}/envio", new { asunto = "x", cuerpo });
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
    }

    [Fact]
    public async Task Reintenta_solo_los_fallidos()
    {
        var c = await Configuracion();
        var lote = await Subir(_c, "a.csv", Csv("61;A;A;1;1;ok1@example.test", "61;B;B;2;1;falla@example.test"));
        var id = lote.GetProperty("id").ToString();
        await _c.PostAsJsonAsync($"/api/lotes/{id}/envio", new { asunto = "Asunto", cuerpo = c.GetProperty("cuerpoPorDefecto").GetString() });
        var fin = await EsperarFin(_c, id);
        Assert.Equal(1, fin.GetProperty("resumen").GetProperty("fallida").GetInt32());
        var fallida = fin.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("estado").GetString() == "Fallida");
        Assert.Equal("SMTP no disponible", fallida.GetProperty("mensaje").GetString());

        var antes = _f.Enviador.Enviados.Count(m => m.Destinatario == "ok1@example.test");
        Assert.Equal(HttpStatusCode.Accepted, (await _c.PostAsync($"/api/lotes/{id}/reintentar", null)).StatusCode);
        await EsperarFin(_c, id);
        Assert.Equal(antes, _f.Enviador.Enviados.Count(m => m.Destinatario == "ok1@example.test")); // no reenvía los exitosos
    }

    [Theory]
    [InlineData("", "<p>{{Enlace}}</p>")]
    [InlineData("Asunto", "<p>sin enlace</p>")]
    [InlineData("Asunto", "<p>{{Enlace}} {{Desconocido}}</p>")]
    [InlineData("Asunto {{Enlace}}", "<p>{{Enlace}}</p>")]
    public async Task Rechaza_plantilla_invalida(string asunto, string cuerpo)
    {
        var lote = await Subir(_c, "a.csv", Csv("61;A;A;1;1;x@example.test"));
        var r = await _c.PostAsJsonAsync($"/api/lotes/{lote.GetProperty("id")}/envio", new { asunto, cuerpo });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync($"/api/lotes/{lote.GetProperty("id")}")).StatusCode);
    }

    [Fact]
    public async Task Rechaza_archivo_invalido_con_problem_details()
    {
        var r = await Subir(_c, "a.pdf", [1, 2, 3], HttpStatusCode.BadRequest);
        Assert.Contains(".xlsx", r.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Lote_inexistente_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync($"/api/lotes/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Envio_exige_json()
    {
        var lote = await Subir(_c, "a.csv", Csv("61;A;A;1;1;x@example.test"));
        var r = await _c.PostAsync($"/api/lotes/{lote.GetProperty("id")}/envio", new FormUrlEncodedContent([new("asunto", "x")]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
    }

    [Fact]
    public async Task Respuestas_api_no_se_cachean()
    {
        var r = await _c.GetAsync("/api/configuracion");
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
    }
}

public class ArranqueTests
{
    [Fact]
    public void Production_no_permite_modo_simulado()
    {
        using var f = new Fabrica { Ambiente = "Production", Config = new() { ["Correo:Modo"] = "Simulado" } };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Simulado", ex.ToString());
    }

    [Fact]
    public void Oracle_sin_credenciales_no_arranca()
    {
        using var f = new Fabrica { Ambiente = "Staging", Config = new() { ["Correo:Remitente"] = "notificaciones@example.test" } };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Oracle:DataSource", ex.ToString());
    }

    [Fact]
    public void Remitente_marcador_no_arranca()
    {
        using var f = new Fabrica { Ambiente = "Production" };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Remitente", ex.ToString());
    }

    [Theory]
    [InlineData("Development", "https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/")]
    [InlineData("Staging", "https://d1i4s8s51j1tuu.cloudfront.net/EnrolamientoHN/")]
    [InlineData("Production", "https://seguros.ficohsa.com/EnrolamientoHN/")]
    public async Task Url_base_por_ambiente(string ambiente, string url)
    {
        using var f = new Fabrica
        {
            Ambiente = ambiente,
            Config = new()
            {
                ["Correo:Remitente"] = "notificaciones@example.test",
                ["Oracle:DataSource"] = "host:1521/servicio", ["Oracle:Usuario"] = "u", ["Oracle:Clave"] = "c",
            },
        };
        var c = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/configuracion");
        Assert.Equal(url, c.GetProperty("urlPublicaBase").GetString());
        Assert.Equal("Oracle", c.GetProperty("modoEnvio").GetString());
    }
}
