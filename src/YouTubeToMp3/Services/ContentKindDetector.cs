using System.Collections.Concurrent;

namespace YouTubeToMp3.Services;

public static class ContentKindDetector
{
    private static readonly ConcurrentDictionary<string, ContentKind> PlaylistKindCache = new(StringComparer.OrdinalIgnoreCase);

    public static ContentKind DetectFromUrl(string url)
    {
        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url))
            return ContentKind.Music;

        if (YouTubeUrlHelper.TryGetListId(url) is not null)
        {
            // Common album pattern: one track URL that also carries a playlist id (youtu.be/VID?list=PL…).
            if (YouTubeUrlHelper.TryGetVideoId(url) is not null)
                return ContentKind.Music;

            if (TryGetCachedPlaylistKind(url) is { } cached)
                return cached;
        }

        return ContentKind.Video;
    }

    public static ContentKind Resolve(ContentKind chosen, string url) =>
        chosen == ContentKind.Auto ? DetectFromUrl(url) : chosen;

    public static void CachePlaylistKind(string url, ContentKind kind)
    {
        var key = PlaylistCacheKey(url);
        if (!string.IsNullOrWhiteSpace(key))
            PlaylistKindCache[key] = kind;
    }

    public static ContentKind? TryGetCachedPlaylistKind(string url)
    {
        var key = PlaylistCacheKey(url);
        return !string.IsNullOrWhiteSpace(key) && PlaylistKindCache.TryGetValue(key, out var kind)
            ? kind
            : null;
    }

    public static void ClearPlaylistKindCache() => PlaylistKindCache.Clear();

    private static string? PlaylistCacheKey(string url) =>
        YouTubeUrlHelper.TryGetListId(url);

    public static string DisplayLabel(ContentKind kind) => kind switch
    {
        ContentKind.Music => "Music",
        ContentKind.Video => "Video",
        _ => "Auto",
    };

    public static string Emoji(ContentKind kind) => kind switch
    {
        ContentKind.Music => "🎵",
        ContentKind.Video => "🎬",
        _ => "✨",
    };

    /// <summary>Suggest MP3 for music, MP4 for video when content is auto-detected.</summary>
    public static DownloadFormat SuggestedFormat(ContentKind resolved)
    {
        if (resolved == ContentKind.Music)
            return DownloadFormat.Mp3;
        return DownloadFormat.Mp4;
    }
}
