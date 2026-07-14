# Testing auto-updates

Use this when you want to confirm **older installed builds** detect and download **newer GitHub Releases**.

## Setup

| Role | YouTube Downloader | Local Music Hub |
|------|-------------------|-----------------|
| **Installed (old)** | 1.9.8 from [Releases](https://github.com/Litbolt123/YouTube-to-MP3/releases) | 0.10.0 from [Releases](https://github.com/Litbolt123/Local-Music-Hub/releases) |
| **Published (new)** | 1.9.9 tag → CI builds installer | 0.10.1 tag → CI builds installer |

**Important:** Do not run `dotnet build` or `build-installer.ps1` locally for the new version until after you test — that syncs the new build into `%LocalAppData%\Programs\` and you will no longer be on the old version.

## Test steps

### Automatic check (startup)

1. Confirm Settings → **Check for updates when the app starts** is on.
2. Fully quit the app (tray → Quit, not just close window if minimize-to-tray is on).
3. Relaunch from Start Menu.
4. Wait ~15 seconds.
5. Expect: update banner on main window and/or tray balloon (if tray is visible and notify is on).

### Manual check (Settings)

1. Open **Settings → Updates (GitHub)**.
2. Click **Check for updates…**
3. Expect: `Newer version available: 1.9.9` (or `0.10.1`).
4. Click **Download and run installer…**
5. Confirm quit prompt → installer should start.
6. Complete setup → app should open at the new version.

### Releases page fallback

**Open releases page** should open GitHub even if the direct `.exe` asset URL is missing.

## Publishing a test release (maintainer)

```powershell
# After bumping Directory.Build.props and docs/RELEASE_BODY.md:
git add -A
git commit -m "v1.9.9: improve update checks"
git push origin main
git tag v1.9.9
git push origin v1.9.9
```

Same pattern for Local Music Hub with `v0.10.1`.

Watch **Actions** on GitHub until the Windows installer workflow is green, then test.

## Troubleshooting

| Issue | Fix |
|-------|-----|
| Says up to date | Installed build is already >= release version; reinstall older setup exe |
| Check failed | Network or GitHub API rate limit; try again |
| Download too small | Use **Open releases page** and download manually |
| No tray balloon | Tray icon must be visible; enable **Show a tray notification** in Settings |
