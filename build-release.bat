@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "PROJECT=src\StreamOrchestra.App\StreamOrchestra.App.csproj"
set "PUBLISH_ROOT=%~dp0publish"
set "PUBLISH_DIR=%PUBLISH_ROOT%\StreamOrchestra-light"
set "ZIP_PATH=%PUBLISH_ROOT%\StreamOrchestra-light.zip"
set "RUNTIME=win-x64"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [StreamOrchestra] .NET SDK was not found.
    echo Install .NET 8 SDK, then run this file again.
    pause
    exit /b 1
)

echo [StreamOrchestra] Running Release tests...
dotnet test "StreamOrchestra.slnx" -c Release --nologo
if errorlevel 1 (
    echo.
    echo [StreamOrchestra] Tests failed. Publish was not created.
    pause
    exit /b 1
)

echo.
echo [StreamOrchestra] Cleaning publish folder...
if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
    if errorlevel 1 (
        echo [StreamOrchestra] Failed to clean publish folder:
        echo %PUBLISH_DIR%
        pause
        exit /b 1
    )
)
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"

echo.
echo [StreamOrchestra] Publishing lightweight framework-dependent Release app for %RUNTIME%...
dotnet publish "%PROJECT%" ^
    -c Release ^
    -r %RUNTIME% ^
    --self-contained false ^
    -p:PublishSingleFile=false ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:AllowedReferenceRelatedFileExtensions= ^
    -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo.
    echo [StreamOrchestra] Publish failed.
    pause
    exit /b 1
)

echo.
echo [StreamOrchestra] Creating zip package...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force"
if errorlevel 1 (
    echo.
    echo [StreamOrchestra] Zip package failed.
    pause
    exit /b 1
)

echo.
echo [StreamOrchestra] Publish complete:
echo %PUBLISH_DIR%
echo %ZIP_PATH%
echo.
echo Run:
echo %PUBLISH_DIR%\StreamOrchestra.App.exe

pause
exit /b 0
