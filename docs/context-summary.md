# Context summary

## Agent environment

- **`move_agent_to_root` does not work** here — never call it. Use absolute paths under `GitHub projects\` for YouTube Downloader or Local Music Hub.
- **Release builds** auto-sync to `%LocalAppData%\Programs\YouTubeToMp3\` and refresh Start Menu / Desktop shortcuts (`scripts\update-windows-shortcuts.ps1`).

## 2026-07-14 — Main screen tips + local rebuild (v1.9.9)

- **Main screen:** Labeled sections with hideable hint text; **Show tips** checkbox in header; Settings → Appearance **Show tips on the main screen** (`ShowMainScreenHints`, default on).
- **Local rebuild:** `scripts\build-installer.ps1` succeeded after closing a running instance (robocopy exit 11 = locked files). Installer: `installer\Output\YouTubeToMp3-Setup-1.9.9.exe` (152.4 MB). Installed copy synced to `%LocalAppData%\Programs\YouTubeToMp3\`.

## 2026-07-14 — Single-instance app (v1.9.7)

- Only one **YouTube Downloader** process can run; a second launch brings the existing window forward.
- **Version:** **1.9.7**

## 2026-07-14 — Import button fixes + album review (v1.9.6)

- **Import button:** relaxed Music Hub detection (`IsMusicHubAvailable`); music paths under `Youtube Music` folder always eligible.
- **Review album:** per-track downloads now show **Import album to Music Hub** when the album queue finishes (not only full-playlist scope).
- **Done panel:** action buttons wrap so Import is not clipped off-screen.
- **Version:** **1.9.6**

## 2026-07-14 — Album import to Music Hub (v1.9.5 / Hub v0.3.4)

- When a **music playlist/album** download finishes, done panel shows **Import album to Music Hub** (folder import).
- Single-track downloads still show **Import to Music Hub**.
- **Version:** **1.9.5**

## 2026-07-14 — Import to Music Hub from Downloader (v1.9.3 / Hub v0.3.2)

- **YouTube Downloader:** **Import to Music Hub** on download complete and in History (when Hub linked + music file).
- **IPC:** writes `%LocalAppData%\LocalMusicHub\import-request.json`; launches Hub with `--import` if not running.
- **Music Hub:** watches import-request file + `--import` CLI; imports track into library.

## 2026-07-13 — Local Music Hub pairing (v1.9.2)

- **Local API section** renamed in Settings; documents browser extension + Local Music Hub on same port/token.
- **`LocalMusicHubIntegration`** — detects Hub install, link status, whether music folder is watched.
- **`GET /health`** — adds `integrations.localMusicHub` (`detected`, `linked`, `watchingMusicFolder`).
- **Settings UI** — Music Hub status line, **Open Music Hub data** button, About mentions Hub pairing.
- **Docs:** `docs/integration-local-music-hub.md`; `browser-extension.md` updated.
- **Version:** **1.9.2**

## 2026-07-13 — Album artist tags on download (v1.9.1)

- **Problem:** Music album downloads embedded track artist only; featured artists caused split albums in players. Review album “Apply to all” overwrote per-track artist with album artist.
- **Fix:** Embed **Album Artist** (`album_artist` / `meta_album_artist`) separately from track artist. Music playlists default album artist from playlist uploader; per-track artist stays from YouTube metadata.
- **Review album:** Top “Album artist” → album-artist tag + folder; per-track Artist column → track artist only. Apply to all updates album name on tracks, not track artist.
- **Jobs:** Music playlist downloads pass `MetadataOverride` with album artist + album even without review.
- **Title cleaner:** Track named same as album (e.g. “One” on *One*) was stripped to blank — only remove album prefix when followed by ` - ` / `:`, not on exact title match.

## 2026-07-13 — v1.9.0 home UI reshape + album vs playlist prompts

- **Layout:** Main window split into priority bands — primary card (URL, folder, format options, cover preview), separate actions card (download buttons), compact status card, optional queue, optional log. Cover-art URL / choose-cover moved into a collapsed **Cover art options** expander under the preview.
- **Smart buttons:** `UpdateDownloadActionsUi()` shows/hides and relabels download actions from detection — single track → one button; music album → song + whole album + review; video/ambiguous playlist → video + whole playlist (no review).
- **Playlist kind prompt:** `PlaylistKindPromptWindow` asks **Music album** vs **Video playlist** when auto-detect is unsure; choice cached per playlist id via `ContentKindDetector.CachePlaylistKind`. Triggered on **Whole playlist/album** and extension playlist downloads. `PlaylistKindResolver` + title heuristics auto-resolve when confident (OST, music.youtube, etc.).
- **Log panel:** `ShowLogPanel` setting (default **off**); Settings checkbox **Show download log on home screen**; status-bar **Show log** / **Hide log** for temporary peek without persisting.
- **Installer:** `YouTubeToMp3-Setup-1.9.0.exe`

## 2026-07-09 — App QoL (quality, parallel, skip, extension check)

- **Lower video presets:** 480p / 360p / 240p / 144p (quality ints 10–13) in `QualityPresets`.
- **Concurrent fragments:** `AppSettings.ConcurrentFragments` (default 4) → yt-dlp `--concurrent-fragments` when > 1.
- **Parallel downloads:** `MaxParallelDownloads` (1–3); one `YtDlpDownloadService` per active job; FIFO queue fills free slots.
- **Skip already downloaded:** `SkipAlreadyDownloaded` (default true) auto-skips history/file duplicates; main checkbox; MessageBox when off; playlists use `--download-archive` at `%LocalAppData%\YouTubeToMp3\download-archive.txt`; singles append archive id on success.
- **Extension API:** POST `/check` + `/download` `forceRedownload`; `BrowserExtensionHost.CheckUrl` wired from MainWindow.
- **Other:** Queue “Open folder”; Ctrl+V YouTube paste; quality saved on combo change; Pause/Resume queue (active jobs continue).
- **No version bump / installer** in this pass; extension UI for `/check` + force is a follow-up.

## 2026-07-08 — v1.6.1 music filename fix (reliable)

- **Root cause:** yt-dlp optional-separator template still produced `C418OneCliffside Hinson`; matching preview skipped custom filename and fell back to broken template.
- **Fix:** `MusicFilenameBuilder` + `YtDlpMetadataService` build `Artist - Album - Title` in C# from yt-dlp metadata; preview and downloads use that name.

## 2026-07-08 — v1.1.4 extension injection regression fix

- **Issue:** v1.1.3 broke all injection — button never appeared.
- **Cause:** `cleanupOrphanedWrappers` used `anchor.contains(wrapper)` which fails across shadow DOM; removed button right after insert. `yt-page-data-updated` restarted inject burst constantly, cancelling scheduled retries.
- **Fix:** Only cleanup wrappers in wrong parent; burst only on video change; scope anchor to `ytd-watch-metadata`; keep watchdog + shadow observers.


- **Issue:** Download button missing when opening a video from YouTube home until manual reload.
- **Cause:** YouTube client-side navigation updates inside shadow DOM; page-level MutationObserver and 20s retry timer missed late-rendered action bar.
- **Fix:** Hook `pushState`/`replaceState`, `yt-navigate-finish`, `yt-page-data-updated`; inject burst retries up to 10s; 750ms watchdog on watch pages; observe shadow roots; clean orphaned wrappers on video change.


- **Extension v1.1.0:** Download button sits in the actions row after Like/Dislike (40px height, flush alignment). Click opens a popover with quick **Audio** / **Video** buttons and **More options** (format, quality, save-as music/video/auto).
- **App:** `/download` API accepts optional `format`, `quality`, `contentKind`; extension downloads use these overrides instead of always using app defaults.
- **Files:** `content.js`, `content.css`, `background.js`, `BrowserExtensionHost.cs`, `MainWindow.xaml.cs`.


- **App:** `BrowserExtensionHost` listens on `127.0.0.1` (default port 47384) with token auth; extension POSTs YouTube URLs to queue downloads.
- **Extension:** `browser-extension/` — MV3 content script adds red Download button on YouTube; options page for port/token.
- **Settings:** Browser extension section (enable, port, token, open folder, test connection).

## 2026-07-08 — v1.5.2 music filename separators

- **Fix:** Music template used invalid `%(field& - )s` syntax; yt-dlp needs `%(field&{} - |)s` so artist/album render as `C418 - One - Cliffside Hinson` instead of `C418OneCliffside Hinson`.

## 2026-07-08 — v1.5.1 MP4 audio compatibility (WMP)

- **Issue:** Merged MP4s used Opus audio; Windows Media Player cannot play Opus in MP4.
- **Fix:** Prefer M4A/AAC audio streams in format selector; ffmpeg merger re-encodes to AAC when needed.

## 2026-07-08 — v1.5.0 audio format options

- **Formats:** MP3 (default), M4A, Opus, FLAC, WAV, plus MP4 video. Shared `DownloadFormats` helper drives UI combos, yt-dlp `--audio-format`, file extensions, and settings `PreferredFormat` tag.

## 2026-07-08 — v1.4.8 YouTube client fix (MP4 quality)

- **Root cause (confirmed via log):** `mweb` client skipped all HTTPS/DASH formats without a GVS PO token → yt-dlp fell back to format **18** (~360p). App also excluded `android_vr`, the client that does not require a PO token.
- **Fix:** New `YouTubeExtractorArgs` — MP4 uses `android_vr,tv_simply,tv,web_safari,default` instead of `default,mweb,-android_vr`.

## 2026-07-08 — v1.4.7 MP4 quality fix

- **Root cause:** MP4 format selectors required `[ext=mp4]` on video streams. YouTube serves 1080p+ as VP9/AV1 in webm, so yt-dlp fell back to ~720p H.264/mp4.
- **Fix:** `QualityPresets.GetMp4FormatSelector` now caps by height only (`bestvideo[height<=N]+bestaudio`); existing `--merge-output-format mp4` remuxes via ffmpeg.

## 2026-07-08 — v1.1.0 UI updates

- **Playlist vs single:** Two buttons — **This video only** (`--no-playlist`) and **Whole playlist**. URLs with `&list=` were downloading the full album before because yt-dlp defaults to playlist mode.
- **MP4:** Format combo (MP3 / MP4). Quality applies to MP3 only; MP4 uses best available merge.
- **Logs:** Each download writes to `%LocalAppData%\YouTubeToMp3\logs\download-YYYYMMDD-HHmmss.log`. **Open logs folder** button on main window.
- **Theme:** Accent color changed from blue to red (`#D32F2F`).

## 2026-07-08 — v1.4.1 fixes

- **Dark theme:** Applied app-wide (main window, history, settings) via `DynamicResource`; text boxes and lists styled for dark mode.
- **Preview bug:** "Loading preview…" is never used as a filename; waits for real preview or uses default yt-dlp naming.
- **Format → folder:** Switching MP3/MP4 updates save path (MP3 → audio/Music path, MP4 → video/Videos path) when Content is Auto.
- **Custom paths:** Settings → Download paths — editable full paths for MP3 and MP4 downloads.

## 2026-07-08 — v1.4.0 major QoL

- **Queue tweak:** Starting a download while another runs always adds to the queue (no extra checkbox needed). Download buttons stay enabled during active downloads.
- **Download history:** `History` button — past downloads with open file/folder and re-download. Stored in `%LocalAppData%\YouTubeToMp3\history.json`.
- **Filename preview:** Shows predicted name via yt-dlp; editable before download (single URL).
- **Smart format:** Content **Auto** picks MP3 for music URLs, MP4 for video URLs (until you change format manually).
- **Batch URLs:** Paste multiple URLs (one per line) to queue them all.
- **Queue reorder:** Drag-and-drop plus Move up/down.
- **Duplicate detection:** Warns if URL is queued, in history, or file already exists.
- **ETA:** Status bar shows yt-dlp ETA during download.
- **Dark mode:** Settings → Appearance → Use dark theme.
- **Per-content folders:** Settings → separate default Music and Videos save folders.
- **Tray mode:** Minimize to tray on close; optional balloon when queue finishes.
- **Settings backup:** Export/import settings JSON in Settings → Backup.

## 2026-07-08 — v1.3.1 filename fix

- **Video filenames:** Regular videos (non-music) now use `Title.ext` instead of the music Artist/Album template. Fixes garbled names like `uploader｜channelplaylist_title& - …` when downloading MP3 audio from a video.
- **Music filenames:** Corrected yt-dlp template syntax (`%(field& - )s` optional segments) so missing artist/album fields are omitted cleanly.

## 2026-07-08 — v1.3.0 QoL

- **Done notification:** Status shows **Done!** with the full saved path, optional completion sound (Settings), and buttons to open the file or its folder.
- **Download queue:** Queue multiple downloads; auto-queue when busy (Settings) or check **Add to queue** to always queue. Queue panel shows waiting items with remove/clear.
- **Music vs video:** Content type picker (Auto / Music / Video). Auto detects YouTube Music albums (`OLAK5uy_`) as music. Separate **Music** and **Videos** subfolders (Settings, on by default).


- **Filenames:** Artist / album / title via yt-dlp templates (`OutputTemplateBuilder`). Single: `Artist - Album - Title`; playlist: `Artist/Album/01 - Artist - Title`. `--embed-metadata` for tags in the file.
- **403:** YouTube client workaround + Deno detection.
- **MP4:** 4K / 1440p quality presets added.


- **Goal:** Local YouTube → MP3 downloader with UI matching other Maple Bear GitHub apps (WPF + `DashboardTheme.xaml` palette from What Am I Doing).
- **Stack:** .NET 8 WPF, yt-dlp + ffmpeg on PATH (user installs once via winget).
- **Project:** `c:\Users\Augus\Desktop\Maple Bear Addon\GitHub projects\YouTube to MP3`
- **Deliverables:**
  - `YouTubeToMp3.exe` via `scripts\publish.ps1` (portable single-file)
  - `YouTubeToMp3-Setup-{version}.exe` via `scripts\build-installer.ps1` (Inno Setup, reinstall/update over existing install)
  - In-app update check (GitHub Releases) in Settings — same pattern as What Am I Doing
- **Note:** Workspace `move_agent_to_root` was skipped (hangs); built using absolute paths.

## 2026-07-08 — Extension v1.2.1 placement fix

- **Bug:** Download button injected with `hasBtn: true, float: false` but misaligned — floated above Like/Dislike, overlapping channel area on Comet/kevlar YouTube UI.
- **Root cause:** `findMountPoint()` used `#actions-inner.firstElementChild` fallback (channel row, not button row) and `findRowMount()` could walk up to column-layout containers.
- **Fix (content.js):** Removed bad fallback; `findInlineMount()` only uses immediate parent (never `#actions-inner`); post-insert `isPlacementAligned()` check auto-switches to float; `ensureFloatingPlacement()` positions left of Like/Share via `getPlacementAnchor()` + `getBoundingClientRect()`.
- **Version:** Extension bumped to **1.2.1** in `manifest.json`.
- **Retest:** Reload extension → refresh YouTube watch page → debug: `{ hasBtn: !!document.getElementById("youtubetomp3-download-btn"), float: !!document.querySelector("[data-ytmp3-float]") }` — expect button left of Like, `float: true` if inline alignment fails.

## 2026-07-08 — Extension v1.2.2 segmented-pill fix

- **Bug:** Download button appeared **between** Like and Dislike inside the segmented pill (not left of the whole group).
- **Cause:** `findAriaAction` matched the inner Like toggle; `insertBefore` ran inside `ytd-segmented-like-dislike-button-renderer` instead of its parent row.
- **Fix:** `findSegmentedLikeHost()` / `normalizeActionAnchor()` walk up (including shadow hosts); always mount before the full segmented renderer; `isPlacementAligned()` rejects placement inside the pill bounds.
- **Version:** Extension **1.2.2**.

## 2026-07-08 — Extension v1.2.3 disappearance fix

- **Bug:** Download button vanished after v1.2.2 — strict alignment check called `wrapper.remove()` before float fallback; `#actions` parent was blocked for inline mount.
- **Fix:** `resolveActionRow()` mounts into `#actions` row (only blocks `#actions-inner` column); relaxed `isInsideLikePill()` check; no DOM removal before float; float fallback in `tryInject`; improved segmented host discovery via position matching.
- **Version:** Extension **1.2.3**.

## 2026-07-08 — Extension v1.2.4 service worker + rebrand

- **Service worker:** MV3 workers go idle after ~30s (normal). Added `chrome.alarms` keepalive + `api.js` direct localhost fetch fallback when the worker is asleep. Options page notes that "inactive" in `chrome://extensions` is expected.
- **Rebrand:** User-facing name unified to **YouTube Downloader** (tray, installer title, extension UI, health API). Internal exe/folder `YouTubeToMp3` unchanged for compatibility.
- **Version:** Extension **1.2.4**.

## 2026-07-08 — Extension v1.2.5 placement stability

- **Bug:** Download button jumped between inline/float, covered Like, or appeared on the video player (wrong anchor in player chrome).
- **Fix:** Anchors scoped to `ytd-watch-metadata` only (exclude `#movie_player`); inline mount only into `#actions` before full segmented like/dislike; pinned float anchor; removed rAF mode flipping; `inline-flex` wrapper instead of `display:contents`.
- **Version:** Extension **1.2.5**.

## 2026-07-08 — Extension v1.2.7 new YouTube UI (WORKING)

- **Root cause:** YouTube replaced `ytd-segmented-like-dislike-button-renderer` with `segmented-like-dislike-button-view-model` inside `#top-level-buttons-computed`. Old selectors found zero anchors → `hasBtn: false`.
- **Fix:** Target both pill hosts; mount before like/dislike in `#top-level-buttons-computed`; inline flex (scrolls with row); float only as fallback with pinned anchor.
- **Verified:** User confirmed Download left of Like/Dislike on Comet. VidIQ `Remix` button sits nearby in same action area — no conflict observed.
- **Version:** Extension **1.2.7**.

## 2026-07-08 — Extension v1.3.0 playlist downloads

- **Feature:** When watch URL has `list=` (excluding radio `RD…` mixes), popover shows **Whole playlist** Audio/Video quick buttons and a scope picker in More options (`single` vs `playlist`). Uses existing app `BrowserExtensionHost` playlist scope.
- **Version:** Extension **1.3.0**.

## 2026-07-08 — App window icon matches tray

- **Request:** Use the same red download-arrow tray icon on open app windows (title bar + taskbar).
- **Change:** `TrayIconAssets.CreateWindowIcon()` converts the programmatic tray bitmap to a cached WPF `BitmapSource`; `DashboardUi.EnsureTheme` / `ApplyTheme` set `Window.Icon` on Main, Settings, and History windows.
- **Note:** Download button ~1s load on YouTube is expected (waits for action row DOM); user confirmed it works.

## 2026-07-08 — Queue window UI

- **Feature:** New **Download Queue** window (toolbar **Queue** button, tray **View queue**, or **Open queue window** on main queue card).
- **Details shown:** Active job with live % / ETA / speed / playlist item X of Y; waiting jobs with title, type, format, quality, scope, folder; session summary (active, waiting, completed).
- **Actions:** Move up/down, remove, clear queue, cancel current download. Updates live while open.

## 2026-07-08 — Music playlist single artist folder

- **Issue:** Album playlists with featured artists (e.g. C418 *One*) saved to multiple artist folders (`C418`, `C418, Disco`, `C418, Laura Shigihara`, …) because per-track `artist` metadata was used for the folder path.
- **Fix:** Before a music playlist download, resolve playlist-level artist/album once (`playlist_uploader` / channel, not per-track artist). All tracks go to `{Music}/C418/One/01 - Title.mp3`. Fallback template also prefers playlist uploader over track artist.

## 2026-07-08 — Installer version pipeline fix

- **Issue:** Re-running `build-installer.ps1` kept producing the same version (1.7.0) because version only changes when `Directory.Build.props` is bumped; compiling `.iss` in Inno IDE alone defaulted to 1.0.0.
- **Fix:** Version bumped to **1.8.0**; `get-version.ps1` reads `Directory.Build.props` directly; build script writes `installer/version.inc`, validates output filename, shows only the new installer; added `bump-version.ps1` and `-BumpPatch` on `build-installer.ps1`.

## 2026-07-08 — App icon still default (TODO next session)

- **Reported:** Custom red download-arrow icon shows in tray (and `Window.Icon` when open), but installed app / taskbar / shortcut still uses default Windows icon.
- **Cause:** No `app.ico` on disk — `YouTubeToMp3.csproj` only sets `<ApplicationIcon>` when `app.ico` exists; programmatic `TrayIconAssets` icon is not embedded in the EXE.
- **Next fix:** Generate `src/YouTubeToMp3/app.ico` from tray artwork (multi-size .ico), ensure csproj + installer `SetupIconFile` / `UninstallDisplayIcon` use it; rebuild installer.

## 2026-07-08 — Whole-queue ETA (TODO next session)

- **Request:** Show ETA for the **entire queue** finishing, not only the current download/file.
- **Current:** Queue window + status bar show per-item ETA from yt-dlp (`current file ETA`).
- **Next fix:** Track per-job durations (or rolling average), sum remaining time for active item + waiting jobs (+ playlist item counts where known); display e.g. `Queue ETA ~12:40` in Queue window summary and optionally main status bar.

## 2026-07-08 — “Now downloading” stuck on first track (TODO next session)

- **Reported:** Starting from a single-song playlist URL (e.g. Cliffside Hension from C418 *One*) and downloading the **whole playlist**, the Queue window “Now downloading” title does not update as each song progresses — stays on the first track name.
- **Cause:** Active title comes from `DownloadJob.PredictedTitle` at job start; `QueueRuntimeState` tracks item X/Y and per-file ETA but not the **current track title** from yt-dlp as playlist items advance.
- **Next fix:** Parse yt-dlp output for per-item title/destination (e.g. on `Downloading item N of M` or `Destination:` lines), expose `CurrentTrackTitle` in runtime state, refresh Queue window + status bar each item.

## 2026-07-09 — v1.8.1: icon, queue ETA, live track, Start with Windows

- **App icon:** Generated `src/YouTubeToMp3/app.ico` (red download arrow); wired as `ApplicationIcon` + WPF resource; Inno `SetupIconFile` set. Tray/window icons prefer embedded `app.ico`.
- **Live “Now downloading”:** Parse yt-dlp destination / titled playlist lines into `CurrentTrackTitle`; Queue window + status bar update per track.
- **Whole-queue ETA:** Rolling averages of completed job/item durations + current file ETA + remaining playlist items → `queue ETA ~m:ss` in queue summary and main status.
- **Start with Windows:** Tray menu toggle + Settings checkbox; HKCU Run with `--minimized` (starts in tray). Enabling also turns on minimize-to-tray.
- **Version:** **1.8.1** (`Directory.Build.props`).

## 2026-07-09 — Default save subfolders renamed

- **Change:** Default content subfolders are now **Youtube Music** and **Youtube Videos** (was `Music` / `Videos`).
- **Migration:** On settings load, exact legacy defaults `Music` / `Videos` are rewritten to the new names. Custom subfolder names are left alone.

## 2026-07-09 — v1.8.2 QoL batch

- **Faster Download button:** Extension injects earlier (`document_end`, early float near metadata, denser poll/burst) so scrolling to comments is not required; snaps inline when Like row appears.
- **Lower qualities:** App + extension presets for 480p / 360p / 240p / 144p.
- **Speed:** `--concurrent-fragments` (Settings, default 4).
- **Parallel downloads:** Settings **Download up to N at once** (1–3).
- **Skip already downloaded:** Default on; checkbox on main window; MessageBox / extension **Download anyway?** sets `forceRedownload`. Playlists use yt-dlp download archive. Extension POST `/check` + force on `/download`.
- **Other:** Queue pause/resume, Open folder, Ctrl+V paste URL, quality remembered per audio/video.
- **Versions:** App **1.8.2**, extension **1.4.0**.

## 2026-07-09 — v1.8.3 bugfixes

- **Content → Format:** Choosing Music/Video on the main window now immediately switches format + quality options (MP3 vs MP4) even with an empty URL. Auto still uses URL detection.
- **Extension button scroll jump:** Floating Download button no longer re-picks anchors on every scroll; pins a stable anchor and upgrades float → inline when the Like row appears. Extension **1.4.1**.

## 2026-07-09 — Extension 1.4.2 inline-only button

- **Issue:** Button still started over the channel area, then jumped to Like, then drifted on scroll (fixed positioning).
- **Fix:** Removed float placement entirely. Download button only mounts **inline** left of Like/Dislike in `#top-level-buttons-computed`, so it scrolls with the page. Dense poll until the row exists (may take ~1s, no wrong starting position).

## 2026-07-09 — Extension 1.4.3 earlier inline mount

- **Issue:** After inline-only fix, button waited until comments/engagement hydrated (Like visibility gates).
- **Fix:** Mount into `#top-level-buttons-computed` as soon as it exists in the DOM (even 0×0), prepend until Like appears, then `insertBefore(like)`. Still no fixed float. Extension **1.4.3**.

## 2026-07-09 — Extension 1.4.4 button missing fix

- **Issue:** 1.4.3 never showed the button — mount required `#top-level-buttons-computed` / direct-child Like, which often isn’t ready (or Like isn’t a direct child).
- **Fix:** Broader early hosts (`#menu`, `#flexible-item-buttons`, Share parent); `directChildContaining` for correct `insertBefore`; append fallback; stop removing on overlap checks. Extension **1.4.4**.

## 2026-07-09 — Extension 1.4.5 spacing + write note

- **Spacing:** Download wrapper margin nudged right (`0 4px 0 6px`).
- **Docs:** `docs/youtube-action-button-injection.md` — how we got instant inline mount without scroll jump (for future apps).

## 2026-07-13 — Embed thumbnail persistence fix

- **Issue:** “Embed thumbnail as cover art” on the main window did not persist across sessions unless the user also changed format/quality.
- **Fix:** Wired `EmbedThumbnailBox` `Checked`/`Unchecked` to `EmbedThumbnailBox_OnChanged`, which saves `App.Settings.EmbedThumbnail` immediately (same pattern as skip-already-downloaded). Added `SetEmbedThumbnailCheckbox` guard so loading settings does not trigger a spurious save. Settings Save also refreshes the main-window checkbox.

## 2026-07-13 — Queue UI, cover art, UTF-8 titles, parallel (v1.8.4)

- **Windows Search / Start Menu shortcut:** Not changed by these code fixes. Shortcut/icon updates only land when you run the installer again.
- **Queue ETA:** Live countdown between yt-dlp updates (`QueueEtaCountdown` + `LiveFileEta`). Metrics split into separate boxes (Status / Queue ETA / File ETA / Speed) so the ETA no longer jumps horizontally next to changing download text.
- **Main status:** Same idea — track / file ETA / queue ETA in separate metric boxes.
- **Special characters in queue titles:** Forced UTF-8 for yt-dlp/Python (`PYTHONUTF8` / `PYTHONIOENCODING`) so titles like “Équinoxe” don’t show as `?` in the queue viewer.
- **Cover art:** “Choose cover art…” on the main window + Settings **Always ask to choose cover art before downloading**. Pick a local image or fetch a YouTube video’s thumbnail; that one image applies to the whole job (including playlists) via ffmpeg after download.
- **Parallel downloads:** Already existed; raised to **1–5** and clarified that each slot runs download + conversion/embed together.

## 2026-07-13 — Live cover art preview (v1.8.4)

- **Before download:** Cover thumbnail appears beside filename preview when a URL is pasted (playlist or single).
- **During download:** Cover shows in the status panel and queue window next to the current track name.
- **Custom cover:** If you picked one image for the whole album, that stays visible for every track.
- **Per-track:** Otherwise each playlist item’s YouTube thumbnail updates as yt-dlp moves to the next video.

## 2026-07-13 — Embed thumbnail persistence + cover centering fix

- **Root cause:** `SelectFormat()` on startup/settings-close called `PersistCurrentChoices()`, which read the embed checkbox before it was synced from settings — overwriting the saved value on disk.
- **Fix:** `ApplySettingsToMainUi()` runs before `SelectFormat()`; embed thumbnail removed from `PersistCurrentChoices` (only its own checkbox handler saves it).
- **Cover preview:** `Stretch="Uniform"` + centered so album art isn’t cropped to the left.

## 2026-07-13 — Album track review + metadata overrides (v1.8.5)

- **Album detector:** `YtDlpMetadataService.GetAlbumReviewAsync` reads playlist entries and enriches each track with artist/album/title from yt-dlp.
- **Review album window:** Grid to edit title, artist, album per track; uncheck rows to skip; bulk “Apply to all” for album artist/name; re-detect button.
- **Manual overrides:** Edited fields are written into file tags via `--replace-in-metadata` on per-track downloads.
- **Smart download path:** No edits → single fast playlist job; any edits or skipped tracks → per-track jobs with fixed artist/album folders.
- **UI:** “Review album…” button on main window; Settings **Review album track details before downloading music playlists** (on by default).
- **Queue display:** Per-track album jobs keep the album name stable and show “Track N of M” from job metadata.

## 2026-07-13 — Editable filenames + UTF-8 album titles (v1.8.5)

- **Editable filename column** in album review — change the saved filename per track (without extension); custom names trigger per-track download jobs.
- **Title cleanup:** Strips album prefixes like `Minecraft Volume Alpha - 3 - …` so titles/filenames show just the song name.
- **UTF-8 fix:** Per-track metadata now fetched via individual video URLs with raw UTF-8 stdout decoding; flat-playlist garbled titles no longer stick when enrichment succeeds.
- **Album column:** Filters yt-dlp `+` / `NA` placeholders.

## 2026-07-13 — Review album UI sync + tag fix + scroll (v1.8.6)

- **Queue / main status:** Album review now writes an updated `CollectionTitle` from the edited album artist + album name (e.g. `ConcernedApe - Stardew Valley OST`) so downloads/queue show the reviewed name, not the YouTube uploader.
- **Tags bug:** `--replace-in-metadata` used invalid syntax, so Apply to all artist/album never embedded. Now uses yt-dlp `--parse-metadata` literals into `artist`/`meta_artist` and `album`/`meta_album`. Per-track review jobs always force those tags.
- **Download confirm:** Header artist/album always applied to every included track on “Download album”.
- **Main window:** Content below the title bar is in a `ScrollViewer` so smaller screens can scroll.
- **Cover reset:** Pasting/changing to a different video or playlist URL clears the previous custom cover selection so album→single song covers don’t stick.

## 2026-07-13 — Missing playlist tracks + music button labels (v1.8.7)

- **Missing tracks:** Flat playlist read now uses `--ignore-errors`, falls back to line order when `playlist_index` is blank, and dedupes by video id. Downloads also pass `--ignore-errors` so one bad item doesn’t stop the rest.
- **Duplicate skip fix:** Album per-track jobs now check the real output path (`Artist/Album/filename`) instead of the main-window preview path; history skips only when the saved file still exists (by video id).
- **Skipped track logging:** Album review logs which track titles were skipped as already downloaded.
- **QoL:** Music mode shows **This song only** / **Whole album** (Auto uses detected music URLs too).

## 2026-07-13 — Bulk album re-download prompt (v1.8.8)

- **Album duplicate prompt:** Review-album downloads show one Yes/No/Cancel dialog for the whole album instead of per-track “download anyway?” boxes.
  - **Yes** = re-download every track
  - **No** = skip already-downloaded tracks, download only new ones
  - **Cancel** = abort

## 2026-07-13 — youtu.be + playlist music detect + cover preview (v1.8.9)

- **youtu.be links:** `TryGetVideoId` now parses `youtu.be/VIDEO_ID` (and `/shorts/`, `/embed/`) so thumbnails, previews, and IDs work on short links.
- **Auto music detection:** URLs with both a video and playlist id (`youtu.be/VID?list=PL…`) auto-detect as **Music** (album track links). Playlist-only URLs probe the playlist title for OST/soundtrack/album keywords (e.g. Stardew Valley OST).
- **Cover preview:** Loads in parallel with filename preview; expands short URLs; for detected music playlists tries playlist artwork first, then the linked track thumbnail.


## 2026-07-14 - Publish v2.0.0

- Version 2.0.0 in Directory.Build.props and installer/version.inc; RELEASE_BODY.md for CI.
