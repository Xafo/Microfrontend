using System.Globalization;
using Enrolamiento.EnvioCorreos.Api.Lotes;

namespace Enrolamiento.EnvioCorreos.Api.Endpoints;

public static class LoteDto
{
    public static object Completo(Lote l)
    {
        lock (l.Bloqueo)
            return new
            {
                id = l.Id,
                nombreArchivo = l.NombreArchivo,
                hoja = l.Hoja,
                creadoUtc = l.CreadoUtc,
                estado = l.Estado.ToString(),
                erroresArchivo = l.ErroresArchivo,
                resumen = Resumen(l),
                filas = l.Filas.Select(f => new
                {
                    numeroFila = f.Datos.NumeroFila,
                    tipoFormulario = f.Datos.TipoFormulario,
                    nombreContratante = f.Datos.NombreContratante,
                    nombreCliente = f.Datos.NombreCliente,
                    numeroPoliza = f.Datos.NumeroPoliza,
                    sumaAsegurada = f.Datos.SumaAsegurada?.ToString(CultureInfo.InvariantCulture),
                    sumaAseguradaTexto = f.Datos.SumaAseguradaTexto,
                    correo = f.Datos.Correo,
                    errores = f.Datos.Errores.Select(e => new { campo = e.Campo, mensaje = e.Mensaje }),
                    enlace = f.Enlace is null ? null : new
                    {
                        cifrado = f.Enlace.Url.AbsoluteUri,
                        referencia = f.EnlaceReferencia?.AbsoluteUri,
                        expiraEnUtc = f.Enlace.ExpiraEnUtc,
                        longitud = f.Enlace.Url.AbsoluteUri.Length,
                        version = f.Enlace.Version,
                    },
                    estado = f.Estado.ToString(),
                    mensaje = f.Mensaje,
                    enviadaUtc = f.EnviadaUtc,
                }).ToList(),
            };
    }

    public static object Estado(Lote l)
    {
        lock (l.Bloqueo)
            return new
            {
                id = l.Id,
                estado = l.Estado.ToString(),
                cancelacionSolicitada = l.CancelacionSolicitada,
                resumen = Resumen(l),
                filas = l.Filas.Select(f => new { numeroFila = f.Datos.NumeroFila, estado = f.Estado.ToString(), mensaje = f.Mensaje, enviadaUtc = f.EnviadaUtc }).ToList(),
            };
    }

    private static Dictionary<string, int> Resumen(Lote l)
    {
        var r = Enum.GetValues<EstadoFila>().ToDictionary(e => Camel(e.ToString()), e => l.Filas.Count(f => f.Estado == e));
        r["total"] = l.Filas.Count;
        return r;
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
