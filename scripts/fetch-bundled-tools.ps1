#Requires -Version 5.1
<#
.SYNOPSIS
  Downloads yt-dlp and ffmpeg into installer\prereq\tools\win-x64 for bundling in the installer.
#>
param([switch] $Force)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $repoRoot 'installer\prereq\tools\win-x64'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$ytPath = Join-Path $dest 'yt-dlp.exe'
$ffPath = Join-Path $dest 'ffmpeg.exe'
$fpPath = Join-Path $dest 'ffprobe.exe'

if (-not $Force -and (Test-Path $ytPath) -and (Test-Path $ffPath)) {
    Write-Host "Bundled tools already present in $dest"
    exit 0
}

Write-Host "Downloading yt-dlp..."
Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' `
    -OutFile $ytPath -UseBasicParsing

Write-Host "Downloading ffmpeg (BtbN win64 gpl)..."
$zip = Join-Path $env:TEMP "lmh-yt-ffmpeg-bundle.zip"
$extract = Join-Path $env:TEMP "lmh-yt-ffmpeg-extract"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
New-Item -ItemType Directory -Force -Path $extract | Out-Null

Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' `
    -OutFile $zip -UseBasicParsing
Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force

$ffmpegSrc = Get-ChildItem -Path $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
$ffprobeSrc = Get-ChildItem -Path $extract -Recurse -Filter 'ffprobe.exe' | Select-Object -First 1
if (-not $ffmpegSrc) { throw 'ffmpeg.exe not found in downloaded archive.' }

Copy-Item $ffmpegSrc.FullName $ffPath -Force
if ($ffprobeSrc) { Copy-Item $ffprobeSrc.FullName $fpPath -Force }

Remove-Item $zip -Force -ErrorAction SilentlyContinue
Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Bundled tools ready:"
Write-Host "  $ytPath"
Write-Host "  $ffPath"
