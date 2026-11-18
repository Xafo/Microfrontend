// ============================================================================
//  LectorEnlaceEnrolamiento.cs
//  Lee y valida el parámetro ?datos= de los enlaces de enrolamiento.
//  Copiar tal cual en el backend del formulario (ASP.NET Core .NET 8 o superior).
//
//  Requisitos:
//   - Variable de entorno ENROLAMIENTO_DP_LLAVES con la MISMA llave del portal de envío.
//   - Nada más: no usa la configuración de Data Protection propia de su aplicación.
// ============================================================================
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Formulario.Enrolamiento;

/// <summary>Los 4 campos que vienen cifrados en el enlace, más su vencimiento.</summary>
public sealed record DatosEnlace(
    string TipoFormulario,      // nombre completo, p. ej. "SPN-F.GTP-61" o "COACEHL (SPN-F.GTP-102)"
    string NombreContratante,
    string NumeroPoliza,
    string Correo,
    DateTimeOffset ExpiraEnUtc);

public interface ILectorEnlaceEnrolamiento
{
    /// <summary>Devuelve los datos si el enlace es auténtico y no venció; si no, null. Nunca lanza excepción.</summary>
    DatosEnlace? Leer(string? datos);
}

public sealed class LectorEnlaceEnrolamiento : ILectorEnlaceEnrolamiento
{
    // Deben coincidir EXACTAMENTE con el portal de envío.
    public const string NombreAplicacion = "Ficohsa.EnrolamientoHN";
    public const string VariableLlaves = "ENROLAMIENTO_DP_LLAVES";
    private static readonly string[] Propositos = ["Enrolamiento", "EnlaceCorreo", "v1"];
    private const int VersionContrato = 1;
    private const int LargoMaximo = 2000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly ITimeLimitedDataProtector _protector;

    public LectorEnlaceEnrolamiento() : this(Environment.GetEnvironmentVariable(VariableLlaves)) { }

    public LectorEnlaceEnrolamiento(string? llaves)
    {
        if (string.IsNullOrWhiteSpace(llaves))
            throw new InvalidOperationException($"Falta la variable de entorno {VariableLlaves} con la llave de los enlaces.");

        // Proveedor de Data Protection aislado: no toca la configuración de su aplicación.
        var servicios = new ServiceCollection();
        servicios.AddDataProtection().SetApplicationName(NombreAplicacion).DisableAutomaticKeyGeneration();
        servicios.Configure<KeyManagementOptions>(o => o.XmlRepository = new LlavesSoloLectura(llaves));
        var proveedor = servicios.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();

        _protector = proveedor.CreateProtector(Propositos[0], Propositos[1..]).ToTimeLimitedDataProtector();
    }

    public DatosEnlace? Leer(string? datos)
    {
        if (string.IsNullOrWhiteSpace(datos) || datos.Length > LargoMaximo) return null;
        try
        {
            // Descifra, verifica integridad (HMAC) y vencimiento. Si algo falla lanza CryptographicException.
            var json = _protector.Unprotect(WebEncoders.Base64UrlDecode(datos), out var expira);
            var c = JsonSerializer.Deserialize<Contenido>(json, Json);
            if (c is null || c.Version != VersionContrato) return null;
            if (new[] { c.TipoFormulario, c.NombreContratante, c.NumeroPoliza, c.Correo }.Any(string.IsNullOrWhiteSpace)) return null;
            return new DatosEnlace(c.TipoFormulario!, c.NombreContratante!, c.NumeroPoliza!, c.Correo!, expira);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null; // alterado, vencido, de otra llave o malformado: respuesta genérica
        }
    }

    /// <summary>Lanza si la llave no sirve. Llamar al arrancar la aplicación.</summary>
    public void Verificar()
    {
        var prueba = _protector.Protect("ok"u8.ToArray(), DateTimeOffset.UtcNow.AddMinutes(1));
        if (Encoding.UTF8.GetString(_protector.Unprotect(prueba, out _)) != "ok")
            throw new InvalidOperationException("La llave de los enlaces no es válida.");
    }

    private sealed record Contenido(int Version, string? TipoFormulario, string? NombreContratante, string? NumeroPoliza, string? Correo);

    /// <summary>Key ring tomado de la variable de entorno (XML o Base64 del XML). Solo lectura.</summary>
    private sealed class LlavesSoloLectura : IXmlRepository
    {
        private readonly List<XElement> _llaves;

        public LlavesSoloLectura(string valor)
        {
            try
            {
                valor = valor.Trim();
                var xml = valor.StartsWith('<') ? valor : Encoding.UTF8.GetString(Convert.FromBase64String(valor));
                var raiz = XElement.Parse(xml);
                _llaves = raiz.Name.LocalName == "key"
                    ? [raiz]
                    : raiz.Elements().Where(e => e.Name.LocalName is "key" or "revocation").ToList();
            }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
            {
                throw new InvalidOperationException($"La variable {VariableLlaves} no contiene una llave válida.", ex);
            }
            if (_llaves.Count == 0) throw new InvalidOperationException($"La variable {VariableLlaves} no contiene llaves.");
        }

        public IReadOnlyCollection<XElement> GetAllElements() => _llaves.Select(e => new XElement(e)).ToList();
        public void StoreElement(XElement element, string friendlyName) =>
            throw new InvalidOperationException("La llave de los enlaces es de solo lectura.");
    }
}
