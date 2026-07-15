using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public static class YouTubeUrlHelper
{
    private static readonly Regex ListIdRegex = new(@"[?&]list=([^&#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoIdQueryRegex = new(@"[?&]v=([^&#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoIdPathRegex = new(
        @"(?:youtu\.be/|/(?:embed|v|shorts)/)([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsMusicAlbumOrPlaylist(string url) =>
        url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("OLAK5uy_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// YouTube Music album links (OLAK5uy_…) work more reliably via music.youtube.com.
    /// </summary>
    public static string Normalize(string url, DownloadScope scope)
    {
        if (!url.Contains("youtube", StringComparison.OrdinalIgnoreCase))
            return url;

        var listId = TryGetListId(url);
        var videoId = TryGetVideoId(url);
        var isMusic = IsMusicAlbumOrPlaylist(url) ||
                      (listId?.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!isMusic)
            return url;

        if (scope == DownloadScope.Playlist && !string.IsNullOrEmpty(listId))
            return $"https://music.youtube.com/playlist?list={listId}";

        if (scope == DownloadScope.SingleVideo && !string.IsNullOrEmpty(videoId))
            return $"https://music.youtube.com/watch?v={videoId}";

        return url;
    }

    public static string? TryGetListId(string url)
    {
        var m = ListIdRegex.Match(url);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }

    public static string? TryGetVideoId(string url)
    {
        var queryMatch = VideoIdQueryRegex.Match(url);
        if (queryMatch.Success)
            return Uri.UnescapeDataString(queryMatch.Groups[1].Value);

        var pathMatch = VideoIdPathRegex.Match(url);
        return pathMatch.Success ? pathMatch.Groups[1].Value : null;
    }

    public static string ExpandToWatchUrl(string url)
    {
        var videoId = TryGetVideoId(url);
        if (string.IsNullOrWhiteSpace(videoId))
            return url.Trim();

        var listId = TryGetListId(url);
        return !string.IsNullOrEmpty(listId)
            ? $"https://www.youtube.com/watch?v={videoId}&list={listId}"
            : $"https://www.youtube.com/watch?v={videoId}";
    }

    public static string? TryGetPlaylistUrl(string url)
    {
        var listId = TryGetListId(url);
        return string.IsNullOrWhiteSpace(listId)
            ? null
            : BuildPlaylistUrl(listId);
    }

    public static string BuildPlaylistUrl(string listId)
    {
        return listId.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase)
            ? $"https://music.youtube.com/playlist?list={listId}"
            : $"https://www.youtube.com/playlist?list={listId}";
    }

    public static string AttachListId(string url, string listId)
    {
        var videoId = TryGetVideoId(url);
        var isMusic = listId.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) ||
                      IsMusicAlbumOrPlaylist(url);

        if (!string.IsNullOrWhiteSpace(videoId))
        {
            return isMusic
                ? $"https://music.youtube.com/watch?v={videoId}&list={listId}"
                : $"https://www.youtube.com/watch?v={videoId}&list={listId}";
        }

        return BuildPlaylistUrl(listId);
    }

    public static string BuildTrackUrl(string sourceUrl, string videoId)
    {
        var listId = TryGetListId(sourceUrl);
        var isMusic = IsMusicAlbumOrPlaylist(sourceUrl) ||
                      (listId?.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) ?? false);

        if (isMusic && !string.IsNullOrEmpty(listId))
            return $"https://music.youtube.com/watch?v={videoId}&list={listId}";

        if (!string.IsNullOrEmpty(listId))
            return $"https://www.youtube.com/watch?v={videoId}&list={listId}";

        return $"https://www.youtube.com/watch?v={videoId}";
    }
}
