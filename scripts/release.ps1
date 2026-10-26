#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepositoryUrl = "https://github.com/Dev-Mons/stream-orchestra",

    # Markdown release notes shown in the in-app update prompt and on the GitHub release page.
    # Defaults to release-notes/<Version>.md; generated from commit subjects when that file is missing.
    [string]$ReleaseNotesPath,

    [switch]$SkipTests,

    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function New-ReleaseNotesFromCommits {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Path
    )

    # Tags are created on GitHub by vpk upload, so fetch them before looking for the previous release.
    git fetch --tags --quiet 2>$null
    $previousTag = git tag --list "v*" --sort=-v:refname --merged HEAD |
        Where-Object { $_ -ne "v$Version" } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($previousTag)) {
        Write-Warning "[release] No previous v* tag found. Write $Path by hand to include release notes."
        return $false
    }

    $subjects = @(git log "$previousTag..HEAD" --no-merges --format="%s" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($subjects.Count -eq 0) {
        Write-Warning "[release] No commits since $previousTag. Release notes were not generated."
        return $false
    }

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    $lines = $subjects | ForEach-Object { "- $_" }
    Set-Content -Path $Path -Value $lines -Encoding utf8NoBOM
    Write-Host "[release] Generated $Path from $($subjects.Count) commit(s) since $previousTag. Review and commit it."
    return $true
}

function Set-GitHubReleaseBody {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryUrl,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Body,
        [Parameter(Mandatory = $true)][string]$Token
    )

    $repoPath = ([Uri]$RepositoryUrl).AbsolutePath.Trim("/") -replace "\.git$", ""
    $headers = @{
        Authorization          = "Bearer $Token"
        Accept                 = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
    $release = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$repoPath/releases/tags/$Tag" -Headers $headers
    $payload = [Text.Encoding]::UTF8.GetBytes((@{ body = $Body } | ConvertTo-Json))
    Invoke-RestMethod -Method Patch `
        -Uri "https://api.github.com/repos/$repoPath/releases/$($release.id)" `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -Body $payload | Out-Null
}

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

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = Join-Path $repoRoot "release-notes/$Version.md"
}

if (-not (Test-Path $ReleaseNotesPath)) {
    Write-Host "[release] $ReleaseNotesPath not found. Generating release notes from commit subjects..."
    New-ReleaseNotesFromCommits -Version $Version -Path $ReleaseNotesPath | Out-Null
}

$releaseNotes = $null
if (Test-Path $ReleaseNotesPath) {
    $releaseNotes = (Get-Content -Path $ReleaseNotesPath -Raw -Encoding utf8).Trim()
    if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
        Write-Warning "[release] $ReleaseNotesPath is empty. Packing without release notes."
        $releaseNotes = $null
    }
    else {
        Write-Host "[release] Release notes: $ReleaseNotesPath"
    }
}

$packArgs = @(
    "pack",
    "--packId", "StreamOrchestra",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "StreamOrchestra.App.exe",
    "--packTitle", "StreamOrchestra",
    "--outputDir", $releasesDir,
    "--noInst",
    "--framework", "net8.0-x64-desktop,webview2"
)
if ($null -ne $releaseNotes) {
    # The notes are stored in releases.win.json, where the app reads them for the update prompt.
    $packArgs += @("--releaseNotes", (Resolve-Path $ReleaseNotesPath).Path)
}

Write-Host "[release] Packing with Velopack..."
vpk @packArgs
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

        if ($null -ne $releaseNotes) {
            Write-Host "[release] Writing release notes to the GitHub release body..."
            try {
                Set-GitHubReleaseBody -RepositoryUrl $RepositoryUrl -Tag "v$Version" -Body $releaseNotes -Token $token
            }
            catch {
                # The release is already published and the app still reads the notes from releases.win.json.
                Write-Warning "[release] Could not update the GitHub release body: $($_.Exception.Message)"
            }
        }
    }
}

Write-Host "[release] Done. Artifacts: $releasesDir"
