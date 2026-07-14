using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public sealed class TrackMetadata
{
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? PlaylistTitle { get; init; }
    public string? Title { get; init; }
}

public sealed class MusicPlaylistFolderInfo
{
    public required string ArtistFolder { get; init; }
    public required string AlbumFolder { get; init; }
}

public static class MusicFilenameBuilder
{
    public static string Format(TrackMetadata metadata) =>
        Format(metadata.Artist, metadata.Album, metadata.PlaylistTitle, metadata.Title);

    public static string Format(string? artist, string? album, string? playlistTitle, string? title)
    {
        var albumName = FirstNonEmpty(album, playlistTitle);
        var parts = new[] { artist, albumName, title }
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p!.Trim())
            .ToList();

        return parts.Count > 0 ? string.Join(" - ", parts) : "download";
    }

    /// <summary>First credited artist when tracks list featured collaborators (e.g. "C418, Laura Shigihara").</summary>
    public static string? PrimaryArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return null;

        var trimmed = artist.Trim();
        var comma = trimmed.IndexOf(',');
        return comma > 0 ? trimmed[..comma].Trim() : trimmed;
    }

    public static string ResolvePlaylistArtistFolder(
        string? playlistUploader,
        string? playlistChannel,
        string? uploader,
        string? channel,
        string? artist)
    {
        foreach (var candidate in new[] { playlistUploader, playlistChannel, uploader, channel })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return OutputTemplateBuilder.SanitizeFileName(candidate.Trim());
        }

        var primary = PrimaryArtist(artist);
        return OutputTemplateBuilder.SanitizeFileName(primary ?? "Unknown Artist");
    }

    public static string ResolvePlaylistAlbumFolder(string? album, string? playlistTitle) =>
        OutputTemplateBuilder.SanitizeFileName(
            FirstNonEmpty(album, playlistTitle) ?? "Unknown Album");

    public static string FormatCollectionTitle(string? artist, string? album) =>
        !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album)
            ? $"{artist.Trim()} - {album.Trim()}"
            : FirstNonEmpty(album, artist) ?? "Playlist";

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();
        return null;
    }
}

public static class MusicTitleCleaner
{
    private static readonly Regex NumberedAlbumPrefixRegex =
        new(@"^.+?\s-\s\d+\s*-\s*(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingIndexRegex =
        new(@"^\d+\s*[-–.]\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Clean(string? rawTitle, string? albumName = null, string? collectionTitle = null)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return "";

        var title = rawTitle.Trim();
        title = LeadingIndexRegex.Replace(title, "");

        foreach (var prefix in new[] { albumName, collectionTitle })
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            var trimmedPrefix = prefix.Trim();
            if (!title.StartsWith(trimmedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = title[trimmedPrefix.Length..];
            // Only strip when the album/collection name is a prefix with a separator after it.
            // If the whole title is just the album name (e.g. track "One" on album "One"), keep it.
            if (remainder.Length == 0)
                break;

            if (remainder[0] is ' ' or '-' or '–' or ':')
            {
                title = remainder.TrimStart(' ', '-', '–', ':');
                break;
            }
        }

        var numbered = NumberedAlbumPrefixRegex.Match(title);
        if (numbered.Success)
            title = numbered.Groups[1].Value.Trim();

        title = title.Trim();
        return string.IsNullOrWhiteSpace(title) ? rawTitle.Trim() : title;
    }
}
