# Guía para el formulario: leer el enlace de enrolamiento

Esta guía es para quien programa el **formulario de enrolamiento** (React + backend .NET 10), es decir, la parte que se ejecuta cuando el cliente abre el enlace del correo.

El sitio de envío (este repo) ya genera y envía el enlace. Al formulario le toca **leerlo, validarlo y precargar los datos**.

---

## 1. Qué recibe el formulario

El cliente abre un enlace como este:

```
https://d3322rrb6yrsp3.cloudfront.net/?datos=CfDJ8EQi1Nr21YFLmveTzNdB8HEL0_...
```

| Ambiente | URL base |
|---|---|
| DEV | `https://d3322rrb6yrsp3.cloudfront.net/` |
| STG | `https://d1i4s8s51j1tuu.cloudfront.net/` |
| PROD | `https://seguros.ficohsa.com/` |

- `datos` está **cifrado y firmado** (ASP.NET Core Data Protection: AES-256-CBC + HMAC-SHA256) y **vence a las 48 h**.
- No lleva ningún dato en texto claro. React **no puede** leerlo; solo el backend, con la llave.
- Dentro van estos 5 campos:

```json
{ "version": 1, "tipoFormulario": "61", "nombreContratante": "CONTRATANTE DE PRUEBA",
  "numeroPoliza": "1000000332", "sumaAsegurada": 2500000.00, "correo": "cliente@dominio.com" }
```

---

## 2. Flujo a programar

```
Cliente abre https://host/?datos=XXX
   │
   ▼
[React]  lee ?datos de la URL ──► POST /api/enlace/validar { datos }  ──►  [Backend .NET]
   │                                                                          │ ValidarContenido(datos)
   │◄──────────── 200 { tipoFormulario, nombreContratante, numeroPoliza, ... } ┘  (o 400 genérico)
   ▼
Precarga el formulario: tipo, contratante, póliza y suma NO editables; correo precargado
   ▼
OTP ► guardar ► el backend vuelve a imponer los valores del enlace (no confía en el navegador)
```

---

## 3. Configuración del backend del formulario

### 3.1 Referenciar la librería
Agregar a la solución el proyecto **`src/Enrolamiento.Enlaces`** de este repo, o su DLL compilada. Es la misma librería que cifra; así las dos partes usan exactamente los mismos algoritmos y propósitos.

```xml
<ProjectReference Include="..\Enrolamiento.Enlaces\Enrolamiento.Enlaces.csproj" />
```

### 3.2 appsettings
```json
"Enlaces": {
  "UrlPublicaBase": "https://d3322rrb6yrsp3.cloudfront.net/",
  "NombreAplicacion": "Ficohsa.EnrolamientoHN",
  "VariableEntornoLlaves": "ENROLAMIENTO_DP_LLAVES"
}
```
`NombreAplicacion` debe ser **idéntico** al del sitio de envío. Si difiere en una sola letra, no descifra.

### 3.3 La llave
Variable de entorno **`ENROLAMIENTO_DP_LLAVES`** con **el mismo valor** que el sitio de envío del mismo ambiente.

- Hay una llave por ambiente (DEV, STG y PROD tienen llaves distintas).
- La llave se pide al responsable del sitio de envío por un canal seguro: bóveda o gestor de contraseñas, o que la cargue directamente quien administra IIS. **Nunca** por chat, correo, Git ni appsettings.
- En IIS se define en el Application Pool o en `web.config` → `<environmentVariables>`.
- Local (PowerShell): `$env:ENROLAMIENTO_DP_LLAVES = "<valor>"`

### 3.4 Program.cs
```csharp
using Enrolamiento.Enlaces;

builder.Services.AddEnlacesEnrolamiento(builder.Configuration);

var app = builder.Build();
app.Services.VerificarLlavesEnlaces(); // si la llave falta o es inválida, la app NO arranca
```

---

## 4. Endpoint de validación (backend)

```csharp
public sealed record ValidarEnlaceRequest(string? Datos);

app.MapPost("/api/enlace/validar", (ValidarEnlaceRequest req, IEnlaceProtegidoService enlaces, HttpContext http) =>
{
    var r = enlaces.ValidarContenido(req.Datos);
    if (!r.Valido)
        // Mensaje genérico: no revelar si venció, si fue alterado o si es de otra llave.
        return Results.Problem("El enlace no es válido o ya venció. Solicite uno nuevo.", statusCode: 400);

    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new
    {
        tipoFormulario = r.Datos!.TipoFormulario,
        nombreContratante = r.Datos.NombreContratante,
        numeroPoliza = r.Datos.NumeroPoliza,
        sumaAsegurada = r.Datos.SumaAsegurada,   // decimal, sin redondeo
        correo = r.Datos.Correo,
        expiraEnUtc = r.ExpiraEnUtc,
    });
});
```

`ValidarContenido` **nunca lanza excepción** por contenido malo. Rechaza el contenido vacío, demasiado largo, alterado, vencido, de otro propósito, de otra llave o de otra versión, y además vuelve a validar los campos (tipos permitidos, correo, montos).

Recomendado: limitar la frecuencia de este endpoint por IP (rate limiting de ASP.NET Core) y no registrar en logs el body ni la URL completa.

---

## 5. Lectura en React

```tsx
useEffect(() => {
  const datos = new URLSearchParams(window.location.search).get('datos')
  if (!datos) { setError('Enlace incompleto.'); return }

  // Quitar ?datos de la barra de direcciones (evita que quede en historial, capturas o referer)
  window.history.replaceState(null, '', window.location.pathname)

  fetch('api/enlace/validar', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ datos }),
  })
    .then(async (r) => {
      if (!r.ok) throw new Error((await r.json()).detail ?? 'Enlace no válido')
      return r.json()
    })
    .then((ctx) => precargarFormulario(ctx))   // tipo/contratante/póliza/suma en solo lectura
    .catch((e) => setError(e.message))
}, [])
```

Si el usuario recarga la página después de `replaceState`, ya no tendrá `?datos`. Por eso el backend debe conservar el contexto validado en la sesión del servidor (o en un identificador propio). **No** se debe guardar `datos` en `localStorage`.

---

## 6. Reglas de seguridad (obligatorias)

1. Descifrar **solo en el backend**. Nunca en React, y nunca exponer un endpoint genérico que descifre cualquier texto.
2. Tipo, contratante, póliza y suma se muestran **no editables**, y al guardar o enviar el backend **vuelve a imponer** los valores del enlace; no se aceptan los que mande el navegador.
3. Un enlace válido **no** prueba que el OTP se verificó. El OTP se vincula con el contexto de la solicitud.
4. El enlace es **reutilizable hasta que vence** y no se puede revocar uno solo (no hay tabla de invitaciones).
5. No registrar en logs la URL completa, el parámetro `datos` ni la respuesta del endpoint.

---

## 7. Cómo probar

1. Conseguir la llave de DEV y ponerla en `ENROLAMIENTO_DP_LLAVES`, en el sitio de envío y en el formulario. Debe ser **la misma**.
2. En el sitio de envío, cargar un Excel de prueba. En **Revisar datos → Ver enlace**, copiar el **enlace cifrado** con el botón *Copiar*. No hace falta enviar el correo.
3. Abrir ese enlace en el formulario. En local se puede cambiar el host por el del entorno de desarrollo y mantener `?datos=...`.
4. Casos a verificar:

| Caso | Resultado esperado |
|---|---|
| Enlace válido | Precarga los 5 campos exactos (acentos, ceros a la izquierda y decimales incluidos) |
| Cambiar un carácter de `datos` | 400 genérico |
| Enlace generado con otra llave (por ejemplo, de LOCAL) | 400 genérico |
| Enlace de más de 48 h | 400 genérico |
| Sin `?datos` | Mensaje "Enlace incompleto" |
| Llave no configurada | La app no arranca (`VerificarLlavesEnlaces`) |

---

## 8. Alternativa: endpoint interno en el sitio de envío

Si el formulario **no puede** tener la llave, el sitio de envío puede exponer `POST /interno/enlaces-enrolamiento/validar` para que el backend del formulario lo llame servidor a servidor. Antes hay que definir la **autenticación servidor a servidor** (identidad de servicio o grupo AD): hoy el sitio de envío no tiene login y ese endpoint quedaría abierto en la red interna. Pedirlo al responsable del sitio de envío.
