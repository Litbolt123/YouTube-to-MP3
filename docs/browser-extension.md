# Browser extension

Send YouTube videos to the desktop app with a **Download** button on watch pages.

**Local Music Hub** (v0.3+) can also queue downloads through the same localhost API — see [integration-local-music-hub.md](integration-local-music-hub.md).

## One-time setup

1. **Rebuild / run the app** (v1.6.0+). Keep it open — clients talk to `http://127.0.0.1:47384` on your PC only.
2. Open **Settings → Local API** in the app.
3. Click **Copy** next to the connection token.
4. Click **Open extension folder** and note that folder path.
5. In **Chrome** or **Edge**:
   - Open `chrome://extensions` or `edge://extensions`
   - Turn on **Developer mode**
   - Click **Load unpacked**
   - Select the `browser-extension` folder
6. Click the extension icon → paste the token → **Save** → **Test connection**.

## Usage

On any YouTube watch page, click the red **Download** button (aligned next to Like/Dislike):

- **Audio** — quick download using your default audio format (MP3 by default).
- **Video** — quick MP4 download using your app’s default video quality.
- **Whole playlist** — when the URL has a `list=` playlist (not autoplay/radio mixes), extra **Audio** / **Video** buttons download the full playlist. **More options…** also includes a **This video only** / **Whole playlist** picker.
- **More options…** — choose format (MP3, M4A, Opus, FLAC, WAV, MP4), quality/resolution (including 480p–144p), and save-as (auto / music / video).

If the app already has this URL (history / file / queue), the extension asks **Download anyway?** — Yes forces a re-download; No skips.

The app queues the download using those choices (or your Settings defaults for quick Audio/Video).

## Local API (app side)

Used by the **browser extension** and **Local Music Hub**.

| Method | Path | Body | Notes |
|--------|------|------|--------|
| GET | `/health` | — | App status / version / `integrations.localMusicHub` |
| POST | `/check` | `{ "url" }` | Returns `{ ok, alreadyDownloaded, inQueue, inHistory, message, title?, path? }` |
| POST | `/download` | `{ url, scope?, format?, quality?, contentKind?, forceRedownload? }` | Queue a download; `forceRedownload: true` bypasses skip-already-downloaded |

All POST routes require header `X-Extension-Token`.

Music Hub typically sends `contentKind: "music"`, `format: "mp3"`, `scope: "single"`.

## Extension version

Current bridge: **1.4.5** (instant inline Download in action row; slight right spacing; see `docs/youtube-action-button-injection.md`).
## Security

- Only listens on `127.0.0.1` (not reachable from other devices).
- Requires the secret token from your app settings.
- Disable anytime in **Settings → Local API**.

## Local Music Hub

- Install **Local Music Hub** and enable **Link with YouTube Downloader** in its Settings.
- Queue downloads from Music Hub's sidebar (paste URL → Download).
- Finished files auto-import into your library via folder watch + `history.json`.

See [integration-local-music-hub.md](integration-local-music-hub.md).

## Troubleshooting

| Problem | Fix |
|---------|-----|
| "Could not reach the app" | Open the desktop app; check extension is enabled in Settings |
| "Invalid token" | Copy the token again from app Settings into extension options |
| Button missing on YouTube | Refresh the page; open `chrome://extensions` → **Reload** on the extension (v1.0.1+ fixes shadow DOM) |
| Port in use | Change the port in app Settings and extension options |
