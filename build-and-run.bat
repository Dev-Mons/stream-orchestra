@echo off
setlocal

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [StreamOrchestra] .NET SDK was not found.
    echo Install .NET 8 SDK, then run this file again.
    pause
    exit /b 1
)

echo [StreamOrchestra] Building Debug app...
dotnet build "src\StreamOrchestra.App\StreamOrchestra.App.csproj" -c Debug
if errorlevel 1 (
    echo.
    echo [StreamOrchestra] Build failed.
    pause
    exit /b 1
)

echo.
echo [StreamOrchestra] Starting app...
dotnet run --no-build --project "src\StreamOrchestra.App\StreamOrchestra.App.csproj" -c Debug
if errorlevel 1 (
    echo.
    echo [StreamOrchestra] App exited with an error.
    pause
    exit /b 1
)

exit /b 0
