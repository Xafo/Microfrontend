using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Enrolamiento.Enlaces;

/// <summary>
/// Key ring de Data Protection de solo lectura, tomado de una variable de entorno.
/// El valor es el XML del key ring (raíz &lt;repository&gt; con uno o más &lt;key&gt;) o ese XML en Base64.
/// No genera ni guarda llaves: si falta o es inválido, el arranque falla (sin llaves efímeras).
/// </summary>
public sealed class LlavesDesdeVariableEntorno : IXmlRepository
{
    private readonly IReadOnlyList<XElement> _llaves;

    public LlavesDesdeVariableEntorno(string nombreVariable)
        : this(nombreVariable, Environment.GetEnvironmentVariable(nombreVariable)) { }

    internal LlavesDesdeVariableEntorno(string nombreVariable, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException($"La variable de entorno '{nombreVariable}' con las llaves de Data Protection no está definida.");
        try
        {
            _llaves = Leer(valor);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
        {
            throw new InvalidOperationException($"La variable de entorno '{nombreVariable}' no contiene un key ring válido.", ex);
        }
        if (_llaves.Count == 0)
            throw new InvalidOperationException($"La variable de entorno '{nombreVariable}' no contiene ninguna llave.");
    }

    public IReadOnlyCollection<XElement> GetAllElements() => _llaves.Select(e => new XElement(e)).ToList();

    public void StoreElement(XElement element, string friendlyName) =>
        throw new InvalidOperationException("El key ring es de solo lectura (variable de entorno). Genere una llave nueva con 'generar-llave' y actualice la variable.");

    internal static IReadOnlyList<XElement> Leer(string valor)
    {
        valor = valor.Trim();
        var xml = valor.StartsWith('<') ? valor : Encoding.UTF8.GetString(Convert.FromBase64String(valor));
        var raiz = XElement.Parse(xml);
        return raiz.Name.LocalName == "key" ? [raiz] : raiz.Elements().Where(e => e.Name.LocalName is "key" or "revocation").ToList();
    }
}
