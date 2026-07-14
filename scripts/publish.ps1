#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes a self-contained single-file win-x64 EXE.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
#>
$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src\YouTubeToMp3'
$publishDir = Join-Path $projectDir 'bin\Publish\win-x64'

if (Test-Path $publishDir) {
    Write-Host "Cleaning $publishDir"
    Remove-Item $publishDir -Recurse -Force
}

Push-Location $projectDir
try {
    & dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$ver = & (Join-Path $repoRoot 'scripts\get-version.ps1')

$extensionSrc = Join-Path $repoRoot 'browser-extension'
$extensionDest = Join-Path $publishDir 'browser-extension'
if (Test-Path $extensionSrc) {
    Write-Host "Copying browser extension to $extensionDest"
    Copy-Item $extensionSrc $extensionDest -Recurse -Force
}

Write-Host ""
Write-Host "Published to: $publishDir\YouTubeToMp3.exe (Version $ver)"
Write-Host "Next: run scripts\build-installer.ps1 for the setup wizard."
& (Join-Path $repoRoot 'scripts\update-windows-shortcuts.ps1') -SourceDir $publishDir
