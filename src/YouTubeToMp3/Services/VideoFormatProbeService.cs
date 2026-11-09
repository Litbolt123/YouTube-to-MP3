using System.Diagnostics;
using System.Text.Json;

namespace YouTubeToMp3.Services;

public sealed record VideoFormatProbeResult(
    IReadOnlyList<int> Heights,
    int? BestHeight,
    int? SuggestedHeight,
    int ScreenHeight);

/// <summary>Lists video heights yt-dlp can see for a URL (for the extension quality picker).</summary>
public static class VideoFormatProbeService
{
    public static async Task<VideoFormatProbeResult> ProbeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var screen = ScreenResolutionService.PreferredVideoHeight;
        var empty = new VideoFormatProbeResult([], null, screen, screen);
        var tools = ToolDependencyService.Check();
        if (!tools.YtDlpFound || tools.YtDlpPath is null || string.IsNullOrWhiteSpace(url))
            return empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("-J --no-download --no-warnings --no-playlist ");
        YouTubeExtractorArgs.Append(sb, tools, url, DownloadFormat.Mp4);
        sb.Append($"\"{url}\"");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.YtDlpPath,
                    Arguments = sb.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);
            if (!process.Start())
                return empty;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(40));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return empty;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(stdout))
                return empty;

            var heights = ParseHeights(stdout);
            var best = heights.Count > 0 ? heights[0] : (int?)null;
            var suggested = best is null
                ? screen
                : heights.Where(h => h <= screen).DefaultIfEmpty(heights[^1]).Max();
            return new VideoFormatProbeResult(heights, best, suggested, screen);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return empty;
        }
    }

    private static IReadOnlyList<int> ParseHeights(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var set = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));

        AddFromNode(root, set);
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
                AddFromNode(entry, set);
        }

        return set.ToList();
    }

    private static void AddFromNode(JsonElement node, SortedSet<int> set)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var fmt in formats.EnumerateArray())
                TryAddFormat(fmt, set);
        }

        if (node.TryGetProperty("height", out var direct))
            TryAddHeight(direct, HasVideoCodec(node), set);
    }

    private static void TryAddFormat(JsonElement fmt, SortedSet<int> set)
    {
        if (!HasVideoCodec(fmt))
            return;
        if (!fmt.TryGetProperty("height", out var heightEl))
            return;
        TryAddHeight(heightEl, true, set);
    }

    private static bool HasVideoCodec(JsonElement fmt)
    {
        if (!fmt.TryGetProperty("vcodec", out var codec))
            return fmt.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.GetInt32() > 0;

        var value = codec.GetString();
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAddHeight(JsonElement heightEl, bool isVideo, SortedSet<int> set)
    {
        if (!isVideo || heightEl.ValueKind != JsonValueKind.Number)
            return;

        var raw = heightEl.GetInt32();
        if (raw < 120)
            return;

        set.Add(raw);
    }
}
