namespace YouTubeToMp3.Services;

public static class DownloadJobPathHelper
{
    public static string? ResolveExpectedOutputPath(DownloadJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CustomFileName))
            return null;

        var ext = DownloadFormats.FileExtension(job.Format);

        if (!string.IsNullOrWhiteSpace(job.MusicArtistFolder) &&
            !string.IsNullOrWhiteSpace(job.MusicAlbumFolder))
        {
            return Path.Combine(
                job.OutputFolder,
                OutputTemplateBuilder.SanitizeFileName(job.MusicArtistFolder),
                OutputTemplateBuilder.SanitizeFileName(job.MusicAlbumFolder),
                job.CustomFileName + ext);
        }
        return Path.Combine(job.OutputFolder, job.CustomFileName + ext);
    }

    public static string? ResolveAlbumFolder(DownloadJob job)
    {
        if (string.IsNullOrWhiteSpace(job.MusicArtistFolder) ||
            string.IsNullOrWhiteSpace(job.MusicAlbumFolder))
        {
            return null;
        }

        return Path.Combine(
            job.OutputFolder,
            OutputTemplateBuilder.SanitizeFileName(job.MusicArtistFolder),
            OutputTemplateBuilder.SanitizeFileName(job.MusicAlbumFolder));
    }
}
