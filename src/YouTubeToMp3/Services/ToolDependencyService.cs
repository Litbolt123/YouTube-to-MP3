using System.Diagnostics;

namespace YouTubeToMp3.Services;

public sealed record ToolCheckResult(
    bool YtDlpFound,
    string? YtDlpPath,
    bool FfmpegFound,
    string? FfmpegPath,
    bool DenoFound,
    string? DenoPath)
{
    public bool AllFound => YtDlpFound && FfmpegFound;

    public string MissingSummary
    {
        get
        {
            var missing = new List<string>();
            if (!YtDlpFound) missing.Add("yt-dlp");
            if (!FfmpegFound) missing.Add("ffmpeg");
            return string.Join(" and ", missing);
        }
    }
}

public static class ToolDependencyService
{
    private static string? _cachedYtDlpVersion;

    private static string BundledToolsDir =>
        Path.Combine(AppContext.BaseDirectory, "tools");

    public static ToolCheckResult Check()
    {
        var yt = FindBundled("yt-dlp.exe")
                 ?? FindOnPath("yt-dlp.exe")
                 ?? FindOnPath("yt-dlp");
        var ff = FindBundled("ffmpeg.exe")
                 ?? FindOnPath("ffmpeg.exe")
                 ?? FindOnPath("ffmpeg");
        var deno = FindOnPath("deno.exe") ?? FindOnPath("deno");
        return new ToolCheckResult(yt is not null, yt, ff is not null, ff, deno is not null, deno);
    }

    private static string? FindBundled(string fileName)
    {
        try
        {
            var candidate = Path.Combine(BundledToolsDir, fileName);
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                /* ignore bad PATH entries */
            }
        }

        return null;
    }

    public static string InstallHint =>
        "yt-dlp and ffmpeg should be included with this install (in the app tools folder).\n\n" +
        "If they are missing, reinstall from the latest setup, or install yt-dlp with:\n\n" +
        "winget install yt-dlp.yt-dlp -e";

    public static string? TryGetYtDlpVersion(string? ytDlpPath)
    {
        if (string.IsNullOrWhiteSpace(ytDlpPath))
            return null;

        if (_cachedYtDlpVersion is not null)
            return _cachedYtDlpVersion;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null)
                return null;

            var version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(version))
                _cachedYtDlpVersion = version;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    public static string DescribeToolsStatus(ToolCheckResult tools)
    {
        var lines = new List<string>();
        if (tools.YtDlpFound)
        {
            var version = TryGetYtDlpVersion(tools.YtDlpPath);
            lines.Add(version is null
                ? $"yt-dlp: {tools.YtDlpPath}"
                : $"yt-dlp {version}: {tools.YtDlpPath}");
        }
        else
            lines.Add("yt-dlp: not found");

        lines.Add(tools.FfmpegFound ? $"ffmpeg: {tools.FfmpegPath}" : "ffmpeg: not found");
        lines.Add(tools.DenoFound
            ? $"Deno: {tools.DenoPath} (YouTube JS — recommended)"
            : "Deno: not on PATH — winget install DenoLand.Deno -e, then restart the app");

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildYouTube403Hint(ToolCheckResult tools, DownloadFormat format)
    {
        var steps = new List<string> { "YouTube returned HTTP 403 or blocked the selected format." };

        steps.Add("Try:");
        steps.Add("1. Retry the download once");
        steps.Add("2. Update the app from GitHub Releases (refreshes bundled yt-dlp)");

        if (!tools.DenoFound)
            steps.Add("3. winget install DenoLand.Deno -e, then restart the app (required for YouTube in 2026)");
        else if (DownloadFormats.IsVideo(format))
            steps.Add("3. For MP4, try a lower quality preset if only audio streams are returned");
        else
            steps.Add("3. Check DIAG: lines in the log file for player_client and format details");

        steps.Add("4. See the log file for full yt-dlp output and DIAG diagnostics");

        steps.Add("");
        steps.Add("If it still fails, YouTube may require a PO token for your network. " +
                  "See: https://github.com/yt-dlp/yt-dlp/wiki/PO-Token-Guide");

        return string.Join(Environment.NewLine, steps);
    }

    public static string YouTubeMusicFormatHint =>
        "YouTube Music album — no audio formats were returned.\n\n" +
        "Try:\n" +
        "1. Update the app from GitHub Releases\n" +
        "2. winget install DenoLand.Deno -e, then restart the app (Deno is required for YouTube in 2026)\n" +
        "3. Retry — the app now uses music.youtube.com for OLAK5uy_ album links\n\n" +
        "Some albums are upload-restricted on YouTube; a track may only be streamable in the YouTube Music app.";
}
