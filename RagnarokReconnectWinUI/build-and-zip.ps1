# Clean, build Release x86 portable output, zip for distribution.
# Uses Release build folder (not dotnet publish) - mirrors PartyWingBuffTools known-good flow.
param(
    [string]$ZipName = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repoRoot = Split-Path $root -Parent
$distRoot = Join-Path $root "dist"
$csprojPath = Join-Path $root "RagnarokReconnectWinUI.csproj"
$csprojXml = [xml](Get-Content -Path $csprojPath)
$projectVersion = $csprojXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    $projectVersion = "0.0.0"
}

$effectiveVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) { $Version } else { $projectVersion }
if ([string]::IsNullOrWhiteSpace($effectiveVersion)) {
    $effectiveVersion = "0.0.0"
}

$packageBaseName = "RagnarokReconnectWinUI-Portable-v$effectiveVersion"
if ([string]::IsNullOrWhiteSpace($ZipName)) {
    $ZipName = "$packageBaseName.zip"
}

$staging = Join-Path $distRoot $packageBaseName
$zipPath = Join-Path $distRoot $ZipName

Write-Host "Cleaning RagnarokReconnectWinUI build outputs and dist... (version $effectiveVersion)"
foreach ($p in @(
        (Join-Path $root "bin"),
        (Join-Path $root "obj")
    )) {
    if (Test-Path $p) {
        try {
            Remove-Item $p -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Cleanup warning for '$p': $($_.Exception.Message)"
        }
    }
}
if (Test-Path $distRoot) {
    Remove-Item $distRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Write-Host "Building Release (x86, win-x86)..."
Push-Location $repoRoot
try {
    dotnet build (Join-Path $root "RagnarokReconnectWinUI.csproj") -c Release -p:Platform=x86 -p:Version=$effectiveVersion -r win-x86
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$src = Join-Path $root "bin\x86\Release\net10.0-windows10.0.26100.0\win-x86"
if (-not (Test-Path $src)) {
    Write-Error "Expected build output not found: $src"
}

# Stage in %TEMP% first to avoid transient locks while zipping.
$tempStage = Join-Path $env:TEMP ("RagnarokReconnectPortable_" + [System.Guid]::NewGuid().ToString("N"))
try {
    Write-Host "Staging to temp for zip: $tempStage"
    New-Item -ItemType Directory -Path $tempStage -Force | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $tempStage -Recurse

    $cmd = Join-Path $tempStage "Run-RagnarokReconnectWinUI.cmd"
    @"
@echo off
setlocal
start "" "%~dp0RagnarokReconnectWinUI.exe"
"@ | Set-Content -Path $cmd -Encoding ASCII

    Write-Host "Creating zip (retries if files are temporarily locked)..."
    $zipOk = $false
    for ($i = 1; $i -le 8; $i++) {
        try {
            if (Test-Path $zipPath) {
                Remove-Item $zipPath -Force
            }
            Compress-Archive -Path (Join-Path $tempStage "*") -DestinationPath $zipPath -CompressionLevel Optimal
            $zipOk = $true
            break
        }
        catch {
            Write-Warning "Zip attempt $i failed: $($_.Exception.Message). Waiting 2s..."
            Start-Sleep -Seconds 2
        }
    }
    if (-not $zipOk) {
        Write-Error "Could not create zip after retries. Close Explorer windows on dist\\ and retry."
    }

    Write-Host "Copying portable folder to dist..."
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item -Path (Join-Path $tempStage "*") -Destination $staging -Recurse -Force
}
finally {
    if (Test-Path $tempStage) {
        Remove-Item $tempStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Done."
Write-Host "  Zip: $zipPath"
Write-Host "  Folder: $staging"
Write-Host "Run RagnarokReconnectWinUI.exe as Administrator for memory reads."
