# Local Music Hub integration

YouTube Downloader pairs with **Local Music Hub** (v0.3+) so finished music downloads appear in your library automatically.

## How it works

1. **Same local API** — Music Hub uses the existing extension API (`/health`, `/check`, `/download`) with the same port and `X-Extension-Token`.
2. **Settings** — Hub reads `%LocalAppData%\YouTubeToMp3\settings.json` for your music output folder, port, and token.
3. **Auto-import** — Hub watches your music folder and polls `history.json` when you queue a download from Music Hub.

## Setup

1. Install and run **both** apps.
2. In YouTube Downloader **Settings → Local API**, keep the API **enabled**.
3. In Local Music Hub **Settings**, enable **Link with YouTube Downloader**.
4. In Music Hub sidebar, paste a YouTube URL and click **Download**.

## Status in YouTube Downloader

**Settings → Local API** shows Music Hub link status:

- Not detected — Hub not installed
- Installed — open Hub once to create settings
- Linked — Hub integration enabled
- Watching music folder — Hub library includes this app's music output path

## Health API

`GET /health` includes integration info:

```json
{
  "status": "ok",
  "app": "YouTube Downloader",
  "version": "1.9.2",
  "integrations": {
    "localMusicHub": {
      "detected": true,
      "linked": true,
      "watchingMusicFolder": true
    }
  }
}
```

## Token changes

If you regenerate the connection token in YouTube Downloader, update the browser extension options. Local Music Hub re-reads the token from `settings.json` on next launch.

### Import from YouTube Downloader

When a music download finishes, **Import to Music Hub** appears in the downloader (if Hub is linked). It writes `import-request.json` and notifies the running Hub, or launches Hub with `--import`.

## See also

- [browser-extension.md](browser-extension.md) — full API reference
- Local Music Hub: `docs/integration-youtube-downloader.md`
