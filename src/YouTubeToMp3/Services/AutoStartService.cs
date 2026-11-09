using Microsoft.Win32;

namespace YouTubeToMp3.Services;

/// <summary>
/// Toggles "start with Windows" via the per-user HKCU\...\CurrentVersion\Run key.
/// Starts minimized to tray so the browser extension bridge is ready without a window.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YouTubeToMp3";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key is null)
                return false;
            return key.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return;
                key.SetValue(ValueName, $"\"{exe}\" --minimized", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            /* registry writes can fail on locked-down machines */
        }
    }

    public static bool ArgsRequestTray(IEnumerable<string> args)
    {
        var list = args as string[] ?? args.ToArray();
        return list.Any(a =>
                   string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) ||
               ArgsExtensionWake(list);
    }

    public static bool ArgsExtensionWake(IEnumerable<string> args) =>
        args.Any(a =>
            string.Equals(a, "--extension-wake", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("youtubetomp3:", StringComparison.OrdinalIgnoreCase));
}
