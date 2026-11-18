using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Enrolamiento.EnvioCorreos.Api.Seguridad;

namespace Enrolamiento.Tests;

public class HashClaveTests
{
    [Fact]
    public void Verifica_la_clave_correcta_y_rechaza_otras()
    {
        var h = HashClave.Crear("Mi-Clave-Segura-1", 1000);
        Assert.StartsWith("PBKDF2-SHA256:1000:", h);
        Assert.DoesNotContain("Mi-Clave", h);
        Assert.DoesNotContain("$", h);
        Assert.True(HashClave.Verificar("Mi-Clave-Segura-1", h));
        Assert.False(HashClave.Verificar("mi-clave-segura-1", h));
        Assert.False(HashClave.Verificar("", h));
    }

    [Fact]
    public void Misma_clave_genera_hashes_distintos_por_la_sal() =>
        Assert.NotEqual(HashClave.Crear("igual-igual-1", 1000), HashClave.Crear("igual-igual-1", 1000));

    [Fact]
    public void Por_defecto_usa_600000_iteraciones() =>
        Assert.StartsWith("PBKDF2-SHA256:600000:", HashClave.Crear("una-clave-larga"));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("clave-en-texto")] [InlineData("PBKDF2-SHA256:10:AAAA:AAAA")]
    [InlineData("PBKDF2-SHA256:600000:no-base64!:AAAA")] [InlineData("SHA1:600000:AAAA:AAAA")]
    public void Formato_invalido(string? valor)
    {
        Assert.False(HashClave.EsFormatoValido(valor));
        Assert.False(HashClave.Verificar("x", valor));
    }
}

public class LoginTests
{
    private static HttpClient SinSesion(Fabrica f)
    {
        var c = f.CreateClient();
        return c;
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string usuario, string clave) =>
        c.PostAsJsonAsync("/api/auth/login", new { usuario, clave });

    [Theory]
    [InlineData("GET", "/api/configuracion")]
    [InlineData("GET", "/api/plantilla")]
    [InlineData("GET", "/api/lotes/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/lotes")]
    [InlineData("POST", "/api/lotes/00000000-0000-0000-0000-000000000001/envio")]
    public async Task Sin_sesion_toda_la_api_responde_401(string metodo, string ruta)
    {
        using var f = new Fabrica();
        var r = await SinSesion(f).SendAsync(new HttpRequestMessage(new HttpMethod(metodo), ruta));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task La_pantalla_carga_sin_sesion_para_mostrar_el_login()
    {
        using var f = new Fabrica();
        var c = SinSesion(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/sesion")).StatusCode);
    }

    [Fact]
    public async Task Login_correcto_crea_sesion_y_logout_la_cierra()
    {
        using var f = new Fabrica();
        var c = SinSesion(f);
        var r = await Login(c, "OPERADOR", Fabrica.Clave); // el usuario no distingue mayúsculas
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = r.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("Enrolamiento.Sesion="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var sesion = await c.GetFromJsonAsync<JsonElement>("/api/auth/sesion");
        Assert.Equal("operador", sesion.GetProperty("usuario").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/configuracion")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/configuracion")).StatusCode);
    }

    [Theory]
    [InlineData("operador", "clave-incorrecta")]
    [InlineData("otro", Fabrica.Clave)]
    [InlineData("", "")]
    public async Task Login_incorrecto_responde_401_con_mensaje_generico(string usuario, string clave)
    {
        using var f = new Fabrica();
        var r = await Login(SinSesion(f), usuario, clave);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("Usuario o contraseña incorrectos.", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Limita_intentos_por_minuto()
    {
        using var f = new Fabrica { Config = new() { ["Login:IntentosPorMinuto"] = "3" } };
        var c = SinSesion(f);
        for (int i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "operador", "mala")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(c, "operador", Fabrica.Clave)).StatusCode);
    }

    [Fact]
    public async Task Post_sin_cabecera_del_portal_se_rechaza()
    {
        using var f = new Fabrica();
        var c = f.ClienteConSesion();
        c.DefaultRequestHeaders.Remove("X-Solicitud");
        var r = await c.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData("Login:Usuario", "", "Login__Usuario")]
    [InlineData("Login:ClaveHash", "contraseña-en-texto-plano", "Login__ClaveHash")]
    public void Sin_credenciales_validas_no_arranca(string clave, string valor, string mensaje)
    {
        using var f = new Fabrica { Config = new() { [clave] = valor } };
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains(mensaje, ex.ToString());
    }
}
