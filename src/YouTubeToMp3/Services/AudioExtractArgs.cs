using System.Text;

namespace YouTubeToMp3.Services;

/// <summary>yt-dlp / ffmpeg settings for audio extraction and transcoding.</summary>
public static class AudioExtractArgs
{
    /// <summary>
    /// All transcodes prefer AAC/M4A — Opus→FLAC/MP3 via ffmpeg adds crackle on OST tracks.
    /// Choose Opus output format for a native no-transcode download (matches YouTube Music).
    /// </summary>
    public static string GetFormatSelector(DownloadFormat format) =>
        format switch
        {
            DownloadFormat.Opus => "bestaudio[acodec=opus]/bestaudio/best",
            _ => QualityPresets.BestAudioFormatSelector,
        };

    public static void Append(StringBuilder sb, DownloadFormat format, int audioQuality)
    {
        sb.Append($"-f {GetFormatSelector(format)} ");
        var quality = format is DownloadFormat.Flac or DownloadFormat.Wav ? 0 : audioQuality;
        sb.Append($"-x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} --audio-quality {quality} ");

        var postprocessor = BuildPostprocessorArgs(format, audioQuality);
        if (!string.IsNullOrEmpty(postprocessor))
            sb.Append($"--postprocessor-args \"{postprocessor}\" ");
    }

    private static string? BuildPostprocessorArgs(DownloadFormat format, int audioQuality)
    {
        if (!DownloadFormats.IsAudio(format))
            return null;

        // FLAC/WAV: yt-dlp defaults only — custom opus/s32 args reintroduced crackle.
        return format switch
        {
            DownloadFormat.Mp3 => BuildMp3ExtractArgs(audioQuality),
            _ => null,
        };
    }

    private static string BuildMp3ExtractArgs(int audioQuality)
    {
        var encode = audioQuality switch
        {
            0 => "-c:a libmp3lame -b:a 320k -compression_level 0",
            9 => "-c:a libmp3lame -q:a 9",
            _ => "-c:a libmp3lame -q:a 5",
        };

        return $"FFmpegExtractAudio:{encode}";
    }
}
