@echo off
setlocal

set "ROOT=%~dp0"
set "BRIDGE=%ROOT%bridge\bin\Release\net8.0-windows\FigmaUnityBridge.exe"

if "%~1"=="" (
  set /p "PROJECT=Enter Unity project path: "
) else (
  set "PROJECT=%~1"
)

if not exist "%PROJECT%\Assets" (
  echo Unity project Assets folder not found:
  echo %PROJECT%\Assets
  echo Usage: start-bridge.bat "D:\Projects\MyUnityGame"
  pause
  exit /b 1
)

if not exist "%BRIDGE%" (
  echo Bridge not found:
  echo %BRIDGE%
  echo.
  echo Build it first with:
  echo   dotnet build bridge\FigmaUnityBridge.csproj -c Release
  pause
  exit /b 1
)

start "" "%BRIDGE%" --project "%PROJECT%"
echo Bridge starting for project:
echo %PROJECT%
echo Health: http://localhost:17890/health
