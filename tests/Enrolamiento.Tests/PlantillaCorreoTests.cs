using Enrolamiento.EnvioCorreos.Api.Correo;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Tests;

public class PlantillaCorreoTests
{
    private const string Imagenes = "https://cdn.example.test/correo/";
    private static readonly DatosPlantilla D = new("José Pérez <b>", "Compañía & Hijos", "CAMP-001", "SPN-F.GTP-61",
        new Uri("https://x.test/?datos=abc_-123"), new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero));

    private static PlantillaCorreo Crear(string? imagenes = Imagenes, int max = 32767, ModoEnvio modo = ModoEnvio.Oracle) =>
        new(Options.Create(new CorreoOptions { ImagenesBaseUrl = imagenes, LongitudMaximaMensaje = max, Modo = modo }));

    [Fact]
    public void Usa_las_cuatro_imagenes_del_diseno_en_orden()
    {
        var html = Crear().Renderizar(D).Html;
        var posiciones = PlantillaCorreo.Imagenes.Select(i => html.IndexOf(Imagenes + i, StringComparison.Ordinal)).ToList();
        Assert.All(posiciones, p => Assert.True(p > 0));
        Assert.Equal(posiciones.OrderBy(p => p), posiciones);   // encabezado, botón, logo, pie
    }

    [Fact]
    public void Inserta_cliente_y_contratante_escapados_y_en_ascii()
    {
        var html = Crear().Renderizar(D).Html;
        Assert.Contains("Estimado(a) <strong>Jos&#233; P&#233;rez &lt;b&gt;</strong>", html);
        Assert.Contains("<strong>Compa&#241;&#237;a &amp; Hijos</strong> pone a <strong>disposici&#243;n de sus colaboradores.</strong>", html);
        Assert.All(html, c => Assert.True(c < 128));
    }

    [Fact]
    public void El_boton_y_el_enlace_de_respaldo_llevan_el_enlace_cifrado()
    {
        var html = Crear().Renderizar(D).Html;
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "href=\"https://x.test/\\?datos=abc_-123\"").Count);
        Assert.Contains("boton-enrolamiento.png", html);
    }

    [Fact]
    public void Fecha_de_vencimiento_en_hora_de_honduras() =>
        Assert.Contains("19/11/2026 12:00", Crear().Renderizar(D).Html);

    [Fact]
    public void Base_de_imagenes_de_la_vista_previa_tiene_prioridad()
    {
        var html = Crear(imagenes: null).Renderizar(D, "/MFOnboardingDigital/correo/").Html;
        Assert.Contains("src=\"/MFOnboardingDigital/correo/encabezado.jpg\"", html);
    }

    [Fact]
    public void Sin_imagenes_configuradas_no_se_puede_enviar()
    {
        Assert.Contains(Crear(imagenes: null).ValidarEnvio(), e => e.Contains("ImagenesBaseUrl"));
        Assert.Empty(Crear().ValidarEnvio());
        Assert.Empty(Crear(imagenes: null, modo: ModoEnvio.Simulado).ValidarEnvio());   // pruebas locales
    }

    [Fact]
    public void Asunto_fijo_sin_tildes()
    {
        var p = new PlantillaCorreo(Options.Create(new CorreoOptions { ImagenesBaseUrl = Imagenes, Asunto = "Completa tu formulario de enrolamiento ¡ya!" }));
        var r = p.Renderizar(D);
        Assert.Equal("Completa tu formulario de enrolamiento ya!", r.Asunto);
        Assert.True(r.AsuntoModificado);
    }

    [Fact]
    public void Mensaje_demasiado_largo_se_reporta() =>
        Assert.Contains(Crear(max: 500).Renderizar(D).Errores, e => e.Contains("supera"));

    [Theory]
    [InlineData("¿Complete su formulario?", "Complete su formulario?")]
    [InlineData("Póliza Ñandú – “ok”", "Poliza Nandu - \"ok\"")]
    public void Asunto_sin_tildes(string entrada, string esperado) => Assert.Equal(esperado, PlantillaCorreo.SinTildes(entrada));

    [Fact]
    public void Ascii_seguro_con_emoji() => Assert.Equal("a&#241;&#128512;", PlantillaCorreo.AAsciiSeguro("añ😀"));

    [Fact]
    public void Las_imagenes_existen_en_el_frontend()
    {
        var carpeta = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "public", "correo"));
        Assert.All(PlantillaCorreo.Imagenes, i => Assert.True(File.Exists(Path.Combine(carpeta, i)), i));
    }
}
