using System.Globalization;

namespace YouTubeToMp3.Services;

public static class DownloadHistoryViewBuilder
{
    public sealed class HistoryDisplayItem
    {
        public required string Id { get; init; }
        public required DownloadHistoryEntry Entry { get; init; }
        public required string Title { get; init; }
        public required string OutputPath { get; init; }
        public required string TypeDisplay { get; init; }
        public required string TimeDisplay { get; init; }
        public required string GroupLabel { get; init; }
        public required int GroupOrder { get; init; }
        public required DateTime CompletedAt { get; init; }
        public bool IsCollection { get; init; }
    }

    public static IReadOnlyList<HistoryDisplayItem> Build(IEnumerable<DownloadHistoryEntry> entries)
    {
        var list = entries.Where(static e => e.Success).ToList();
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<HistoryDisplayItem>();

        foreach (var entry in list.Where(static e => e.IsCollection))
        {
            consumed.Add(entry.Id);
            rows.Add(ToDisplayItem(entry));
        }

        var legacy = list.Where(e => !consumed.Contains(e.Id)).ToList();

        foreach (var group in legacy
                     .GroupBy(GetLegacyCollectionKey)
                     .Where(static g => g.Key is not null && g.Count() >= 2))
        {
            var items = group.OrderByDescending(GetCompletedAt).ToList();
            foreach (var item in items)
                consumed.Add(item.Id);

            rows.Add(ToLegacyCollection(items));
        }

        foreach (var entry in legacy.Where(e => !consumed.Contains(e.Id)))
            rows.Add(ToDisplayItem(entry));

        return rows
            .OrderByDescending(static r => r.CompletedAt)
            .ToList();
    }

    private static HistoryDisplayItem ToDisplayItem(DownloadHistoryEntry entry)
    {
        var completedAt = GetCompletedAt(entry);
        var (groupLabel, groupOrder) = completedAt == DateTime.MinValue
            ? ("Unknown date", int.MaxValue)
            : DownloadHistoryGrouping.GetGroup(completedAt);

        var title = entry.IsCollection && !string.IsNullOrWhiteSpace(entry.CollectionTitle)
            ? entry.CollectionTitle!
            : entry.Title;

        return new HistoryDisplayItem
        {
            Id = entry.Id,
            Entry = entry,
            Title = title,
            OutputPath = entry.OutputPath,
            TypeDisplay = BuildTypeDisplay(entry),
            TimeDisplay = completedAt == DateTime.MinValue
                ? "—"
                : completedAt.ToString("t", CultureInfo.CurrentCulture),
            GroupLabel = groupLabel,
            GroupOrder = groupOrder,
            CompletedAt = completedAt,
            IsCollection = entry.IsCollection,
        };
    }

    private static HistoryDisplayItem ToLegacyCollection(List<DownloadHistoryEntry> items)
    {
        var newest = items[0];
        var folder = Path.GetDirectoryName(newest.OutputPath) ?? newest.OutputFolder;
        var listId = items.Select(static e => YouTubeUrlHelper.TryGetListId(e.Url)).FirstOrDefault(static id => id is not null);
        var url = listId is not null
            ? items.FirstOrDefault(e => YouTubeUrlHelper.TryGetListId(e.Url) == listId)?.Url ?? newest.Url
            : newest.Url;

        var title = items
            .Select(static e => e.Title)
            .FirstOrDefault(static t => !string.IsNullOrWhiteSpace(t))
            ?? Path.GetFileName(folder)
            ?? "Album";

        if (folder is not null &&
            title.Equals(Path.GetFileNameWithoutExtension(newest.OutputPath), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(Path.GetFileName(folder)))
        {
            title = Path.GetFileName(folder)!;
        }

        var synthetic = new DownloadHistoryEntry
        {
            Id = newest.Id,
            Url = url,
            Title = title,
            CollectionTitle = title,
            OutputPath = folder ?? newest.OutputPath,
            OutputFolder = newest.OutputFolder,
            Format = newest.Format,
            ContentKind = newest.ContentKind,
            Scope = nameof(DownloadScope.Playlist),
            CompletedUtc = newest.CompletedUtc,
            Success = true,
            IsCollection = true,
            TrackCount = items.Count,
        };

        return ToDisplayItem(synthetic);
    }

    private static string? GetLegacyCollectionKey(DownloadHistoryEntry entry)
    {
        var listId = YouTubeUrlHelper.TryGetListId(entry.Url);
        if (!string.IsNullOrWhiteSpace(listId))
            return $"list:{listId}:{GetDayKey(entry)}";

        if (!string.Equals(entry.ContentKind, nameof(ContentKind.Music), StringComparison.OrdinalIgnoreCase))
            return null;

        var folder = Path.GetDirectoryName(entry.OutputPath);
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        return $"dir:{folder}:{GetDayKey(entry)}";
    }

    private static string GetDayKey(DownloadHistoryEntry entry)
    {
        var at = GetCompletedAt(entry);
        return at == DateTime.MinValue ? "unknown" : at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTime GetCompletedAt(DownloadHistoryEntry entry) =>
        DownloadHistoryGrouping.TryParseCompletedUtc(entry.CompletedUtc, out var local)
            ? local
            : DateTime.MinValue;

    public static string BuildTypeDisplay(DownloadHistoryEntry entry)
    {
        var kind = entry.ContentKind;
        var format = entry.Format.ToUpperInvariant();
        var count = Math.Max(1, entry.TrackCount);

        if (!entry.IsCollection)
            return $"{kind} · {format} · Single";

        var isMusic = string.Equals(kind, nameof(ContentKind.Music), StringComparison.OrdinalIgnoreCase);
        var label = isMusic ? "Album" : "Playlist";
        var unit = isMusic
            ? (count == 1 ? "track" : "tracks")
            : (count == 1 ? "video" : "videos");
        return $"{kind} · {format} · {label} ({count} {unit})";
    }

    public static int CountMediaFiles(string path, DownloadFormat format)
    {
        if (Directory.Exists(path))
        {
            var ext = DownloadFormats.FileExtension(format);
            return Directory.EnumerateFiles(path, "*" + ext, SearchOption.AllDirectories).Count();
        }

        return File.Exists(path) ? 1 : 0;
    }
}
