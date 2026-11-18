// Ejemplo: agregar en Program.cs del backend del formulario.
using Formulario.Enrolamiento;

// 1) Registrar el lector (falla al arrancar si falta ENROLAMIENTO_DP_LLAVES o es inválida)
var lector = new LectorEnlaceEnrolamiento();
lector.Verificar();
builder.Services.AddSingleton<ILectorEnlaceEnrolamiento>(lector);

// ... var app = builder.Build();

// 2) Endpoint que llama React con el valor de ?datos=
app.MapPost("/api/enlace/validar", (ValidarEnlaceRequest req, ILectorEnlaceEnrolamiento enlaces, HttpContext http) =>
{
    var d = enlaces.Leer(req.Datos);
    if (d is null)
        return Results.Problem("El enlace no es válido o ya venció. Solicite uno nuevo.", statusCode: 400);

    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new
    {
        tipoFormulario = d.TipoFormulario,       // "SPN-F.GTP-61", "COACEHL (SPN-F.GTP-102)", ...
        nombreContratante = d.NombreContratante,
        numeroPoliza = d.NumeroPoliza,
        correo = d.Correo,
        expiraEnUtc = d.ExpiraEnUtc,
    });
});

public sealed record ValidarEnlaceRequest(string? Datos);
