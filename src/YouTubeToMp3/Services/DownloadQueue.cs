namespace YouTubeToMp3.Services;

public sealed class DownloadJob
{
    public required string Url { get; init; }
    public required DownloadScope Scope { get; init; }
    public required DownloadFormat Format { get; init; }
    public required int Quality { get; init; }
    public required bool EmbedThumbnail { get; init; }
    public required ContentKind ContentKind { get; init; }
    public required string OutputFolder { get; init; }

    /// <summary>Base file name without extension, when user overrides the preview.</summary>
    public string? CustomFileName { get; init; }

    public string? PredictedTitle { get; init; }

    /// <summary>Stable album/playlist label shown while downloading the whole collection.</summary>
    public string? CollectionTitle { get; init; }

    /// <summary>When true, bypass skip-already-downloaded / download-archive for this job.</summary>
    public bool ForceRedownload { get; init; }

    /// <summary>Optional local image path embedded instead of the YouTube thumbnail.</summary>
    public string? CustomCoverArtPath { get; init; }

    public TrackMetadataOverride? MetadataOverride { get; init; }

    public string? MusicArtistFolder { get; init; }

    public string? MusicAlbumFolder { get; init; }

    public int? PlaylistTrackIndex { get; init; }

    public int? PlaylistTrackTotal { get; init; }

    public string QueueLabel =>
        $"{ContentKindDetector.Emoji(ContentKind)} {ContentKindDetector.DisplayLabel(ContentKind)} · " +
        $"{DownloadFormats.ToDisplayLabel(Format)} · " +
        $"{(Scope == DownloadScope.Playlist ? "Playlist" : "Single")} · " +
        (!string.IsNullOrWhiteSpace(CollectionTitle)
            ? TruncateTitle(CollectionTitle)
            : string.IsNullOrWhiteSpace(PredictedTitle) ? TruncateUrl(Url) : TruncateTitle(PredictedTitle));

    private static string TruncateUrl(string url)
    {
        if (url.Length <= 56)
            return url;
        return url[..53] + "…";
    }

    private static string TruncateTitle(string title) =>
        title.Length <= 56 ? title : title[..53] + "…";

    public DownloadJob WithForceRedownload(bool forceRedownload) => new()
    {
        Url = Url,
        Scope = Scope,
        Format = Format,
        Quality = Quality,
        EmbedThumbnail = EmbedThumbnail,
        ContentKind = ContentKind,
        OutputFolder = OutputFolder,
        CustomFileName = CustomFileName,
        PredictedTitle = PredictedTitle,
        CollectionTitle = CollectionTitle,
        ForceRedownload = forceRedownload,
        CustomCoverArtPath = CustomCoverArtPath,
        MetadataOverride = MetadataOverride,
        MusicArtistFolder = MusicArtistFolder,
        MusicAlbumFolder = MusicAlbumFolder,
        PlaylistTrackIndex = PlaylistTrackIndex,
        PlaylistTrackTotal = PlaylistTrackTotal,
    };
}

public sealed class DownloadQueue
{
    private readonly List<DownloadJob> _items = [];
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_lock)
                return _items.Count;
        }
    }

    public void Enqueue(DownloadJob job)
    {
        lock (_lock)
            _items.Add(job);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EnqueueMany(IEnumerable<DownloadJob> jobs)
    {
        lock (_lock)
            _items.AddRange(jobs);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryDequeue(out DownloadJob? job)
    {
        lock (_lock)
        {
            if (_items.Count == 0)
            {
                job = null;
                return false;
            }

            job = _items[0];
            _items.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<DownloadJob> Snapshot()
    {
        lock (_lock)
            return _items.ToList();
    }

    public bool ContainsUrl(string url)
    {
        lock (_lock)
            return _items.Any(j => string.Equals(j.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        lock (_lock)
            _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _items.Count)
                _items.RemoveAt(index);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Move(int fromIndex, int toIndex)
    {
        lock (_lock)
        {
            if (fromIndex < 0 || fromIndex >= _items.Count || toIndex < 0 || toIndex >= _items.Count || fromIndex == toIndex)
                return;

            var item = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, item);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
