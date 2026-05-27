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

set "OUTPUT_DIR=artifacts\publish\StreamOrchestra"

echo Cleaning publish output...
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"

echo Publishing Stream Orchestra...
dotnet publish "src\StreamOrchestra.App\StreamOrchestra.App.csproj" -c Release -r win-x64 --self-contained false -o "%OUTPUT_DIR%"
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
    echo.
    echo Publish completed.
    echo Output: %CD%\%OUTPUT_DIR%
    echo Run: %CD%\%OUTPUT_DIR%\StreamOrchestra.App.exe
) else (
    echo.
    echo Publish failed with code %EXIT_CODE%.
)

echo.
if /I not "%~1"=="/nopause" pause
popd
exit /b %EXIT_CODE%
