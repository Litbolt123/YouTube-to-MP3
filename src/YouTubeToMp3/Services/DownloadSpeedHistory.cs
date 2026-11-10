using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public sealed class DownloadSpeedSnapshot
{
    public double DownloadMibPerSec { get; init; } = 12;
    public double MergeMibPerSec { get; init; } = 5;
    public double CoverRealtimeFactor { get; init; } = 8;
    public int SampleCount { get; init; }
}

public static class DownloadSpeedHistory
{
    private static readonly Regex DownloadDoneRegex = new(
        @"\[download\]\s+100%\s+of\s+([\d.]+)\s*(KiB|MiB|GiB|KB|MB|GB)\s+in\s+(\d{1,2}:\d{2}(?::\d{2})?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MergerRegex = new(@"\[Merger\]", RegexOptions.Compiled);
    private static readonly Regex DeleteOriginalRegex = new(@"Deleting original file", RegexOptions.Compiled);
    private static readonly Regex LogTimeRegex = new(@"^\[(\d{2}):(\d{2}):(\d{2})\]", RegexOptions.Compiled);
    private static readonly Regex SpeedRegex = new(
        @"at\s+([\d.]+)\s*(KiB|MiB|GiB|KB|MB|GB)/s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly object Gate = new();
    private static DownloadSpeedSnapshot? _cached;
    private static DateTime _cachedUtc = DateTime.MinValue;

    public static DownloadSpeedSnapshot GetSnapshot(bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (!forceRefresh && _cached is not null && DateTime.UtcNow - _cachedUtc < TimeSpan.FromMinutes(10))
                return _cached;

            _cached = LoadFromLogs() ?? Defaults();
            _cachedUtc = DateTime.UtcNow;
            TryPersist(_cached);
            return _cached;
        }
    }

    public static TimeSpan EstimateMerge(long totalBytes, TimeSpan? mediaDuration = null)
    {
        var snap = GetSnapshot();
        var fromBytes = totalBytes > 0
            ? TimeSpan.FromSeconds(totalBytes / (snap.MergeMibPerSec * 1024 * 1024))
            : TimeSpan.Zero;

        var fromDuration = mediaDuration is { } d && d > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Max(8, d.TotalSeconds / Math.Max(1.0, snap.CoverRealtimeFactor * 0.35)))
            : TimeSpan.Zero;

        var estimate = fromBytes > fromDuration ? fromBytes : fromDuration;
        if (estimate < TimeSpan.FromSeconds(5))
            estimate = TimeSpan.FromSeconds(5);
        return estimate;
    }

    public static TimeSpan EstimateCover(TimeSpan? mediaDuration)
    {
        var snap = GetSnapshot();
        if (mediaDuration is not { } d || d <= TimeSpan.Zero)
            return TimeSpan.FromSeconds(20);

        var seconds = d.TotalSeconds / Math.Max(1.0, snap.CoverRealtimeFactor);
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 600));
    }

    public static TimeSpan? EstimateDownloadRemaining(double? percent, long? totalBytes)
    {
        if (percent is null or <= 0 or >= 100)
            return null;

        var snap = GetSnapshot();
        if (totalBytes is { } bytes && bytes > 0)
        {
            var remainingBytes = bytes * (1 - percent.Value / 100.0);
            return TimeSpan.FromSeconds(remainingBytes / (snap.DownloadMibPerSec * 1024 * 1024));
        }

        return null;
    }

    private static DownloadSpeedSnapshot Defaults() => new();

    private static DownloadSpeedSnapshot? LoadFromLogs()
    {
        var persisted = TryLoadPersisted();
        var logDir = AppPaths.LogsDirectory;
        if (!Directory.Exists(logDir))
            return persisted;

        var downloadRates = new List<double>();
        var mergeRates = new List<double>();
        var coverFactors = new List<double>();

        foreach (var path in Directory.EnumerateFiles(logDir, "download-*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(40))
        {
            try
            {
                AnalyzeLog(path, downloadRates, mergeRates, coverFactors);
            }
            catch
            {
                /* ignore bad logs */
            }
        }

        if (downloadRates.Count == 0 && mergeRates.Count == 0 && persisted is not null)
            return persisted;

        return new DownloadSpeedSnapshot
        {
            DownloadMibPerSec = MedianOr(downloadRates, persisted?.DownloadMibPerSec ?? 12),
            MergeMibPerSec = MedianOr(mergeRates, persisted?.MergeMibPerSec ?? 5),
            CoverRealtimeFactor = MedianOr(coverFactors, persisted?.CoverRealtimeFactor ?? 8),
            SampleCount = downloadRates.Count + mergeRates.Count + coverFactors.Count,
        };
    }

    private static void AnalyzeLog(string path, List<double> downloadRates, List<double> mergeRates, List<double> coverFactors)
    {
        var lines = File.ReadLines(path);
        double? lastTotalBytes = null;
        DateTime? mergerAt = null;
        double? mergerBytes = null;
        DateTime? coverStart = null;
        TimeSpan? lastMediaHint = null;

        foreach (var line in lines)
        {
            var stamp = TryLogTime(line);

            foreach (Match speed in SpeedRegex.Matches(line))
            {
                if (TryToBytes(speed.Groups[1].Value, speed.Groups[2].Value, out var bps) && bps > 0)
                    downloadRates.Add(bps / (1024.0 * 1024.0));
            }

            var done = DownloadDoneRegex.Match(line);
            if (done.Success &&
                TryToBytes(done.Groups[1].Value, done.Groups[2].Value, out var total) &&
                QueueDisplayHelper.ParseEta(done.Groups[3].Value) is { } took &&
                took > TimeSpan.Zero)
            {
                lastTotalBytes = total;
                downloadRates.Add(total / took.TotalSeconds / (1024.0 * 1024.0));
            }

            if (MergerRegex.IsMatch(line))
            {
                mergerAt = stamp;
                mergerBytes = lastTotalBytes;
            }
            else if (mergerAt is not null && DeleteOriginalRegex.IsMatch(line) && stamp is not null)
            {
                var elapsed = stamp.Value - mergerAt.Value;
                if (elapsed.TotalSeconds >= 2 && mergerBytes is { } bytes && bytes > 0)
                    mergeRates.Add(bytes / elapsed.TotalSeconds / (1024.0 * 1024.0));
                mergerAt = null;
            }

            if (line.Contains("Embedding custom cover art", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Embedding cover art", StringComparison.OrdinalIgnoreCase))
            {
                coverStart ??= stamp;
            }
            else if (coverStart is not null &&
                     stamp is not null &&
                     line.Contains("Embedding cover art (", StringComparison.OrdinalIgnoreCase))
            {
                var elapsed = stamp.Value - coverStart.Value;
                if (elapsed.TotalSeconds >= 1 && lastMediaHint is { } media && media.TotalSeconds > 1)
                    coverFactors.Add(media.TotalSeconds / elapsed.TotalSeconds);
                coverStart = null;
            }

            if (FfmpegProgressParser.TryParseDuration(line, out var duration))
                lastMediaHint = duration;
            if (FfmpegProgressParser.TryParseTimestamp(line, out var pos) && pos > (lastMediaHint ?? TimeSpan.Zero))
                lastMediaHint = pos;
        }
    }

    private static DateTime? TryLogTime(string line)
    {
        var m = LogTimeRegex.Match(line);
        if (!m.Success)
            return null;
        if (!int.TryParse(m.Groups[1].Value, out var h) ||
            !int.TryParse(m.Groups[2].Value, out var min) ||
            !int.TryParse(m.Groups[3].Value, out var s))
            return null;
        var today = DateTime.Today;
        return today + new TimeSpan(h, min, s);
    }

    private static bool TryToBytes(string value, string unit, out double bytes)
    {
        bytes = 0;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return false;

        bytes = unit.ToLowerInvariant() switch
        {
            "kib" or "kb" => n * 1024,
            "mib" or "mb" => n * 1024 * 1024,
            "gib" or "gb" => n * 1024 * 1024 * 1024,
            _ => n,
        };
        return bytes > 0;
    }

    private static double MedianOr(List<double> values, double fallback)
    {
        var usable = values.Where(v => v > 0.05 && v < 500).OrderBy(v => v).ToList();
        if (usable.Count == 0)
            return fallback;
        return usable[usable.Count / 2];
    }

    private static string PersistPath => Path.Combine(AppPaths.DataDirectory, "speed-history.json");

    private static DownloadSpeedSnapshot? TryLoadPersisted()
    {
        try
        {
            var path = PersistPath;
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<DownloadSpeedSnapshot>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static void TryPersist(DownloadSpeedSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(PersistPath, JsonSerializer.Serialize(snapshot));
        }
        catch
        {
            /* ignore */
        }
    }
}
