using Enrolamiento.Enlaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Enrolamiento.Tests;

internal static class Utilidades
{
    public const string Variable = "ENROLAMIENTO_DP_LLAVES_PRUEBAS";
    public static readonly string LlavePrueba = GeneradorLlaves.CrearBase64("Ficohsa.EnrolamientoHN", TimeSpan.FromDays(365));

    static Utilidades() => Environment.SetEnvironmentVariable(Variable, LlavePrueba);

    public static ServiceProvider Crear(TimeProvider? reloj = null, Dictionary<string, string?>? extra = null, string? llave = null)
    {
        Environment.SetEnvironmentVariable(Variable, LlavePrueba);
        var variable = Variable;
        if (llave is not null)
        {
            variable = Variable + "_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(variable, llave);
        }
        var valores = new Dictionary<string, string?>
        {
            ["Enlaces:UrlPublicaBase"] = "https://seguros.example.test/EnrolamientoHN/",
            ["Enlaces:VariableEntornoLlaves"] = variable,
        };
        foreach (var kv in extra ?? []) valores[kv.Key] = kv.Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var s = new ServiceCollection().AddLogging();
        if (reloj is not null) s.AddSingleton(reloj);
        s.AddEnlacesEnrolamiento(config);
        return s.BuildServiceProvider();
    }

    public static DatosEnrolamiento Datos(string nombre = "CONTRATANTE DE PRUEBA", decimal suma = 2500000.00m, string tipo = "61",
        string poliza = "1000000332", string correo = "cliente@example.test") => new(tipo, nombre, poliza, suma, correo);
}
