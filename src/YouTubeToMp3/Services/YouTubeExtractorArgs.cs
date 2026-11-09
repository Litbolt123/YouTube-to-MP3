using System.Text;

namespace YouTubeToMp3.Services;

/// <summary>
/// yt-dlp YouTube extractor arguments. Client choice strongly affects available resolutions.
/// </summary>
public static class YouTubeExtractorArgs
{
    /// <summary>
    /// Broken or token-gated clients excluded from all downloads.
    /// android_sdkless: 403 on many videos (yt-dlp #15712). tv_simply/mweb: require PO tokens.
    /// </summary>
    private const string ExcludeBroken = ",-android_sdkless,-tv_simply,-mweb";

    /// <summary>
    /// Player clients that work without manual GVS PO tokens.
    /// Prefer yt-dlp's maintained default set, then exclude known-broken clients.
    /// </summary>
    public static string GetPlayerClients(string url, DownloadFormat format)
    {
        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url))
            return "default,web_music" + ExcludeBroken;

        if (format is DownloadFormat.Flac or DownloadFormat.Wav)
            return "ios,default" + ExcludeBroken;

        return "default" + ExcludeBroken;
    }

    public static void Append(StringBuilder sb, ToolCheckResult tools, string url, DownloadFormat format)
    {
        if (!IsYouTubeUrl(url))
            return;

        var clients = GetPlayerClients(url, format);
        sb.Append($"--extractor-args \"youtube:player_client={clients}\" ");

        if (tools.DenoFound && tools.DenoPath is not null)
        {
            sb.Append($"--js-runtimes deno:\"{tools.DenoPath}\" ");
            sb.Append("--remote-components ejs:github ");
        }
    }

    private static bool IsYouTubeUrl(string url) =>
        url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase);
}
