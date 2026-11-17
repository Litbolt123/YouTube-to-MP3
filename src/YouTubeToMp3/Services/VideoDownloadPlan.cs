using System.Globalization;
using System.Text;

namespace YouTubeToMp3.Services;

public enum VideoQualityMode
{
    Compatible,
    Best,
}

public static class VideoQualityModes
{
    public static VideoQualityMode Parse(string? value) =>
        string.Equals(value, "best", StringComparison.OrdinalIgnoreCase)
            ? VideoQualityMode.Best
            : VideoQualityMode.Compatible;

    public static string ToTag(VideoQualityMode mode) =>
        mode == VideoQualityMode.Best ? "best" : "compatible";

    public static string Label(VideoQualityMode mode) =>
        mode == VideoQualityMode.Best ? "Best (VP9/AV1 MKV)" : "Compatible (H.264 MP4)";
}

public sealed record VideoDownloadPlan(
    VideoQualityMode Mode,
    string Container,
    string FormatSelector,
    string MergerArgs,
    string FallbackNote)
{
    public static VideoDownloadPlan Create(VideoQualityMode mode, int quality)
    {
        var height = QualityPresets.ResolveVideoHeightCap(quality);
        var container = mode == VideoQualityMode.Best ? "mkv" : "mp4";
        var audioCodec = mode == VideoQualityMode.Best ? null : "mp4a";
        var fallback = mode == VideoQualityMode.Best
            ? "VP9, then AV1, saved as MKV. A lower match is used when that height is missing. VP9/AV1 are never muxed into MP4."
            : "H.264 + m4a at the chosen height and up to 60fps, then H.264 without the fps cap, then a lower H.264. VP9/AV1 are never muxed into MP4.";
        return new VideoDownloadPlan(
            mode,
            container,
            BuildFormatSelector(mode, height),
            BuildMergerArgs(container, audioCodec),
            fallback);
    }

    public static string BuildFormatSelector(VideoQualityMode mode, int? heightCap)
    {
        var height = heightCap is int h
            ? "[height<=" + h.ToString(CultureInfo.InvariantCulture) + "]"
            : "";
        var heightFps = height + "[fps<=60]";

        if (mode == VideoQualityMode.Best)
        {
            return
                $"bestvideo{heightFps}[vcodec^=vp9]+bestaudio/" +
                $"bestvideo{heightFps}[vcodec^=vp09]+bestaudio/" +
                $"bestvideo{height}[vcodec^=vp9]+bestaudio/" +
                $"bestvideo{heightFps}[vcodec^=av01]+bestaudio/" +
                $"bestvideo{height}[vcodec^=av01]+bestaudio/" +
                $"bestvideo{heightFps}[vcodec!*=avc1]+bestaudio/" +
                $"bestvideo{height}+bestaudio";
        }

        return
            $"bestvideo{heightFps}[vcodec^=avc1]+bestaudio[ext=m4a]/" +
            $"bestvideo{heightFps}[vcodec^=avc1]+bestaudio[acodec^=mp4a]/" +
            $"bestvideo{height}[vcodec^=avc1]+bestaudio[ext=m4a]/" +
            $"bestvideo{height}[vcodec^=avc1]+bestaudio[acodec^=mp4a]/" +
            $"bestvideo{height}[vcodec^=h264]+bestaudio[ext=m4a]/" +
            $"bestvideo{height}[vcodec^=h264]+bestaudio[acodec^=mp4a]";
    }

    public static bool ShouldCopyAudio(string container, string? audioCodec)
    {
        if (container.Equals("mkv", StringComparison.OrdinalIgnoreCase) ||
            container.Equals("webm", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(audioCodec))
            return true;

        var codec = audioCodec.ToLowerInvariant();
        if (codec.Contains("opus", StringComparison.Ordinal) ||
            codec.Contains("vorbis", StringComparison.Ordinal))
            return false;

        return true;
    }

    public static string BuildMergerArgs(string container, string? audioCodec)
    {
        if (ShouldCopyAudio(container, audioCodec))
            return "-c:v copy -c:a copy -progress pipe:2";

        return "-c:v copy -c:a aac -b:a 192k -progress pipe:2";
    }

    public void AppendYtDlpArgs(StringBuilder sb)
    {
        sb.Append($"-f \"{FormatSelector}\" ");
        sb.Append($"--merge-output-format {Container} ");
        sb.Append($"--remux-video {Container} ");
        sb.Append($"--postprocessor-args \"Merger+ffmpeg_o:{MergerArgs}\" ");
    }
}
