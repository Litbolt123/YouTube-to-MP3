using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeToMp3.Services;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = AppPaths.DefaultMusicFolder;

    /// <summary>Override save root for music. Empty = use OutputFolder.</summary>
    public string MusicOutputFolder { get; set; } = "";

    /// <summary>Override save root for videos. Empty = use OutputFolder.</summary>
    public string VideosOutputFolder { get; set; } = "";

    /// <summary>yt-dlp --audio-quality (0 = best VBR, 5 = default, 9 = worst).</summary>
    public int AudioQuality { get; set; } = 0;

    /// <summary>MP4 height preset (0 = best, 5 = 1080p, 9 = 720p, 10–13 = 480/360/240/144).</summary>
    public int VideoQuality { get; set; } = 0;

    /// <summary>mp3, m4a, opus, flac, wav, or mp4</summary>
    public string PreferredFormat { get; set; } = "mp3";

    /// <summary>Embed YouTube thumbnail as cover art inside the MP3/MP4 file.</summary>
    public bool EmbedThumbnail { get; set; } = true;

    /// <summary>When true, prompt to choose cover art before each download that embeds art.</summary>
    public bool AlwaysAskCoverArt { get; set; }

    /// <summary>When true, open album track review for music playlists before downloading.</summary>
    public bool ReviewAlbumTracksBeforeDownload { get; set; } = true;

    /// <summary>Show the download log panel on the home screen.</summary>
    public bool ShowLogPanel { get; set; }

    /// <summary>Save music and videos under separate subfolders.</summary>
    public bool UseContentSubfolders { get; set; } = true;

    public string MusicSubfolder { get; set; } = "Youtube Music";

    public string VideosSubfolder { get; set; } = "Youtube Videos";

    public bool AutoQueueWhenBusy { get; set; } = true;

    /// <summary>yt-dlp --concurrent-fragments. 0 or 1 = disabled; typical 2–8.</summary>
    public int ConcurrentFragments { get; set; } = 4;

    /// <summary>How many downloads may run at once (1–5). Each job includes download + convert/embed.</summary>
    public int MaxParallelDownloads { get; set; } = 1;

    /// <summary>Auto-skip URLs already in history or on disk (unless force redownload).</summary>
    public bool SkipAlreadyDownloaded { get; set; } = true;

    public bool PlaySoundOnComplete { get; set; } = true;

    public bool UseDarkTheme { get; set; }

    public bool MinimizeToTray { get; set; }

    /// <summary>Launch with Windows (HKCU Run). Starts minimized to tray.</summary>
    public bool StartWithWindows { get; set; }

    public bool NotifyOnQueueComplete { get; set; } = true;

    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Tray balloon when a newer GitHub release exists (startup check).</summary>
    public bool NotifyTrayOnUpdate { get; set; } = true;

    /// <summary>Allow the browser extension to queue downloads via localhost.</summary>
    public bool BrowserExtensionEnabled { get; set; } = true;

    /// <summary>Localhost port for the browser extension API.</summary>
    public int BrowserExtensionPort { get; set; } = 47384;

    /// <summary>Shared secret the extension must send with each request.</summary>
    public string BrowserExtensionToken { get; set; } = "";

    public string? LastUpdateCheckUtc { get; set; }

    public string? DismissedUpdateVersion { get; set; }
}

public static class AppSettingsService
{
    public const int DefaultExtensionPort = 47384;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions JsonOptions = SerializerOptions;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var migrated = MigrateDefaultSubfolders(settings);
            ClampDownloadLimits(settings);
            EnsureExtensionToken(settings);
            if (migrated)
                Save(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ClampDownloadLimits(settings);
        EnsureExtensionToken(settings);
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    public static void ClampDownloadLimits(AppSettings settings)
    {
        if (settings.ConcurrentFragments < 0)
            settings.ConcurrentFragments = 0;
        if (settings.ConcurrentFragments > 8)
            settings.ConcurrentFragments = 8;

        if (settings.MaxParallelDownloads < 1)
            settings.MaxParallelDownloads = 1;
        if (settings.MaxParallelDownloads > 5)
            settings.MaxParallelDownloads = 5;
    }

    public static void EnsureExtensionToken(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BrowserExtensionToken))
            settings.BrowserExtensionToken = Guid.NewGuid().ToString("N");
    }

    /// <summary>Rename legacy default subfolders so existing installs pick up the new names.</summary>
    private static bool MigrateDefaultSubfolders(AppSettings settings)
    {
        var changed = false;
        if (string.Equals(settings.MusicSubfolder, "Music", StringComparison.Ordinal))
        {
            settings.MusicSubfolder = "Youtube Music";
            changed = true;
        }

        if (string.Equals(settings.VideosSubfolder, "Videos", StringComparison.Ordinal))
        {
            settings.VideosSubfolder = "Youtube Videos";
            changed = true;
        }

        return changed;
    }
}
