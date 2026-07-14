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

    public static string YouTube403Hint =>
        "YouTube returned HTTP 403 or only low quality (~360p) was available.\n" +
        "Try:\n" +
        "1. Update the app from GitHub Releases (bundled yt-dlp updates with each installer)\n" +
        "2. winget upgrade yt-dlp.yt-dlp -e\n" +
        "3. winget install DenoLand.Deno -e (then open a new terminal)\n" +
        "4. Retry the download\n\n" +
        "If quality is still capped, YouTube may require a PO token for your network. " +
        "See: https://github.com/yt-dlp/yt-dlp/wiki/PO-Token-Guide";

    public static string YouTubeMusicFormatHint =>
        "YouTube Music album — no audio formats were returned.\n\n" +
        "Try:\n" +
        "1. Update the app from GitHub Releases\n" +
        "2. winget install DenoLand.Deno -e, then restart the app (Deno is required for YouTube in 2026)\n" +
        "3. Retry — the app now uses music.youtube.com for OLAK5uy_ album links\n\n" +
        "Some albums are upload-restricted on YouTube; a track may only be streamable in the YouTube Music app.";
}
