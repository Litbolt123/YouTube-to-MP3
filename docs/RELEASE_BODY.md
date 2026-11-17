## YouTube Downloader 2.2.0

### Highlights
- **Compatible and Best video modes:** Compatible (the default) saves H.264 video and AAC audio in an MP4 so Movies and TV plays smoothly. Best keeps the highest VP9 or AV1 stream and saves an MKV. VP9 and AV1 are never put in an MP4.

- **Screen matching uses real pixels:** On high-DPI displays the quality cap follows the physical panel (for example a 2560x1600 screen picks 1440p), not the scaled size.

- **Copies streams when it can:** Video is stream-copied. Audio is copied unless the container cannot hold it (for example Opus going into MP4).

- **Clearer download report:** After each file, the app shows the real codec, resolution, and bitrate from ffprobe, and flags sources under 1000 kb/s. Whole-job progress is shown as a percent. Format diagnostic logging is back.

### Install
Download **YouTubeToMp3-Setup-2.2.0.exe** below, close the app, and run the installer over your current version. Settings in `%LocalAppData%\YouTubeToMp3` are preserved.

### Verify
1. Settings → About shows **2.2.0**.
2. Settings shows Compatible (H.264 MP4) and Best (VP9/AV1 in MKV).
3. Download a video in Compatible mode and confirm the saved file is H.264 in MP4.