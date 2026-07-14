## YouTube Downloader 1.9.8

### Distribution
- **Bundled yt-dlp and ffmpeg** in the installer — friends can download without winget or PATH setup.
- **GitHub Actions** builds `YouTubeToMp3-Setup-1.9.8.exe` on tag push (`v1.9.8`).
- Updated first-run notice: tools are included; Deno remains optional for YouTube Music edge cases.

### For maintainers
- Bump `Directory.Build.props`, edit this file, commit, then `git tag v1.9.8 && git push origin v1.9.8`.
