using System.Data;
using Enrolamiento.Enlaces;
using ExcelDataReader;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

public sealed class FormatoArchivoException(string mensaje) : Exception(mensaje);

/// <summary>Lee .xlsx, .xls o .csv con el mismo formato de columnas y valida cada fila.</summary>
public sealed class LectorDestinatarios(IOptions<CargaOptions> carga, ValidadorDatosEnrolamiento validador)
{
    private readonly CargaOptions _o = carga.Value;

    // Codificaciones de Windows (1252) para .xls antiguos y CSV guardados por Excel.
    static LectorDestinatarios() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    public static readonly string[] ExtensionesPermitidas = [".xlsx", ".xls", ".csv"];

    public ResultadoLectura Leer(string nombreArchivo, byte[] contenido)
    {
        var ext = Path.GetExtension(nombreArchivo).ToLowerInvariant();
        if (!ExtensionesPermitidas.Contains(ext))
            throw new FormatoArchivoException("Solo se aceptan archivos .xlsx, .xls o .csv.");
        if (contenido.Length == 0) throw new FormatoArchivoException("El archivo está vacío.");

        var esZip = contenido.Length > 4 && contenido[0] == 0x50 && contenido[1] == 0x4B && contenido[2] == 0x03 && contenido[3] == 0x04;
        var esOle = contenido.Length > 8 && contenido[0] == 0xD0 && contenido[1] == 0xCF && contenido[2] == 0x11 && contenido[3] == 0xE0;
        if ((ext == ".xlsx" && !esZip) || (ext == ".xls" && !esOle) || (ext == ".csv" && (esZip || esOle)))
            throw new FormatoArchivoException($"El contenido del archivo no corresponde a la extensión {ext}.");

        var hojas = ext == ".csv" ? [("CSV", LectorCsv.Leer(contenido))] : LeerExcel(contenido);

        var ordenadas = hojas.OrderByDescending(h => string.Equals(h.Item1.Trim(), _o.NombreHoja, StringComparison.OrdinalIgnoreCase));
        foreach (var (hoja, celdas) in ordenadas)
        {
            var enc = BuscarEncabezados(celdas);
            if (enc is null) continue;
            return Mapear(hoja, celdas, enc.Value.fila, enc.Value.indices);
        }
        throw new FormatoArchivoException(
            $"No se encontraron los encabezados esperados: {string.Join(", ", Columnas.Obligatorias)}.");
    }

    private static List<(string, List<object?[]>)> LeerExcel(byte[] contenido)
    {
        try
        {
            using var ms = new MemoryStream(contenido);
            using var lector = ExcelReaderFactory.CreateReader(ms);
            var ds = lector.AsDataSet();
            return ds.Tables.Cast<DataTable>()
                .Select(t => (t.TableName, t.Rows.Cast<DataRow>().Select(r => r.ItemArray).ToList()))
                .ToList();
        }
        catch (Exception ex) when (ex is not FormatoArchivoException)
        {
            throw new FormatoArchivoException("No se pudo leer el archivo de Excel. Verifique que no esté dañado ni protegido con contraseña.");
        }
    }

    private static (int fila, Dictionary<string, int> indices)? BuscarEncabezados(List<object?[]> celdas)
    {
        var obligatorias = Columnas.Obligatorias.ToDictionary(Columnas.Normalizar);
        for (int f = 0; f < Math.Min(10, celdas.Count); f++)
        {
            var indices = new Dictionary<string, int>();
            for (int c = 0; c < celdas[f].Length; c++)
                if (obligatorias.TryGetValue(Columnas.Normalizar(ConversorValores.ATexto(celdas[f][c])), out var col))
                    indices.TryAdd(col, c);
            if (indices.Count == obligatorias.Count) return (f, indices);
        }
        return null;
    }

    private ResultadoLectura Mapear(string hoja, List<object?[]> celdas, int filaEnc, Dictionary<string, int> idx)
    {
        object? Celda(object?[] r, string col) => idx[col] < r.Length ? r[idx[col]] : null;

        var filas = new List<FilaLeida>();
        var errores = new List<string>();
        var vistos = new Dictionary<string, int>();

        for (int f = filaEnc + 1; f < celdas.Count; f++)
        {
            var r = celdas[f];
            if (r.All(v => ConversorValores.ATexto(v).Length == 0)) continue;
            if (filas.Count >= _o.FilasMaximas)
            {
                errores.Add($"El archivo supera el máximo de {_o.FilasMaximas} filas. Divida el envío.");
                break;
            }

            var sumaCruda = Celda(r, Columnas.SumaAsegurada);
            var fila = new FilaLeida
            {
                NumeroFila = f + 1,
                TipoFormulario = ConversorValores.ATexto(Celda(r, Columnas.TipoFormulario)),
                NombreContratante = ConversorValores.ATexto(Celda(r, Columnas.NombreContratante)),
                NombreCliente = ConversorValores.ATexto(Celda(r, Columnas.NombreCliente)),
                NumeroPoliza = ConversorValores.ATexto(Celda(r, Columnas.NumeroPoliza)),
                SumaAseguradaTexto = ConversorValores.ATexto(sumaCruda),
                SumaAsegurada = ConversorValores.ASuma(sumaCruda),
                Correo = ConversorValores.ATexto(Celda(r, Columnas.Correo)),
            };

            if (fila.SumaAsegurada is null)
                fila.Errores.Add(new("SumaAsegurada", fila.SumaAseguradaTexto.Length == 0 ? "Es obligatorio." : $"'{fila.SumaAseguradaTexto}' no es un monto válido."));
            foreach (var e in validador.Validar(fila.ADatos()))
                if (!(e.Campo == "SumaAsegurada" && fila.SumaAsegurada is null)) fila.Errores.Add(e);

            if (string.IsNullOrWhiteSpace(fila.NombreCliente)) fila.Errores.Add(new("NombreCliente", "Es obligatorio."));
            else if (fila.NombreCliente.Length > _o.LongitudMaximaNombreCliente) fila.Errores.Add(new("NombreCliente", $"Supera {_o.LongitudMaximaNombreCliente} caracteres."));
            else if (fila.NombreCliente.Any(char.IsControl)) fila.Errores.Add(new("NombreCliente", "Contiene caracteres de control."));

            var clave = $"{fila.TipoFormulario}|{fila.NumeroPoliza}|{fila.Correo}".ToLowerInvariant();
            if (vistos.TryGetValue(clave, out var anterior))
                fila.Errores.Add(new("Fila", $"Duplicada con la fila {anterior} (mismo tipo, póliza y correo)."));
            else vistos[clave] = fila.NumeroFila;

            filas.Add(fila);
        }

        if (filas.Count == 0 && errores.Count == 0) errores.Add("El archivo no tiene filas con datos debajo de los encabezados.");
        return new ResultadoLectura(filas, errores, hoja);
    }
}
