# Publishing a Windows release (YouTube Downloader)

## Version source

Edit **`Directory.Build.props`** at the repo root:

```xml
<Version>1.9.8</Version>
```

Tag must be **`v` + Version** (e.g. `v1.9.8`).

## Before you tag

1. Bump version in `Directory.Build.props`.
2. Edit **`docs/RELEASE_BODY.md`** with user-facing bullets (CI requires ≥80 chars).
3. Commit and push to `main`.
4. Tag and push:

```powershell
git tag v1.9.8
git push origin v1.9.8
```

## What CI does

Workflow: **`.github/workflows/release-windows.yml`**

On tag push:

1. Fetches bundled **yt-dlp** + **ffmpeg**
2. Publishes self-contained app + copies `tools\` and `browser-extension\`
3. Compiles Inno → `YouTubeToMp3-Setup-<version>.exe`
4. Creates GitHub Release with `docs/RELEASE_BODY.md` as description

Manual **workflow_dispatch** builds an artifact only (no Release).

## Local build

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Output: `installer\Output\YouTubeToMp3-Setup-<version>.exe`

## Installer asset name

Auto-update looks for assets named **`YouTubeToMp3-Setup-*.exe`**.

Repository: `Litbolt123/YouTube-to-MP3`
