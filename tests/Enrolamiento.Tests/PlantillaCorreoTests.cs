using Enrolamiento.EnvioCorreos.Api.Correo;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Tests;

public class PlantillaCorreoTests
{
    private static readonly DatosPlantilla D = new("Ana", "Contratante", "123", "61",
        new Uri("https://x.test/EnrolamientoHN/?datos=abc_-123"), new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Incluye_imagenes_de_encabezado_y_pie_si_estan_configuradas()
    {
        var p = new PlantillaCorreo(Options.Create(new CorreoOptions
        { ImagenEncabezadoUrl = "https://cdn.test/header.png", ImagenPieUrl = "https://cdn.test/footer.png" }));
        var html = p.Renderizar("A", PlantillaCorreo.CuerpoPorDefecto, D).Html;
        Assert.True(html.IndexOf("header.png") < html.IndexOf("Estimado(a) Ana") && html.IndexOf("Estimado(a) Ana") < html.IndexOf("footer.png"));
    }

    [Fact]
    public void Sin_imagenes_no_incluye_img()
    {
        var html = new PlantillaCorreo(Options.Create(new CorreoOptions())).Renderizar("A", PlantillaCorreo.CuerpoPorDefecto, D).Html;
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Fecha_de_vencimiento_en_hora_de_honduras()
    {
        var html = new PlantillaCorreo(Options.Create(new CorreoOptions())).Renderizar("A", "<p>{{Enlace}} {{FechaVencimiento}}</p>", D).Html;
        Assert.Contains("19/11/2026 12:00", html);
    }

    [Fact]
    public void Mensaje_demasiado_largo_se_reporta()
    {
        var p = new PlantillaCorreo(Options.Create(new CorreoOptions { LongitudMaximaMensaje = 500 }));
        Assert.Contains(p.Renderizar("A", PlantillaCorreo.CuerpoPorDefecto, D).Errores, e => e.Contains("supera"));
    }

    [Theory]
    [InlineData("¿Complete su formulario?", "Complete su formulario?")]
    [InlineData("Póliza Ñandú – “ok”", "Poliza Nandu - \"ok\"")]
    public void Asunto_sin_tildes(string entrada, string esperado) => Assert.Equal(esperado, PlantillaCorreo.SinTildes(entrada));

    [Fact]
    public void Ascii_seguro_con_emoji() => Assert.Equal("a&#241;&#128512;", PlantillaCorreo.AAsciiSeguro("añ😀"));
}
