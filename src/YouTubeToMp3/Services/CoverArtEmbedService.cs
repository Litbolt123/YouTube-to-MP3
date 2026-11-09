using System.Diagnostics;
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            return;
        if (string.IsNullOrWhiteSpace(coverImagePath) || !File.Exists(coverImagePath))
            return;

        var tools = ToolDependencyService.Check();
        if (tools.FfmpegPath is null)
            throw new InvalidOperationException("ffmpeg is required to embed custom cover art.");

        var ext = Path.GetExtension(mediaPath);
        var tempOut = mediaPath + ".cover.tmp" + ext;
        try
        {
            if (File.Exists(tempOut))
                File.Delete(tempOut);

            var args = BuildFfmpegArgs(mediaPath, coverImagePath, tempOut, ext);
            using var process = new Process
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

            string stderr;
            try
            {
                stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(tempOut))
                throw new InvalidOperationException(DescribeFailure(stderr, process.ExitCode));

            File.Move(tempOut, mediaPath, overwrite: true);
        }
        finally
        {
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
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

    private static string BuildFfmpegArgs(string mediaPath, string coverPath, string tempOut, string ext)
    {
        var mediaQ = Quote(mediaPath);
        var coverQ = Quote(coverPath);
        var outQ = Quote(tempOut);

        if (string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return $"-y -i {mediaQ} -i {coverQ} -map 0:a -map 1:0 -c copy -id3v2_version 3 " +
                   $"-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".flac", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            // Copy audio only — mapping all of input 0 can remux picture streams and glitch playback.
            return $"-y -i {mediaQ} -i {coverQ} -map 0:a:0 -map 1:0 -c:a copy -c:v mjpeg " +
                   $"-disposition:v:1 attached_pic -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".m4v", StringComparison.OrdinalIgnoreCase))
        {
            // The cover must be its own stream after the real video; tagging v:0 marks the H.264 track as a picture and the mp4 muxer rejects it.
            return $"-hide_banner -nostats -loglevel error -y -i {mediaQ} -i {coverQ} -map 0:v:0 -map 0:a? -map 1:0 -c copy -c:v:1 mjpeg " +
                   $"-disposition:v:0 default -disposition:v:1 attached_pic " +
                   $"-metadata:s:v:1 title=\"Album cover\" -metadata:s:v:1 comment=\"Cover (front)\" {outQ}";
        }

        if (string.Equals(ext, ".m4a", StringComparison.OrdinalIgnoreCase))
        {
            return $"-hide_banner -nostats -loglevel error -y -i {mediaQ} -i {coverQ} -map 0:a -map 1:0 -c copy -c:v:0 mjpeg " +
                   $"-disposition:v:0 attached_pic -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
        }

        return $"-y -i {mediaQ} -i {coverQ} -map 0 -map 1 -c copy -disposition:v:0 attached_pic " +
               $"-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {outQ}";
    }

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
