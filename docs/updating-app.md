# Updating YouTube Downloader

Settings live in `%LocalAppData%\YouTubeToMp3\`. Installing a newer setup **over** the old one keeps your settings.

## Build a new installer (developers)

1. **Bump the version** in `Directory.Build.props` (or run `.\scripts\bump-version.ps1` for +0.0.1).
2. Run **`.\scripts\build-installer.ps1`** from the repo root (or double-click `scripts\Build-Installer.bat`).
3. Output: `installer\Output\YouTubeToMp3-Setup-x.y.z.exe`

Do **not** compile `installer\YouTubeToMp3.iss` directly in Inno Setup without running the build script — the version comes from `Directory.Build.props`.

Shortcut: `.\scripts\build-installer.ps1 -BumpPatch` bumps patch and builds in one step.

## Update steps (users)

1. Download **`YouTubeToMp3-Setup-x.y.z.exe`** from GitHub Releases (or use **Settings → Check for updates…**).
2. Close the app.
3. Run the installer. If you already accepted the current terms version, the wizard skips the legal pages on update.
4. Re-open the app.

## Portable EXE (no installer)

`scripts\publish.ps1` builds a single self-contained EXE at:

`src\YouTubeToMp3\bin\Publish\win-x64\YouTubeToMp3.exe`

You can run that file directly; it does not register an uninstall entry.

## yt-dlp / ffmpeg

The app shell is updated by the installer. **yt-dlp** and **ffmpeg** are separate tools on your PATH — update them with:

```cmd
winget upgrade yt-dlp.yt-dlp
```
