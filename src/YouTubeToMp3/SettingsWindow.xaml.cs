using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace YouTubeToMp3;

public partial class SettingsWindow
{
    private UpdateCheckResult? _lastCheck;

    public SettingsWindow()
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();
        DownloadFormats.PopulateCombo(FormatCombo, compactLabels: true);
        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = App.Settings;
        FolderBox.Text = s.OutputFolder;
        MusicFolderBox.Text = s.MusicOutputFolder;
        VideosFolderBox.Text = s.VideosOutputFolder;
        SelectFormat(s.PreferredFormat);
        EmbedThumbnailBox.IsChecked = s.EmbedThumbnail;
        AlwaysAskCoverArtBox.IsChecked = s.AlwaysAskCoverArt;
        ReviewAlbumTracksBox.IsChecked = s.ReviewAlbumTracksBeforeDownload;
        ShowLogPanelBox.IsChecked = s.ShowLogPanel;
        UseSubfoldersBox.IsChecked = s.UseContentSubfolders;
        MusicSubfolderBox.Text = s.MusicSubfolder;
        VideosSubfolderBox.Text = s.VideosSubfolder;
        DarkThemeBox.IsChecked = s.UseDarkTheme;
        MinimizeToTrayBox.IsChecked = s.MinimizeToTray;
        StartWithWindowsBox.IsChecked = AutoStartService.IsEnabled() || s.StartWithWindows;
        NotifyQueueBox.IsChecked = s.NotifyOnQueueComplete;
        SkipAlreadyDownloadedBox.IsChecked = s.SkipAlreadyDownloaded;
        MaxParallelBox.Text = s.MaxParallelDownloads.ToString(CultureInfo.InvariantCulture);
        ConcurrentFragmentsBox.Text = s.ConcurrentFragments.ToString(CultureInfo.InvariantCulture);
        PlaySoundBox.IsChecked = s.PlaySoundOnComplete;
        BrowserExtensionBox.IsChecked = s.BrowserExtensionEnabled;
        ExtensionPortBox.Text = s.BrowserExtensionPort.ToString(CultureInfo.InvariantCulture);
        ExtensionTokenBox.Text = s.BrowserExtensionToken;
        ExtensionStatusText.Text =
            s.BrowserExtensionEnabled
                ? $"Listening on http://127.0.0.1:{s.BrowserExtensionPort}/ when the app is open."
                : "Local API disabled — browser extension and Local Music Hub cannot queue downloads.";
        MusicHubStatusText.Text = LocalMusicHubIntegration.DescribeStatus(
            LocalMusicHubIntegration.GetLinkStatus(s.MusicOutputFolder));
        AutoCheckBox.IsChecked = s.AutoCheckUpdates;
        NotifyTrayOnUpdateBox.IsChecked = s.NotifyTrayOnUpdate;
        AboutText.Text =
            $"Version {App.VersionDisplay}\n" +
            $"Settings: {AppPaths.DataDirectory}\n" +
            $"Download logs: {AppPaths.LogsDirectory}\n" +
            $"History: {AppPaths.HistoryPath}\n\n" +
            "Pairs with Local Music Hub (v0.3+) to auto-import music downloads into your library.\n" +
            "Requires yt-dlp and ffmpeg on your PATH (install once with winget).";
        UpdateStatusText.Text = "Check GitHub for a newer installer.";
    }

    private void FormatCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var format = GetSelectedFormat();
        var quality = DownloadFormats.IsVideo(format) ? App.Settings.VideoQuality : App.Settings.AudioQuality;
        QualityPresets.ApplyToCombo(QualityCombo, format, quality);
    }

    private DownloadFormat GetSelectedFormat() => DownloadFormats.GetSelectedFormat(FormatCombo);

    private void SelectFormat(string? preferred)
    {
        DownloadFormats.SelectInCombo(FormatCombo, preferred);

        var format = DownloadFormats.FromTag(preferred);
        var quality = DownloadFormats.IsVideo(format) ? App.Settings.VideoQuality : App.Settings.AudioQuality;
        QualityPresets.ApplyToCombo(QualityCombo, format, quality);
    }

    private int GetSelectedQuality() => QualityPresets.GetSelectedQuality(QualityCombo);

    private void BrowseFolder_OnClick(object sender, RoutedEventArgs e) =>
        BrowseInto(FolderBox, FolderBox.Text);

    private void BrowseMusicFolder_OnClick(object sender, RoutedEventArgs e) =>
        BrowseInto(MusicFolderBox, MusicFolderBox.Text);

    private void BrowseVideosFolder_OnClick(object sender, RoutedEventArgs e) =>
        BrowseInto(VideosFolderBox, VideosFolderBox.Text);

    private static void BrowseInto(System.Windows.Controls.TextBox box, string current)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose folder",
            SelectedPath = Directory.Exists(current) ? current : AppPaths.DefaultMusicFolder,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            box.Text = dlg.SelectedPath;
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Checking GitHub…";
        DownloadInstallerButton.IsEnabled = false;
        _lastCheck = await UpdateCheckService.CheckLatestReleaseAsync();

        if (!_lastCheck.Success)
        {
            UpdateStatusText.Text = _lastCheck.ErrorMessage ?? "Check failed.";
            return;
        }

        if (_lastCheck.NoPublishedReleases)
        {
            UpdateStatusText.Text = "No published releases yet on GitHub.";
            return;
        }

        if (_lastCheck.IsNewerThanCurrent)
        {
            UpdateStatusText.Text = $"Newer version available: {_lastCheck.LatestVersion} (you have {App.VersionDisplay}).";
            DownloadInstallerButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastCheck.InstallerDownloadUrl);
            UpdateAvailabilityCache.Set(_lastCheck.LatestVersion, _lastCheck.InstallerDownloadUrl);
        }
        else
        {
            UpdateStatusText.Text = $"You are up to date ({App.VersionDisplay}).";
            UpdateAvailabilityCache.Clear();
        }

        App.Settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        App.SaveSettings();
    }

    private async void DownloadInstaller_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lastCheck?.InstallerDownloadUrl is not { } url)
        {
            UpdateCheckService.OpenUpdateDownload(null);
            return;
        }

        UpdateStatusText.Text = "Downloading installer…";
        DownloadInstallerButton.IsEnabled = false;
        var (path, error) = await UpdateCheckService.DownloadInstallerToTempAsync(url, _lastCheck.LatestVersion);
        if (path is null)
        {
            UpdateStatusText.Text = error ?? "Download failed.";
            DownloadInstallerButton.IsEnabled = true;
            return;
        }

        var confirm = MessageBox.Show(this,
            "The app will close and the installer will start.\n\nQuit this app before continuing in the setup wizard.",
            "Install update",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (confirm != MessageBoxResult.OK)
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Install update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenReleases_OnClick(object sender, RoutedEventArgs e) =>
        UpdateCheckService.OpenUpdateDownload(null);

    private void CopyExtensionToken_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ExtensionTokenBox.Text);
            ExtensionStatusText.Text = "Token copied. Paste it into the extension options page.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy token", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RegenerateExtensionToken_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Generate a new token? Update the browser extension options. Local Music Hub picks up the new token next time it starts.",
            "Regenerate token",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        ExtensionTokenBox.Text = Guid.NewGuid().ToString("N");
        ExtensionStatusText.Text = "New token created. Copy it into the extension options, then save settings here.";
    }

    private void OpenExtensionFolder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = BrowserExtensionInstaller.GetExtensionFolder();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Extension folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenMusicHubData_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = LocalMusicHubIntegration.LocalMusicHubDataDirectory;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Local Music Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestExtension_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ExtensionPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Enter a valid port (1–65535).", "Test connection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExtensionStatusText.Text = "Testing connection…";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await client.GetStringAsync($"http://127.0.0.1:{port}/health");
            ExtensionStatusText.Text = $"App responded: {json}";
        }
        catch (Exception ex)
        {
            ExtensionStatusText.Text = $"Could not reach the app on port {port}. Is it running with the extension enabled? ({ex.Message})";
        }
    }

    private void ExportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON settings (*.json)|*.json",
            FileName = "YouTubeToMp3-settings.json",
        };
        if (dlg.ShowDialog() != true)
            return;

        if (SettingsTransferService.Export(ReadSettingsFromUi(), dlg.FileName))
            MessageBox.Show(this, "Settings exported.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, "Export failed.", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ImportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON settings (*.json)|*.json" };
        if (dlg.ShowDialog() != true)
            return;

        var (settings, error) = SettingsTransferService.Import(dlg.FileName);
        if (settings is null)
        {
            MessageBox.Show(this, error ?? "Import failed.", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        App.ReplaceSettings(settings);
        LoadFromSettings();
        MessageBox.Show(this, "Settings imported. Click Save to keep them.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private AppSettings ReadSettingsFromUi()
    {
        var format = GetSelectedFormat();
        var quality = GetSelectedQuality();
        return new AppSettings
        {
            OutputFolder = FolderBox.Text.Trim(),
            MusicOutputFolder = MusicFolderBox.Text.Trim(),
            VideosOutputFolder = VideosFolderBox.Text.Trim(),
            PreferredFormat = DownloadFormats.ToTag(format),
            AudioQuality = DownloadFormats.IsVideo(format) ? App.Settings.AudioQuality : quality,
            VideoQuality = DownloadFormats.IsVideo(format) ? quality : App.Settings.VideoQuality,
            EmbedThumbnail = EmbedThumbnailBox.IsChecked == true,
            AlwaysAskCoverArt = AlwaysAskCoverArtBox.IsChecked == true,
            ReviewAlbumTracksBeforeDownload = ReviewAlbumTracksBox.IsChecked == true,
            ShowLogPanel = ShowLogPanelBox.IsChecked == true,
            UseContentSubfolders = UseSubfoldersBox.IsChecked == true,
            MusicSubfolder = string.IsNullOrWhiteSpace(MusicSubfolderBox.Text) ? "Youtube Music" : MusicSubfolderBox.Text.Trim(),
            VideosSubfolder = string.IsNullOrWhiteSpace(VideosSubfolderBox.Text) ? "Youtube Videos" : VideosSubfolderBox.Text.Trim(),
            UseDarkTheme = DarkThemeBox.IsChecked == true,
            MinimizeToTray = MinimizeToTrayBox.IsChecked == true || StartWithWindowsBox.IsChecked == true,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            NotifyOnQueueComplete = NotifyQueueBox.IsChecked == true,
            SkipAlreadyDownloaded = SkipAlreadyDownloadedBox.IsChecked == true,
            MaxParallelDownloads = ParseClampedInt(MaxParallelBox.Text, 1, 5, 1),
            ConcurrentFragments = ParseClampedInt(ConcurrentFragmentsBox.Text, 0, 8, 4),
            PlaySoundOnComplete = PlaySoundBox.IsChecked == true,
            BrowserExtensionEnabled = BrowserExtensionBox.IsChecked == true,
            BrowserExtensionPort = ParseExtensionPort(),
            BrowserExtensionToken = ExtensionTokenBox.Text.Trim(),
            AutoCheckUpdates = AutoCheckBox.IsChecked == true,
            NotifyTrayOnUpdate = NotifyTrayOnUpdateBox.IsChecked == true,
            AutoQueueWhenBusy = true,
            LastUpdateCheckUtc = App.Settings.LastUpdateCheckUtc,
            DismissedUpdateVersion = App.Settings.DismissedUpdateVersion,
        };
    }

    private static int ParseClampedInt(string text, int min, int max, int fallback)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return Math.Clamp(value, min, max);
        return fallback;
    }

    private int ParseExtensionPort()
    {
        if (int.TryParse(ExtensionPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
            port is > 0 and < 65536)
            return port;
        return AppSettingsService.DefaultExtensionPort;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettingsFromUi();
        AutoStartService.SetEnabled(settings.StartWithWindows);
        if (settings.StartWithWindows)
            settings.MinimizeToTray = true;

        App.ReplaceSettings(settings);
        App.SaveSettings();
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
