# Lessons

What worked and what did not. **Read this before repeating a fix.** After a fix or a failed approach, append a class (not a chat transcript): Symptom · Failed · Worked · Verify · Do not regress.

Vault index: `C:\Users\Augus\OneDrive\Documents\Obsidian Vault\Atlas\Lessons.md`


Also read `GitHub projects\docs\app-development-pipeline.md` section 10.

## 2026-08-24 — version string

**Failed:** Trusting README version (stale 1.9.8).

**Worked:** `Directory.Build.props` is **2.1.1**. Folder name is `YouTube to MP3`; product is YouTube Downloader.

## Extension wake via custom protocol (Chrome)

- **Symptom:** Extension says app not running; `youtubetomp3://` never launches desktop app.
- **Failed:** `chrome.tabs.create({ url: "youtubetomp3://wake" })` from MV3 service worker after `await` in download flow — Chrome blocks external protocol without user gesture.
- **Worked:** Synchronous hidden `<a href="youtubetomp3://wake">.click()` in the **content script** at the start of the button click handler (before any `await`). Register protocol in sync script to `%LocalAppData%\Programs\YouTubeToMp3\YouTubeToMp3.exe`.
- **Verify:** Quit app → YouTube Download → Audio → allow protocol prompt once → app starts in tray → download queues.
- **Do not regress:** Never wake only from background SW after async health check; always poke protocol synchronously on click.

## Extension wake — download not queued / window pops up

- **Symptom:** YouTube button wakes app but no download; main window flashes on screen instead of staying in tray.
- **Failed:** `ShowMainWindow()` on every extension download; `ActivateFromSecondInstance` called `ApplySettings` (restarts HTTP listener) on `extension-wake` signal while `/download` was in flight; API host started only in `OnLoaded`.
- **Worked:** Start `BrowserExtensionHost` in `MainWindow` constructor; skip listener restart when config unchanged; extension-wake signal does not restart API; extension downloads stay in tray with balloon notification; extension waits 700ms after wake before health probe; `/check` connection errors do not block `/download`.
- **Verify:** Quit app → YouTube Audio → app stays in tray, tray balloon "queued", download runs; no window flash.
- **Do not regress:** Do not call `ShowMainWindow` for normal extension single-track downloads.

## Tray restore after hide (WPF)

- **Symptom:** First tray "Open downloader" flashes a black window and disappears; second click works.
- **Failed:** `WindowState.Minimized` + `Hide()`; restoring from the WinForms NotifyIcon thread; calling `Hide()` synchronously in cancelled `Closing`.
- **Worked:** Create MainWindow in `OnStartup` (no StartupUri). Restore on the WPF dispatcher: ShowInTaskbar, Visible, WindowState.Normal, Show, Activate. Defer tray Hide with `BeginInvoke`. Keep WindowState.Normal while hidden.
- **Verify:** Quit → extension wakes to tray → Open downloader once → window stays visible and themed.
- **Do not regress:** Do not set Minimized when hiding to tray; do not Show/Hide WPF windows from the NotifyIcon thread.

## Extension Video click swallowed

- **Symptom:** YouTube Download → Video does not start a download while the app is in the tray.
- **Failed:** `MessageBox` owned by a hidden window; silent `return` with no balloon; `/download` racing a cold start.
- **Worked:** Never MessageBox when `silentSkip` or window is hidden; balloon + log on every extension outcome; retry `/download` on connection errors; keep the caller-chosen format (do not run smart-format after an extension request).
- **Verify:** Tray-only app + YouTube Video → tray balloon "queued" and MP4 job in queue/log.
- **Do not regress:** Do not prompt over a hidden MainWindow for extension jobs.

## YouTube 403 — tv_simply PO token (2026-09)

- **Symptom:** Log shows `tv_simply client ... require a GVS PO Token` then `HTTP Error 403: Forbidden` on download.
- **Failed:** Client list included `tv_simply` (and `mweb`) — YouTube now requires PO tokens yt-dlp cannot auto-generate for those clients.
- **Worked:** `YouTubeExtractorArgs` uses `android,android_vr,web_safari,...,-tv_simply,-mweb`. User has Deno + winget yt-dlp 2026.07.04 on PATH.
- **Verify:** Retry same video after rebuild; log should not mention tv_simply PO token warning.
- **Do not regress:** Do not add tv_simply or mweb back without PO token provider support.

## YouTube 403 — SABR / android_sdkless (2026-09)

- **Symptom:** `Some android client https formats have been skipped` (SABR experiment), then `HTTP Error 403` on format 140/251; MP4 job fell back to audio-only stream.
- **Failed:** Forcing `android,android_vr,tv,...` client list; bundled yt-dlp 2026.07.04.
- **Worked:** `default,-android_sdkless,-tv_simply,-mweb` + refresh bundled yt-dlp each build (2026.08.19+) + Deno `--remote-components ejs:github`.
- **Verify:** Same video `TdHULsdse1g` downloads as MP4 merge; Settings → About shows yt-dlp version; log header shows tool versions.
- **Do not regress:** Do not force android/tv clients ahead of yt-dlp default; always refresh yt-dlp in `fetch-bundled-tools.ps1`.

## Download failure diagnosis — structured logs

- **Symptom:** User reports 403 or vague failure; hard to reproduce from UI alone.
- **Worked:** `DIAG:` lines in `%LocalAppData%\YouTubeToMp3\logs\` — preflight (tools, player_client, format selector), full yt-dlp args, run summary (warnings + likely_cause/action). Grep `DIAG:` in log file.
- **Verify:** Failed download log contains `DIAG: --- preflight ---` and `DIAG: --- run summary ---`.
- **Do not regress:** Keep DIAG prefix stable for grep; log full command args on every job.

## UI scope label consistency

- **Symptom:** MP3 done panel said "(video)"; queue said "Single"; logs said "single track".
- **Worked:** `DownloadScopeLabels.For/ForLog/ForDonePanel/ForJob` used everywhere; format + content kind drive label.
- **Verify:** Download one MP3 → done text says "single track"; MP4 says "single video"; music playlist says "album".
- **Do not regress:** Do not hardcode "Video" / "Single" in completion or queue UI.

## Extension wake — white window still appears (playtest 2026-09-15)

- **Symptom:** After downloads started working, clicking YouTube Download still opened a blank white YouTube Downloader window instead of staying in the tray.
- **Failed (playtest):** Hiding after a normal `Show()` — WPF paints an unthemed white frame first. Treating a protocol launch as a user activate (`ShowMainWindow`) if `--extension-wake` was missing from older registry commands. `Hide()` without `Show()` skipped `Loaded`, so `activate.signal` was never watched.
- **Worked:** Protocol command is `"exe" --minimized --extension-wake "%1"` and any `youtubetomp3:` argument counts as wake. Cold start: Opacity 0 + off-screen `Show()` to fire `Loaded`, then `Hide()` with `ShowActivated=false`. Tray restore recenters and sets Opacity 1. Second instance with wake signal must not call `ShowMainWindow`.
- **Verify (August playtest 2026-09-15):** “That works now… All the issues I gave you are fixed.” Extension click stays in tray (no white window).
- **Do not regress:** Do not `Show()` a visible MainWindow on extension wake. Do not skip `Loaded` (FileSystemWatcher + API apply live there).

## MP4 "Video" saved audio-only .m4a

- **Symptom:** Extension **Video** produced an `.m4a` that was only audio.
- **Failed:** Format selector ended with `/best`. Splicing `AacAudio` (which contains `/`) after `bestvideo+` also split later branches into audio-only. `--check-formats` was added to skip 403 video URLs.
- **Worked:** Every `/` alternative is a full `bestvideo[height<=N][vcodec!*=av01]+bestaudio` pair (no audio-only branch). `--remux-video mp4`.
- **Verify (August playtest 2026-09-15):** Video downloads as real video (not `.m4a` audio). Same night after the format-400 hang fix.
- **Do not regress:** Never append `/best`. Never interpolate a `/`-separated audio chain into a video+audio selector.

## `--check-formats` hangs on "Testing format 400"

- **Symptom:** MP4 job sits forever on `[info] Testing format 400` (same video `TdHULsdse1g`, 1440p). Playtest 2026-09-15.
- **Failed:** `--check-formats` probes YouTube DASH (AV1 format 400) before download; SABR/CDN can stall with no timeout. Default `bestvideo` prefers AV1 over VP9.
- **Worked:** Drop `--check-formats`. `--socket-timeout 20`. Prefer `vcodec!*=av01` (VP9/H.264) before AV1.
- **Verify (August playtest 2026-09-15):** After dropping `--check-formats`, 1440p Video of `TdHULsdse1g` downloaded. “That works now! Just like I want it.”
- **Do not regress:** Do not re-add `--check-formats` without a socket timeout and an AV1-last selector.

## Video quality list + match screen (not 4K on 1080p)

- **Symptom:** No per-video resolution picker; default could fetch 4K on a 1080p display. YouTube button stuck on **Detecting this video…**.
- **Failed:** Waiting on `POST /formats` (`yt-dlp -J`) before filling the YouTube-page dropdown. Isolated content scripts cannot read `window.ytInitialPlayerResponse`.
- **Worked:** YouTube page reads `streamingData` from a MAIN-world `page-bridge.js`. Quality lives **in the open panel** (Video caption + dropdown). The red watch-page button stays **Download** / Queued / Downloading / **Downloaded ✓**.
- **Verify (August playtest 2026-09-16):** “Download 1080p60 ✓” on the closed button was too much — quality belongs after click. Checkmark only after a real save.
- **Do not regress:** Do not put resolution on the closed YouTube button. Do not show ✓ until the file is saved (or Settings timed-reset while the file still exists). Do not default quality `0` while match-screen is on. Do not block the YouTube UI on yt-dlp JSON.

## Extension button live states (Queued / Downloading / Downloaded)

- **Symptom:** YouTube Download button jumped back to "Download" after 2.5s, or sat on Detecting; after a finished job it did not say Downloaded; deleting the file did not restore Download.
- **Failed:** Local `textContent = "Queued"` then `setTimeout(..., 2500)` reset. Isolated-world quality probe. Second ffmpeg cover pass after mutagen `--embed-thumbnail` logged **Conversion failed!** even though the MP4 was fine.
- **Worked:** `POST /status` (video id) → queued / downloading+percent / downloaded (`File.Exists` on history path) / idle+downloadedBefore. Settings: keep Downloaded until the file is gone, or reset after 1–60s with a ✓ mark. Skip app cover re-embed for video when yt-dlp already embedded the thumbnail.
- **Verify (August playtest 2026-09-16):** Quality list in the **open panel** is correct (`1080p60 (matches your screen, highest)`). Closed button must **not** say `Download 1080p60`. After a save it should be **Downloaded ✓**.
- **Do not regress:** Do not reset the YouTube button on a timer unless that Settings mode is on. Do not put resolution on the closed button. Do not run a second ffmpeg cover pass on mutagen-stamped MP4. `/health` and `/status` must not count as idle-timeout activity.

## MV3 isolated world cannot read page `window` globals

- **Symptom:** Extension quality UI empty or stuck detecting on YouTube SPA navigations.
- **Failed:** `window.ytInitialPlayerResponse` from the content script (isolated world).
- **Worked:** Second content script `world: "MAIN"` posts slim `streamingData` via `postMessage`. Isolated script listens and fills the button/panel.
- **Verify:** Navigate watch → watch without full reload; quality label still updates.
- **Do not regress:** Do not assume content scripts share the page's `window` object.
