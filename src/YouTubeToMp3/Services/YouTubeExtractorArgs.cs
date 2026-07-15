using System.Text;

namespace YouTubeToMp3.Services;

/// <summary>
/// yt-dlp YouTube extractor arguments. Client choice strongly affects available resolutions.
/// </summary>
public static class YouTubeExtractorArgs
{
    /// <summary>
    /// Player clients that expose high-res streams without a manual GVS PO token.
    /// android_vr: no PO token required (yt-dlp wiki). web_safari: HLS without GVS token.
    /// Avoid mweb/web-only — they skip HTTPS/DASH formats without a PO token (falls back to ~360p format 18).
    /// </summary>
    public static string GetPlayerClients(string url, DownloadFormat format)
    {
        if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(url))
            return "web_music,android_vr,tv_simply,default";

        if (format is DownloadFormat.Flac or DownloadFormat.Wav)
            return "ios,android_vr,tv_simply,web_safari,default,web_music";

        return DownloadFormats.IsVideo(format)
            ? "android_vr,tv_simply,tv,web_safari,default"
            : "android_vr,tv_simply,default,web_music";
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
