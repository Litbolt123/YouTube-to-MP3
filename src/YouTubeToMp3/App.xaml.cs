using System.Globalization;
using System.Windows;
using YouTubeToMp3.Services;
using Application = System.Windows.Application;

namespace YouTubeToMp3;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _updateTrayNotifiedThisSession;

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
            SchedulePostStartupUpdateCheck();
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

    private static void SchedulePostStartupUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                await CheckForUpdatesOnStartupAsync().ConfigureAwait(false);
            }
            catch
            {
                /* best-effort */
            }
        });
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        var r = await UpdateCheckService.CheckLatestReleaseAsync().ConfigureAwait(false);
        if (r.Success)
        {
            Settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveSettings();
        }

        if (!r.Success || r.NoPublishedReleases || !r.IsNewerThanCurrent ||
            string.IsNullOrWhiteSpace(r.LatestVersion))
        {
            UpdateAvailabilityCache.Clear();
            return;
        }

        if (string.Equals(Settings.DismissedUpdateVersion, r.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            UpdateAvailabilityCache.Clear();
            return;
        }

        UpdateAvailabilityCache.Set(r.LatestVersion, r.InstallerDownloadUrl);

        var showTray = Settings.NotifyTrayOnUpdate && !_updateTrayNotifiedThisSession;
        if (showTray)
            _updateTrayNotifiedThisSession = true;

        await Current.Dispatcher.InvokeAsync(() =>
        {
            if (Current.MainWindow is MainWindow mw)
                mw.ApplyStartupUpdate(r, showTray);
        });
    }

    public static void SaveSettings()
    {
        AppSettingsService.Save(Settings);
    }

    public static string VersionDisplay =>
        UpdateCheckService.CurrentAssemblyVersion.ToString(3);
}
