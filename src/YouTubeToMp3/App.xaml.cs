using System.Globalization;
using System.Windows;
using YouTubeToMp3.Services;
using Application = System.Windows.Application;

namespace YouTubeToMp3;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static AppSettings Settings { get; private set; } = new();

    public static void ReplaceSettings(AppSettings settings) => Settings = settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceService.TryBecomePrimaryInstance(out _singleInstanceMutex))
        {
            SingleInstanceService.NotifyPrimaryInstance();
            Shutdown();
            return;
        }

        Settings = AppSettingsService.Load();
        Settings.StartWithWindows = AutoStartService.IsEnabled();
        if (Settings.StartWithWindows)
            Settings.MinimizeToTray = true;
        DashboardUi.ApplyAppTheme(Settings.UseDarkTheme);
        base.OnStartup(e);

        if (Settings.AutoCheckUpdates)
            _ = CheckForUpdatesOnStartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var r = await UpdateCheckService.CheckLatestReleaseAsync().ConfigureAwait(false);
            if (!r.Success || !r.IsNewerThanCurrent || string.IsNullOrWhiteSpace(r.LatestVersion))
                return;

            if (string.Equals(Settings.DismissedUpdateVersion, r.LatestVersion, StringComparison.OrdinalIgnoreCase))
                return;

            await Current.Dispatcher.InvokeAsync(() =>
            {
                if (Current.MainWindow is MainWindow mw)
                    mw.ShowUpdateAvailable(r);
            });
        }
        catch
        {
            /* best-effort */
        }
    }

    public static void SaveSettings()
    {
        AppSettingsService.Save(Settings);
    }

    public static string VersionDisplay =>
        UpdateCheckService.CurrentAssemblyVersion.ToString(3);
}
