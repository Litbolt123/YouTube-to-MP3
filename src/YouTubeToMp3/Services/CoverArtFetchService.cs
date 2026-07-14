using System.Diagnostics;
using System.Net.Http;

namespace YouTubeToMp3.Services;

public static class CoverArtFetchService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>Download a YouTube video’s best thumbnail to a temp jpg path.</summary>
    public static async Task<(string? path, string? error)> FetchThumbnailFromUrlAsync(
        string videoOrPlaylistUrl,
        CancellationToken cancellationToken = default)
    {
        var tools = ToolDependencyService.Check();
        if (tools.YtDlpPath is null)
            return (null, "yt-dlp is not available.");

        var url = YouTubeUrlHelper.ExpandToWatchUrl(videoOrPlaylistUrl.Trim());
        var scope = YouTubeUrlHelper.TryGetListId(url) is not null &&
                    YouTubeUrlHelper.TryGetVideoId(url) is null
            ? DownloadScope.Playlist
            : DownloadScope.SingleVideo;
        url = YouTubeUrlHelper.Normalize(url, scope);
        var tempDir = Path.Combine(Path.GetTempPath(), "YouTubeToMp3-covers");
        Directory.CreateDirectory(tempDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var outTemplate = Path.Combine(tempDir, $"cover-{stamp}.%(ext)s");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.YtDlpPath,
                    Arguments =
                        $"--no-playlist --skip-download --write-thumbnail --convert-thumbnails jpg " +
                        $"-o \"{outTemplate}\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);

            if (!process.Start())
                return (null, "Could not start yt-dlp.");

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var jpg = Directory.EnumerateFiles(tempDir, $"cover-{stamp}*.jpg").FirstOrDefault()
                      ?? Directory.EnumerateFiles(tempDir, $"cover-{stamp}*.webp").FirstOrDefault()
                      ?? Directory.EnumerateFiles(tempDir, $"cover-{stamp}*.*").FirstOrDefault();

            if (jpg is null || !File.Exists(jpg))
            {
                // Fallback: print thumbnail URL and download with HttpClient
                var thumbUrl = await GetThumbnailUrlInternalAsync(tools.YtDlpPath, url, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(thumbUrl))
                    return (null, string.IsNullOrWhiteSpace(stderr) ? "No thumbnail found." : stderr.Trim());

                var dest = Path.Combine(tempDir, $"cover-{stamp}.jpg");
                await DownloadUrlToFileAsync(thumbUrl, dest, cancellationToken).ConfigureAwait(false);
                return File.Exists(dest) ? (dest, null) : (null, "Failed to download thumbnail.");
            }

            return (jpg, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public static async Task<string?> GetThumbnailUrlAsync(
        string videoOrPlaylistUrl,
        CancellationToken cancellationToken = default)
    {
        var tools = ToolDependencyService.Check();
        if (tools.YtDlpPath is null)
            return null;

        var scope = YouTubeUrlHelper.TryGetListId(videoOrPlaylistUrl) is not null &&
                    YouTubeUrlHelper.TryGetVideoId(videoOrPlaylistUrl) is null
            ? DownloadScope.Playlist
            : DownloadScope.SingleVideo;
        var url = YouTubeUrlHelper.Normalize(
            YouTubeUrlHelper.ExpandToWatchUrl(videoOrPlaylistUrl.Trim()),
            scope);
        return await GetThumbnailUrlInternalAsync(tools.YtDlpPath, url, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildThumbnailPrintArgs(string url)
    {
        var hasListOnly = YouTubeUrlHelper.TryGetListId(url) is not null &&
                          YouTubeUrlHelper.TryGetVideoId(url) is null;
        return hasListOnly
            ? $"--print thumbnail \"{url}\""
            : $"--no-playlist --print thumbnail \"{url}\"";
    }

    private static async Task<string?> GetThumbnailUrlInternalAsync(
        string ytDlpPath,
        string url,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = BuildThumbnailPrintArgs(url),
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
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            return null;

        return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DownloadUrlToFileAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        await using var stream = await Http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(destPath);
        await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }
}
