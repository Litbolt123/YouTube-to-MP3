using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace YouTubeToMp3.Services;

public sealed class DiscoveredPlaylist
{
    public required string ListId { get; init; }
    public string? Title { get; init; }
    public int? TrackCount { get; init; }
    public required string PlaylistUrl { get; init; }
    public required string WatchUrl { get; init; }
}

/// <summary>
/// Finds album/playlist list ids for track-only YouTube links (e.g. youtu.be/VID without ?list=).
/// </summary>
public static class PlaylistDiscoveryService
{
    private static readonly ConcurrentDictionary<string, DiscoveredPlaylist?> CacheByVideoId = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache() => CacheByVideoId.Clear();

    public static string GetEffectiveUrl(string url)
    {
        if (YouTubeUrlHelper.TryGetListId(url) is not null)
            return url;

        var videoId = YouTubeUrlHelper.TryGetVideoId(url);
        if (videoId is null)
            return url;

        return CacheByVideoId.TryGetValue(videoId, out var discovered) && discovered is not null
            ? discovered.WatchUrl
            : url;
    }

    public static string? GetEffectivePlaylistUrl(string url)
    {
        if (YouTubeUrlHelper.TryGetPlaylistUrl(url) is { } existing)
            return existing;

        var videoId = YouTubeUrlHelper.TryGetVideoId(url);
        if (videoId is null)
            return null;

        return CacheByVideoId.TryGetValue(videoId, out var discovered) && discovered is not null
            ? discovered.PlaylistUrl
            : null;
    }

    public static bool HasPlaylistContext(string url) =>
        YouTubeUrlHelper.TryGetListId(url) is not null || GetEffectivePlaylistUrl(url) is not null;

    public static async Task<string> EnsurePlaylistUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (YouTubeUrlHelper.TryGetPlaylistUrl(url) is { } playlistUrl)
            return playlistUrl;

        var discovered = await TryDiscoverFromVideoAsync(url, cancellationToken).ConfigureAwait(false);
        return discovered?.PlaylistUrl ?? url;
    }

    public static async Task<DiscoveredPlaylist?> TryDiscoverFromVideoAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (YouTubeUrlHelper.TryGetListId(url) is not null)
            return null;

        var videoId = YouTubeUrlHelper.TryGetVideoId(url);
        if (videoId is null)
            return null;

        if (CacheByVideoId.TryGetValue(videoId, out var cached))
            return cached;

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        DiscoveredPlaylist? found = null;
        foreach (var probeUrl in BuildProbeUrls(url, videoId))
        {
            found = await ProbeAsync(probeUrl, url, tools, cancellationToken).ConfigureAwait(false);
            if (found is not null)
                break;
        }

        CacheByVideoId[videoId] = found;
        if (found is not null)
        {
            ContentKindDetector.CachePlaylistKind(found.PlaylistUrl,
                LooksLikeMusicAlbum(found) ? ContentKind.Music : ContentKind.Video);
            ContentKindDetector.CachePlaylistKind(found.WatchUrl,
                LooksLikeMusicAlbum(found) ? ContentKind.Music : ContentKind.Video);
        }

        return found;
    }

    private static IEnumerable<string> BuildProbeUrls(string original, string videoId)
    {
        yield return $"https://music.youtube.com/watch?v={videoId}";
        yield return YouTubeUrlHelper.ExpandToWatchUrl(original);
    }

    private static async Task<DiscoveredPlaylist?> ProbeAsync(
        string probeUrl,
        string originalUrl,
        ToolCheckResult tools,
        CancellationToken cancellationToken)
    {
        var args = new StringBuilder();
        args.Append("--no-download --no-warnings --no-playlist ");
        YouTubeExtractorArgs.Append(args, tools, probeUrl, DownloadFormat.Mp3);
        args.Append("--print \"%(playlist_id)s|||%(playlist_title)s|||%(album)s|||%(playlist_count)s\" ");
        args.Append($"\"{probeUrl}\"");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.YtDlpPath!,
                    Arguments = args.ToString(),
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
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;

            var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var parts = line.Split("|||");
            var listId = NullIfMetadata(parts.ElementAtOrDefault(0));
            var playlistTitle = NullIfMetadata(parts.ElementAtOrDefault(1));
            var album = NullIfMetadata(parts.ElementAtOrDefault(2));
            var count = ParseCount(parts.ElementAtOrDefault(3));

            if (!ShouldAcceptList(listId, playlistTitle, album, count))
                return null;

            var title = playlistTitle ?? album;
            var playlistUrl = YouTubeUrlHelper.BuildPlaylistUrl(listId!);
            var watchUrl = YouTubeUrlHelper.AttachListId(originalUrl, listId!);

            return new DiscoveredPlaylist
            {
                ListId = listId!,
                Title = title,
                TrackCount = count,
                PlaylistUrl = playlistUrl,
                WatchUrl = watchUrl,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldAcceptList(string? listId, string? playlistTitle, string? album, int? count)
    {
        if (!IsUsableListId(listId))
            return false;

        if (listId!.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase))
            return true;

        var label = playlistTitle ?? album;
        if (MusicPlaylistHeuristics.LooksLikeMusic(label))
            return count is null or >= 2;

        return count is >= 2;
    }

    private static bool IsUsableListId(string? listId)
    {
        if (string.IsNullOrWhiteSpace(listId))
            return false;

        if (listId is "NA" or "None" or "N/A")
            return false;

        if (listId.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
            return false;

        return listId.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) ||
               listId.StartsWith("PL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMusicAlbum(DiscoveredPlaylist discovered) =>
        discovered.ListId.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) ||
        MusicPlaylistHeuristics.LooksLikeMusic(discovered.Title);

    private static int? ParseCount(string? value)
    {
        var cleaned = NullIfMetadata(value);
        return int.TryParse(cleaned, out var count) && count > 0 ? count : null;
    }

    private static string? NullIfMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed is "+" or "NA" or "None" or "N/A" ? null : trimmed;
    }
}
