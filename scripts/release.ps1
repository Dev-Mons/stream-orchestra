#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepositoryUrl = "https://github.com/Dev-Mons/stream-orchestra",

    [switch]$SkipTests,

    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$project = "src/StreamOrchestra.App/StreamOrchestra.App.csproj"
$publishDir = Join-Path $repoRoot "publish"
$releasesDir = Join-Path $repoRoot "Releases"

Write-Host "[release] Version: $Version"
Write-Host "[release] Repository root: $repoRoot"

if (-not $SkipTests) {
    Write-Host "[release] Running tests..."
    dotnet test "StreamOrchestra.slnx" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $releasesDir) { Remove-Item -Recurse -Force $releasesDir }
New-Item -ItemType Directory -Path $releasesDir | Out-Null

$assemblyVersion = "$Version.0"

Write-Host "[release] Publishing framework-dependent win-x64..."
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    Write-Host "[release] Installing Velopack CLI (vpk) globally..."
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "vpk install failed." }
}

if (-not $SkipUpload) {
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Warning "[release] GITHUB_TOKEN is not set. Skipping download of prior releases (delta patches will be unavailable)."
    }
    else {
        Write-Host "[release] Downloading prior releases for delta packaging..."
        vpk download github `
            --repoUrl $RepositoryUrl `
            --outputDir $releasesDir `
            --token $token
        if ($LASTEXITCODE -ne 0) { Write-Warning "[release] vpk download exited non-zero. Continuing without prior releases." }
    }
}

Write-Host "[release] Packing with Velopack..."
vpk pack `
    --packId DevMons.StreamOrchestra `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe StreamOrchestra.App.exe `
    --packTitle StreamOrchestra `
    --outputDir $releasesDir `
    --noInst `
    --framework net8.0-x64-desktop,webview2
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

if (-not $SkipUpload) {
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Warning "[release] GITHUB_TOKEN is not set. Skipping upload."
    }
    else {
        Write-Host "[release] Uploading to GitHub Releases..."
        vpk upload github `
            --repoUrl $RepositoryUrl `
            --outputDir $releasesDir `
            --publish `
            --releaseName "StreamOrchestra v$Version" `
            --tag "v$Version" `
            --token $token
        if ($LASTEXITCODE -ne 0) { throw "vpk upload failed." }
    }
}

Write-Host "[release] Done. Artifacts: $releasesDir"
