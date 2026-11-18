using Microsoft.AspNetCore.DataProtection;
using Enrolamiento.Enlaces;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Enrolamiento.Tests;

public class EnlaceProtegidoServiceTests
{
    private static string Datos(Uri url) => QueryHelpers.ParseQuery(url.Query)["datos"]!;

    [Theory]
    [InlineData("CONTRATANTE DE PRUEBA")]
    [InlineData("José Ñúñez Peña & Cía. \"La Única\" <S.A.> 100% ¿ok?")]
    [InlineData("Ünïcödé 漢字 😀 'comillas'")]
    public void Recupera_todos_los_campos_exactos(string nombre)
    {
        using var sp = Utilidades.Crear();
        var svc = sp.GetRequiredService<IEnlaceProtegidoService>();
        var datos = Utilidades.Datos(nombre: nombre);

        var enlace = svc.GenerarEnlace(datos);
        var r = svc.ValidarContenido(Datos(enlace.Url));

        Assert.True(r.Valido);
        Assert.Equal(datos, r.Datos);
        Assert.Equal(1, r.Version);
    }

    [Fact]
    public void Contenido_cifrado_tiene_solo_los_cuatro_campos()
    {
        using var sp = Utilidades.Crear();
        var cifrado = Datos(sp.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos()).Url);
        var json = sp.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Enrolamiento", "EnlaceCorreo", "v1").ToTimeLimitedDataProtector()
            .Unprotect(WebEncoders.Base64UrlDecode(cifrado), out _);
        var campos = System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["version", "tipoFormulario", "nombreContratante", "numeroPoliza", "correo"], campos);
    }

    [Fact]
    public void Rechaza_contenido_con_campos_extra()
    {
        using var sp = Utilidades.Crear();
        var json = """{"version":1,"tipoFormulario":"SPN-F.GTP-61","nombreContratante":"A","numeroPoliza":"1","sumaAsegurada":1,"correo":"a@b.co"}"""u8.ToArray();
        var protegido = sp.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Enrolamiento", "EnlaceCorreo", "v1").ToTimeLimitedDataProtector()
            .Protect(json, DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(sp.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(WebEncoders.Base64UrlEncode(protegido)).Valido);
    }

    [Fact]
    public void Url_usa_base_configurada_y_no_lleva_datos_en_claro()
    {
        using var sp = Utilidades.Crear();
        var enlace = sp.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos());
        var url = enlace.Url.AbsoluteUri;

        Assert.StartsWith("https://seguros.example.test/EnrolamientoHN/?datos=", url);
        foreach (var claro in new[] { "CONTRATANTE", "1000000332", "cliente", "example.test/EnrolamientoHN/?tipo" })
            Assert.DoesNotContain(claro, url[50..], StringComparison.OrdinalIgnoreCase);
        Assert.True(url.Length < 2000, $"Largo {url.Length}");
    }

    [Fact]
    public void Vence_en_48_horas_segun_reloj_del_servidor()
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        using var sp = Utilidades.Crear(reloj);
        var enlace = sp.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos());
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero), enlace.ExpiraEnUtc);
    }

    [Fact]
    public void Rechaza_enlace_vencido()
    {
        // Generado con un reloj 49 h en el pasado: según el reloj real ya venció.
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-49));
        using var sp = Utilidades.Crear(reloj);
        var svc = sp.GetRequiredService<IEnlaceProtegidoService>();
        var r = svc.ValidarContenido(Datos(svc.GenerarEnlace(Utilidades.Datos()).Url));
        Assert.False(r.Valido);
        Assert.Equal(MotivoRechazo.InvalidoOVencido, r.Motivo);
    }

    [Fact]
    public void Rechaza_contenido_manipulado()
    {
        using var sp = Utilidades.Crear();
        var svc = sp.GetRequiredService<IEnlaceProtegidoService>();
        var bytes = WebEncoders.Base64UrlDecode(Datos(svc.GenerarEnlace(Utilidades.Datos()).Url));
        bytes[^5] ^= 0x01;
        Assert.Equal(MotivoRechazo.InvalidoOVencido, svc.ValidarContenido(WebEncoders.Base64UrlEncode(bytes)).Motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no*es*base64url")]
    public void Rechaza_contenido_malformado(string? valor)
    {
        using var sp = Utilidades.Crear();
        Assert.Equal(MotivoRechazo.Malformado, sp.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(valor).Motivo);
    }

    [Fact]
    public void Rechaza_contenido_demasiado_largo()
    {
        using var sp = Utilidades.Crear();
        Assert.Equal(MotivoRechazo.Malformado, sp.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(new string('A', 5000)).Motivo);
    }

    [Fact]
    public void Rechaza_contenido_de_otro_proposito()
    {
        using var sp = Utilidades.Crear();
        var otro = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
            .CreateProtector("Enrolamiento", "OtroUso", "v1").ToTimeLimitedDataProtector()
            .Protect("{}"u8.ToArray(), DateTimeOffset.UtcNow.AddHours(1));
        var r = sp.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(WebEncoders.Base64UrlEncode(otro));
        Assert.Equal(MotivoRechazo.InvalidoOVencido, r.Motivo);
    }

    [Fact]
    public void Rechaza_version_no_soportada_aunque_este_cifrada_correctamente()
    {
        using var sp = Utilidades.Crear();
        var json = """{"version":2,"tipoFormulario":"SPN-F.GTP-61","nombreContratante":"A","numeroPoliza":"1","correo":"a@b.co"}"""u8.ToArray();
        var protegido = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
            .CreateProtector("Enrolamiento", "EnlaceCorreo", "v1").ToTimeLimitedDataProtector()
            .Protect(json, DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(sp.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(WebEncoders.Base64UrlEncode(protegido)).Valido);
    }

    [Fact]
    public void Rechaza_enlace_generado_con_otra_llave()
    {
        using var a = Utilidades.Crear();
        using var b = Utilidades.Crear(llave: GeneradorLlaves.CrearBase64("Ficohsa.EnrolamientoHN", TimeSpan.FromDays(30)));
        var url = a.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos()).Url;
        Assert.False(b.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(Datos(url)).Valido);
    }

    [Fact]
    public void Funciona_tras_recrear_la_aplicacion_con_la_misma_llave()
    {
        Uri url;
        using (var a = Utilidades.Crear()) url = a.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos()).Url;
        using var b = Utilidades.Crear();
        Assert.True(b.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(Datos(url)).Valido);
    }

    [Fact]
    public void Llave_rotada_conserva_las_anteriores()
    {
        var rotada = GeneradorLlaves.CrearBase64("Ficohsa.EnrolamientoHN", TimeSpan.FromDays(30), Utilidades.LlavePrueba);
        Uri url;
        using (var a = Utilidades.Crear()) url = a.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos()).Url;
        using var b = Utilidades.Crear(llave: rotada);
        Assert.True(b.GetRequiredService<IEnlaceProtegidoService>().ValidarContenido(Datos(url)).Valido);
    }

    [Fact]
    public void Sin_llaves_falla_explicitamente()
    {
        using var sp = Utilidades.Crear(extra: new() { ["Enlaces:VariableEntornoLlaves"] = "VARIABLE_QUE_NO_EXISTE_123" });
        Assert.ThrowsAny<Exception>(() => sp.VerificarLlavesEnlaces());
    }

    [Fact]
    public void Llave_corrupta_falla_explicitamente()
    {
        Assert.Throws<InvalidOperationException>(() => new LlavesDesdeVariableEntorno("X", "esto-no-es-xml"));
    }

    [Theory]
    [InlineData("http://seguros.example.test/EnrolamientoHN/")]
    [InlineData("https://seguros.example.test/EnrolamientoHN/?x=1")]
    [InlineData("https://seguros.example.test/EnrolamientoHN")]
    [InlineData("/EnrolamientoHN/")]
    public void Rechaza_url_base_invalida_al_arrancar(string url)
    {
        using var sp = Utilidades.Crear(extra: new() { ["Enlaces:UrlPublicaBase"] = url });
        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<EnlacesOptions>>().Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100")]
    public void Rechaza_vigencia_fuera_de_rango(string horas)
    {
        using var sp = Utilidades.Crear(extra: new() { ["Enlaces:VigenciaHoras"] = horas });
        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<EnlacesOptions>>().Value);
    }

    [Fact]
    public void Enlace_demasiado_largo_se_rechaza()
    {
        using var sp = Utilidades.Crear(extra: new() { ["Enlaces:LongitudMaximaUrl"] = "200" });
        var ex = Assert.Throws<DatosEnrolamientoInvalidosException>(() =>
            sp.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(Utilidades.Datos()));
        Assert.Contains(ex.Errores, e => e.Campo == "Enlace");
    }

    [Fact]
    public void Enlace_maximo_con_unicode_cabe_en_limite_de_iis()
    {
        using var sp = Utilidades.Crear();
        var datos = new DatosEnrolamiento("COACEHL (SPN-F.GTP-102)", new string('Ñ', 200), new string('9', 30),
            new string('a', 64) + "@" + new string('b', 60) + "." + new string('c', 60) + "." + new string('d', 60) + ".hn");
        Assert.Empty(sp.GetRequiredService<ValidadorDatosEnrolamiento>().Validar(datos));
        var url = sp.GetRequiredService<IEnlaceProtegidoService>().GenerarEnlace(datos).Url.AbsoluteUri;
        Assert.True(url.Length <= 2000, $"Largo {url.Length}");
        Console.WriteLine($"Largo máximo de enlace con datos al límite: {url.Length}");
    }

    [Fact]
    public void Enlace_de_referencia_muestra_datos_en_claro()
    {
        using var sp = Utilidades.Crear();
        var url = sp.GetRequiredService<IEnlaceProtegidoService>().ConstruirEnlaceReferenciaSinCifrar(Utilidades.Datos(nombre: "José"));
        Assert.Contains("numeroPoliza=1000000332", url.AbsoluteUri);
        Assert.Contains("Jos%C3%A9", url.AbsoluteUri);
    }
}
