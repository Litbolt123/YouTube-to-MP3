#Requires -Version 5.1
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

$toolsSrc = Join-Path $repoRoot 'installer\prereq\tools\win-x64'
$toolsDest = Join-Path $publishDir 'tools'
if (Test-Path $toolsSrc) {
    Write-Host "Copying bundled tools to $toolsDest"
    if (Test-Path $toolsDest) { Remove-Item $toolsDest -Recurse -Force }
    Copy-Item $toolsSrc $toolsDest -Recurse -Force
}
else {
    Write-Warning "Bundled tools not found at $toolsSrc - run scripts\fetch-bundled-tools.ps1 first."
}

Write-Host "Published (installer build) to: $publishDir (Version $ver)"
& (Join-Path $repoRoot 'scripts\update-windows-shortcuts.ps1') -SourceDir $publishDir
