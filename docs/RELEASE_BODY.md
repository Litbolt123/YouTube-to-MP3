## YouTube Downloader 2.1.0

### Highlights
- **Cleaner audio:** Prefer AAC sources for MP3/FLAC to reduce crackle; FLAC cover embed uses audio-only copy (no remux glitches).
- **Reliable FLAC downloads:** Stage files under LocalAppData before moving to your music folder — fixes Access denied errors when Music Hub is watching the library.
- **Playlist cover art:** Whole-playlist jobs now embed covers even when some tracks fail (403); partial success still commits and embeds all finished files.
- **Download history:** Grouped album view and improved history browsing.

### Install
Download **YouTubeToMp3-Setup-2.1.0.exe** below. Settings in `%LocalAppData%\YouTubeToMp3` are preserved.

### Verify
1. Settings → About shows **2.1.0**.
2. Download a FLAC track with embed cover enabled — file should land in your library with artwork.
3. Play the file in Local Music Hub — no bass/treble crackle vs browser playback.
