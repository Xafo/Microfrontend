using System.Security.Cryptography;
using System.Text;

namespace Enrolamiento.EnvioCorreos.Api.Seguridad;

/// <summary>
/// Hash de contraseña con PBKDF2-SHA256 y sal aleatoria. No es reversible: se guarda el hash, nunca la contraseña.
/// Formato: PBKDF2-SHA256:iteraciones:salBase64:hashBase64  (sin "$" para no romperse en PowerShell).
/// </summary>
public static class HashClave
{
    public const string Prefijo = "PBKDF2-SHA256";
    public const int IteracionesPorDefecto = 600_000;
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;

    public static string Crear(string clave, int iteraciones = IteracionesPorDefecto)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave), sal, iteraciones, HashAlgorithmName.SHA256, TamanoHash);
        return $"{Prefijo}:{iteraciones}:{Convert.ToBase64String(sal)}:{Convert.ToBase64String(hash)}";
    }

    public static bool EsFormatoValido(string? hashGuardado) => Leer(hashGuardado) is not null;

    /// <summary>Compara en tiempo constante.</summary>
    public static bool Verificar(string clave, string? hashGuardado)
    {
        if (Leer(hashGuardado) is not var (iteraciones, sal, esperado)) return false;
        var calculado = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave ?? ""), sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static (int, byte[], byte[])? Leer(string? valor)
    {
        var p = (valor ?? "").Trim().Split(':');
        if (p.Length != 4 || p[0] != Prefijo || !int.TryParse(p[1], out var it) || it < 1000) return null;
        try { return (it, Convert.FromBase64String(p[2]), Convert.FromBase64String(p[3])); }
        catch (FormatException) { return null; }
    }
}
