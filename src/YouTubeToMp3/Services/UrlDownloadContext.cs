namespace YouTubeToMp3.Services;

public enum UrlDownloadContext
{
    SingleTrack,
    MusicAlbum,
    VideoPlaylist,
    AmbiguousPlaylist,
}

public static class PlaylistKindResolver
{
    public static UrlDownloadContext GetDownloadContext(string url, ContentKind chosen)
    {
        url = PlaylistDiscoveryService.GetEffectiveUrl(url);

        if (YouTubeUrlHelper.TryGetListId(url) is null)
            return UrlDownloadContext.SingleTrack;

        if (chosen == ContentKind.Music)
            return UrlDownloadContext.MusicAlbum;

        if (chosen == ContentKind.Video)
            return UrlDownloadContext.VideoPlaylist;

        var resolved = ContentKindDetector.Resolve(ContentKind.Auto, url);
        if (resolved == ContentKind.Music)
            return UrlDownloadContext.MusicAlbum;

        if (resolved == ContentKind.Video && !IsAmbiguousPlaylist(url))
            return UrlDownloadContext.VideoPlaylist;

        return UrlDownloadContext.AmbiguousPlaylist;
    }

    public static bool IsAmbiguousPlaylist(string url)
    {
        url = PlaylistDiscoveryService.GetEffectiveUrl(url);

        if (YouTubeUrlHelper.TryGetListId(url) is null)
            return false;

        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url))
            return false;

        if (YouTubeUrlHelper.TryGetVideoId(url) is not null)
            return false;

        return ContentKindDetector.TryGetCachedPlaylistKind(url) is null;
    }

    public static bool HasPlaylist(string url) => PlaylistDiscoveryService.HasPlaylistContext(url);
}
