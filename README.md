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

Cliente ─► abre https://<URL base>/EnrolamientoHN/?datos=<cifrado> ─► backend del formulario valida con la librería
```

El enlace que recibe el cliente **nunca** lleva datos en texto claro. El "enlace real" de la pantalla es solo de referencia y no se envía.

## Archivo de entrada

Hoja `Destinatarios` (o la primera hoja que tenga estos encabezados). El orden de columnas no importa y no hace falta una tabla de Excel con nombre. La plantilla se descarga desde la pantalla (`api/plantilla`).

| Columna | Regla |
|---|---|
| Tipo de Formulario | 61, 62, 63, 64, 101, 102 o 161 |
| Nombre del Contratante | Obligatorio, máx. 200 |
| Nombre del Cliente | Obligatorio, máx. 200 (saludo del correo; no viaja en el enlace) |
| Numero de Poliza | Obligatorio, máx. 30, letras/números/`-`/`/`; conserva ceros a la izquierda |
| Suma Asegurada | > 0, máx. 2 decimales, sin redondeo. Acepta `2500000`, `2,500,000.00`, `2.500.000,00` |
| Correo | Un solo correo válido |
| Enlace | Opcional; se ignora (lo genera el servidor) |

CSV: UTF-8, UTF-16 o Windows-1252 (el "CSV" de Excel), separador `;`, `,` o tabulador. Filas duplicadas (mismo tipo, póliza y correo) se marcan con error.

## Configuración por ambiente

`ASPNETCORE_ENVIRONMENT` selecciona el archivo:

| Ambiente | Archivo | URL base del enlace | Envío |
|---|---|---|---|
| `Local` | appsettings.Local.json | https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/ | Simulado (no envía) |
| `Development` (DEV) | appsettings.Development.json | https://d3322rrb6yrsp3.cloudfront.net/EnrolamientoHN/ | Oracle |
| `Staging` (STG) | appsettings.Staging.json | https://d1i4s8s51j1tuu.cloudfront.net/EnrolamientoHN/ | Oracle |
| `Production` (PROD) | appsettings.Production.json | https://seguros.ficohsa.com/EnrolamientoHN/ | Oracle (Simulado prohibido) |

Valores que deben completar en cada `appsettings.{Ambiente}.json` (hoy tienen marcadores `__..._DEV__`, `__..._STG__`, `__..._PROD__`):

- `Correo:Remitente` → parámetro `sender` de pr_mail.
- `Oracle:DataSource` → descriptor TNS o `host:1521/servicio`.
- `Oracle:Usuario` y `Oracle:Clave` → **recomendado por variables de entorno** del sitio IIS (`Oracle__Usuario`, `Oracle__Clave`) en lugar del archivo. Las variables de entorno tienen prioridad sobre el JSON.
- `Correo:ImagenEncabezadoUrl` / `Correo:ImagenPieUrl` → URLs **HTTPS públicas** de las imágenes de encabezado y pie (el cliente de correo las descarga). Vacío = sin imagen.

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

## Desarrollo local

```bash
cd web && npm ci && npm run build          # compila el frontend a wwwroot
cd ../src/Enrolamiento.EnvioCorreos.Api
export ENROLAMIENTO_DP_LLAVES=$(dotnet run -- generar-llave)
dotnet run --launch-profile Local          # http://localhost:5180
```

Con `npm run dev` (puerto 5173) el frontend usa la API local mediante proxy. En modo Simulado, un correo que empiece con `falla` simula un error para probar los reintentos.

Pruebas: `dotnet test` (121 pruebas).

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

1. **⚠ El destinatario está fijo.** En la línea 100 se llama `begin_mail(sender, 'darwin.martinez@...', subject)` y no se usa el parámetro `recipients`. Tal como está, **todos los correos llegan a esa persona**. Hay que cambiarlo a `begin_mail(sender, recipients, subject)` antes de usarlo en STG/PROD.
2. **Los errores se tragan.** El `WHEN OTHERS` guarda `SQLERRM` pero el `INSERT INTO MAIL_ERRORES` está comentado y no relanza la excepción. La pantalla marca "Enviada" cuando el procedimiento termina sin error, pero si falla el SMTP Oracle no lo reporta. Recomendamos descomentar el registro de errores o hacer `RAISE` para que la pantalla muestre el fallo real.
3. **Codificación.** `begin_mail` declara `text/html; charset=iso-8859-1`. Por eso el backend convierte todo carácter no ASCII del HTML en entidades (`&#233;`) y envía el asunto sin tildes, para que nada se corrompa sin importar la conversión de juego de caracteres de utl_smtp.
4. **Tamaño.** `message` es VARCHAR2, con un máximo de 32 767 caracteres. El backend valida el tamaño antes de llamar.

## Seguridad y limitaciones

- Por ahora la pantalla no tiene login: cualquiera en la red interna con acceso al sitio puede enviar correos. Se recomienda agregar Windows Authentication y un grupo AD en cuanto sea posible.
- El enlace se puede reutilizar hasta que vence (48 h). No hay revocación individual ni uso único, porque no se crean tablas.
- Un enlace válido no reemplaza el OTP del formulario.
- No hay historial en la aplicación; el registro queda en pr_mail. Los logs de la aplicación no contienen correos, nombres ni enlaces.
- Las respuestas de la API se marcan `Cache-Control: no-store`.
