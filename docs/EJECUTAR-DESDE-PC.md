# Ejecutar el portal desde una PC contra Oracle DEV, STG o PROD

Guía para que otra persona del equipo levante el portal **Envío de enlaces de enrolamiento** en su PC conectado a la base de datos de cualquier ambiente.

> ⚠ Los envíos son **reales**: el portal ejecuta `ficohsa.pr_mail.mail` en la base del ambiente elegido.
> PROD desde una PC solo para casos puntuales y autorizados; lo normal es usar el sitio IIS ([INSTALAR-IIS.md](INSTALAR-IIS.md)).

| Perfil | Ambiente | Base Oracle | URL del enlace que recibe el cliente | Puerto local |
|---|---|---|---|---|
| `DEV` | Development | Oracle DEV | `https://d3322rrb6yrsp3.cloudfront.net/?datos=...` | http://localhost:5180 |
| `STG` | Staging | Oracle STG | `https://d1i4s8s51j1tuu.cloudfront.net/?datos=...` | http://localhost:5181 |
| `PROD` | Production | Oracle PROD | `https://seguros.ficohsa.com/?datos=...` | http://localhost:5182 |

---

## 1. Requisitos (una vez)

| Herramienta | Versión | Comprobar |
|---|---|---|
| .NET SDK | 10.x | `dotnet --list-sdks` debe mostrar `10.` |
| Node.js (incluye npm) | 18 o superior (recomendado 22 LTS) | `node -v` |
| Acceso de red a Oracle del ambiente | Puerto 1521 | `Test-NetConnection HOST_ORACLE -Port 1521` |

---

## 2. Obtener el código y compilar (una vez, y en cada actualización)

```powershell
git clone https://github.com/Xafo/Microfrontend.git
cd Microfrontend
git checkout claude/pensive-ptolemy-7d2nje      # o main, cuando esté integrado
```
(o descargar el ZIP de GitHub y descomprimir en una ruta corta, por ejemplo `C:\Proyectos\Microfrontend`)

**Logo:** copiar el logo oficial a `web\public\logo-ficohsa.png` (no está en el repo).

```powershell
cd web
npm install
npm run build
cd ..\src\Enrolamiento.EnvioCorreos.Api
dotnet build
```

Los comandos siguientes se ejecutan en **`src\Enrolamiento.EnvioCorreos.Api`**.

---

## 3. Configurar cada ambiente (una vez por ambiente)

Cada ambiente usa **3 cosas**: un archivo local con credenciales, una llave de cifrado y el hash del login.

### 3.1 Archivo local de credenciales

Copie el ejemplo del ambiente quitándole `.ejemplo`:

| Ambiente | Copiar | Como |
|---|---|---|
| DEV | `appsettings.Development.local.json.ejemplo` | `appsettings.Development.local.json` |
| STG | `appsettings.Staging.local.json.ejemplo` | `appsettings.Staging.local.json` |
| PROD | `appsettings.Production.local.json.ejemplo` | `appsettings.Production.local.json` |

```powershell
Copy-Item appsettings.Development.local.json.ejemplo appsettings.Development.local.json
```

Complete los datos reales (ejemplo de DEV):

```json
{
  "Enlaces": { "VariableEntornoLlaves": "ENROLAMIENTO_DP_LLAVES_DEV" },
  "Login":   { "Usuario": "operador", "ClaveHash": "PBKDF2-SHA256:600000:...:..." },
  "SolicitudesApi": { "Usuario": "USUARIO_API", "Clave": "CLAVE_API" },
  "Correo":  { "Remitente": "remitente@ficohsa.com" },
  "Oracle":  {
    "DataSource": "HOST_ORACLE_DEV:1521/SERVICIO_DEV",
    "Usuario": "USUARIO_DEV",
    "Clave": "CLAVE_DEV"
  }
}
```

- **`DataSource`:** el mismo host, puerto y servicio de SQL Developer o Toad. También acepta el descriptor TNS completo: `(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=...)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=...)))`.
- **`ClaveHash`:** ver el paso 3.3.
- Estos archivos `*.local.json` **los ignora Git y no se copian al publicar**. Nunca los envíe por correo ni chat.

### 3.2 Llave de cifrado de los enlaces

La llave de cada ambiente **la entrega el responsable** (es la misma que usa el formulario de Emerson; ver `docs\LLAVE-ENLACES-EMERSON.txt`). **No genere una propia**: los enlaces no se podrían abrir en el formulario.

Guárdela en una variable de su usuario de Windows, con el nombre del ambiente:

```powershell
[Environment]::SetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES_DEV",  "PEGAR-LLAVE-DEV",  "User")
[Environment]::SetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES_STG",  "PEGAR-LLAVE-STG",  "User")
[Environment]::SetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES_PROD", "PEGAR-LLAVE-PROD", "User")
```

O, si la recibió en un archivo:
```powershell
[Environment]::SetEnvironmentVariable("ENROLAMIENTO_DP_LLAVES_DEV", (Get-Content .\llave-dev.txt -Raw).Trim(), "User")
Remove-Item .\llave-dev.txt
```

**Cierre y vuelva a abrir la terminal y VS Code** para que tome las variables.

### 3.3 Usuario y contraseña del portal

```powershell
dotnet run --no-build --no-launch-profile -- generar-clave
```
Pide la contraseña dos veces sin mostrarla (mínimo 10 caracteres) e imprime una línea `PBKDF2-SHA256:600000:...`. Péguela en `Login:ClaveHash` del archivo local (paso 3.1). Puede usar la misma o una distinta por ambiente.

---

## 4. Ejecutar

```powershell
dotnet run --launch-profile DEV      # http://localhost:5180
dotnet run --launch-profile STG      # http://localhost:5181
dotnet run --launch-profile PROD     # http://localhost:5182
```

Se abre el navegador con el **login**. Después de ingresar, la barra superior debe mostrar el ambiente (**DEV**, **STG** o **PROD**) y **no** debe decir "Envío simulado". Para detenerlo: **Ctrl+C**.

Puede tener los tres abiertos a la vez (usan puertos distintos).

**Primera prueba en cada ambiente:** cargue un archivo de **una sola fila** con su propio correo. Revise el enlace (*Revisar datos → Ver enlace*), envíe y confirme que el correo llegó.

> ⚠ Mientras `ficohsa.pr_mail.mail` tenga el destinatario fijo en el código (línea 100), todos los correos llegan a esa persona y no al cliente.

---

## 5. Problemas comunes

| Mensaje al arrancar o en pantalla | Causa | Solución |
|---|---|---|
| `La variable de entorno 'ENROLAMIENTO_DP_LLAVES_DEV' ... no está definida` | Falta la llave del ambiente o la terminal es anterior a crearla | Paso 3.2 y abrir una terminal nueva |
| `... no está definida` con `ENROLAMIENTO_DP_LLAVES` (sin sufijo) | Falta el archivo local del ambiente o le falta `Enlaces:VariableEntornoLlaves` | Paso 3.1 |
| `Configure el usuario del portal...` / `Configure Login__ClaveHash...` | Falta `Login` en el archivo local, o se pegó la contraseña en vez del hash | Paso 3.3 |
| `Correo:Remitente debe ser un correo válido...` | Remitente vacío o con el marcador de ejemplo | Completar `Correo:Remitente` |
| `Configure Oracle:DataSource, Oracle:Usuario y Oracle:Clave` | Datos de Oracle vacíos o con marcadores `__..__` | Completar `Oracle` |
| `No existe ningún perfil de inicio con el nombre "STG"` | Código desactualizado | Actualizar el repo |
| En el envío: `Oracle ORA-12541` / `ORA-12170` | No hay conexión al servidor Oracle | Revisar VPN/red y `DataSource` |
| En el envío: `Oracle ORA-01017` | Usuario o clave de Oracle incorrectos | Revisar `Oracle:Usuario` / `Clave` |
| En el envío: `ORA-06550` / `PLS-00201` | El usuario no tiene permiso sobre `ficohsa.pr_mail` | Pedir `GRANT EXECUTE` al DBA |
| "Usuario o contraseña incorrectos" | Credenciales del portal | Verifique usuario y contraseña; si la olvidó, genere un hash nuevo |
| "Demasiados intentos" | 5 intentos fallidos en 1 minuto | Esperar 1 minuto |
| El formulario de Emerson dice "enlace no válido" | La llave de su PC no es la del ambiente | Use la llave entregada (paso 3.2) |
| La pantalla no tiene los últimos cambios | No se recompiló el frontend | `cd web` → `npm run build` |

---

## Resumen para compartir

1. Instalar **.NET 10 SDK** y **Node 22**.
2. Clonar o descargar el repo y copiar el logo a `web\public\logo-ficohsa.png`.
3. `cd web` → `npm install` → `npm run build` → `cd ..\src\Enrolamiento.EnvioCorreos.Api` → `dotnet build`
4. Por ambiente: copiar `appsettings.{Ambiente}.local.json.ejemplo` a `.local.json` y completar Login, Remitente y Oracle.
5. Guardar la llave entregada en `ENROLAMIENTO_DP_LLAVES_DEV` / `_STG` / `_PROD`.
6. `generar-clave` → pegar el hash en `Login:ClaveHash`.
7. `dotnet run --launch-profile DEV` (o `STG` / `PROD`).
