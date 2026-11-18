# Instalar en IIS (frontend + backend)

El portal se publica como **un solo sitio IIS**: el backend ASP.NET Core sirve la API (`/api/...`) y también la pantalla React (`wwwroot`). No hay que instalar el frontend por separado.

```
Servidor IIS
└── Sitio o aplicación "EnvioEnlaces"  (Application Pool: EnvioEnlaces, sin código administrado)
    ├── Enrolamiento.EnvioCorreos.Api.dll   ← backend .NET 10
    ├── wwwroot\                           ← pantalla React compilada
    ├── Plantillas\                        ← plantilla Excel
    ├── appsettings*.json                  ← configuración por ambiente (sin secretos)
    └── web.config                         ← generado al publicar
```

Repita esta guía por ambiente (DEV, STG y PROD), cada uno con **su** configuración y **sus** secretos.

---

## 1. Preparar el servidor (una vez)

1. **Rol IIS** instalado (Administrador del servidor → Agregar roles → Servidor web IIS).
2. **ASP.NET Core 10 Hosting Bundle**: descargar de https://dotnet.microsoft.com/download/dotnet/10.0, sección *Hosting Bundle*, e instalar. Después, en una consola como administrador:
   ```powershell
   net stop was /y
   net start w3svc
   ```
   Para comprobarlo, en IIS → servidor → **Módulos** debe aparecer `AspNetCoreModuleV2`.
3. **Red:** el servidor debe llegar a Oracle del ambiente (por ejemplo `Test-NetConnection HOST_ORACLE -Port 1521`). El correo lo envía Oracle (`pr_mail`), no IIS.
4. **Certificado HTTPS** del dominio interno, instalado en el servidor (recomendado; ver paso 6).

No hace falta instalar Node en el servidor.

---

## 2. Generar el paquete (en la PC del desarrollador)

Desde la raíz del repo:

```powershell
# 1) Pantalla (el logo debe estar en web\public\logo-ficohsa.png)
cd web
npm install
npm run build
cd ..

# 2) Backend + pantalla en una carpeta
dotnet publish src\Enrolamiento.EnvioCorreos.Api -c Release -o publicar
```

Revise que `publicar\` contenga `Enrolamiento.EnvioCorreos.Api.dll`, `web.config`, `wwwroot\index.html` y `Plantillas\Enrolamiento_EnvioCorreos.xlsx`.

> Variante sin Hosting Bundle en el servidor (más pesada, incluye .NET):
> `dotnet publish src\Enrolamiento.EnvioCorreos.Api -c Release -r win-x64 --self-contained true -o publicar`

---

## 3. Completar la configuración del ambiente (sin secretos)

En `publicar\`, edite **solo** el archivo del ambiente que va a instalar:

| Ambiente | Archivo | `ASPNETCORE_ENVIRONMENT` |
|---|---|---|
| DEV | `appsettings.Development.json` | `Development` |
| STG | `appsettings.Staging.json` | `Staging` |
| PROD | `appsettings.Production.json` | `Production` |

```json
{
  "Enlaces": { "UrlPublicaBase": "https://seguros.ficohsa.com/" },
  "Correo": {
    "Modo": "Oracle",
    "Remitente": "remitente@ficohsa.com",
    "ImagenEncabezadoUrl": "https://.../header.png",
    "ImagenPieUrl": "https://.../footer.png"
  },
  "Oracle": {
    "DataSource": "HOST_ORACLE:1521/SERVICIO",
    "Usuario": "",
    "Clave": ""
  }
}
```

**Deje vacíos `Oracle:Usuario` y `Oracle:Clave`.** Van como variables de entorno en el paso 5. Las URL de las imágenes deben ser HTTPS públicas, o dejarse vacías.

---

## 4. Crear el Application Pool y el sitio

### 4.1 Copiar archivos
Copie el contenido de `publicar\` a, por ejemplo, `D:\Sitios\EnvioEnlaces\`.

### 4.2 Application Pool
IIS → **Grupos de aplicaciones** → Agregar:
- Nombre: `EnvioEnlaces`
- Versión de .NET CLR: **Sin código administrado**
- Modo de canalización: **Integrada**

En **Configuración avanzada** del pool:

| Opción | Valor | Por qué |
|---|---|---|
| Modo de inicio (Start Mode) | `AlwaysRunning` | Arranca sin esperar la primera visita |
| Tiempo de espera de inactividad (Idle Time-out) | `0` | No se apaga a mitad de un envío |
| Reciclaje → Intervalo de tiempo regular | `0`, o una hora fija de madrugada | Los lotes viven en memoria; un reciclaje corta un envío en curso |
| Identidad | `ApplicationPoolIdentity` | — |

### 4.3 Sitio o aplicación
- **Sitio nuevo:** IIS → Sitios → Agregar sitio → ruta física `D:\Sitios\EnvioEnlaces`, pool `EnvioEnlaces`, enlace HTTPS 443 con el certificado.
- **O aplicación dentro de un sitio existente:** clic derecho en el sitio → Agregar aplicación → alias `EnvioEnlaces`, misma ruta y pool. La pantalla funciona en una subruta (`https://servidor/EnvioEnlaces/`) sin cambios.

### 4.4 Permisos de carpeta
```powershell
icacls "D:\Sitios\EnvioEnlaces" /grant "IIS AppPool\EnvioEnlaces:(OI)(CI)RX"
```

---

## 5. Secretos como variables de entorno del sitio

Se guardan en la configuración de IIS (`applicationHost.config`), **no** en el `web.config`. Así no se pierden al publicar una versión nueva ni quedan en la carpeta del sitio.

### 5.1 Generar los valores (en la PC del desarrollador, con el código)
```powershell
cd src\Enrolamiento.EnvioCorreos.Api
dotnet build
dotnet run --no-build --no-launch-profile -- generar-llave | Out-File -Encoding ascii llave-prod.txt   # llave de enlaces del ambiente
dotnet run --no-build --no-launch-profile -- generar-clave                                         # hash de la contraseña del portal
```
- La **llave** es una por ambiente y **la misma** que configura el backend del formulario (Emerson). Entréguela por la bóveda corporativa.
- El **hash** es la línea `PBKDF2-SHA256:...`. La contraseña no se guarda en ningún lado.

### 5.2 Cargarlos en IIS (PowerShell **como administrador** en el servidor)
```powershell
Import-Module WebAdministration
$sitio = "EnvioEnlaces"          # o "Default Web Site/EnvioEnlaces" si es una aplicación

function Set-VarSitio($nombre, $valor) {
  $f = "system.webServer/aspNetCore/environmentVariables"
  Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Location $sitio -Filter $f -Name "." -AtElement @{name=$nombre} -ErrorAction SilentlyContinue
  Add-WebConfigurationProperty    -PSPath "MACHINE/WEBROOT/APPHOST" -Location $sitio -Filter $f -Name "." -Value @{name=$nombre; value=$valor}
}

Set-VarSitio "ASPNETCORE_ENVIRONMENT" "Production"             # Development | Staging | Production
Set-VarSitio "ENROLAMIENTO_DP_LLAVES" "PEGAR-LA-LLAVE-DEL-AMBIENTE"
Set-VarSitio "Login__Usuario"         "operador"
Set-VarSitio "Login__ClaveHash"       "PBKDF2-SHA256:600000:...:..."
Set-VarSitio "Oracle__Usuario"        "USUARIO_ORACLE"
Set-VarSitio "Oracle__Clave"          "CLAVE_ORACLE"

Restart-WebAppPool "EnvioEnlaces"
```

Alternativa gráfica: IIS → sitio → **Editor de configuración** → sección `system.webServer/aspNetCore` → *Desde: ApplicationHost.config <location path='EnvioEnlaces'>* → `environmentVariables` → agregar cada nombre y valor → **Aplicar**.

> Los nombres llevan **doble guion bajo** (`Login__Usuario`). Borre del servidor el archivo de la llave cuando termine.
> En el servidor la llave va en `ENROLAMIENTO_DP_LLAVES` (sin sufijo), porque cada servidor atiende un solo ambiente. Los sufijos `_DEV`, `_STG` y `_PROD` son solo para ejecutar desde una PC.

---

## 6. HTTPS (recomendado)

- Enlace HTTPS con el certificado interno. La cookie de sesión pasa a **Secure** automáticamente.
- Opcional: un enlace HTTP que redirija a HTTPS (módulo URL Rewrite).
- El portal es de uso **interno**: no lo publique a Internet. Limítelo por firewall a la red corporativa.

---

## 7. Verificar

1. Abra `https://servidor/` (o `/EnvioEnlaces/`). Debe aparecer el **login**.
2. Ingrese. La barra debe mostrar el ambiente (**DEV**, **STG** o **PROD**) y **no** debe decir "Envío simulado".
3. Descargue la plantilla y cargue un archivo de **una fila** con su propio correo.
4. Revise que el enlace cifrado empiece con la URL del ambiente (`https://seguros.ficohsa.com/?datos=...` en PROD).
5. Envíe y confirme la llegada del correo.
6. Pídale a Emerson que abra ese enlace en el formulario del mismo ambiente: debe precargar los datos (eso confirma que las llaves coinciden).

---

## 8. Si no arranca

| Error en el navegador | Causa probable | Qué hacer |
|---|---|---|
| **HTTP 500.19** | Falta el Hosting Bundle | Paso 1.2 |
| **HTTP 500.30 / 500.31** – *app failed to start* | Falta configuración (llave, login, Oracle, remitente) | Ver el detalle (abajo) |
| **HTTP 503** | El Application Pool se detuvo | Iniciar el pool y revisar el Visor de eventos |
| Login responde "Demasiados intentos" | 5 intentos fallidos en 1 minuto | Esperar 1 minuto |

**Ver el motivo exacto:**
- Visor de eventos → Registros de Windows → **Aplicación** → origen *IIS AspNetCore Module V2*.
- O, temporalmente, en `web.config` cambie `stdoutLogEnabled="false"` por `"true"`, cree la carpeta `logs` con permiso de escritura para `IIS AppPool\EnvioEnlaces`, recargue y lea `logs\stdout_*.log`. **Vuelva a dejarlo en `false`** al terminar.

La aplicación siempre indica qué falta, por ejemplo: `Configure Login__ClaveHash con el hash...`, `La variable de entorno 'ENROLAMIENTO_DP_LLAVES'... no está definida`, `Configure Oracle:DataSource, Oracle:Usuario y Oracle:Clave`.

---

## 9. Actualizar a una versión nueva

1. Genere el paquete nuevo (paso 2) y vuelva a completar el `appsettings.{Ambiente}.json` (paso 3), o conserve el del servidor.
2. En el servidor, copie un archivo `app_offline.htm` a la carpeta del sitio. El sitio queda en mantenimiento y libera los archivos.
3. Copie los archivos nuevos **sin reemplazar** el `appsettings.{Ambiente}.json` del servidor.
4. Borre `app_offline.htm`.

Las variables del paso 5 no se tocan: están en IIS, no en la carpeta. No actualice mientras haya un envío en curso.

---

## 10. Lista de verificación por ambiente

- [ ] Hosting Bundle .NET 10 instalado y `AspNetCoreModuleV2` visible
- [ ] Pool `EnvioEnlaces`: sin código administrado, AlwaysRunning, Idle 0, reciclaje controlado
- [ ] `appsettings.{Ambiente}.json` con URL base, remitente, DataSource e imágenes
- [ ] Variables: `ASPNETCORE_ENVIRONMENT`, `ENROLAMIENTO_DP_LLAVES`, `Login__Usuario`, `Login__ClaveHash`, `Oracle__Usuario`, `Oracle__Clave`
- [ ] La misma `ENROLAMIENTO_DP_LLAVES` configurada en el backend del formulario de ese ambiente
- [ ] HTTPS con certificado y acceso solo desde la red interna
- [ ] Prueba de una fila enviada y abierta en el formulario
- [ ] `pr_mail` corregido (destinatario no fijo) antes de usar con clientes reales
