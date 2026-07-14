using System.Text.Json;

namespace YouTubeToMp3.Services;

/// <summary>
/// Detects whether Local Music Hub is installed and linked to this app's music folder / API.
/// </summary>
public static partial class LocalMusicHubIntegration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static LocalMusicHubLinkStatus GetLinkStatus(string? musicOutputFolder = null)
    {
        musicOutputFolder ??= TryReadDownloaderMusicFolder();
        var installed = Directory.Exists(LocalMusicHubDataDirectory) ||
                        File.Exists(LocalMusicHubSettingsPath);

        if (!installed)
        {
            return new LocalMusicHubLinkStatus(
                Installed: false,
                SettingsFound: false,
                Linked: false,
                WatchingMusicFolder: false);
        }

        var hub = TryReadHubSettings();
        if (hub is null)
        {
            return new LocalMusicHubLinkStatus(
                Installed: true,
                SettingsFound: false,
                Linked: false,
                WatchingMusicFolder: false);
        }

        var linked = hub.IntegrateYouTubeDownloader;
        var watching = linked && FolderWatchedByHub(musicOutputFolder, hub.LibraryFolders);
        return new LocalMusicHubLinkStatus(
            Installed: true,
            SettingsFound: true,
            Linked: linked,
            WatchingMusicFolder: watching);
    }

    public static string LocalMusicHubDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalMusicHub");

    public static string LocalMusicHubSettingsPath =>
        Path.Combine(LocalMusicHubDataDirectory, "settings.json");

    private static string? TryReadDownloaderMusicFolder()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return null;

            var json = File.ReadAllText(AppPaths.SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("musicOutputFolder", out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var folder = prop.GetString();
                return string.IsNullOrWhiteSpace(folder) ? null : folder;
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static LocalMusicHubSettingsSnapshot? TryReadHubSettings()
    {
        try
        {
            if (!File.Exists(LocalMusicHubSettingsPath))
                return null;

            var json = File.ReadAllText(LocalMusicHubSettingsPath);
            return JsonSerializer.Deserialize<LocalMusicHubSettingsSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool FolderWatchedByHub(string? musicFolder, IReadOnlyList<string> libraryFolders)
    {
        if (string.IsNullOrWhiteSpace(musicFolder) || libraryFolders.Count == 0)
            return false;

        return libraryFolders.Any(f =>
            !string.IsNullOrWhiteSpace(f) &&
            string.Equals(Path.GetFullPath(f.Trim()), Path.GetFullPath(musicFolder.Trim()),
                StringComparison.OrdinalIgnoreCase));
    }

    public static string DescribeStatus(LocalMusicHubLinkStatus status)
    {
        if (!status.Installed)
            return "Local Music Hub not detected. Install it to browse and play downloads from your library.";

        if (!status.SettingsFound)
            return "Local Music Hub installed — open it once so settings are created.";

        if (!status.Linked)
            return "Local Music Hub found — enable YouTube Downloader integration in Music Hub Settings.";

        return status.WatchingMusicFolder
            ? "Linked with Local Music Hub — new music downloads auto-import into your library."
            : "Linked with Local Music Hub — it will read this app's music folder from settings.";
    }
}

public sealed class LocalMusicHubSettingsSnapshot
{
    public bool IntegrateYouTubeDownloader { get; set; } = true;
    public List<string> LibraryFolders { get; set; } = [];
}

public readonly record struct LocalMusicHubLinkStatus(
    bool Installed,
    bool SettingsFound,
    bool Linked,
    bool WatchingMusicFolder);
