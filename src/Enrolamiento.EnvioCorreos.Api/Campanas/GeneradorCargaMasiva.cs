using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Enrolamiento.EnvioCorreos.Api.Campanas;

/// <summary>
/// Llena la plantilla de carga masiva (Plantillas/Carga_Masiva.xlsx) con una fila por solicitud.
/// Solo se completan las columnas marcadas en amarillo; las demás quedan vacías para el área que hace la carga.
/// Las columnas se ubican por el texto del encabezado, no por la letra.
/// </summary>
public sealed class GeneradorCargaMasiva
{
    public static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "Plantillas", "Carga_Masiva.xlsx");

    private static readonly (string Encabezado, Func<SolicitudCampana, string> Valor, Tipo Tipo)[] Mapeo =
    [
        ("PRIMER APELLIDO", s => s.PrimerApellido, Tipo.Texto),
        ("SEGUNDO APELLIDO", s => s.SegundoApellido, Tipo.Texto),
        ("PRIMER NOMBRE", s => s.PrimerNombre, Tipo.Texto),
        ("SEGUNDO NOMBRE", s => s.SegundoNombre, Tipo.Texto),
        ("TIPODOC", s => s.TipoDocumento, Tipo.Numero),
        ("NUMIDENTIDAD", s => s.NumeroDocumento, Tipo.Texto),
        ("PAISNAC", s => s.Nacionalidad, Tipo.Texto),
        ("FECNAC", s => s.FechaNacimiento, Tipo.Fecha),
        ("SEXO", s => s.Sexo, Tipo.Texto),
        ("PROFESION", s => s.Profesion, Tipo.Texto),
        ("TELEFONO1", s => s.Celular, Tipo.Texto),
        ("TELEFONO2", s => s.TelefonoFijo, Tipo.Texto),
        ("FECHA INCLUSION", s => s.FechaIngresoPoliza, Tipo.Fecha),
    ];

    private enum Tipo { Texto, Numero, Fecha }

    private static readonly string[] FormatosFecha = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "dd-MM-yyyy"];

    public byte[] Generar(IReadOnlyList<SolicitudCampana> solicitudes)
    {
        using var libro = new XLWorkbook(Ruta);
        var hoja = libro.Worksheet(1);

        // La plantilla repite encabezados (varias columnas "VACIO"): se toma la primera aparición de cada uno.
        var columnas = hoja.Row(1).CellsUsed()
            .GroupBy(c => Normalizar(c.GetString()))
            .ToDictionary(g => g.Key, g => g.First().Address.ColumnNumber);
        var faltan = Mapeo.Where(m => !columnas.ContainsKey(m.Encabezado)).Select(m => m.Encabezado).ToList();
        if (faltan.Count > 0) throw new InvalidOperationException($"La plantilla de carga masiva no tiene las columnas: {string.Join(", ", faltan)}.");

        var fila = 2;
        foreach (var s in solicitudes)
        {
            foreach (var (encabezado, valor, tipo) in Mapeo)
            {
                var celda = hoja.Cell(fila, columnas[encabezado]);
                var v = valor(s);
                if (v.Length == 0) continue;
                switch (tipo)
                {
                    case Tipo.Numero when long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n):
                        celda.SetValue(n);
                        break;
                    case Tipo.Fecha when DateTime.TryParseExact(v, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f):
                        celda.SetValue(f);
                        celda.Style.DateFormat.Format = "dd/mm/yyyy";
                        break;
                    default:
                        celda.SetValue(v);          // texto: conserva guiones y ceros a la izquierda
                        celda.Style.NumberFormat.Format = "@";
                        break;
                }
            }
            fila++;
        }

        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Normalizar(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(c));
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
