using System.Collections.Concurrent;

namespace YouTubeToMp3.Services;

public sealed class ContentKindSignals
{
    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? Channel { get; init; }
    public string? Uploader { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Track { get; init; }
    public string? PlaylistTitle { get; init; }
    public int? CategoryId { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public double? DurationSeconds { get; init; }
    public bool? IsPlaylist { get; init; }
}

public static class ContentKindDetector
{
    private static readonly ConcurrentDictionary<string, ContentKind> PlaylistKindCache = new(StringComparer.OrdinalIgnoreCase);

    public static ContentKind DetectFromUrl(string url)
    {
        url = PlaylistDiscoveryService.GetEffectiveUrl(url);
        return Detect(new ContentKindSignals { Url = url, IsPlaylist = YouTubeUrlHelper.TryGetListId(url) is not null });
    }

    public static ContentKind Detect(ContentKindSignals signals)
    {
        var url = PlaylistDiscoveryService.GetEffectiveUrl(signals.Url ?? "");

        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url))
            return ContentKind.Music;

        var listId = YouTubeUrlHelper.TryGetListId(url);
        if (listId?.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) == true)
            return ContentKind.Music;

        if (listId is not null && TryGetCachedPlaylistKind(url) is { } cached)
            return cached;

        var score = Score(signals, url, listId);
        return score >= 2 ? ContentKind.Music : ContentKind.Video;
    }

    public static int Score(ContentKindSignals signals, string? url = null, string? listId = null)
    {
        url ??= PlaylistDiscoveryService.GetEffectiveUrl(signals.Url ?? "");
        listId ??= YouTubeUrlHelper.TryGetListId(url);
        var score = 0;

        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url) ||
            listId?.StartsWith("OLAK5uy_", StringComparison.OrdinalIgnoreCase) == true)
            return 100;

        if (signals.CategoryId == 10)
            score += 4;
        if (signals.Categories is { } cats &&
            cats.Any(c => c.Contains("Music", StringComparison.OrdinalIgnoreCase)))
            score += 4;

        if (IsTopicChannel(signals.Channel) || IsTopicChannel(signals.Uploader))
            score += 4;

        if (!string.IsNullOrWhiteSpace(signals.Artist) && !string.IsNullOrWhiteSpace(signals.Track ?? signals.Title))
            score += 2;
        if (!string.IsNullOrWhiteSpace(signals.Album))
            score += 2;

        var title = FirstNonEmpty(signals.Title, signals.Track, signals.PlaylistTitle);
        if (MusicPlaylistHeuristics.LooksLikeMusic(title))
            score += 3;
        if (MusicPlaylistHeuristics.LooksLikeMusic(signals.PlaylistTitle))
            score += 2;
        if (LooksLikeArtistTitlePair(title))
            score += 2;
        if (MusicPlaylistHeuristics.LooksLikeVideo(title) || MusicPlaylistHeuristics.LooksLikeVideo(signals.PlaylistTitle))
            score -= 5;

        if (signals.DurationSeconds is { } dur)
        {
            if (dur is >= 90 and <= 15 * 60)
                score += 1;
            if (dur > 45 * 60 && !MusicPlaylistHeuristics.LooksLikeMusic(title))
                score -= 2;
        }

        // why: a watch URL with a random PL id is usually a video, not an album
        if (listId is not null &&
            YouTubeUrlHelper.TryGetVideoId(url) is not null &&
            score < 2)
            score -= 1;

        return score;
    }

    public static ContentKind Resolve(ContentKind chosen, string url) =>
        chosen == ContentKind.Auto ? DetectFromUrl(url) : chosen;

    public static ContentKind Resolve(ContentKind chosen, ContentKindSignals signals) =>
        chosen == ContentKind.Auto ? Detect(signals) : chosen;

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

    public static DownloadFormat SuggestedFormat(ContentKind resolved)
    {
        if (resolved == ContentKind.Music)
            return DownloadFormat.Mp3;
        return DownloadFormat.Mp4;
    }


    private static bool LooksLikeArtistTitlePair(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || MusicPlaylistHeuristics.LooksLikeVideo(title))
            return false;
        var parts = title.Split(" - ", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               parts[0].Length is >= 2 and <= 80 &&
               parts[1].Length is >= 2 and <= 120;
    }

    private static bool IsTopicChannel(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Trim().EndsWith("- Topic", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v));
}
