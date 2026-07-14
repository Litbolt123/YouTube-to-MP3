namespace YouTubeToMp3.Services;

public static class ContentPathResolver
{
    /// <summary>
    /// Which folder tree to use. When content is Auto, follows the selected format (MP3 → Music, MP4 → Videos).
    /// </summary>
    public static ContentKind ResolveStorageKind(ContentKind chosenContent, DownloadFormat format)
    {
        if (chosenContent != ContentKind.Auto)
            return chosenContent;

        return DownloadFormats.IsAudio(format) ? ContentKind.Music : ContentKind.Video;
    }

    public static string ResolveRootFolder(AppSettings settings, ContentKind storageKind)
    {
        if (storageKind == ContentKind.Music && !string.IsNullOrWhiteSpace(settings.MusicOutputFolder))
            return settings.MusicOutputFolder.Trim();

        if (storageKind == ContentKind.Video && !string.IsNullOrWhiteSpace(settings.VideosOutputFolder))
            return settings.VideosOutputFolder.Trim();

        return settings.OutputFolder;
    }

    public static string ResolveBaseFolder(string rootFolder, ContentKind storageKind, AppSettings settings)
    {
        var root = string.IsNullOrWhiteSpace(rootFolder)
            ? ResolveRootFolder(settings, storageKind)
            : rootFolder;

        if (!settings.UseContentSubfolders)
            return root;

        var sub = storageKind == ContentKind.Music
            ? settings.MusicSubfolder
            : settings.VideosSubfolder;

        return Path.Combine(root, sub);
    }

    public static string ResolveOutputFolder(
        AppSettings settings,
        ContentKind chosenContent,
        DownloadFormat format)
    {
        var storageKind = ResolveStorageKind(chosenContent, format);
        var root = ResolveRootFolder(settings, storageKind);
        return ResolveBaseFolder(root, storageKind, settings);
    }
}
