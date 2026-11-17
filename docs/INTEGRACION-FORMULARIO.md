# Integración para el backend del formulario de enrolamiento

El cliente llega a `https://<URL base>/EnrolamientoHN/?datos=<contenido protegido>`. El backend del formulario (no React) debe validar `datos`.

## Opción recomendada: misma librería y mismas llaves (sin llamada HTTP)

1. Referenciar el proyecto `src/Enrolamiento.Enlaces` (o su DLL).
2. Configurar en el sitio del formulario:
   - Variable `ENROLAMIENTO_DP_LLAVES` con **el mismo valor** que el sitio de envío del mismo ambiente.
   - Sección `Enlaces` con el mismo `NombreAplicacion` (`Ficohsa.EnrolamientoHN`) y la misma `UrlPublicaBase`.
3. Registrar y usar:

```csharp
builder.Services.AddEnlacesEnrolamiento(builder.Configuration);
var app = builder.Build();
app.Services.VerificarLlavesEnlaces(); // falla al arrancar si no hay llaves

// En el endpoint del backend que recibe el parámetro "datos":
var r = enlaces.ValidarContenido(datos);   // IEnlaceProtegidoService
if (!r.Valido) return Results.Problem("El enlace no es válido o venció.", statusCode: 400); // mensaje genérico
// r.Datos: TipoFormulario, NombreContratante, NumeroPoliza, SumaAsegurada (decimal), Correo
// r.ExpiraEnUtc, r.Version (= 1)
```

`ValidarContenido` nunca lanza por contenido inválido. Rechaza contenido vacío, demasiado largo, alterado, vencido, de otro propósito, de otra llave o de otra versión, y no distingue la causa a propósito.

## Contrato del contenido (versión 1)

Propósitos de Data Protection: `Enrolamiento` → `EnlaceCorreo` → `v1`, con `ITimeLimitedDataProtector` y vigencia de 48 h. El valor de `datos` es Base64Url. El JSON cifrado es:

```json
{ "version": 1, "tipoFormulario": "61", "nombreContratante": "...", "numeroPoliza": "...", "sumaAsegurada": 2500000.00, "correo": "..." }
```

Cambiar campos o propósitos exige una versión nueva (`v2`) y mantener `v1` mientras existan enlaces vigentes.

## Responsabilidades del equipo del formulario

1. Recibir `datos` y validarlo **en el backend**. Nunca exponer un endpoint genérico de descifrado ni descifrar en React.
2. Precargar tipo, contratante, póliza y suma como valores **no editables**, y precargar el correo de contacto.
3. Volver a imponer en el servidor los valores del enlace al guardar o enviar; no confiar en lo que mande el navegador.
4. Vincular el OTP, el contexto y la solicitud. Un enlace válido **no** prueba que el OTP se verificó.
5. No registrar en logs la URL completa ni el parámetro `datos`.

## Alternativa: endpoint interno de validación

Si el formulario no puede compartir las llaves, se puede agregar `POST /interno/enlaces-enrolamiento/validar` en este backend. **Requiere definir antes la autenticación servidor a servidor** (identidad de servicio o grupo). Hoy el sitio no tiene login, así que exponerlo sería un descifrador abierto en la red interna.
