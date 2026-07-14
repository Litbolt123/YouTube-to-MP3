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

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(TrayIconAssets.CreateHeader("YouTube Downloader"));
        menu.Items.Add("Open downloader", null, (_, _) => ShowWindow());
        menu.Items.Add("View queue", null, (_, _) => _window?.ShowQueue());
        menu.Items.Add("Download history", null, (_, _) => _window?.ShowHistory());
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

    public void ShowMainWindow()
    {
        if (_window is null)
            return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowWindow() => ShowMainWindow();

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

    private static void ExitApp()
    {
        if (Application.Current?.MainWindow is MainWindow mw)
            mw.ForceExit();

        Application.Current?.Shutdown();
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
