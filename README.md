# YouTube Downloader

Download music and videos from YouTube to your PC — pairs with **Local Music Hub** for playback and library management.

**License:** [Apache License 2.0](LICENSE)

**Current version: 1.9.8**

## Install (friends)

1. Download **`YouTubeToMp3-Setup-1.9.8.exe`** from [GitHub Releases](https://github.com/Litbolt123/YouTube-to-MP3/releases).
2. Run the installer — **yt-dlp and ffmpeg are bundled**; no winget step required.
3. Optional: `winget install DenoLand.Deno -e` for YouTube Music edge cases in 2026.
4. Optional: install **Local Music Hub** for a full local library player.

Full two-app setup: see Local Music Hub repo → `docs/music-stack-setup.md`.

## Features

- Single URLs, playlists, albums (YouTube Music)
- MP3 / M4A / FLAC / Opus / WAV; cover art embed
- Browser extension — see **[friend setup guide](docs/browser-extension-friend-guide.md)** (load unpacked from the installed app folder)
- History, parallel downloads, auto-import to Music Hub
- **Auto-update check** via GitHub Releases

## Build installer

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

This fetches bundled yt-dlp/ffmpeg, publishes a self-contained app, and compiles the Inno installer.

Output: `installer\Output\YouTubeToMp3-Setup-<version>.exe`

## Publish a release

See `docs/releasing.md`. Bump `Directory.Build.props`, edit `docs/RELEASE_BODY.md`, then push tag `v<Version>`.

## Data

Settings and history: `%LocalAppData%\YouTubeToMp3\`
