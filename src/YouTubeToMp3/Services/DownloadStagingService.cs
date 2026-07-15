namespace YouTubeToMp3.Services;

/// <summary>
/// Writes yt-dlp output to a private staging folder, then moves finished files into the library
/// so folder watchers (e.g. Local Music Hub) do not lock in-progress or metadata temp files.
/// </summary>
public static class DownloadStagingService
{
    public static string CreateSessionDirectory()
    {
        var dir = Path.Combine(AppPaths.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string MapToFinalPath(string stagingDir, string outputFolder, string stagingPath)
    {
        var rel = Path.GetRelativePath(stagingDir, stagingPath);
        return Path.GetFullPath(Path.Combine(outputFolder, rel));
    }

    public static bool IsIncompleteArtifact(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains(".temp.", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static IReadOnlyList<string> ListMediaFiles(string stagingDir)
    {
        if (!Directory.Exists(stagingDir))
            return [];

        return Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories)
            .Where(path => !IsIncompleteArtifact(path))
            .Where(CoverArtEmbedService.IsEmbeddableMedia)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> CommitAll(string stagingDir, string outputFolder)
    {
        if (!Directory.Exists(stagingDir))
            return [];

        var committed = new List<string>();
        foreach (var stagingPath in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            if (IsIncompleteArtifact(stagingPath))
                continue;
            if (!CoverArtEmbedService.IsEmbeddableMedia(stagingPath))
                continue;

            var dest = MapToFinalPath(stagingDir, outputFolder, stagingPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (File.Exists(dest))
            {
                try
                {
                    File.Delete(dest);
                }
                catch (IOException ex) when (IsAccessDenied(ex))
                {
                    throw new IOException(
                        $"Could not replace \"{Path.GetFileName(dest)}\". Close Local Music Hub or any app playing that file, then try again.",
                        ex);
                }
            }

            try
            {
                File.Move(stagingPath, dest);
            }
            catch (IOException ex) when (IsAccessDenied(ex))
            {
                throw new IOException(
                    $"Could not save \"{Path.GetFileName(dest)}\". Close Local Music Hub or any app using that file, then try again.",
                    ex);
            }

            committed.Add(dest);
        }

        TryDeleteStagingDirectory(stagingDir);
        return committed;
    }

    public static void TryDeleteStagingDirectory(string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    private static bool IsAccessDenied(IOException ex) =>
        ex.HResult == unchecked((int)0x80070005) ||
        ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
}
