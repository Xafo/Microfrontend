@echo off
REM ==========================================================================
REM  Envio de enlaces de enrolamiento - ejecucion LOCAL en Windows
REM  Abre http://localhost:5180 con envio SIMULADO (no envia correos reales).
REM ==========================================================================
setlocal
cd /d "%~dp0"

if not exist "Enrolamiento.EnvioCorreos.Api.exe" (
  echo No se encontro Enrolamiento.EnvioCorreos.Api.exe en esta carpeta.
  pause
  exit /b 1
)

REM Llave de cifrado SOLO para uso local. Se crea una vez y se reutiliza.
if not exist "llave-local.txt" (
  echo Generando llave local de cifrado...
  "Enrolamiento.EnvioCorreos.Api.exe" generar-llave > "llave-local.txt"
)
for /f "usebackq delims=" %%a in ("llave-local.txt") do set "ENROLAMIENTO_DP_LLAVES=%%a"

if "%ASPNETCORE_ENVIRONMENT%"=="" set "ASPNETCORE_ENVIRONMENT=Local"
if "%PUERTO%"=="" set "PUERTO=5180"
set "ASPNETCORE_URLS=http://localhost:%PUERTO%"

echo.
echo  Ambiente : %ASPNETCORE_ENVIRONMENT%
echo  URL      : http://localhost:%PUERTO%
echo  Cierre esta ventana (o Ctrl+C) para detener.
echo.
start "" "http://localhost:%PUERTO%"
"Enrolamiento.EnvioCorreos.Api.exe"
if errorlevel 1 pause
