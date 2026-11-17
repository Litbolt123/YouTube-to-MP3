using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace YouTubeToMp3.Services;

public sealed record MediaProbeResult(
    int? Width,
    int? Height,
    double? Fps,
    string? VideoCodec,
    string? AudioCodec,
    string Container,
    long? BitrateBitsPerSecond,
    string? LowSourceNote,
    string? FallbackNote)
{
    public string Describe()
    {
        var parts = new List<string>();
        if (Width is > 0 && Height is > 0)
            parts.Add($"{Width}x{Height}");
        if (Fps is > 0)
            parts.Add(FormatFps(Fps.Value) + "fps");
        if (!string.IsNullOrWhiteSpace(VideoCodec))
            parts.Add(VideoCodec);
        if (!string.IsNullOrWhiteSpace(AudioCodec) && string.IsNullOrWhiteSpace(VideoCodec))
            parts.Add(AudioCodec);
        parts.Add(Container);
        if (BitrateBitsPerSecond is > 0)
            parts.Add(FormatBitrate(BitrateBitsPerSecond.Value));
        return string.Join(' ', parts);
    }

    public string Report()
    {
        var text = Describe();
        if (!string.IsNullOrWhiteSpace(FallbackNote))
            text += Environment.NewLine + FallbackNote;
        if (!string.IsNullOrWhiteSpace(LowSourceNote))
            text += Environment.NewLine + LowSourceNote;
        return text;
    }

    public static MediaProbeResult? ParseFfprobeJson(string json, string? filePath, VideoQualityMode mode, int? requestedHeight)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int? width = null;
        int? height = null;
        double? fps = null;
        string? videoCodec = null;
        string? audioCodec = null;
        long? videoBits = null;
        long? audioBits = null;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = ReadString(stream, "codec_type");
                if (string.Equals(type, "video", StringComparison.OrdinalIgnoreCase) && videoCodec is null)
                {
                    videoCodec = ReadString(stream, "codec_name");
                    width = ReadInt(stream, "width");
                    height = ReadInt(stream, "height");
                    fps = ParseFrameRate(ReadString(stream, "avg_frame_rate"))
                          ?? ParseFrameRate(ReadString(stream, "r_frame_rate"));
                    videoBits = ReadLong(stream, "bit_rate");
                }
                else if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase) && audioCodec is null)
                {
                    audioCodec = ReadString(stream, "codec_name");
                    audioBits = ReadLong(stream, "bit_rate");
                }
            }
        }

        long? formatBits = null;
        string? formatName = null;
        if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
        {
            formatName = ReadString(format, "format_name");
            formatBits = ReadLong(format, "bit_rate");
        }

        var container = ContainerFrom(formatName, filePath);
        var bits = videoBits is > 0 ? videoBits : formatBits is > 0 ? formatBits : audioBits;
        var low = DescribeLowSourceBitrate(bits);
        var fallback = DescribeFallback(mode, requestedHeight, height, videoCodec);
        return new MediaProbeResult(width, height, fps, videoCodec, audioCodec, container, bits, low, fallback);
    }

    public static string? DescribeLowSourceBitrate(long? bitsPerSecond)
    {
        if (bitsPerSecond is not > 0)
            return null;

        var kbps = bitsPerSecond.Value / 1000d;
        if (kbps >= 1000)
            return null;

        var rounded = (int)(Math.Round(kbps / 50d, MidpointRounding.AwayFromZero) * 50);
        if (rounded < 50)
            rounded = Math.Max(1, (int)Math.Round(kbps, MidpointRounding.AwayFromZero));
        return $"Source is only ~{rounded} kb/s - YouTube's copy is low quality";
    }

    public static string? DescribeFallback(VideoQualityMode mode, int? requestedHeight, int? savedHeight, string? videoCodec)
    {
        if (mode == VideoQualityMode.Compatible &&
            !IsH264(videoCodec) &&
            !string.IsNullOrWhiteSpace(videoCodec))
        {
            return $"Saved {videoCodec} instead of H.264. Compatible mode does not put VP9 or AV1 in an MP4.";
        }

        if (requestedHeight is int want && savedHeight is int got && got > 0 && got + 16 < want)
        {
            var codec = string.IsNullOrWhiteSpace(videoCodec) ? "video" : videoCodec;
            return $"Fell back to {got}p {codec}. Nothing at {want}p matched this quality mode.";
        }

        return null;
    }

    public static async Task<string?> DescribeFilesAsync(
        string? ffmpegPath,
        IReadOnlyList<string> files,
        VideoQualityMode mode,
        int? requestedHeight,
        CancellationToken cancellationToken)
    {
        var ffprobe = FindFfprobe(ffmpegPath);
        if (ffprobe is null)
            return "ffprobe missing, so the saved resolution and bitrate were not checked.";

        var reports = new List<string>();
        foreach (var file in files.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ProbeFileAsync(ffprobe, file, mode, requestedHeight, cancellationToken).ConfigureAwait(false);
            if (parsed is null)
                continue;
            reports.Add(parsed.Report());
        }

        return reports.Count == 0 ? null : string.Join(Environment.NewLine, reports);
    }

    public static string? FindFfprobe(string? ffmpegPath)
    {
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var dir = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var beside = Path.Combine(dir, "ffprobe.exe");
                if (File.Exists(beside))
                    return beside;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, "ffprobe.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                /* ignore bad PATH entries */
            }
        }

        return null;
    }

    private static async Task<MediaProbeResult?> ProbeFileAsync(
        string ffprobePath,
        string file,
        VideoQualityMode mode,
        int? requestedHeight,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = "-v error -print_format json -show_streams -show_format \"" + file + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return null;

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            /* priority is best-effort */
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        try
        {
            return ParseFfprobeJson(stdout, file, mode, requestedHeight);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0" || value == "N/A")
            return null;

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            var fps = num / den;
            return fps > 0 ? fps : null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) && plain > 0)
            return plain;

        return null;
    }

    private static bool IsH264(string? videoCodec)
    {
        if (string.IsNullOrWhiteSpace(videoCodec))
            return false;
        var codec = videoCodec.ToLowerInvariant();
        return codec.Contains("h264", StringComparison.Ordinal) ||
               codec.Contains("avc", StringComparison.Ordinal);
    }

    private static string ContainerFrom(string? formatName, string? filePath)
    {
        var ext = Path.GetExtension(filePath ?? "").TrimStart('.').ToLowerInvariant();
        if (ext is "mp4" or "mkv" or "webm" or "m4a" or "mp3" or "opus" or "flac" or "wav")
            return ext;

        if (string.IsNullOrWhiteSpace(formatName))
            return "unknown";
        if (formatName.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            return "mp4";
        if (formatName.Contains("matroska", StringComparison.OrdinalIgnoreCase))
            return "mkv";
        return formatName.Split(',')[0];
    }

    private static string FormatFps(double fps)
    {
        var rounded = Math.Round(fps);
        if (Math.Abs(fps - rounded) < 0.05)
            return ((int)rounded).ToString(CultureInfo.InvariantCulture);
        return fps.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatBitrate(long bitsPerSecond)
    {
        var kbps = (int)Math.Round(bitsPerSecond / 1000d, MidpointRounding.AwayFromZero);
        return kbps.ToString(CultureInfo.InvariantCulture) + " kb/s";
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
            return n;
        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            return n;
        return null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
            return n;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            return n;
        return null;
    }
}
