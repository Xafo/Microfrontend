using Enrolamiento.Enlaces;
using Microsoft.Extensions.DependencyInjection;

namespace Enrolamiento.Tests;

public class ValidadorTests
{
    private static IReadOnlyList<ErrorCampo> Validar(DatosEnrolamiento d)
    {
        using var sp = Utilidades.Crear();
        return sp.GetRequiredService<ValidadorDatosEnrolamiento>().Validar(d);
    }

    [Fact]
    public void Tipos_configurados_reemplazan_los_por_defecto()
    {
        using var sp = Utilidades.Crear(extra: new() { ["Enlaces:TiposFormularioPermitidos:0"] = "61" });
        var v = sp.GetRequiredService<ValidadorDatosEnrolamiento>();
        Assert.Empty(v.Validar(Utilidades.Datos(tipo: "61")));
        Assert.NotEmpty(v.Validar(Utilidades.Datos(tipo: "62")));
    }

    [Fact] public void Datos_validos() => Assert.Empty(Validar(Utilidades.Datos()));

    [Theory]
    [InlineData("61")] [InlineData("62")] [InlineData("63")] [InlineData("64")]
    [InlineData("101")] [InlineData("102")] [InlineData("161")]
    public void Tipos_permitidos(string tipo) => Assert.Empty(Validar(Utilidades.Datos(tipo: tipo)));

    [Theory] [InlineData("60")] [InlineData("")] [InlineData(" ")] [InlineData("061")]
    public void Tipo_no_permitido(string tipo) => Assert.Contains(Validar(Utilidades.Datos(tipo: tipo)), e => e.Campo == "TipoFormulario");

    [Theory] [InlineData("")] [InlineData("   ")] [InlineData(" Juan")]
    public void Nombre_invalido(string n) => Assert.Contains(Validar(Utilidades.Datos(nombre: n)), e => e.Campo == "NombreContratante");

    [Fact] public void Nombre_demasiado_largo() => Assert.Contains(Validar(Utilidades.Datos(nombre: new string('A', 201))), e => e.Campo == "NombreContratante");

    [Theory] [InlineData("")] [InlineData("12 34")] [InlineData("12;DROP")]
    public void Poliza_invalida(string p) => Assert.Contains(Validar(Utilidades.Datos(poliza: p)), e => e.Campo == "NumeroPoliza");

    [Theory] [InlineData("0")] [InlineData("-5")] [InlineData("1.234")] [InlineData("1000000000000")]
    public void Suma_invalida(string s) => Assert.Contains(Validar(Utilidades.Datos(suma: decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture))), e => e.Campo == "SumaAsegurada");

    [Theory] [InlineData("1.20")] [InlineData("1.200")] [InlineData("2500000")]
    public void Suma_valida(string s) => Assert.Empty(Validar(Utilidades.Datos(suma: decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture))));

    [Theory] [InlineData("sin-arroba")] [InlineData("a@b")] [InlineData("Juan <a@b.com>")] [InlineData("a b@c.com")] [InlineData("a@b.com,c@d.com")]
    public void Correo_invalido(string c) => Assert.Contains(Validar(Utilidades.Datos(correo: c)), e => e.Campo == "Correo");

    [Theory] [InlineData("nombre.apellido@ficohsa.com")] [InlineData("o'neil+x@dominio.com.hn")]
    public void Correo_valido(string c) => Assert.Empty(Validar(Utilidades.Datos(correo: c)));
}
