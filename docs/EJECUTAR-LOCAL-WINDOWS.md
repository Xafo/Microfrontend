# Ejecutar en local (Windows)

## Opción A: sin instalar nada (recomendada)

La carpeta `1-ejecutable-windows` del zip trae la aplicación ya compilada para Windows x64, con el runtime de .NET incluido. No requiere Node ni .NET.

1. Descomprima el zip en una ruta corta, por ejemplo `C:\Enrolamiento\`.
2. Abra `1-ejecutable-windows` y haga doble clic en **`iniciar-local.cmd`**.
3. Se abre el navegador en http://localhost:5180. Si no abre, entre manualmente.
4. Para detenerla, cierre la ventana negra.

Si Windows muestra "Windows protegió su PC", elija *Más información → Ejecutar de todas formas*. Si el antivirus o Windows bloquea los archivos descargados, haga clic derecho sobre el zip, *Propiedades → Desbloquear*, y vuelva a descomprimir.

En este modo (`Local`) el envío es **simulado**: no se manda ningún correo. Un correo que empiece con `falla` simula un error para probar los reintentos. La plantilla de Excel se descarga desde la pantalla.

### Cambiar el puerto
En una consola `cmd` ubicada en la carpeta:
```
set PUERTO=5200
iniciar-local.cmd
```

### Probar contra Oracle DEV desde su máquina
1. Complete `appsettings.Development.json` en `1-ejecutable-windows`: `Correo:Remitente` y `Oracle:DataSource`.
2. En `cmd`:
```
set ASPNETCORE_ENVIRONMENT=Development
set Oracle__Usuario=SU_USUARIO
set Oracle__Clave=SU_CLAVE
iniciar-local.cmd
```
⚠ Esto **envía correos reales** con pr_mail. Revise antes la nota sobre el destinatario fijo en el README.

## Opción B: desde el código fuente

Requiere el **SDK de .NET 10**. La carpeta `2-codigo-fuente` trae:
- `web/node_modules` ya instalados para Windows x64, así que no hace falta `npm install`.
- `paquetes-nuget/` con todos los paquetes de .NET y un `nuget.config` que los usa, así que no se necesita internet para compilar.
- `wwwroot` ya compilado, así que no hace falta Node solo para ejecutar.

```
cd 2-codigo-fuente\src\Enrolamiento.EnvioCorreos.Api
for /f "delims=" %a in ('dotnet run -- generar-llave') do set "ENROLAMIENTO_DP_LLAVES=%a"
dotnet run --launch-profile Local
```
Abra http://localhost:5180.

Pruebas: `cd 2-codigo-fuente` y luego `dotnet test`.

Si modifica el frontend (requiere Node 18 o superior):
```
cd 2-codigo-fuente\web
npm run build        rem recompila a wwwroot
npm run dev          rem modo desarrollo en http://localhost:5173 (con la API corriendo en 5180)
```
