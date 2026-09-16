@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=D:\Unreal\Svn"
set "BRIDGE=%ROOT%bridge\bin\Release\net8.0-windows\FigmaUnityBridge.exe"

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
