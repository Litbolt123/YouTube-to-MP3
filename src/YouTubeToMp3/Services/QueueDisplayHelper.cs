using System.Globalization;
using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public sealed class QueueRowViewModel
{
    public required int Position { get; init; }
    public required string Status { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public required string Format { get; init; }
    public required string Quality { get; init; }
    public required string Scope { get; init; }
    public required string Folder { get; init; }
    public required string Url { get; init; }
    public required string Progress { get; init; }
    public required DownloadJob Job { get; init; }
}

public static class QueueDisplayHelper
{
    private static readonly Regex LeadingIndexRegex = new(@"^\d+\s*[-–.]\s*", RegexOptions.Compiled);

    public static string TitleFor(DownloadJob job, QueueRuntimeState? runtime = null)
    {
        if (!string.IsNullOrWhiteSpace(job.CollectionTitle) &&
            (job.Scope == DownloadScope.Playlist || job.PlaylistTrackIndex is not null))
            return job.CollectionTitle!;

        if (runtime is not null && !string.IsNullOrWhiteSpace(runtime.CurrentTrackTitle))
            return runtime.CurrentTrackTitle!;

        return string.IsNullOrWhiteSpace(job.PredictedTitle) ? job.Url : job.PredictedTitle!;
    }

    public static string? PlaylistTrackHint(QueueRuntimeState? runtime, DownloadJob? job = null)
    {
        var idx = runtime?.PlaylistIndex ?? job?.PlaylistTrackIndex;
        var total = runtime?.PlaylistTotal ?? job?.PlaylistTrackTotal;
        if (idx is not { } trackIndex || total is not { } trackTotal)
            return null;

        return $"Track {trackIndex} of {trackTotal}";
    }

    public static string ScopeFor(DownloadJob job) =>
        job.Scope == DownloadScope.Playlist ? "Playlist" : "Single";

    public static QueueRowViewModel ToRow(
        DownloadJob job,
        int position,
        string status,
        string progress = "—",
        QueueRuntimeState? runtime = null)
    {
        return new QueueRowViewModel
        {
            Position = position,
            Status = status,
            Title = TitleFor(job, status == "Active" ? runtime : null),
            Kind = ContentKindDetector.DisplayLabel(job.ContentKind),
            Format = DownloadFormats.ToDisplayLabel(job.Format),
            Quality = QualityPresets.GetLabel(job.Format, job.Quality),
            Scope = ScopeFor(job),
            Folder = job.OutputFolder,
            Url = job.Url,
            Progress = progress,
            Job = job,
        };
    }

    public static string FormatActiveProgress(QueueRuntimeState state)
    {
        var parts = new List<string>();
        if (state.Percent is { } p)
            parts.Add($"{p.ToString("0.#", CultureInfo.InvariantCulture)}%");
        if (!string.IsNullOrWhiteSpace(state.LiveFileEta))
            parts.Add($"ETA {state.LiveFileEta}");
        if (!string.IsNullOrWhiteSpace(state.Speed))
            parts.Add(state.Speed);
        if (state.PlaylistIndex is { } idx && state.PlaylistTotal is { } total)
            parts.Add($"Item {idx}/{total}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Starting…";
    }

    public static string BuildSummary(
        bool isProcessing,
        int waiting,
        int completed,
        QueueRuntimeState? active,
        TimeSpan? queueEta = null,
        int activeCount = 0,
        bool queuePaused = false)
    {
        var parts = new List<string>();
        if (isProcessing)
        {
            var n = activeCount > 0 ? activeCount : 1;
            parts.Add(n == 1 ? "1 active" : $"{n} active");
        }

        if (waiting > 0)
            parts.Add($"{waiting} waiting");
        if (completed > 0)
            parts.Add($"{completed} completed");
        if (queuePaused)
            parts.Add("paused");

        if (parts.Count == 0)
            return "Queue is empty.";

        var line = string.Join(" · ", parts);
        if (queueEta is { } eta && eta > TimeSpan.Zero)
            line += $" · queue ETA ~{FormatDuration(eta)}";
        else if (isProcessing && active is not null && !string.IsNullOrWhiteSpace(active.Eta))
            line += $" · current file ETA {active.Eta}";

        return line;
    }

    public static string CleanTrackTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var name = Path.GetFileNameWithoutExtension(raw.Trim());
        name = LeadingIndexRegex.Replace(name, "");
        return string.IsNullOrWhiteSpace(name) ? raw.Trim() : name.Trim();
    }

    public static TimeSpan? ParseEta(string? eta)
    {
        if (string.IsNullOrWhiteSpace(eta))
            return null;

        var parts = eta.Trim().Split(':');
        try
        {
            return parts.Length switch
            {
                2 => new TimeSpan(0, int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)),
                3 => new TimeSpan(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture)),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public static TimeSpan? EstimateQueueRemaining(
        QueueRuntimeState? runtime,
        DownloadJob? activeJob,
        IReadOnlyList<DownloadJob> waiting,
        IReadOnlyList<TimeSpan> completedJobDurations,
        IReadOnlyList<TimeSpan> completedItemDurations)
    {
        if (activeJob is null && waiting.Count == 0)
            return null;

        var remaining = TimeSpan.Zero;
        var avgJob = AverageOrDefault(completedJobDurations, TimeSpan.FromMinutes(2));
        var avgItem = AverageOrDefault(completedItemDurations, TimeSpan.FromSeconds(45));

        if (activeJob is not null && runtime is not null)
        {
            var currentEta = ParseEta(runtime.Eta);
            if (currentEta is { } cur)
                remaining += cur;
            else if (runtime.Percent is > 0 and < 100 && completedItemDurations.Count > 0)
                remaining += TimeSpan.FromTicks((long)(avgItem.Ticks * (1 - runtime.Percent.Value / 100.0)));
            else
                remaining += avgItem;

            if (activeJob.Scope == DownloadScope.Playlist &&
                runtime.PlaylistIndex is { } idx &&
                runtime.PlaylistTotal is { } total &&
                total > idx)
            {
                remaining += TimeSpan.FromTicks(avgItem.Ticks * (total - idx));
            }
        }

        foreach (var job in waiting)
        {
            if (job.Scope == DownloadScope.Playlist)
            {
                // Unknown playlist length — assume a short album-sized batch.
                remaining += TimeSpan.FromTicks(avgItem.Ticks * 8);
            }
            else
            {
                remaining += avgJob;
            }
        }

        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}";
        return $"{(int)value.TotalMinutes}:{value.Seconds:D2}";
    }

    private static TimeSpan AverageOrDefault(IReadOnlyList<TimeSpan> values, TimeSpan fallback)
    {
        if (values.Count == 0)
            return fallback;

        var ticks = values.Average(static v => (double)v.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
}
