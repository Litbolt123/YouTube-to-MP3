using Microsoft.Win32;

namespace YouTubeToMp3.Services;

/// <summary>
/// Registers <c>youtubetomp3://</c> so the browser extension can launch or wake the desktop app.
/// </summary>
public static class AppWakeProtocolService
{
    public const string Protocol = "youtubetomp3";
    private const string ClassesRoot = @"Software\Classes\" + Protocol;

    public static void EnsureRegistered()
    {
        var exe = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(exe))
            return;

        try
        {
            using var protocolKey = Registry.CurrentUser.CreateSubKey(ClassesRoot);
            if (protocolKey is null)
                return;

            protocolKey.SetValue("", "URL:YouTube Downloader", RegistryValueKind.String);
            protocolKey.SetValue("URL Protocol", "", RegistryValueKind.String);

            using var commandKey = protocolKey.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(
                "",
                $"\"{exe}\" --minimized --extension-wake \"%1\"",
                RegistryValueKind.String);
        }
        catch
        {
            /* registry writes can fail on locked-down machines */
        }
    }

    private static string? ResolveExecutablePath()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "YouTubeToMp3",
            "YouTubeToMp3.exe");
        if (File.Exists(installed))
            return installed;

        return Environment.ProcessPath;
    }
}
