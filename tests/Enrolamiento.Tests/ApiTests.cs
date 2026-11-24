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
    public const string Usuario = "operador";
    public const string Clave = "Clave-De-Prueba-2026";
    // Pocas iteraciones solo para que las pruebas sean rápidas; el comando generar-clave usa 600 000.
    public static readonly string ClaveHash = Enrolamiento.EnvioCorreos.Api.Seguridad.HashClave.Crear(Clave, 1000);

    public EnviadorDePrueba Enviador { get; } = new();
    public string Ambiente { get; init; } = "Local";
    public Dictionary<string, string?> Config { get; init; } = [];
    /// <summary>Servicios de prueba adicionales (p. ej. API de solicitudes simulada).</summary>
    public Action<IServiceCollection>? ServiciosExtra { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        Environment.SetEnvironmentVariable(Utilidades.Variable, Utilidades.LlavePrueba);
        b.UseEnvironment(Ambiente);
        b.UseSetting("Enlaces:VariableEntornoLlaves", Utilidades.Variable);
        b.UseSetting("Correo:PausaEntreEnviosMs", "0");
        b.UseSetting("Correo:ImagenesBaseUrl", "https://cdn.example.test/correo/");
        b.UseSetting("Login:Usuario", Usuario);
        b.UseSetting("Login:ClaveHash", ClaveHash);
        b.UseSetting("Login:IntentosPorMinuto", "1000");
        foreach (var kv in Config) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureTestServices(s => { s.AddSingleton<IEnviadorCorreo>(Enviador); ServiciosExtra?.Invoke(s); });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Solicitud", "1");
    }

    /// <summary>Cliente con la sesión ya iniciada.</summary>
    public HttpClient ClienteConSesion()
    {
        var c = CreateClient();
        c.PostAsJsonAsync("/api/auth/login", new { usuario = Usuario, clave = Clave }).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        return c;
    }
}

public class ApiTests : IClassFixture<Fabrica>
{
    private readonly Fabrica _f;
    private readonly HttpClient _c;
    public ApiTests(Fabrica f) { _f = f; _c = f.ClienteConSesion(); }

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
        "Tipo de Formulario;Nombre del Contratante;Nombre del Cliente;Agrupador;Correo\n" + string.Join("\n", filas));

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
        Assert.Equal("Completa tu formulario de enrolamiento", c.GetProperty("asunto").GetString());
        Assert.True(c.GetProperty("imagenesCorreoConfiguradas").GetBoolean());
        Assert.Contains("Agrupador", c.GetProperty("columnas").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("https://d3322rrb6yrsp3.cloudfront.net/", c.GetProperty("urlPublicaBase").GetString());
        Assert.Equal(48, c.GetProperty("vigenciaHoras").GetInt32());
        Assert.Equal(["SPN-F.GTP-61", "SPN-F.GTP-62", "SPN-F.GTP-63", "SPN-F.GTP-64", "SPN-F.GTP-101", "SPN-F.GTP-161", "COACEHL (SPN-F.GTP-102)"],
            c.GetProperty("tiposFormulario").EnumerateArray().Select(t => t.GetString()!).ToArray());
    }

    [Fact]
    public async Task Descarga_plantilla_excel_con_leyenda_de_tipos_y_se_puede_volver_a_cargar()
    {
        var r = await _c.GetAsync("/api/plantilla");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();

        using var libro = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var hoja = libro.Worksheet("Destinatarios");
        var textos = hoja.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Tipos de formulario aceptados", textos);
        Assert.Contains("COACEHL (SPN-F.GTP-102)", textos);
        Assert.Contains("SPN-F.GTP-161", textos);

        // La leyenda no se interpreta como filas de datos.
        var lote = await Subir(_c, "plantilla.xlsx", bytes);
        Assert.Equal(0, lote.GetProperty("resumen").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Tipo_agregado_en_configuracion_se_acepta_y_aparece_en_la_leyenda()
    {
        using var f = new Fabrica { Config = new() { ["Enlaces:TiposFormularioPermitidos:0"] = "SPN-F.GTP-61", ["Enlaces:TiposFormularioPermitidos:1"] = "SPN-F.GTP-999" } };
        var c = f.ClienteConSesion();
        var lote = await Subir(c, "a.csv", Csv("SPN-F.GTP-999;A;A;1;x@example.test", "SPN-F.GTP-62;B;B;2;y@example.test"));
        var filas = lote.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal("Lista", filas[0].GetProperty("estado").GetString());
        Assert.Equal("Invalida", filas[1].GetProperty("estado").GetString());

        using var libro = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await c.GetByteArrayAsync("/api/plantilla")));
        Assert.Contains("SPN-F.GTP-999", libro.Worksheet("Destinatarios").CellsUsed().Select(x => x.GetString()));
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
        Assert.StartsWith("https://d3322rrb6yrsp3.cloudfront.net/?datos=", cifrado);
        Assert.Contains("numeroPoliza=1000000332", enlace.GetProperty("referencia").GetString());
        Assert.Equal(JsonValueKind.Null, filas[2].GetProperty("enlace").ValueKind);

        // El backend del formulario (mismas llaves) recupera exactamente los datos.
        var svc = _f.Services.GetRequiredService<IEnlaceProtegidoService>();
        var r = svc.ValidarContenido(QueryHelpers.ParseQuery(new Uri(cifrado).Query)["datos"]);
        Assert.True(r.Valido);
        Assert.Equal(new DatosEnrolamiento("SPN-F.GTP-61", "CONTRATANTE DE PRUEBA", "1000000332", "cliente@example.test"), r.Datos);
    }

    [Fact]
    public async Task Vista_previa_usa_el_correo_fijo_con_los_datos_de_la_fila()
    {
        var lote = await Subir(_c, "a.csv", Csv(
            "SPN-F.GTP-61;Contratante Uno;Ana;CAMP-1;ana@example.test",
            "SPN-F.GTP-61;Compañía <Dos>;José <b>Pérez</b>;CAMP-1;jose@example.test"));
        var v = await _c.GetFromJsonAsync<JsonElement>($"/api/lotes/{lote.GetProperty("id")}/vista-previa?numeroFila=3");
        var html = v.GetProperty("html").GetString()!;

        Assert.Equal("Completa tu formulario de enrolamiento", v.GetProperty("asunto").GetString());
        Assert.Contains("Estimado(a) <strong>Jos&#233; &lt;b&gt;P&#233;rez&lt;/b&gt;</strong>", html);
        Assert.Contains("<strong>Compa&#241;&#237;a &lt;Dos&gt;</strong> pone a", html);
        Assert.Contains("href=\"https://d3322rrb6yrsp3.cloudfront.net/?datos=", html);
        Assert.Contains("src=\"https://cdn.example.test/correo/encabezado.jpg\"", html);
        Assert.Empty(v.GetProperty("advertencias").EnumerateArray());
        Assert.All(html, ch => Assert.True(ch < 128));
    }

    [Fact]
    public async Task Envia_por_fila_y_no_permite_enviar_dos_veces()
    {
        var lote = await Subir(_c, "a.csv", Csv(
            "SPN-F.GTP-61;Contratante A;Cliente A;111;a@example.test",
            "SPN-F.GTP-62;Contratante B;Cliente B;222;b@example.test",
            "99;Inválida;X;333;c@example.test"));
        var id = lote.GetProperty("id").ToString();

        var r = await _c.PostAsync($"/api/lotes/{id}/envio", null);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var fin = await EsperarFin(_c, id);
        Assert.Equal(2, fin.GetProperty("resumen").GetProperty("enviada").GetInt32());
        Assert.Equal(1, fin.GetProperty("resumen").GetProperty("invalida").GetInt32());

        var enviados = _f.Enviador.Enviados.Where(m => m.Destinatario is "a@example.test" or "b@example.test").ToList();
        Assert.Equal(2, enviados.Count);
        Assert.All(enviados, m => Assert.Equal("no-responder@example.test", m.Remitente));
        Assert.All(enviados, m => Assert.Equal("Completa tu formulario de enrolamiento", m.Asunto));
        Assert.Contains(enviados, m => m.Html.Contains("Cliente B") && m.Html.Contains("Contratante B"));

        Assert.Equal(HttpStatusCode.Conflict, (await _c.PostAsync($"/api/lotes/{id}/envio", null)).StatusCode);
    }

    [Fact]
    public async Task Reintenta_solo_los_fallidos()
    {
        var lote = await Subir(_c, "a.csv", Csv("SPN-F.GTP-61;A;A;1;ok1@example.test", "SPN-F.GTP-61;B;B;2;falla@example.test"));
        var id = lote.GetProperty("id").ToString();
        await _c.PostAsync($"/api/lotes/{id}/envio", null);
        var fin = await EsperarFin(_c, id);
        Assert.Equal(1, fin.GetProperty("resumen").GetProperty("fallida").GetInt32());
        var fallida = fin.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("estado").GetString() == "Fallida");
        Assert.Equal("SMTP no disponible", fallida.GetProperty("mensaje").GetString());

        var antes = _f.Enviador.Enviados.Count(m => m.Destinatario == "ok1@example.test");
        Assert.Equal(HttpStatusCode.Accepted, (await _c.PostAsync($"/api/lotes/{id}/reintentar", null)).StatusCode);
        await EsperarFin(_c, id);
        Assert.Equal(antes, _f.Enviador.Enviados.Count(m => m.Destinatario == "ok1@example.test")); // no reenvía los exitosos
    }

    [Fact]
    public async Task Sin_imagenes_configuradas_no_envia_y_la_vista_previa_lo_advierte()
    {
        using var f = new Fabrica
        {
            Config = new()
            {
                ["Correo:ImagenesBaseUrl"] = "", ["Correo:Modo"] = "Oracle",
                ["Oracle:DataSource"] = "host:1521/servicio", ["Oracle:Usuario"] = "u", ["Oracle:Clave"] = "c",
            },
        };
        var c = f.ClienteConSesion();
        var lote = await Subir(c, "a.csv", Csv("SPN-F.GTP-61;A;A;1;x@example.test"));
        var id = lote.GetProperty("id").ToString();

        var v = await c.GetFromJsonAsync<JsonElement>($"/api/lotes/{id}/vista-previa");
        Assert.Contains("src=\"/correo/encabezado.jpg\"", v.GetProperty("html").GetString());   // imágenes del propio portal
        Assert.Contains(v.GetProperty("advertencias").EnumerateArray(), a => a.GetString()!.Contains("ImagenesBaseUrl"));

        var r = await c.PostAsync($"/api/lotes/{id}/envio", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(f.Enviador.Enviados);
    }

    [Fact]
    public async Task La_peticion_de_envio_ya_no_lleva_html()
    {
        // El WAF bloqueaba el POST con el HTML del correo: ahora la vista previa es GET y el envío no tiene cuerpo.
        var lote = await Subir(_c, "a.csv", Csv("SPN-F.GTP-61;A;A;1;x@example.test"));
        var r = await _c.PostAsJsonAsync($"/api/lotes/{lote.GetProperty("id")}/vista-previa", new { cuerpo = "<p>x</p>" });
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
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
    public async Task Respuestas_api_no_se_cachean()
    {
        var r = await _c.GetAsync("/api/configuracion");
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
    }
}

public class ArranqueTests
{
    private static readonly Dictionary<string, string?> OracleOk = new()
    {
        ["Correo:Remitente"] = "notificaciones@example.test",
        ["Oracle:DataSource"] = "host:1521/servicio", ["Oracle:Usuario"] = "u", ["Oracle:Clave"] = "c",
    };

    [Theory]
    [InlineData("Development", "http://172.26.12.34/MFOnboardingDigital/correo")]   // servidor interno sin certificado, sin "/" final
    [InlineData("Staging", "http://servidor-qa/MFOnboardingDigital/correo/")]
    [InlineData("Production", "http://172.26.12.34/MFOnboardingDigital/correo/")]          // también en Production
    [InlineData("Production", "https://seguros.example.test/MFOnboardingDigital/correo")]
    public async Task Imagenes_por_http_o_https_en_cualquier_ambiente(string ambiente, string url)
    {
        using var f = new Fabrica { Ambiente = ambiente, Config = new(OracleOk) { ["Correo:ImagenesBaseUrl"] = url } };
        var c = f.ClienteConSesion();
        var lote = await ApiTests.Subir(c, "a.csv", ApiTests.Csv("SPN-F.GTP-61;A;Ana;C1;a@example.test"));
        var v = await c.GetFromJsonAsync<JsonElement>($"/api/lotes/{lote.GetProperty("id")}/vista-previa");
        Assert.Contains($"src=\"{url.TrimEnd('/')}/encabezado.jpg\"", v.GetProperty("html").GetString());
        Assert.Empty(v.GetProperty("advertencias").EnumerateArray());
    }

    [Theory]
    [InlineData("Production", "/MFOnboardingDigital/correo/", "absoluta")]
    [InlineData("Development", "ftp://servidor/correo/", "absoluta")]
    public void Imagenes_invalidas_no_arranca(string ambiente, string url, string mensaje)
    {
        using var f = new Fabrica { Ambiente = ambiente, Config = new(OracleOk) { ["Correo:ImagenesBaseUrl"] = url } };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains(mensaje, ex.ToString());
    }

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
    [InlineData("Development", "https://d3322rrb6yrsp3.cloudfront.net/")]
    [InlineData("Staging", "https://d1i4s8s51j1tuu.cloudfront.net/")]
    [InlineData("Production", "https://seguros.ficohsa.com/")]
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
        var c = await f.ClienteConSesion().GetFromJsonAsync<JsonElement>("/api/configuracion");
        Assert.Equal(url, c.GetProperty("urlPublicaBase").GetString());
        Assert.Equal("Oracle", c.GetProperty("modoEnvio").GetString());
    }
}

public class ConfiguracionLocalTests
{
    [Fact]
    public async Task Archivo_local_de_credenciales_se_aplica()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var a in Directory.GetFiles(AppContext.BaseDirectory, "appsettings*.json"))
            File.Copy(a, Path.Combine(dir, Path.GetFileName(a)));
        // Llave propia del ambiente en otra variable (ENROLAMIENTO_DP_LLAVES_DEV), indicada en el archivo local.
        Environment.SetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES_DEV_PRUEBA", Utilidades.LlavePrueba);
        File.WriteAllText(Path.Combine(dir, "appsettings.Development.local.json"),
            """{ "Enlaces": { "VariableEntornoLlaves": "ENROLAMIENTO_DP_LLAVES_DEV_PRUEBA" }, "Correo": { "Remitente": "dev@example.test" }, "Oracle": { "DataSource": "h:1521/s", "Usuario": "u", "Clave": "c" } }""");
        using var f = new Fabrica { Ambiente = "Development" };
        // UseSetting de la fábrica tiene prioridad sobre los JSON: se quita la variable de llave para que mande el archivo local.
        using var conRaiz = f.WithWebHostBuilder(b => { b.UseContentRoot(dir); b.UseSetting("Enlaces:VariableEntornoLlaves", null); });
        // WithWebHostBuilder crea otra fábrica: se inicia sesión a mano.
        var cli = conRaiz.CreateClient();
        cli.DefaultRequestHeaders.Add("X-Solicitud", "1");
        (await cli.PostAsJsonAsync("/api/auth/login", new { usuario = Fabrica.Usuario, clave = Fabrica.Clave })).EnsureSuccessStatusCode();
        var c = await cli.GetFromJsonAsync<JsonElement>("/api/configuracion");
        Assert.Equal("dev@example.test", c.GetProperty("remitente").GetString());
        Assert.Equal("https://d3322rrb6yrsp3.cloudfront.net/", c.GetProperty("urlPublicaBase").GetString());
    }
}


public class SubrutaIisTests
{
    private static async Task<string> Pagina(Fabrica f, string url)
    {
        var r = await f.Server.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        return await r.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("http://localhost/MFOnboardingDigital")]      // sin barra final (caso IIS que fallaba)
    [InlineData("http://localhost/MFOnboardingDigital/")]
    [InlineData("http://localhost/MFOnboardingDigital/revision")]
    public async Task En_subruta_la_pantalla_lleva_base_href_de_la_aplicacion(string url)
    {
        using var f = new Fabrica();
        f.Server.BaseAddress = new Uri("http://localhost/MFOnboardingDigital/");
        var html = await Pagina(f, url);
        Assert.Contains("<base href=\"/MFOnboardingDigital/\" />", html);
        Assert.Contains("./assets/", html);
    }

    [Fact]
    public async Task Ruta_de_api_inexistente_no_devuelve_la_pantalla()
    {
        using var f = new Fabrica();
        var r = await f.ClienteConSesion().GetAsync("/api/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task En_la_raiz_la_base_es_barra()
    {
        using var f = new Fabrica();
        var html = await Pagina(f, "http://localhost/");
        Assert.Contains("<base href=\"/\" />", html);
    }

    [Fact]
    public async Task Los_archivos_de_la_pantalla_se_sirven_dentro_de_la_subruta()
    {
        using var f = new Fabrica();
        f.Server.BaseAddress = new Uri("http://localhost/MFOnboardingDigital/");
        var html = await Pagina(f, "http://localhost/MFOnboardingDigital");
        var js = System.Text.RegularExpressions.Regex.Match(html, @"\./(assets/index-[^""]+\.js)").Groups[1].Value;
        var r = await f.Server.CreateClient().GetAsync("http://localhost/MFOnboardingDigital/" + js);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
