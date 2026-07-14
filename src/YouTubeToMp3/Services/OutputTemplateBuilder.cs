namespace YouTubeToMp3.Services;

public static class OutputTemplateBuilder
{
    /// <summary>
    /// yt-dlp output templates. Pair with --output-na-placeholder "" so missing fields are omitted cleanly.
    /// Music: Artist - Album - Title. Video: Title only (or Playlist/01 - Title for playlists).
    /// </summary>
    public static string Build(string outputFolder, DownloadScope scope, ContentKind contentKind, string? customFileName = null)
    {
        if (!string.IsNullOrWhiteSpace(customFileName))
            return Path.Combine(outputFolder, SanitizeFileName(customFileName) + ".%(ext)s");

        var fileName = contentKind == ContentKind.Music
            ? scope == DownloadScope.Playlist ? BuildMusicPlaylistFileName() : BuildMusicSingleFileName()
            : scope == DownloadScope.Playlist ? BuildVideoPlaylistFileName() : BuildVideoSingleFileName();

        return Path.Combine(outputFolder, fileName);
    }

    public static string BuildMusicPlaylistWithFixedFolders(string outputFolder, string artistFolder, string albumFolder) =>
        Path.Combine(
            outputFolder,
            SanitizeFileName(artistFolder),
            SanitizeFileName(albumFolder),
            "%(playlist_index)02d - %(title)s.%(ext)s");

    public static string BuildMusicTrackWithFixedFolders(
        string outputFolder,
        string artistFolder,
        string albumFolder,
        string fileNameWithoutExt) =>
        Path.Combine(
            outputFolder,
            SanitizeFileName(artistFolder),
            SanitizeFileName(albumFolder),
            SanitizeFileName(fileNameWithoutExt) + ".%(ext)s");

    public static async Task<string> ResolveAsync(
        string outputFolder,
        DownloadScope scope,
        ContentKind contentKind,
        DownloadFormat format,
        string url,
        string? customFileName = null,
        string? musicArtistFolder = null,
        string? musicAlbumFolder = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(customFileName) &&
            !string.IsNullOrWhiteSpace(musicArtistFolder) &&
            !string.IsNullOrWhiteSpace(musicAlbumFolder))
        {
            return BuildMusicTrackWithFixedFolders(
                outputFolder,
                musicArtistFolder,
                musicAlbumFolder,
                customFileName);
        }

        if (!string.IsNullOrWhiteSpace(customFileName))
            return Build(outputFolder, scope, contentKind, customFileName);

        if (contentKind == ContentKind.Music && scope == DownloadScope.Playlist)
        {
            var folders = await YtDlpMetadataService.GetMusicPlaylistFolderInfoAsync(
                url, format, contentKind, cancellationToken).ConfigureAwait(false);
            if (folders is not null)
            {
                return BuildMusicPlaylistWithFixedFolders(
                    outputFolder,
                    folders.ArtistFolder,
                    folders.AlbumFolder);
            }
        }

        return Build(outputFolder, scope, contentKind);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }

    /// <summary>Music single fallback when no custom name is supplied (prefer MusicFilenameBuilder in app code).</summary>
    private static string BuildMusicSingleFileName() =>
        "%(artist|uploader|channel&{} - |)s%(album|playlist_title&{} - |)s%(title)s.%(ext)s";

    /// <summary>Music playlist fallback when playlist metadata could not be resolved up front.</summary>
    private static string BuildMusicPlaylistFileName() =>
        "%(playlist_uploader|playlist_channel|uploader|channel)s/%(album|playlist_title)s/%(playlist_index)02d - %(title)s.%(ext)s";

    /// <summary>Video single: Title.ext</summary>
    private static string BuildVideoSingleFileName() =>
        "%(title)s.%(ext)s";

    /// <summary>Video playlist: PlaylistName/01 - Title.ext</summary>
    private static string BuildVideoPlaylistFileName() =>
        "%(playlist_title|channel)s/%(playlist_index)02d - %(title)s.%(ext)s";
}
