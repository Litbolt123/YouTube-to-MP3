using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace YouTubeToMp3.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private Icon? _ownedIcon;
    private MainWindow? _window;
    private ToolStripMenuItem? _startWithWindowsItem;
    private bool _disposed;

    public TrayIconService()
    {
        _ownedIcon = TrayIconAssets.CreateIcon();

        _icon = new NotifyIcon
        {
            Text = "YouTube Downloader — audio, video & browser bridge",
            Icon = _ownedIcon,
            Visible = false,
        };

        var menu = BuildMenu();
        TrayIconAssets.ApplyMenuStyle(menu);
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowWindow();
        menu.Opening += (_, _) => RefreshStartWithWindowsItem();
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = _window?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(TrayIconAssets.CreateHeader("YouTube Downloader"));
        menu.Items.Add("Open downloader", null, (_, _) => ShowWindow());
        menu.Items.Add("View queue", null, (_, _) => RunOnUi(() =>
        {
            ShowMainWindow();
            _window?.ShowQueue();
        }));
        menu.Items.Add("Download history", null, (_, _) => RunOnUi(() =>
        {
            ShowMainWindow();
            _window?.ShowHistory();
        }));
        menu.Items.Add("Open music folder", null, (_, _) => OpenMusicFolder());
        menu.Items.Add("Open videos folder", null, (_, _) => OpenVideosFolder());
        menu.Items.Add(new ToolStripSeparator());

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
        };
        _startWithWindowsItem.Click += StartWithWindows_OnClick;
        menu.Items.Add(_startWithWindowsItem);
        RefreshStartWithWindowsItem();

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit YouTube Downloader", null, (_, _) => ExitApp());
        return menu;
    }

    private void StartWithWindows_OnClick(object? sender, EventArgs e)
    {
        if (_startWithWindowsItem is null)
            return;

        var enabled = _startWithWindowsItem.Checked;
        AutoStartService.SetEnabled(enabled);
        App.Settings.StartWithWindows = enabled;
        if (enabled)
            App.Settings.MinimizeToTray = true;
        App.SaveSettings();
        RefreshStartWithWindowsItem();
        _window?.OnAutoStartChanged();
    }

    private void RefreshStartWithWindowsItem()
    {
        if (_startWithWindowsItem is null)
            return;

        var enabled = AutoStartService.IsEnabled();
        _startWithWindowsItem.Checked = enabled;
        _startWithWindowsItem.Text = enabled ? "Start with Windows (on)" : "Start with Windows (off)";
    }

    public void Attach(MainWindow window)
    {
        _window = window;
    }

    public void ShowTrayIcon()
    {
        _icon.Visible = true;
    }

    public void HideTrayIcon()
    {
        _icon.Visible = false;
    }

    public void MinimizeToTray()
    {
        if (_window is null)
            return;

        _window.Hide();
        _window.WindowState = WindowState.Normal;
        _icon.Visible = true;
        _icon.ShowBalloonTip(
            2500,
            "YouTube Downloader",
            "Minimized to tray. Browser extension can still queue downloads.",
            ToolTipIcon.Info);
    }

    public void NotifyQueueComplete(int count)
    {
        if (!_icon.Visible && _window is { IsVisible: false })
        {
            _icon.ShowBalloonTip(
                3000,
                "YouTube Downloader",
                count <= 1 ? "Download complete." : $"All {count} queued downloads complete.",
                ToolTipIcon.Info);
        }
    }

    public void ShowUpdateAvailableBalloon(string version, string downloadUrl)
    {
        if (!_icon.Visible)
            return;

        try
        {
            _pendingUpdateBalloonUrl = downloadUrl;
            _icon.BalloonTipClicked -= UpdateBalloon_OnClicked;
            _icon.BalloonTipClicked += UpdateBalloon_OnClicked;
            _icon.ShowBalloonTip(
                14000,
                $"YouTube Downloader {version} is available",
                "Click here to open the download. Quit this app before running the installer.",
                ToolTipIcon.Info);
        }
        catch
        {
            /* ignore */
        }
    }

    private string? _pendingUpdateBalloonUrl;

    private void UpdateBalloon_OnClicked(object? sender, EventArgs e)
    {
        var url = _pendingUpdateBalloonUrl;
        _pendingUpdateBalloonUrl = null;
        if (string.IsNullOrWhiteSpace(url))
            url = UpdateCheckService.ReleasesPageUrl;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    public void ShowMainWindow()
    {
        RunOnUi(RestoreMainWindow);
    }

    private void RestoreMainWindow()
    {
        if (_window is null)
            return;

        _window.PrepareVisibleRestore();
        _window.ShowInTaskbar = true;
        _window.ShowActivated = true;
        _window.Visibility = Visibility.Visible;
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Focus();
    }

    private void ShowWindow() => ShowMainWindow();

    public void NotifyExtensionDownloadQueued(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            summary = "Download queued from browser.";

        if (summary.Length > 120)
            summary = summary[..117] + "…";

        try
        {
            if (!_icon.Visible)
                _icon.Visible = true;

            _icon.ShowBalloonTip(
                3500,
                "YouTube Downloader",
                summary,
                ToolTipIcon.Info);
        }
        catch
        {
            /* ignore */
        }
    }

    public void NotifyExtensionDownloadSkipped(string reason)
    {
        try
        {
            if (!_icon.Visible)
                return;

            _icon.ShowBalloonTip(
                3000,
                "YouTube Downloader",
                reason,
                ToolTipIcon.Info);
        }
        catch
        {
            /* ignore */
        }
    }

    public void NotifyExtensionDownloadFailed(string message)
    {
        try
        {
            if (!_icon.Visible)
                _icon.Visible = true;

            var text = string.IsNullOrWhiteSpace(message) ? "Extension download failed." : message;
            if (text.Length > 120)
                text = text[..117] + "…";

            _icon.ShowBalloonTip(
                4000,
                "YouTube Downloader",
                text,
                ToolTipIcon.Warning);
        }
        catch
        {
            /* ignore */
        }
    }

    public void ShowIdleExitBalloon(int idleMinutes)
    {
        if (!_icon.Visible)
            return;

        try
        {
            _icon.ShowBalloonTip(
                2500,
                "YouTube Downloader",
                $"Closed after {idleMinutes} minutes idle. The browser extension will wake it when needed.",
                ToolTipIcon.Info);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void OpenMusicFolder() => OpenResolvedFolder(ContentKind.Music, DownloadFormat.Mp3);

    private static void OpenVideosFolder() => OpenResolvedFolder(ContentKind.Video, DownloadFormat.Mp4);

    private static void OpenResolvedFolder(ContentKind kind, DownloadFormat format)
    {
        var folder = ContentPathResolver.ResolveOutputFolder(App.Settings, kind, format);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    private void ExitApp()
    {
        RunOnUi(() =>
        {
            _window?.ForceExit();
            Application.Current?.Shutdown();
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon?.Dispose();
        _ownedIcon = null;
    }
}
