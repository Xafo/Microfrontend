using System.Data;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace Enrolamiento.EnvioCorreos.Api.Correo;

public sealed record MensajeCorreo(string Remitente, string Destinatario, string Asunto, string Html);

public interface IEnviadorCorreo
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct);
}

/// <summary>Ejecuta ficohsa.pr_mail.mail(sender, recipients, subject, message).</summary>
public sealed class EnviadorOracle(IOptions<OracleOptions> opciones) : IEnviadorCorreo
{
    private readonly OracleOptions _o = opciones.Value;

    public async Task EnviarAsync(MensajeCorreo m, CancellationToken ct)
    {
        var cs = new OracleConnectionStringBuilder { DataSource = _o.DataSource, UserID = _o.Usuario, Password = _o.Clave, Pooling = true };
        await using var con = new OracleConnection(cs.ConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = _o.Procedimiento;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.BindByName = true;
        cmd.CommandTimeout = _o.TiempoEsperaSegundos;
        cmd.Parameters.Add("sender", OracleDbType.Varchar2, m.Remitente, ParameterDirection.Input);
        cmd.Parameters.Add("recipients", OracleDbType.Varchar2, m.Destinatario, ParameterDirection.Input);
        cmd.Parameters.Add("subject", OracleDbType.Varchar2, m.Asunto, ParameterDirection.Input);
        cmd.Parameters.Add("message", OracleDbType.Varchar2, m.Html, ParameterDirection.Input);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>No envía nada. Para DEV/local y pruebas. Se rechaza en Production.</summary>
public sealed class EnviadorSimulado(ILogger<EnviadorSimulado> log) : IEnviadorCorreo
{
    public async Task EnviarAsync(MensajeCorreo m, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(150, 450), ct);
        if (m.Destinatario.StartsWith("falla", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Falla simulada (destinatario que empieza con 'falla').");
        log.LogInformation("Correo simulado: {Bytes} caracteres", m.Html.Length);
    }
}
