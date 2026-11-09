namespace YouTubeToMp3.Services;

/// <summary>Consistent scope labels across logs, queue, done panel, and status.</summary>
public static class DownloadScopeLabels
{
    public static string ForJob(DownloadJob job) =>
        For(job.Scope, job.Format, job.ContentKind);

    public static string For(DownloadScope scope, DownloadFormat format, ContentKind contentKind)
    {
        if (scope == DownloadScope.Playlist)
            return contentKind == ContentKind.Music ? "Album" : "Playlist";

        return DownloadFormats.IsVideo(format) ? "Single video" : "Single track";
    }

    public static string ForLog(DownloadScope scope, DownloadFormat format) =>
        scope == DownloadScope.Playlist
            ? "playlist"
            : DownloadFormats.IsVideo(format) ? "single video" : "single track";

    public static string ForDonePanel(DownloadJob? job, DownloadScope scope, DownloadFormat format, ContentKind contentKind)
    {
        if (job?.MusicAlbumFolder is not null && !string.IsNullOrWhiteSpace(job.MusicAlbumFolder))
            return "album track";

        return For(scope, format, contentKind).ToLowerInvariant();
    }

    public static string StatusFromError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Download failed.";

        var firstLine = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
            return "Download failed.";

        return message.Contains('\n') || message.Contains('\r')
            ? $"{firstLine} — see log for details"
            : firstLine;
    }
}
