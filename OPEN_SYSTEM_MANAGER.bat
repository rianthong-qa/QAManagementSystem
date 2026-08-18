@echo off
setlocal
cd /d "%~dp0"
if exist ".artifacts\system-manager\ProMaxx2.ServiceManager.exe" (
    start "" ".artifacts\system-manager\ProMaxx2.ServiceManager.exe"
    exit /b 0
)
dotnet run --project "tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj"
if errorlevel 1 pause
