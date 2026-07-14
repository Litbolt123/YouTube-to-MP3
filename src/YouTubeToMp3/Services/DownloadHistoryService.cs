using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeToMp3.Services;

public sealed class DownloadHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string Format { get; set; } = "mp3";
    public string ContentKind { get; set; } = "Video";
    public string Scope { get; set; } = "SingleVideo";
    public string CompletedUtc { get; set; } = "";
    public bool Success { get; set; }
}

public sealed class DownloadHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxEntries = 500;
    private readonly List<DownloadHistoryEntry> _entries = [];

    public IReadOnlyList<DownloadHistoryEntry> Entries => _entries;

    public void Load()
    {
        _entries.Clear();
        try
        {
            if (!File.Exists(AppPaths.HistoryPath))
                return;

            var json = File.ReadAllText(AppPaths.HistoryPath);
            var list = JsonSerializer.Deserialize<List<DownloadHistoryEntry>>(json, JsonOptions);
            if (list is not null)
                _entries.AddRange(list);
        }
        catch
        {
            /* ignore corrupt history */
        }
    }

    public void Add(DownloadHistoryEntry entry)
    {
        _entries.Insert(0, entry);
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
        Save();
    }

    public bool ContainsUrl(string url) =>
        _entries.Any(e => e.Success && string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));

    public DownloadHistoryEntry? FindByUrl(string url) =>
        _entries.FirstOrDefault(e => e.Success && string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));

    public DownloadHistoryEntry? FindByVideoId(string url)
    {
        var videoId = YouTubeUrlHelper.TryGetVideoId(url);
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        return _entries.FirstOrDefault(e =>
            e.Success &&
            string.Equals(YouTubeUrlHelper.TryGetVideoId(e.Url), videoId, StringComparison.OrdinalIgnoreCase));
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(AppPaths.HistoryPath, json);
        }
        catch
        {
            /* ignore */
        }
    }
}
