namespace YouTubeToMp3.Services;

public static class BrowserExtensionInstaller
{
    public static void EnsureInstalled()
    {
        var dest = AppPaths.BrowserExtensionDirectory;
        if (Directory.Exists(dest) && File.Exists(Path.Combine(dest, "manifest.json")))
            return;

        var source = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(dest);
        CopyDirectory(source, dest);
    }

    public static string GetExtensionFolder()
    {
        EnsureInstalled();
        if (Directory.Exists(AppPaths.BrowserExtensionDirectory))
            return AppPaths.BrowserExtensionDirectory;

        var bundled = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        return Directory.Exists(bundled) ? bundled : AppPaths.DataDirectory;
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
