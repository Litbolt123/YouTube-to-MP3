using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public readonly record struct SelectedFormatInfo(
    string FormatId,
    string VideoCodec,
    string AudioCodec,
    string Height,
    string Fps);

/// <summary>Structured diagnostic lines for download logs (grep for "DIAG:").</summary>
public static class DownloadDiagnostics
{
    private static readonly Regex FormatInfoRegex = new(
        @"\[info\].*Downloading \d+ format\(s\):\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string Prefix = "DIAG:";

    private static string AppVersion =>
        typeof(DownloadDiagnostics).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static IEnumerable<string> BuildPreflight(
        ToolCheckResult tools,
        string url,
        DownloadFormat format,
        DownloadScope scope,
        ContentKind contentKind,
        int quality,
        int concurrentFragments,
        bool useDownloadArchive,
        bool embedThumbnail,
        string outputTemplate,
        TrackMetadataOverride? metadataOverride)
    {
        yield return $"{Prefix} --- preflight ---";
        yield return $"{Prefix} app {AppVersion}";

        var ytVersion = ToolDependencyService.TryGetYtDlpVersion(tools.YtDlpPath);
        yield return ytVersion is null
            ? $"{Prefix} yt-dlp: {tools.YtDlpPath ?? "(missing)"}"
            : $"{Prefix} yt-dlp {ytVersion}: {tools.YtDlpPath}";

        yield return tools.FfmpegFound
            ? $"{Prefix} ffmpeg: {tools.FfmpegPath}"
            : $"{Prefix} ffmpeg: (missing)";

        yield return tools.DenoFound
            ? $"{Prefix} Deno: {tools.DenoPath} (YouTube JS enabled)"
            : $"{Prefix} Deno: not on PATH — YouTube may fail without it";

        var mode = VideoQualityModes.Parse(App.Settings.VideoQualityMode);
        var plan = DownloadFormats.IsVideo(format) ? VideoDownloadPlan.Create(mode, quality) : null;
        yield return $"{Prefix} format={DownloadFormats.ToDisplayLabel(format)} scope={scope} content={contentKind}";
        yield return $"{Prefix} quality={QualityPresets.GetLabel(format, quality)} ({quality})";
        if (plan is not null)
        {
            yield return $"{Prefix} quality_mode={VideoQualityModes.ToTag(plan.Mode)} container={plan.Container}";
            yield return $"{Prefix} merge_ffmpeg={plan.MergerArgs}";
            yield return $"{Prefix} fallback={plan.FallbackNote}";
        }
        yield return $"{Prefix} concurrent_fragments={concurrentFragments} download_archive={useDownloadArchive} embed_thumbnail={embedThumbnail}";

        if (IsYouTubeUrl(url))
        {
            yield return $"{Prefix} youtube_player_client={YouTubeExtractorArgs.GetPlayerClients(url, format)}";
            yield return $"{Prefix} format_selector={DescribeFormatSelector(format, quality)}";
        }

        if (metadataOverride?.HasAny == true)
            yield return $"{Prefix} metadata_override=true";

        yield return $"{Prefix} output_template={outputTemplate}";
        yield return $"{Prefix} url={url}";
    }

    public static string DescribeFormatSelector(DownloadFormat format, int quality) =>
        DownloadFormats.IsAudio(format)
            ? AudioExtractArgs.GetFormatSelector(format)
            : VideoDownloadPlan.Create(VideoQualityModes.Parse(App.Settings.VideoQualityMode), quality).FormatSelector;

    public static bool TryParseSelectedFormat(string line, out SelectedFormatInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        const string marker = "SELECTED:";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var payload = line[(index + marker.Length)..].Trim();
        var parts = payload.Split('|');
        if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[0]))
            return false;

        info = new SelectedFormatInfo(
            parts[0].Trim(),
            parts[1].Trim(),
            parts[2].Trim(),
            parts[3].Trim(),
            parts[4].Trim());
        return true;
    }

    public static string FormatSelectedLine(SelectedFormatInfo info) =>
        $"{Prefix} selected_format id={info.FormatId} vcodec={info.VideoCodec} acodec={info.AudioCodec} height={info.Height} fps={info.Fps}";

    public static IEnumerable<string> FormatCommand(string exePath, string arguments)
    {
        yield return $"{Prefix} command: {exePath}";
        yield return $"{Prefix} args: {arguments}";
    }

    public static IEnumerable<string> BuildRunSummary(
        IReadOnlyList<string> lines,
        int exitCode,
        DownloadFormat format,
        ToolCheckResult tools,
        bool success)
    {
        yield return $"{Prefix} --- run summary (exit {exitCode}, success={success}) ---";

        var combined = string.Join('\n', lines);

        foreach (var formatLine in lines.Where(l => FormatInfoRegex.IsMatch(l)))
        {
            var match = FormatInfoRegex.Match(formatLine);
            yield return $"{Prefix} selected_formats={match.Groups[1].Value.Trim()}";
        }

        foreach (var line in lines)
        {
            if (TryParseSelectedFormat(line, out var selected))
                yield return FormatSelectedLine(selected);
        }

        foreach (var warning in SummarizeWarnings(combined))
            yield return $"{Prefix} {warning}";

        if (!success && combined.Contains("403", StringComparison.Ordinal) &&
            combined.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{Prefix} likely_cause=HTTP 403 — see hints below";
            if (!tools.DenoFound)
                yield return $"{Prefix} action=install Deno and restart the app";
            else if (combined.Contains("SABR", StringComparison.OrdinalIgnoreCase))
                yield return $"{Prefix} action=update app (refreshes yt-dlp) and retry; YouTube SABR experiment may block some clients";
            else
                yield return $"{Prefix} action=retry once, then update app from GitHub Releases";
        }

        if (combined.Contains("Only images are available", StringComparison.OrdinalIgnoreCase))
            yield return $"{Prefix} likely_cause=no downloadable media formats returned (JS runtime or PO token issue)";

        if (combined.Contains("Signature solving failed", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("n challenge solving failed", StringComparison.OrdinalIgnoreCase))
            yield return $"{Prefix} likely_cause=YouTube JS challenge failed — Deno + latest yt-dlp required";
    }

    private static IEnumerable<string> SummarizeWarnings(string combined)
    {
        if (combined.Contains("SABR", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("missing a url", StringComparison.OrdinalIgnoreCase))
            yield return "warning=SABR-only streaming experiment — some HTTPS formats skipped";

        if (combined.Contains("PO Token", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("PO token", StringComparison.OrdinalIgnoreCase))
            yield return "warning=PO token required for some clients";

        if (combined.Contains("android_sdkless", StringComparison.OrdinalIgnoreCase))
            yield return "warning=android_sdkless client involved";

        if (combined.Contains("tv_simply", StringComparison.OrdinalIgnoreCase))
            yield return "warning=tv_simply client involved (usually excluded)";

        if (combined.Contains("Signature solving failed", StringComparison.OrdinalIgnoreCase))
            yield return "warning=signature solving failed";

        if (combined.Contains("n challenge solving failed", StringComparison.OrdinalIgnoreCase))
            yield return "warning=n challenge solving failed";

        if (combined.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            yield return "warning=requested format not available";
    }

    private static bool IsYouTubeUrl(string url) =>
        url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase);
}
