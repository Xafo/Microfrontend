using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Enrolamiento.Tests;

public class LectorDestinatariosTests
{
    private static ResultadoLectura Leer(string archivo, CargaOptions? o = null)
    {
        using var sp = Utilidades.Crear();
        var lector = new LectorDestinatarios(Options.Create(o ?? new CargaOptions()), sp.GetRequiredService<ValidadorDatosEnrolamiento>());
        return lector.Leer(archivo, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Archivos", archivo)));
    }

    [Theory]
    [InlineData("destinatarios.xlsx")]
    [InlineData("destinatarios.xls")]
    public void Lee_excel(string archivo)
    {
        var r = Leer(archivo);
        Assert.Equal("Destinatarios", r.Hoja);
        Assert.Equal(4, r.Filas.Count); // la fila vacía se omite

        var a = r.Filas[0];
        Assert.Equal(2, a.NumeroFila);
        Assert.Equal(("SPN-F.GTP-61", "CONTRATANTE DE PRUEBA", "José Pérez", "1000000332", "cliente@example.test"),
            (a.TipoFormulario, a.NombreContratante, a.NombreCliente, a.NumeroPoliza, a.Correo));
        Assert.Empty(a.Errores);

        var b = r.Filas[1];
        Assert.Equal("0001234", b.NumeroPoliza);            // ceros a la izquierda conservados
        Assert.Equal("Compañía Ñandú S.A.", b.NombreContratante);
        Assert.Empty(b.Errores);

        var c = r.Filas[2];
        Assert.Contains(c.Errores, e => e.Campo == "TipoFormulario");
        Assert.Contains(c.Errores, e => e.Campo == "Correo");

        Assert.Contains(r.Filas[3].Errores, e => e.Campo == "Fila" && e.Mensaje.Contains("fila 2"));
    }

    [Fact]
    public void Lee_csv_utf8_con_punto_y_coma_y_comillas()
    {
        var r = Leer("csv_utf8.csv");
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal("Pérez; Hijos & Cía", r.Filas[0].NombreContratante);
        Assert.Equal("0001000332", r.Filas[0].NumeroPoliza);
        Assert.Empty(r.Filas[0].Errores);
        Assert.Equal("Dice \"hola\"", r.Filas[1].NombreContratante);
        Assert.Equal("SPN-F.GTP-161", r.Filas[1].TipoFormulario);   // "  spn-f.gtp-161 " normalizado
        Assert.Empty(r.Filas[1].Errores);
        Assert.Equal(4, r.Filas[1].NumeroFila);
    }

    [Fact]
    public void Lee_csv_windows_1252_con_comas()
    {
        var r = Leer("csv_1252.csv");
        var f = Assert.Single(r.Filas);
        Assert.Equal("Compañía Ñandú", f.NombreContratante);
        Assert.Equal("COACEHL (SPN-F.GTP-102)", f.TipoFormulario);   // espacios dobles compactados
        Assert.Empty(f.Errores);
    }

    [Fact]
    public void Archivo_sin_columna_suma_asegurada_se_acepta()
    {
        using var sp = Utilidades.Crear();
        var lector = new LectorDestinatarios(Options.Create(new CargaOptions()), sp.GetRequiredService<ValidadorDatosEnrolamiento>());
        var csv = "Tipo de Formulario;Nombre del Contratante;Nombre del Cliente;Numero de Poliza;Correo\nSPN-F.GTP-61;Empresa;Ana;123;ana@example.test\n"u8.ToArray();
        var f = Assert.Single(lector.Leer("a.csv", csv).Filas);
        Assert.Empty(f.Errores);
    }

    [Fact]
    public void Rechaza_archivo_sin_encabezados()
    {
        var ex = Assert.Throws<FormatoArchivoException>(() => Leer("sin_encabezados.csv"));
        Assert.Contains("Nombre del Cliente", ex.Message);
    }

    [Fact]
    public void Limita_filas()
    {
        var r = Leer("destinatarios.xlsx", new CargaOptions { FilasMaximas = 2 });
        Assert.Equal(2, r.Filas.Count);
        Assert.Contains(r.ErroresArchivo, e => e.Contains("máximo"));
    }

    [Theory]
    [InlineData("a.pdf", new byte[] { 1, 2, 3 })]
    [InlineData("a.xlsx", new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45 })]
    [InlineData("a.csv", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 })]
    [InlineData("a.csv", new byte[0])]
    public void Rechaza_extension_o_contenido_incorrecto(string nombre, byte[] bytes)
    {
        using var sp = Utilidades.Crear();
        var lector = new LectorDestinatarios(Options.Create(new CargaOptions()), sp.GetRequiredService<ValidadorDatosEnrolamiento>());
        Assert.Throws<FormatoArchivoException>(() => lector.Leer(nombre, bytes));
    }

    [Fact]
    public void Plantilla_entregada_tiene_los_encabezados()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Plantillas", "Enrolamiento_EnvioCorreos.xlsx");
        using var sp = Utilidades.Crear();
        var lector = new LectorDestinatarios(Options.Create(new CargaOptions()), sp.GetRequiredService<ValidadorDatosEnrolamiento>());
        var r = lector.Leer(ruta, File.ReadAllBytes(ruta));
        Assert.Empty(r.Filas);
        Assert.Contains(r.ErroresArchivo, e => e.Contains("no tiene filas"));
    }

    [Theory]
    [InlineData(1000000332d, "1000000332")]
    [InlineData(12.5d, "12.5")]
    public void Numero_a_texto_sin_notacion_cientifica(double v, string esperado) => Assert.Equal(esperado, ConversorValores.ATexto(v));
}
