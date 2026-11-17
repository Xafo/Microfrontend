namespace Enrolamiento.EnvioCorreos.Api.Correo;

public enum ModoEnvio { Simulado, Oracle }

public sealed class CorreoOptions
{
    public const string Seccion = "Correo";

    /// <summary>Simulado: no envía nada (solo para DEV/local). Oracle: ejecuta el procedimiento configurado.</summary>
    public ModoEnvio Modo { get; set; } = ModoEnvio.Simulado;

    /// <summary>Parámetro "sender" de pr_mail.mail.</summary>
    public string Remitente { get; set; } = "";

    public string AsuntoPorDefecto { get; set; } = "Complete su formulario de enrolamiento";

    /// <summary>Cuerpo HTML editable por defecto. Si está vacío se usa <see cref="PlantillaCorreo.CuerpoPorDefecto"/>.</summary>
    public string? CuerpoPorDefecto { get; set; }

    /// <summary>Imágenes del correo: URLs HTTPS públicas (el cliente de correo las descarga). Vacío = no se incluye.</summary>
    public string? ImagenEncabezadoUrl { get; set; }
    public string? ImagenPieUrl { get; set; }

    /// <summary>Límite de VARCHAR2 en PL/SQL para el parámetro message.</summary>
    public int LongitudMaximaMensaje { get; set; } = 32767;
    public int LongitudMaximaAsunto { get; set; } = 200;

    /// <summary>Pausa entre correos para no saturar el SMTP.</summary>
    public int PausaEntreEnviosMs { get; set; } = 250;
}

public sealed class OracleOptions
{
    public const string Seccion = "Oracle";

    /// <summary>Descriptor TNS o EZConnect (host:puerto/servicio) del ambiente.</summary>
    public string DataSource { get; set; } = "";
    public string Usuario { get; set; } = "";
    /// <summary>Preferible por variable de entorno Oracle__Clave; nunca en Git.</summary>
    public string Clave { get; set; } = "";
    public string Procedimiento { get; set; } = "ficohsa.pr_mail.mail";
    public int TiempoEsperaSegundos { get; set; } = 60;

    public bool Completa() =>
        new[] { DataSource, Usuario, Clave, Procedimiento }.All(v => !string.IsNullOrWhiteSpace(v) && !v.Contains("__"));
}
