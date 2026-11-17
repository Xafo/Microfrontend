using System.Text;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

/// <summary>CSV RFC 4180 con detección de codificación (UTF-8 / UTF-16 / Windows-1252) y separador (; , tab).</summary>
public static class LectorCsv
{
    public static List<object?[]> Leer(byte[] bytes)
    {
        var texto = Decodificar(bytes);
        var primera = texto.Split('\n', 2)[0];
        var sep = new[] { ';', ',', '\t' }.OrderByDescending(c => primera.Count(x => x == c)).First();
        return Parsear(texto, sep);
    }

    public static string Decodificar(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(b); } // "CSV (delimitado por comas)" de Excel en Windows
    }

    private static List<object?[]> Parsear(string t, char sep)
    {
        var filas = new List<object?[]>();
        var fila = new List<object?>();
        var campo = new StringBuilder();
        bool comillas = false;
        for (int i = 0; i < t.Length; i++)
        {
            var c = t[i];
            if (comillas)
            {
                if (c == '"') { if (i + 1 < t.Length && t[i + 1] == '"') { campo.Append('"'); i++; } else comillas = false; }
                else campo.Append(c);
            }
            else if (c == '"' && campo.Length == 0) comillas = true;
            else if (c == sep) { fila.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { fila.Add(campo.ToString()); campo.Clear(); filas.Add([.. fila]); fila.Clear(); }
            else campo.Append(c);
        }
        if (campo.Length > 0 || fila.Count > 0) { fila.Add(campo.ToString()); filas.Add([.. fila]); }
        return filas;
    }
}
