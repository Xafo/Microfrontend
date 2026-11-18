# Envío de enlaces de enrolamiento

Microfrontend interno (React) y su backend (.NET 10) para:

1. Cargar un archivo `.xlsx`, `.xls` o `.csv` con los clientes.
2. Validar cada fila y **cifrar** su enlace al formulario de enrolamiento (ASP.NET Core Data Protection, vigencia de 48 h).
3. Editar el asunto y el cuerpo del correo, con vista previa.
4. Enviar los correos con `ficohsa.pr_mail.mail` (Oracle) y mostrar la ejecución fila por fila.

```
web/                              React + Vite + TS → se compila a src/Enrolamiento.EnvioCorreos.Api/wwwroot
src/Enrolamiento.Enlaces/         Librería reutilizable: generar y validar enlaces protegidos
src/Enrolamiento.EnvioCorreos.Api Backend + hosting del microfrontend (un solo sitio IIS)
tests/Enrolamiento.Tests/         Pruebas unitarias y de integración
```

## Flujo

```
Operador ─► sube archivo ─► API lee y valida filas ─► cifra el enlace de cada fila válida
        ◄── tabla con errores, enlace real (referencia) y enlace cifrado
        ─► edita asunto/cuerpo ─► vista previa ─► confirma
        ─► API ejecuta pr_mail.mail por fila en segundo plano ─► progreso en vivo, cancelar, reintentar fallidos

Cliente ─► abre https://<URL base>/?datos=<cifrado> ─► backend del formulario valida con la librería
```

El enlace que recibe el cliente **nunca** lleva datos en texto claro. El "enlace real" de la pantalla es solo de referencia y no se envía.

## Archivo de entrada

Hoja `Destinatarios` (o la primera hoja que tenga estos encabezados). El orden de columnas no importa y no hace falta una tabla de Excel con nombre. La plantilla se descarga desde la pantalla (`api/plantilla`).

| Columna | Regla |
|---|---|
| Tipo de Formulario | Nombre completo, escrito: `SPN-F.GTP-61`, `SPN-F.GTP-62`, `SPN-F.GTP-63`, `SPN-F.GTP-64`, `SPN-F.GTP-101`, `SPN-F.GTP-161` o `COACEHL (SPN-F.GTP-102)`. No importan mayúsculas ni espacios de más |
| Nombre del Contratante | Obligatorio, máx. 200 |
| Nombre del Cliente | Obligatorio, máx. 200 (saludo del correo; no viaja en el enlace) |
| Numero de Poliza | Obligatorio, máx. 30, letras/números/`-`/`/`; conserva ceros a la izquierda |
| Correo | Un solo correo válido |
| Enlace | Opcional; se ignora (lo genera el servidor) |
| Suma Asegurada | Ya no se usa. Si viene en un archivo anterior, se ignora |

CSV: UTF-8, UTF-16 o Windows-1252 (el "CSV" de Excel), separador `;`, `,` o tabulador. Filas duplicadas (mismo tipo, póliza y correo) se marcan con error.

### Agregar un tipo de formulario

Los tipos aceptados están en `src/Enrolamiento.EnvioCorreos.Api/appsettings.json`. Para aceptar uno nuevo, agregue una línea al final de la lista y reinicie el sitio:

```json
"TiposFormularioPermitidos": [
  "SPN-F.GTP-61",
  ...
  "COACEHL (SPN-F.GTP-102)",
  "NUEVO-TIPO"
]
```

El tipo nuevo se acepta en la carga y aparece automáticamente en la leyenda del portal y en la plantilla de Excel descargada, en la columna H de la hoja «Destinatarios». La leyenda puede quedarse en el archivo al cargarlo: solo se leen las columnas esperadas. Avise al equipo del formulario, porque el `tipoFormulario` viaja tal cual dentro del enlace.

## Login

El portal pide usuario y contraseña. Hay un solo usuario y se configura con dos variables:

| Variable | Valor |
|---|---|
| `Login__Usuario` | Nombre de usuario (no distingue mayúsculas) |
| `Login__ClaveHash` | **Hash** de la contraseña, nunca la contraseña en texto |

La contraseña no se guarda en ningún lado. Se guarda un hash PBKDF2-SHA256 con sal aleatoria y 600 000 iteraciones, que no se puede revertir. La aplicación no arranca si falta el usuario o si `Login__ClaveHash` no es un hash válido.

**1. Generar el hash** (desde `src/Enrolamiento.EnvioCorreos.Api`). Pide la contraseña dos veces sin mostrarla, con un mínimo de 10 caracteres:
```powershell
dotnet run --no-launch-profile -- generar-clave
# o, ya publicado:  .\Enrolamiento.EnvioCorreos.Api.exe generar-clave
```
Imprime algo como `PBKDF2-SHA256:600000:xxxx==:yyyy=`.

**2. Guardarlo**
- En su PC, con los secretos de usuario de .NET (quedan fuera del proyecto y de Git):
  ```powershell
  dotnet user-secrets set "Login:Usuario" "operador"
  dotnet user-secrets set "Login:ClaveHash" "PBKDF2-SHA256:600000:xxxx==:yyyy="
  dotnet user-secrets list
  ```
- Como variable de entorno de Windows (en una terminal nueva):
  ```powershell
  [Environment]::SetEnvironmentVariable("Login__Usuario", "operador", "User")
  [Environment]::SetEnvironmentVariable("Login__ClaveHash", "PBKDF2-SHA256:600000:xxxx==:yyyy=", "User")
  ```
- En IIS, en el `web.config` publicado:
  ```xml
  <aspNetCore ...>
    <environmentVariables>
      <environmentVariable name="Login__Usuario" value="operador" />
      <environmentVariable name="Login__ClaveHash" value="PBKDF2-SHA256:600000:xxxx==:yyyy=" />
    </environmentVariables>
  </aspNetCore>
  ```

**Cambiar la contraseña:** genere un hash nuevo, reemplace `Login__ClaveHash` y reinicie el sitio.

**Seguridad:** la sesión dura 8 horas renovables (`Login:DuracionSesionHoras`), con cookie HttpOnly y SameSite=Strict, que pasa a Secure si el sitio usa HTTPS. Se permiten 5 intentos de login por minuto por IP (`Login:IntentosPorMinuto`) y el mensaje de error no indica qué dato falló. Toda la `/api` exige sesión, y los POST deben traer la cabecera `X-Solicitud` como protección CSRF. Se recomienda publicar el sitio en HTTPS.

## Configuración por ambiente

`ASPNETCORE_ENVIRONMENT` selecciona el archivo:

| Ambiente | Archivo | URL base del enlace | Envío |
|---|---|---|---|
| `Local` | appsettings.Local.json | https://d3322rrb6yrsp3.cloudfront.net/ | Simulado (no envía) |
| `Development` (DEV) | appsettings.Development.json | https://d3322rrb6yrsp3.cloudfront.net/ | Oracle |
| `Staging` (STG) | appsettings.Staging.json | https://d1i4s8s51j1tuu.cloudfront.net/ | Oracle |
| `Production` (PROD) | appsettings.Production.json | https://seguros.ficohsa.com/ | Oracle (Simulado prohibido) |

Valores que deben completar en cada `appsettings.{Ambiente}.json` (hoy tienen marcadores `__..._DEV__`, `__..._STG__`, `__..._PROD__`):

- `Correo:Remitente` → parámetro `sender` de pr_mail.
- `Oracle:DataSource` → descriptor TNS o `host:1521/servicio`.
- `Oracle:Usuario` y `Oracle:Clave` → **recomendado por variables de entorno** del sitio IIS (`Oracle__Usuario`, `Oracle__Clave`) en lugar del archivo. Las variables de entorno tienen prioridad sobre el JSON.
- `Correo:ImagenEncabezadoUrl` / `Correo:ImagenPieUrl` → URLs **HTTPS públicas** de las imágenes de encabezado y pie (el cliente de correo las descarga). Vacío = sin imagen.

**Ejecutar desde una PC contra Oracle DEV, STG o PROD:** ver **[docs/EJECUTAR-DESDE-PC.md](docs/EJECUTAR-DESDE-PC.md)** (perfiles `DEV`, `STG` y `PROD`, archivo `appsettings.{Ambiente}.local.json` y llave `ENROLAMIENTO_DP_LLAVES_{DEV|STG|PROD}`).

**Instalar en IIS:** ver **[docs/INSTALAR-IIS.md](docs/INSTALAR-IIS.md)**.

La aplicación **no arranca** si falta algo crítico: llaves, remitente válido, credenciales Oracle o URL base HTTPS. Así no hay modos silenciosos.

Otros parámetros (`appsettings.json`): `Enlaces:VigenciaHoras` (48), `Enlaces:LongitudMaximaUrl` (2000), `Carga:FilasMaximas` (2000), `Carga:TamanoMaximoBytes` (5 MB), `Correo:PausaEntreEnviosMs` (250), `Lotes:DuracionHoras` (8 h en memoria).

## Llaves de cifrado (variable de entorno)

Las llaves de Data Protection se leen de la variable `ENROLAMIENTO_DP_LLAVES`: es el key ring en XML, codificado en Base64. Es de solo lectura, sin generación automática ni llaves efímeras.

```powershell
# Generar (una vez por ambiente; cada ambiente con su propia llave)
dotnet Enrolamiento.EnvioCorreos.Api.dll generar-llave

# Rotar conservando las anteriores (los enlaces ya enviados siguen siendo válidos)
$env:ENROLAMIENTO_DP_LLAVES = "<valor actual>"
dotnet Enrolamiento.EnvioCorreos.Api.dll generar-llave --rotar
```

- La llave dura 2 años. Al rotarla se conservan las anteriores. Nunca borren una llave mientras existan enlaces vigentes que dependan de ella.
- El valor es un secreto: no va en Git, en appsettings ni en logs. Guárdenlo en la configuración del sitio o del Application Pool y respáldenlo en su bóveda de secretos.
- El backend del formulario necesita **la misma** variable, el mismo `NombreAplicacion` (`Ficohsa.EnrolamientoHN`) y la librería `Enrolamiento.Enlaces` (ver [docs/INTEGRACION-FORMULARIO.md](docs/INTEGRACION-FORMULARIO.md)).

## Ejecutar

- **Desde una PC contra Oracle DEV, STG o PROD:** [docs/EJECUTAR-DESDE-PC.md](docs/EJECUTAR-DESDE-PC.md)
- **En IIS (frontend y backend en un solo sitio):** [docs/INSTALAR-IIS.md](docs/INSTALAR-IIS.md)
- **Modo de desarrollo sin base de datos** (perfil `Local`, envío simulado): requiere la variable `ENROLAMIENTO_DP_LLAVES` y el login en `dotnet user-secrets`. Un correo que empiece con `falla` simula un error.
- **Frontend con recarga automática:** con la API corriendo, `cd web` y luego `npm run dev` (http://localhost:5173).

Pruebas: `dotnet test`.

## Publicación en IIS

```powershell
cd web; npm ci; npm run build; cd ..
dotnet publish src/Enrolamiento.EnvioCorreos.Api -c Release -o publish
```

1. Instalar el **ASP.NET Core 10 Hosting Bundle** en el servidor.
2. Crear el sitio o aplicación apuntando a `publish/` (sirve la pantalla y la API juntas; funciona también como aplicación dentro de otro sitio).
3. En el Application Pool o en `web.config` (`<environmentVariables>`), definir:
   - `ASPNETCORE_ENVIRONMENT` = `Development` | `Staging` | `Production`
   - `ENROLAMIENTO_DP_LLAVES` = key ring del ambiente
   - `Oracle__Usuario`, `Oracle__Clave`
4. En el Application Pool: **Idle Time-out = 0**, **Start Mode = AlwaysRunning** y evitar reciclajes en horario de envío. Los lotes viven en memoria: un reciclaje durante un envío lo interrumpe y obliga a recargar el archivo (pr_mail ya registró lo enviado).
5. Acceso solo desde la red interna (por ahora la pantalla no tiene login).

## Notas sobre `pr_mail` (revisar con el equipo de BD)

Revisamos `ficohsa.pr_mail.mail(sender, recipients, subject, message)` en la captura compartida:

1. **⚠ El destinatario está fijo.** En la línea 100 se llama `begin_mail` con un correo escrito directamente en el código y no se usa el parámetro `recipients`. Tal como está, **todos los correos llegan a esa persona**. Hay que cambiarlo a `begin_mail(sender, recipients, subject)` antes de usarlo en STG/PROD.
2. **Los errores se tragan.** El `WHEN OTHERS` guarda `SQLERRM` pero el `INSERT INTO MAIL_ERRORES` está comentado y no relanza la excepción. La pantalla marca "Enviada" cuando el procedimiento termina sin error, pero si falla el SMTP Oracle no lo reporta. Recomendamos descomentar el registro de errores o hacer `RAISE` para que la pantalla muestre el fallo real.
3. **Codificación.** `begin_mail` declara `text/html; charset=iso-8859-1`. Por eso el backend convierte todo carácter no ASCII del HTML en entidades (`&#233;`) y envía el asunto sin tildes, para que nada se corrompa sin importar la conversión de juego de caracteres de utl_smtp.
4. **Tamaño.** `message` es VARCHAR2, con un máximo de 32 767 caracteres. El backend valida el tamaño antes de llamar.

## Seguridad y limitaciones

- El login es de un solo usuario compartido. Si se necesita saber quién envió cada lote, el siguiente paso es Windows Authentication con un grupo AD.
- El enlace se puede reutilizar hasta que vence (48 h). No hay revocación individual ni uso único, porque no se crean tablas.
- Un enlace válido no reemplaza el OTP del formulario.
- No hay historial en la aplicación; el registro queda en pr_mail. Los logs de la aplicación no contienen correos, nombres ni enlaces.
- Las respuestas de la API se marcan `Cache-Control: no-store`.
