using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeToMp3.Services;

public static partial class LocalMusicHubIntegration
{
    private static readonly string[] ImportableAudioExtensions =
        [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma", ".webm"];

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string ImportRequestPath =>
        Path.Combine(LocalMusicHubDataDirectory, "import-request.json");

    /// <summary>True when Music Hub has been installed or its executable can be found.</summary>
    public static bool IsMusicHubAvailable() =>
        File.Exists(LocalMusicHubSettingsPath) ||
        Directory.Exists(LocalMusicHubDataDirectory) ||
        TryResolveHubExePath() is not null;

    public static bool CanImport(string? path, ContentKind contentKind = ContentKind.Auto)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!IsMusicHubAvailable())
            return false;

        if (contentKind == ContentKind.Video)
            return false;

        if (contentKind == ContentKind.Music)
            return true;

        if (IsImportableAudioPath(path) || IsUnderMusicOutputTree(path))
            return true;

        return false;
    }

    public static bool CanImportHistoryEntry(DownloadHistoryEntry entry)
    {
        if (!entry.Success || string.IsNullOrWhiteSpace(entry.OutputPath))
            return false;

        var kind = ParseContentKind(entry.ContentKind);
        return CanImport(entry.OutputPath, kind);
    }

    public static bool CanImportAlbumFolder(string? folder, ContentKind contentKind = ContentKind.Music)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;

        if (!IsMusicHubAvailable())
            return false;

        if (contentKind == ContentKind.Video)
            return false;

        return Directory.EnumerateFiles(folder)
            .Any(f => contentKind == ContentKind.Music || IsImportableAudioPath(f));
    }

    public static LocalMusicHubImportResult RequestImport(string path, ContentKind contentKind = ContentKind.Auto)
    {
        if (!IsMusicHubAvailable())
        {
            return LocalMusicHubImportResult.Fail(
                "Local Music Hub not found. Install it and open it once.");
        }

        if (!File.Exists(path))
            return LocalMusicHubImportResult.Fail("Saved file no longer exists.");

        if (contentKind == ContentKind.Video)
        {
            return LocalMusicHubImportResult.Fail(
                "This file cannot be imported (video-only downloads are not supported).");
        }

        if (contentKind != ContentKind.Music &&
            !IsImportableAudioPath(path) &&
            !IsUnderMusicOutputTree(path))
        {
            return LocalMusicHubImportResult.Fail(
                "This file type cannot be imported into Local Music Hub.");
        }

        try
        {
            Directory.CreateDirectory(LocalMusicHubDataDirectory);
            var payload = new LocalMusicHubImportRequest
            {
                Path = Path.GetFullPath(path),
                RequestedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(ImportRequestPath, JsonSerializer.Serialize(payload, ImportJsonOptions));

            if (!IsHubRunning())
            {
                var exe = TryResolveHubExePath();
                if (exe is null)
                {
                    return LocalMusicHubImportResult.Success(
                        "Import queued. Open Local Music Hub to add this track to your library.");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--import \"{payload.Path}\"",
                    UseShellExecute = true,
                });
            }

            return LocalMusicHubImportResult.Success("Sent to Local Music Hub.");
        }
        catch (Exception ex)
        {
            return LocalMusicHubImportResult.Fail(ex.Message);
        }
    }

    public static LocalMusicHubImportResult RequestImportAlbum(string folder, ContentKind contentKind = ContentKind.Music)
    {
        if (!IsMusicHubAvailable())
        {
            return LocalMusicHubImportResult.Fail(
                "Local Music Hub not found. Install it and open it once.");
        }

        if (!CanImportAlbumFolder(folder, contentKind))
            return LocalMusicHubImportResult.Fail("No importable music tracks found in this album folder.");

        try
        {
            Directory.CreateDirectory(LocalMusicHubDataDirectory);
            var fullFolder = Path.GetFullPath(folder);
            var payload = new LocalMusicHubImportRequest
            {
                Path = fullFolder,
                ImportFolder = true,
                RequestedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(ImportRequestPath, JsonSerializer.Serialize(payload, ImportJsonOptions));

            if (!IsHubRunning())
            {
                var exe = TryResolveHubExePath();
                if (exe is null)
                {
                    return LocalMusicHubImportResult.Success(
                        "Album import queued. Open Local Music Hub to add these tracks.");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--import \"{fullFolder}\" --import-folder",
                    UseShellExecute = true,
                });
            }

            return LocalMusicHubImportResult.Success("Sent album to Local Music Hub.");
        }
        catch (Exception ex)
        {
            return LocalMusicHubImportResult.Fail(ex.Message);
        }
    }

    public static bool IsHubRunning() =>
        Process.GetProcessesByName("LocalMusicHub").Length > 0;

    public static string? TryResolveHubExePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Local Music Hub", "LocalMusicHub.exe"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "Local Music Hub",
                "src", "LocalMusicHub", "bin", "Release", "net8.0-windows10.0.19041.0", "LocalMusicHub.exe")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "Local Music Hub",
                "src", "LocalMusicHub", "bin", "Debug", "net8.0-windows10.0.19041.0", "LocalMusicHub.exe")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsImportableAudioPath(string path) =>
        ImportableAudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsUnderMusicOutputTree(string path)
    {
        try
        {
            var settings = AppSettingsService.Load();
            var musicRoot = ContentPathResolver.ResolveOutputFolder(settings, ContentKind.Music, DownloadFormat.Mp3);
            if (string.IsNullOrWhiteSpace(musicRoot))
                return false;

            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(musicRoot);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static ContentKind ParseContentKind(string? value) =>
        value?.Equals("Music", StringComparison.OrdinalIgnoreCase) == true ? ContentKind.Music
        : value?.Equals("Video", StringComparison.OrdinalIgnoreCase) == true ? ContentKind.Video
        : ContentKind.Auto;
}

public sealed class LocalMusicHubImportRequest
{
    public string Path { get; set; } = "";
    public bool ImportFolder { get; set; }
    public string RequestedUtc { get; set; } = "";
}

public sealed class LocalMusicHubImportResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";

    public static LocalMusicHubImportResult Success(string message) => new() { Ok = true, Message = message };
    public static LocalMusicHubImportResult Fail(string message) => new() { Ok = false, Message = message };
}
