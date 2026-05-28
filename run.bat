@echo off
setlocal

pushd "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK was not found.
    echo Install the .NET 8 SDK, then run this file again.
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    popd
    exit /b 1
)

echo Building Stream Orchestra...
dotnet build "src\StreamOrchestra.App\StreamOrchestra.App.csproj" -c Debug -nologo
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
if not "%BUILD_EXIT_CODE%"=="0" (
    echo.
    echo Build failed with code %BUILD_EXIT_CODE%.
    pause
    popd
    exit /b %BUILD_EXIT_CODE%
)

echo Starting Stream Orchestra...
dotnet run --project "src\StreamOrchestra.App\StreamOrchestra.App.csproj" --no-build -c Debug
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Stream Orchestra exited with code %EXIT_CODE%.
    pause
)

popd
exit /b %EXIT_CODE%
