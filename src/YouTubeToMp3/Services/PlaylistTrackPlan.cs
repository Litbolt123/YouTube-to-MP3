using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YouTubeToMp3.Services;

public sealed class TrackMetadataOverride
{
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Album { get; init; }
    public string? Title { get; init; }

    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Artist) ||
        !string.IsNullOrWhiteSpace(AlbumArtist) ||
        !string.IsNullOrWhiteSpace(Album) ||
        !string.IsNullOrWhiteSpace(Title);

    public static TrackMetadataOverride ForMusicAlbum(string albumArtist, string album) => new()
    {
        AlbumArtist = string.IsNullOrWhiteSpace(albumArtist) ? null : albumArtist.Trim(),
        Album = string.IsNullOrWhiteSpace(album) ? null : album.Trim(),
    };
}

public sealed class AlbumReviewResult
{
    public required string PlaylistUrl { get; init; }
    public required string CollectionTitle { get; init; }
    public required string ArtistFolder { get; init; }
    public required string AlbumFolder { get; init; }
    public required List<PlaylistTrackPlan> Tracks { get; init; }
}

public sealed class PlaylistTrackPlan : INotifyPropertyChanged
{
    public required int Index { get; init; }
    public required string VideoId { get; init; }
    public required string VideoUrl { get; init; }

    public string? DetectedArtist { get; set; }
    public string? DetectedAlbum { get; set; }
    public string? DetectedTitle { get; set; }
    public string? DetectedFileName { get; set; }

    private string _artist = "";
    private string _album = "";
    private string _title = "";
    private string? _fileNameOverride;
    private bool _include = true;

    public string Artist
    {
        get => _artist;
        set
        {
            if (_artist == value)
                return;
            _artist = value;
            NotifyMetadataChanged();
        }
    }

    public string Album
    {
        get => _album;
        set
        {
            if (_album == value)
                return;
            _album = value;
            NotifyMetadataChanged();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
                return;
            _title = value;
            NotifyMetadataChanged();
        }
    }

    /// <summary>Download filename without extension (editable).</summary>
    public string FileName
    {
        get => _fileNameOverride ?? BuildDefaultFileName();
        set
        {
            var normalized = NormalizeFileNameInput(value);
            var defaultName = BuildDefaultFileName();
            _fileNameOverride = string.Equals(normalized, defaultName, StringComparison.Ordinal)
                ? null
                : normalized;
            NotifyFileNameChanged();
        }
    }

    public bool Include
    {
        get => _include;
        set
        {
            if (_include == value)
                return;
            _include = value;
            OnPropertyChanged();
        }
    }

    public string FileNameWithoutExtension =>
        OutputTemplateBuilder.SanitizeFileName(_fileNameOverride ?? BuildDefaultFileName());

    public string DisplayFileName => $"{FileNameWithoutExtension}.mp3";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshDetectedFileName()
    {
        DetectedFileName = BuildDefaultFileName();
        if (_fileNameOverride is null)
            NotifyFileNameChanged();
    }

    private string BuildDefaultFileName() =>
        OutputTemplateBuilder.SanitizeFileName($"{Index:00} - {MusicTitleCleaner.Clean(Title, Album)}");

    private static string NormalizeFileNameInput(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4].TrimEnd();
        return OutputTemplateBuilder.SanitizeFileName(Path.GetFileNameWithoutExtension(trimmed));
    }

    private void NotifyMetadataChanged()
    {
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Album));
        OnPropertyChanged(nameof(Title));
        if (_fileNameOverride is null)
            NotifyFileNameChanged();
    }

    private void NotifyFileNameChanged()
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FileNameWithoutExtension));
        OnPropertyChanged(nameof(DisplayFileName));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool HasFieldEdits() =>
        !string.Equals(Normalize(Artist), Normalize(DetectedArtist), StringComparison.Ordinal) ||
        !string.Equals(Normalize(Album), Normalize(DetectedAlbum), StringComparison.Ordinal) ||
        !string.Equals(Normalize(Title), Normalize(DetectedTitle), StringComparison.Ordinal) ||
        _fileNameOverride is not null;

    public TrackMetadataOverride? BuildMetadataOverride(string? albumArtist, bool forceWrite = false)
    {
        if (forceWrite)
        {
            return new TrackMetadataOverride
            {
                Artist = string.IsNullOrWhiteSpace(Artist) ? null : Artist.Trim(),
                AlbumArtist = string.IsNullOrWhiteSpace(albumArtist) ? null : albumArtist.Trim(),
                Album = string.IsNullOrWhiteSpace(Album) ? null : Album.Trim(),
                Title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim(),
            };
        }

        string? trackArtist = null;
        string? album = null;
        string? title = null;

        if (!string.Equals(Normalize(Artist), Normalize(DetectedArtist), StringComparison.Ordinal))
            trackArtist = string.IsNullOrWhiteSpace(Artist) ? null : Artist.Trim();
        if (!string.Equals(Normalize(Album), Normalize(DetectedAlbum), StringComparison.Ordinal))
            album = string.IsNullOrWhiteSpace(Album) ? null : Album.Trim();
        if (!string.Equals(Normalize(Title), Normalize(DetectedTitle), StringComparison.Ordinal))
            title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim();

        if (trackArtist is null && album is null && title is null)
            return null;

        return new TrackMetadataOverride
        {
            Artist = trackArtist,
            AlbumArtist = string.IsNullOrWhiteSpace(albumArtist) ? null : albumArtist.Trim(),
            Album = album,
            Title = title,
        };
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
}

public static class AlbumReviewService
{
    public static bool RequiresPerTrackJobs(AlbumReviewResult review)
    {
        if (review.Tracks.Any(static t => !t.Include))
            return true;

        if (review.Tracks.Any(static t => t.HasFieldEdits()))
            return true;

        return false;
    }

    public static IEnumerable<DownloadJob> BuildJobs(
        AlbumReviewResult review,
        DownloadFormat format,
        int quality,
        ContentKind contentKind,
        string outputFolder,
        bool embedThumbnail,
        string? customCoverArtPath,
        bool forceRedownload)
    {
        var included = review.Tracks.Where(static t => t.Include).ToList();
        if (included.Count == 0)
            yield break;

        var albumMetadata = TrackMetadataOverride.ForMusicAlbum(review.ArtistFolder, review.AlbumFolder);

        if (!RequiresPerTrackJobs(review))
        {
            yield return new DownloadJob
            {
                Url = review.PlaylistUrl,
                Scope = DownloadScope.Playlist,
                Format = format,
                Quality = quality,
                EmbedThumbnail = embedThumbnail,
                ContentKind = contentKind,
                OutputFolder = outputFolder,
                PredictedTitle = review.CollectionTitle,
                CollectionTitle = review.CollectionTitle,
                ForceRedownload = forceRedownload,
                CustomCoverArtPath = customCoverArtPath,
                MetadataOverride = albumMetadata.HasAny ? albumMetadata : null,
                MusicArtistFolder = review.ArtistFolder,
                MusicAlbumFolder = review.AlbumFolder,
            };
            yield break;
        }

        foreach (var track in included)
        {
            yield return new DownloadJob
            {
                Url = track.VideoUrl,
                Scope = DownloadScope.SingleVideo,
                Format = format,
                Quality = quality,
                EmbedThumbnail = embedThumbnail,
                ContentKind = contentKind,
                OutputFolder = outputFolder,
                CustomFileName = track.FileNameWithoutExtension,
                PredictedTitle = track.Title,
                CollectionTitle = review.CollectionTitle,
                ForceRedownload = forceRedownload,
                CustomCoverArtPath = customCoverArtPath,
                MetadataOverride = track.BuildMetadataOverride(review.ArtistFolder, forceWrite: true),
                MusicArtistFolder = review.ArtistFolder,
                MusicAlbumFolder = review.AlbumFolder,
                PlaylistTrackIndex = track.Index,
                PlaylistTrackTotal = included.Count,
            };
        }
    }
}
