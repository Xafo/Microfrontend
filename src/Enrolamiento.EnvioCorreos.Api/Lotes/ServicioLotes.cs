using System.Threading.Channels;
using Enrolamiento.Enlaces;
using Enrolamiento.EnvioCorreos.Api.Carga;
using Enrolamiento.EnvioCorreos.Api.Correo;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Lotes;

public sealed class ConflictoLoteException(string mensaje) : Exception(mensaje);
public sealed class PlantillaInvalidaException(IReadOnlyList<string> errores) : Exception(string.Join(" ", errores))
{
    public IReadOnlyList<string> Errores { get; } = errores;
}

public sealed class ServicioLotes(
    LectorDestinatarios lector,
    IEnlaceProtegidoService enlaces,
    PlantillaCorreo plantilla,
    AlmacenLotes almacen,
    Channel<Guid> cola,
    TimeProvider reloj)
{
    /// <summary>Lee el archivo, valida y cifra el enlace de cada fila válida (el cifrado ocurre antes de enviar).</summary>
    public Lote Preparar(string nombreArchivo, byte[] contenido)
    {
        var lectura = lector.Leer(nombreArchivo, contenido);
        var filas = new List<FilaLote>();
        foreach (var f in lectura.Filas)
        {
            var fila = new FilaLote(f);
            if (f.Errores.Count == 0)
            {
                try
                {
                    fila.Enlace = enlaces.GenerarEnlace(f.ADatos());
                    fila.EnlaceReferencia = enlaces.ConstruirEnlaceReferenciaSinCifrar(f.ADatos());
                    fila.Estado = EstadoFila.Lista;
                }
                catch (DatosEnrolamientoInvalidosException ex)
                {
                    f.Errores.AddRange(ex.Errores);
                }
            }
            filas.Add(fila);
        }

        var lote = new Lote
        {
            NombreArchivo = Path.GetFileName(nombreArchivo),
            Hoja = lectura.Hoja,
            CreadoUtc = reloj.GetUtcNow(),
            Filas = filas,
            ErroresArchivo = lectura.ErroresArchivo,
        };
        almacen.Guardar(lote);
        return lote;
    }

    public CorreoRenderizado VistaPrevia(Lote lote, string asunto, string cuerpo, int? numeroFila)
    {
        FilaLote? fila;
        lock (lote.Bloqueo)
            fila = lote.Filas.FirstOrDefault(f => f.Enlace is not null && (numeroFila is null || f.Datos.NumeroFila == numeroFila));
        if (fila is null) throw new ConflictoLoteException("No hay filas válidas para la vista previa.");
        return plantilla.Renderizar(asunto, cuerpo, ADatosPlantilla(fila));
    }

    public void Enviar(Lote lote, string asunto, string cuerpo)
    {
        var errores = plantilla.ValidarPlantilla(asunto, cuerpo);
        if (errores.Count > 0) throw new PlantillaInvalidaException(errores);

        lock (lote.Bloqueo)
        {
            if (lote.Estado != EstadoLote.Preparado)
                throw new ConflictoLoteException("Este lote ya fue enviado. Use 'Reintentar fallidos' o cargue un archivo nuevo.");
            var listas = lote.Filas.Where(f => f.Estado == EstadoFila.Lista).ToList();
            if (listas.Count == 0) throw new ConflictoLoteException("No hay filas válidas para enviar.");
            foreach (var f in listas) { f.Estado = EstadoFila.Pendiente; f.Mensaje = null; }
            lote.Asunto = asunto;
            lote.Cuerpo = cuerpo;
            lote.Estado = EstadoLote.Enviando;
            lote.CancelacionSolicitada = false;
        }
        Encolar(lote);
    }

    public void ReintentarFallidos(Lote lote)
    {
        lock (lote.Bloqueo)
        {
            if (lote.Estado != EstadoLote.Finalizado) throw new ConflictoLoteException("Solo se puede reintentar cuando el envío terminó.");
            var fallidas = lote.Filas.Where(f => f.Estado is EstadoFila.Fallida or EstadoFila.Cancelada).ToList();
            if (fallidas.Count == 0) throw new ConflictoLoteException("No hay filas fallidas o canceladas para reintentar.");
            foreach (var f in fallidas) { f.Estado = EstadoFila.Pendiente; f.Mensaje = null; }
            lote.Estado = EstadoLote.Enviando;
            lote.CancelacionSolicitada = false;
        }
        Encolar(lote);
    }

    public void Cancelar(Lote lote)
    {
        lock (lote.Bloqueo)
        {
            if (lote.Estado != EstadoLote.Enviando) throw new ConflictoLoteException("El lote no se está enviando.");
            lote.CancelacionSolicitada = true;
        }
    }

    public static DatosPlantilla ADatosPlantilla(FilaLote f) => new(f.Datos.NombreCliente, f.Datos.NombreContratante,
        f.Datos.NumeroPoliza, f.Datos.TipoFormulario, f.Enlace!.Url, f.Enlace.ExpiraEnUtc);

    private void Encolar(Lote lote)
    {
        if (!cola.Writer.TryWrite(lote.Id)) throw new InvalidOperationException("No se pudo encolar el envío.");
    }
}

/// <summary>Procesa los lotes encolados de uno en uno, fila por fila, llamando a pr_mail.</summary>
public sealed class ProcesadorEnvios(
    Channel<Guid> cola,
    AlmacenLotes almacen,
    PlantillaCorreo plantilla,
    IEnviadorCorreo enviador,
    IOptions<CorreoOptions> correo,
    TimeProvider reloj,
    ILogger<ProcesadorEnvios> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        await foreach (var id in cola.Reader.ReadAllAsync(detener))
        {
            var lote = almacen.Obtener(id);
            if (lote is null) continue;
            try { await ProcesarAsync(lote, detener); }
            catch (OperationCanceledException) when (detener.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Error inesperado procesando el lote {Lote}", id);
                lock (lote.Bloqueo)
                {
                    foreach (var f in lote.Filas.Where(f => f.Estado is EstadoFila.Pendiente or EstadoFila.Enviando))
                    { f.Estado = EstadoFila.Fallida; f.Mensaje = "Error interno del procesador."; }
                    lote.Estado = EstadoLote.Finalizado;
                }
            }
        }
    }

    private async Task ProcesarAsync(Lote lote, CancellationToken detener)
    {
        var o = correo.Value;
        log.LogInformation("Inicia envío del lote {Lote}", lote.Id);
        while (true)
        {
            FilaLote? fila;
            string asunto, cuerpo;
            lock (lote.Bloqueo)
            {
                if (lote.CancelacionSolicitada)
                {
                    foreach (var f in lote.Filas.Where(f => f.Estado == EstadoFila.Pendiente))
                    { f.Estado = EstadoFila.Cancelada; f.Mensaje = "Cancelado por el operador."; }
                }
                fila = lote.Filas.FirstOrDefault(f => f.Estado == EstadoFila.Pendiente);
                if (fila is null) { lote.Estado = EstadoLote.Finalizado; break; }
                fila.Estado = EstadoFila.Enviando;
                asunto = lote.Asunto!;
                cuerpo = lote.Cuerpo!;
            }

            var (estado, mensaje) = await EnviarFilaAsync(fila, asunto, cuerpo, o, detener);
            lock (lote.Bloqueo)
            {
                fila.Estado = estado;
                fila.Mensaje = mensaje;
                if (estado == EstadoFila.Enviada) fila.EnviadaUtc = reloj.GetUtcNow();
            }
            log.LogInformation("Lote {Lote} fila {Fila}: {Estado}", lote.Id, fila.Datos.NumeroFila, estado);
            if (o.PausaEntreEnviosMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(o.PausaEntreEnviosMs), reloj, detener);
        }
        log.LogInformation("Finaliza envío del lote {Lote}", lote.Id);
    }

    private async Task<(EstadoFila, string?)> EnviarFilaAsync(FilaLote fila, string asunto, string cuerpo, CorreoOptions o, CancellationToken detener)
    {
        if (fila.Enlace!.ExpiraEnUtc <= reloj.GetUtcNow().AddMinutes(30))
            return (EstadoFila.Fallida, "El enlace está vencido o por vencer. Cargue el archivo de nuevo para regenerarlo.");

        var r = plantilla.Renderizar(asunto, cuerpo, ServicioLotes.ADatosPlantilla(fila));
        if (r.Errores.Count > 0) return (EstadoFila.Fallida, string.Join(" ", r.Errores));

        try
        {
            await enviador.EnviarAsync(new MensajeCorreo(o.Remitente, fila.Datos.Correo, r.Asunto, r.Html), detener);
            return (EstadoFila.Enviada, null);
        }
        catch (OperationCanceledException) when (detener.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falló el envío de la fila {Fila}", fila.Datos.NumeroFila);
            return (EstadoFila.Fallida, ex is Oracle.ManagedDataAccess.Client.OracleException oe
                ? $"Oracle ORA-{oe.Number:00000}: {Primeralinea(oe.Message)}"
                : Primeralinea(ex.Message));
        }
    }

    private static string Primeralinea(string s) => s.Split('\n', 2)[0].Trim();
}
