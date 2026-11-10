using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace YouTubeToMp3.Services;

public static class CoverArtEmbedService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".opus", ".flac", ".wav", ".ogg",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".m4v",
    };

    public static async Task EmbedAsync(
        string mediaPath,
        string coverImagePath,
        CancellationToken cancellationToken = default) =>
        await EmbedAsync(mediaPath, coverImagePath, progress: null, cancellationToken).ConfigureAwait(false);

    public static async Task EmbedAsync(
        string mediaPath,
        string coverImagePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            return;
        if (string.IsNullOrWhiteSpace(coverImagePath) || !File.Exists(coverImagePath))
            return;

        var tools = ToolDependencyService.Check();
        if (tools.FfmpegPath is null)
            throw new InvalidOperationException("ffmpeg is required to embed custom cover art.");

        var probePath = FindSiblingFfprobe(tools.FfmpegPath) ?? tools.FfmpegPath;
        var duration = await ProbeDurationAsync(probePath, mediaPath, cancellationToken).ConfigureAwait(false);

        var ext = Path.GetExtension(mediaPath);
        var tempOut = mediaPath + ".cover.tmp" + ext;
        Process? process = null;
        try
        {
            if (File.Exists(tempOut))
                File.Delete(tempOut);

            var args = BuildFfmpegArgs(mediaPath, coverImagePath, tempOut, ext);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.FfmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);

            if (!process.Start())
                throw new InvalidOperationException("Could not start ffmpeg for cover art.");

            var stderr = new StringBuilder();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;

                stderr.AppendLine(line);
                if (duration is { } total &&
                    FfmpegProgressParser.TryParseTimestamp(line, out var position))
                {
                    var pct = FfmpegProgressParser.ToPercent(position, total);
                    if (pct is { } value)
                        progress?.Report(value);
                }
                else if (line.StartsWith("progress=", StringComparison.OrdinalIgnoreCase) &&
                         line.Contains("end", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(100);
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(tempOut))
                throw new InvalidOperationException(DescribeFailure(stderr.ToString(), process.ExitCode));

            File.Move(tempOut, mediaPath, overwrite: true);
            progress?.Report(100);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            process?.Dispose();
            try
            {
                if (File.Exists(tempOut))
                    File.Delete(tempOut);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static async Task EmbedIntoManyAsync(
        IEnumerable<string> mediaPaths,
        string coverImagePath,
        CancellationToken cancellationToken = default)
    {
        foreach (var path in mediaPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsEmbeddableMedia(path))
                await EmbedAsync(path, coverImagePath, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string DescribeFailure(string stderr, int exitCode)
    {
        var detail = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => !l.Equals("Conversion failed!", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(detail)
            ? $"ffmpeg failed embedding cover art (exit {exitCode})."
            : $"ffmpeg failed embedding cover art: {detail}";
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    public static bool IsEmbeddableMedia(string path)
    {
        var ext = Path.GetExtension(path);
        return AudioExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }

    public static async Task<TimeSpan?> ProbeDurationAsync(
        string? ffprobeOrFfmpegPath,
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ffprobeOrFfmpegPath) || !File.Exists(mediaPath))
            return null;

        try
        {
            var isFfprobe = Path.GetFileNameWithoutExtension(ffprobeOrFfmpegPath)
                .Contains("ffprobe", StringComparison.OrdinalIgnoreCase);
            var args = isFfprobe
                ? $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\""
                : $"-i \"{mediaPath}\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffprobeOrFfmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);
            if (!process.Start())
                return null;

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return FfmpegProgressParser.TryParseDuration(stderr, out var duration) ? duration : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSiblingFfprobe(string ffmpegPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(ffmpegPath);
            if (dir is null)
                return null;
            var candidate = Path.Combine(dir, "ffprobe.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildFfmpegArgs(string mediaPath, string coverPath, string tempOut, string ext)
    {
        var mediaQ = Quote(mediaPath);
        var coverQ = Quote(coverPath);
        var outQ = Quote(tempOut);
        // why: -progress pipe:2 gives out_time_ms lines we can map to a real percent bar
        const string progress = "-hide_banner -nostats -loglevel error -progress pipe:2 ";

        if (string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return $"{progress}-y -i {mediaQ} -i {coverQ} -map 0:a -map 1:0 -c copy -id3v2_version 3 " +
                   $"-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".flac", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return $"{progress}-y -i {mediaQ} -i {coverQ} -map 0:a:0 -map 1:0 -c:a copy -c:v mjpeg " +
                   $"-disposition:v:1 attached_pic -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".m4v", StringComparison.OrdinalIgnoreCase))
        {
            return $"{progress}-y -i {mediaQ} -i {coverQ} -map 0:v:0 -map 0:a? -map 1:0 -c copy -c:v:1 mjpeg " +
                   $"-disposition:v:0 default -disposition:v:1 attached_pic " +
                   $"-metadata:s:v:1 title=\"Album cover\" -metadata:s:v:1 comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".m4a", StringComparison.OrdinalIgnoreCase))
        {
            return $"{progress}-y -i {mediaQ} -i {coverQ} -map 0:a -map 1:0 -c copy -c:v:0 mjpeg " +
                   $"-disposition:v:0 attached_pic -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        return $"{progress}-y -i {mediaQ} -i {coverQ} -map 0 -map 1 -c copy -disposition:v:0 attached_pic " +
               $"-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
    }

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
