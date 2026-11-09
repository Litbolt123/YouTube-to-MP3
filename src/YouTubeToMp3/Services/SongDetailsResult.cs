namespace YouTubeToMp3.Services;

/// <summary>Editable single-track song tags before download.</summary>
public sealed class SongDetailsResult
{
    public required string Url { get; init; }
    public string DetectedArtist { get; init; } = "";
    public string DetectedAlbumArtist { get; init; } = "";
    public string DetectedAlbum { get; init; } = "";
    public string DetectedTitle { get; init; } = "";
    public string DetectedFileName { get; init; } = "";

    public string Artist { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";

    public TrackMetadataOverride BuildMetadataOverride() => new()
    {
        Artist = NullIfBlank(Artist),
        AlbumArtist = NullIfBlank(AlbumArtist) ?? NullIfBlank(Artist),
        Album = NullIfBlank(Album),
        Title = NullIfBlank(Title),
    };

    public string FileNameWithoutExtension
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FileName)
                ? BuildDefaultFileName(Artist, Album, Title)
                : FileName.Trim();
            if (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }

            return OutputTemplateBuilder.SanitizeFileName(name);
        }
    }

    public static SongDetailsResult FromMetadata(string url, TrackMetadata metadata)
    {
        var artist = metadata.Artist?.Trim() ?? "";
        var album = FirstNonEmpty(metadata.Album, metadata.PlaylistTitle) ?? "";
        var title = metadata.Title?.Trim() ?? "";

        // YouTube often leaves artist blank and puts "Artist - Title" in the title.
        if (string.IsNullOrWhiteSpace(artist) && TrySplitArtistTitle(title, out var parsedArtist, out var parsedTitle))
        {
            artist = parsedArtist;
            title = parsedTitle;
        }

        title = MusicTitleCleaner.Clean(title, album);
        var albumArtist = MusicFilenameBuilder.PrimaryArtist(artist) ?? artist;
        var fileName = BuildDefaultFileName(artist, album, title);

        return new SongDetailsResult
        {
            Url = url,
            DetectedArtist = artist,
            DetectedAlbumArtist = albumArtist,
            DetectedAlbum = album,
            DetectedTitle = title,
            DetectedFileName = fileName,
            Artist = artist,
            AlbumArtist = albumArtist,
            Album = album,
            Title = title,
            FileName = fileName,
        };
    }

    public static string BuildDefaultFileName(string? artist, string? album, string? title) =>
        OutputTemplateBuilder.SanitizeFileName(MusicFilenameBuilder.Format(artist, album, null, title));

    private static bool TrySplitArtistTitle(string title, out string artist, out string songTitle)
    {
        artist = "";
        songTitle = "";
        var sep = title.IndexOf(" - ", StringComparison.Ordinal);
        if (sep <= 0 || sep + 3 >= title.Length)
            return false;

        artist = title[..sep].Trim();
        songTitle = title[(sep + 3)..].Trim();
        return artist.Length > 0 && songTitle.Length > 0;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();
        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
