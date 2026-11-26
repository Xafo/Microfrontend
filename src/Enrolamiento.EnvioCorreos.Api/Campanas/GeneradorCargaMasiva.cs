using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Campanas;

/// <summary>
/// Llena la plantilla de carga masiva (Plantillas/Carga_Masiva.xlsx) según las reglas del área de suscripción:
/// una fila por asegurado titular (TIPOCLIEN A) y debajo una por cada dependiente (TIPOCLIEN D).
/// Columnas que completa el área comercial (COD. RESP. PAGO, FECHA INCLUSIÓN, CODPUNTOSERVICIO, AFILIADA) y las
/// que van vacías (DIRECCION, NUMCERTIFICADO, FECMOD, FECEXC) no se tocan. Las columnas se ubican por encabezado.
/// </summary>
public sealed class GeneradorCargaMasiva(IOptions<CargaMasivaOptions> opciones)
{
    public static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "Plantillas", "Carga_Masiva.xlsx");

    private static readonly string[] Requeridas =
    [
        "TIPOPROCESO", "PRIMER APELLIDO", "SEGUNDO APELLIDO", "PRIMER NOMBRE", "SEGUNDO NOMBRE", "TIPOCLIEN", "CODPARENT",
        "TIPOPERSONA", "TIPODOC", "NUMIDENTIDAD", "PAISNAC", "FECNAC", "SEXO", "PROFESION", "TELEFONO1", "TELEFONO2",
        "CONTINUIDAD_COBERTURA", "SALARIO",
    ];

    private static readonly string[] FormatosFecha = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "dd-MM-yyyy"];

    private readonly Dictionary<string, string> _tiposDocumento = Combinar(opciones.Value.TiposDocumento);
    private readonly HashSet<string> _continuidad = new(
        opciones.Value.FormulariosContinuidad.Count > 0 ? opciones.Value.FormulariosContinuidad : CargaMasivaOptions.FormulariosContinuidadPorDefecto,
        StringComparer.OrdinalIgnoreCase);

    public byte[] Generar(IReadOnlyList<SolicitudCampana> solicitudes)
    {
        using var libro = new XLWorkbook(Ruta);
        var hoja = libro.Worksheet(1);

        // La plantilla repite encabezados (varias columnas "VACIO"): se toma la primera aparición de cada uno.
        var col = hoja.Row(1).CellsUsed()
            .GroupBy(c => Normalizar(c.GetString()))
            .ToDictionary(g => g.Key, g => g.First().Address.ColumnNumber);
        var faltan = Requeridas.Where(r => !col.ContainsKey(r)).ToList();
        if (faltan.Count > 0) throw new InvalidOperationException($"La plantilla de carga masiva no tiene las columnas: {string.Join(", ", faltan)}.");

        var fila = 2;
        foreach (var s in solicitudes)
        {
            var esDni = ClaseDocumento(s.TipoDocumento) == "DNI";
            var continuidad = _continuidad.Contains(s.TipoFormulario.Trim()) ? "S" : "N";
            var sexo = Sexo(s.Sexo);

            // Asegurado titular (pantalla "Datos generales del asegurado")
            Texto(hoja, fila, col["TIPOPROCESO"], "INC");
            Texto(hoja, fila, col["PRIMER APELLIDO"], s.PrimerApellido);
            Texto(hoja, fila, col["SEGUNDO APELLIDO"], s.SegundoApellido);
            Texto(hoja, fila, col["PRIMER NOMBRE"], s.PrimerNombre);
            Texto(hoja, fila, col["SEGUNDO NOMBRE"], s.SegundoNombre);
            Texto(hoja, fila, col["TIPOCLIEN"], "A");
            Numero(hoja, fila, col["CODPARENT"], sexo == "F" ? 2 : sexo == "M" ? 1 : null);   // 0001 titular masculino / 0002 femenino
            var tipoPersona = esDni ? "L" : "E";
            Texto(hoja, fila, col["TIPOPERSONA"], tipoPersona);
            Numero(hoja, fila, col["TIPODOC"], TipoDoc(tipoPersona, ClaseDocumento(s.TipoDocumento)));
            Texto(hoja, fila, col["NUMIDENTIDAD"], s.NumeroDocumento);
            Texto(hoja, fila, col["PAISNAC"], s.Nacionalidad);
            Fecha(hoja, fila, col["FECNAC"], s.FechaNacimiento);
            Texto(hoja, fila, col["SEXO"], sexo);
            Texto(hoja, fila, col["PROFESION"], s.Profesion);
            Texto(hoja, fila, col["TELEFONO1"], s.Celular);
            Texto(hoja, fila, col["TELEFONO2"], s.TelefonoFijo);
            Texto(hoja, fila, col["CONTINUIDAD_COBERTURA"], continuidad);
            Decimal(hoja, fila, col["SALARIO"], s.SueldoMensual);
            fila++;

            // Dependientes (pantalla "Dependientes"): mismo NUMIDENTIDAD del titular, TIPOPERSONA L / TIPODOC 1.
            foreach (var d in s.Dependientes)
            {
                Texto(hoja, fila, col["TIPOPROCESO"], "INC");
                Texto(hoja, fila, col["PRIMER APELLIDO"], d.PrimerApellido);
                Texto(hoja, fila, col["SEGUNDO APELLIDO"], d.SegundoApellido);
                Texto(hoja, fila, col["PRIMER NOMBRE"], d.PrimerNombre);
                Texto(hoja, fila, col["SEGUNDO NOMBRE"], d.SegundoNombre);
                Texto(hoja, fila, col["TIPOCLIEN"], "D");
                Numero(hoja, fila, col["CODPARENT"], CodigoParentesco(d.Parentesco));   // 0003 cónyuge, 0004 hijo/a, 0030 hijastro/a
                Texto(hoja, fila, col["TIPOPERSONA"], "L");
                Numero(hoja, fila, col["TIPODOC"], 1);
                Texto(hoja, fila, col["NUMIDENTIDAD"], s.NumeroDocumento);
                Fecha(hoja, fila, col["FECNAC"], d.FechaNacimiento);
                Texto(hoja, fila, col["SEXO"], Sexo(d.Genero));
                Texto(hoja, fila, col["CONTINUIDAD_COBERTURA"], continuidad);
                fila++;
            }
        }

        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }

    private string? ClaseDocumento(string tipo) =>
        _tiposDocumento.TryGetValue(Normalizar(tipo), out var c) ? c : null;

    /// <summary>L → 1; E + pasaporte → 4; E + carnet de residente → 2.</summary>
    private static int? TipoDoc(string tipoPersona, string? clase) => (tipoPersona, clase) switch
    {
        ("L", _) => 1,
        ("E", "PASAPORTE") => 4,
        ("E", "CARNET") => 2,
        _ => null,
    };

    public static string Sexo(string v) => Normalizar(v) switch
    {
        "F" or "FEMENINO" or "FEMENINA" => "F",
        "M" or "MASCULINO" => "M",
        var x => x,
    };

    /// <summary>"0003" → 3, "0004" → 4, "0030" → 30; también acepta el texto (CONYUGE, HIJO/A, HIJASTRO/A).</summary>
    public static int? CodigoParentesco(string v)
    {
        var n = Normalizar(v);
        if (int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out var codigo)) return codigo;
        if (n.StartsWith("CONYUGE") || n.StartsWith("ESPOS")) return 3;
        if (n.StartsWith("HIJASTR")) return 30;
        if (n.StartsWith("HIJ")) return 4;
        return null;
    }

    private static void Texto(IXLWorksheet h, int f, int c, string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return;
        var celda = h.Cell(f, c);
        celda.SetValue(v.Trim());            // texto: conserva guiones y ceros a la izquierda
        celda.Style.NumberFormat.Format = "@";
    }

    private static void Numero(IXLWorksheet h, int f, int c, int? v)
    {
        if (v is null) return;
        h.Cell(f, c).SetValue(v.Value);
    }

    private static void Decimal(IXLWorksheet h, int f, int c, string v)
    {
        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
        {
            h.Cell(f, c).SetValue(d);
            h.Cell(f, c).Style.NumberFormat.Format = "0.00";
        }
    }

    private static void Fecha(IXLWorksheet h, int f, int c, string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return;
        if (DateTime.TryParseExact(v.Trim(), FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            h.Cell(f, c).SetValue(fecha);
            h.Cell(f, c).Style.DateFormat.Format = "dd/mm/yyyy";
        }
        else Texto(h, f, c, v);
    }

    private static Dictionary<string, string> Combinar(Dictionary<string, string> configurados)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in CargaMasivaOptions.TiposDocumentoPorDefecto) r[Normalizar(k)] = v;
        foreach (var (k, v) in configurados) r[Normalizar(k)] = Normalizar(v);
        return r;
    }

    private static string Normalizar(string? s)
    {
        var sb = new StringBuilder();
        foreach (var c in (s ?? "").Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(c));
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
