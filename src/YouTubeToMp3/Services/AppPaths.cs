namespace YouTubeToMp3.Services;

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeToMp3");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string StagingDirectory => Path.Combine(DataDirectory, "staging");

    public static string HistoryPath => Path.Combine(DataDirectory, "history.json");

    public static string DownloadArchivePath => Path.Combine(DataDirectory, "download-archive.txt");

    public static string BrowserExtensionDirectory => Path.Combine(DataDirectory, "browser-extension");

    public static string DefaultMusicFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
}
