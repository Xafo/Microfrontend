using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Enrolamiento.Enlaces;

/// <summary>Crea un key ring nuevo para guardar en la variable de entorno. Uso: dotnet Enrolamiento.EnvioCorreos.Api.dll generar-llave</summary>
public static class GeneradorLlaves
{
    /// <param name="anterior">Key ring vigente (XML o Base64) para conservar llaves que aún validan enlaces emitidos.</param>
    public static string CrearBase64(string nombreAplicacion, TimeSpan vidaLlave, string? anterior = null)
    {
        var repo = new RepositorioEnMemoria();
        if (!string.IsNullOrWhiteSpace(anterior))
            foreach (var e in LlavesDesdeVariableEntorno.Leer(anterior)) repo.StoreElement(e, "");

        var servicios = new ServiceCollection();
        servicios.AddDataProtection().SetApplicationName(nombreAplicacion).DisableAutomaticKeyGeneration();
        servicios.Configure<KeyManagementOptions>(o => o.XmlRepository = repo);
        using var sp = servicios.BuildServiceProvider();

        var ahora = DateTimeOffset.UtcNow;
        sp.GetRequiredService<IKeyManager>().CreateNewKey(ahora, ahora.Add(vidaLlave));

        var xml = new XElement("repository", repo.GetAllElements()).ToString(SaveOptions.DisableFormatting);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
    }

    private sealed class RepositorioEnMemoria : IXmlRepository
    {
        private readonly List<XElement> _e = [];
        public IReadOnlyCollection<XElement> GetAllElements() => _e.ToList();
        public void StoreElement(XElement element, string friendlyName) => _e.Add(new XElement(element));
    }
}
