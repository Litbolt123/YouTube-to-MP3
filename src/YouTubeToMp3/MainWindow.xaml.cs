using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeToMp3;

public partial class MainWindow
{
    private static readonly HashSet<string> PreviewPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Loading preview…",
        "(Preview unavailable)",
        "(Multiple URLs — filenames vary)",
    };

    private sealed class ActiveDownloadContext
    {
        public required DownloadJob Job { get; init; }
        public required YtDlpDownloadService Downloader { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required QueueRuntimeState Runtime { get; init; }
        public DateTime JobStartedUtc { get; set; }
        public DateTime ItemStartedUtc { get; set; }
        public int? LastPlaylistIndex { get; set; }
        public bool IsPrimary { get; set; }
    }

    private readonly List<ActiveDownloadContext> _activeDownloads = [];
    private readonly DownloadLogService _logService = new();
    private readonly DownloadQueue _queue = new();
    private readonly DownloadHistoryService _history = new();
    private readonly TrayIconService _tray = new();
    private readonly BrowserExtensionHost _extensionHost = new();
    private readonly QueueRuntimeState _queueRuntime = new();
    private readonly QueueEtaCountdown _queueEtaCountdown = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _statusTickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _coverUrlTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private UpdateCheckResult? _pendingUpdate;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _coverPreviewCts;
    private bool _sessionLogStarted;
    private bool _forceClose;
    private bool _formatManuallyChanged;
    private bool _applyingSmartFormat;
    private bool _updatingPreviewBox;
    private bool _applyingSkipCheckbox;
    private bool _applyingEmbedThumbnailCheckbox;
    private bool _previewIsLoading;
    private bool _queuePaused;
    private Task _previewTask = Task.CompletedTask;
    private int _dragSourceIndex = -1;
    private System.Windows.Point _dragStartPoint;
    private string? _lastSavedPath;
    private ContentKind _lastSavedContentKind = ContentKind.Auto;
    private bool _lastImportIsAlbum;
    private string? _predictedFileName;
    private string? _predictedFullPath;
    private int _queueSessionCount;
    private int _sessionCompletedCount;
    private QueueWindow? _queueWindow;
    private readonly List<TimeSpan> _completedJobDurations = [];
    private readonly List<TimeSpan> _completedItemDurations = [];
    private bool _startMinimizedToTray;
    private string? _customCoverArtPath;
    private string? _coverBoundUrlIdentity;
    private bool _suppressCoverUrlChange;
    private string? _displayCoverArtPath;
    private string? _lastCoverVideoUrl;
    private bool _logPeekVisible;
    private bool _logForceHidden;
    private FileSystemWatcher? _activateSignalWatcher;

    private bool IsProcessing => _activeDownloads.Count > 0;
    private DownloadJob? PrimaryJob => _activeDownloads.FirstOrDefault(c => c.IsPrimary)?.Job
                                       ?? _activeDownloads.FirstOrDefault()?.Job;
    private ActiveDownloadContext? PrimaryContext =>
        _activeDownloads.FirstOrDefault(c => c.IsPrimary) ?? _activeDownloads.FirstOrDefault();

    public MainWindow()
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();
        DownloadFormats.PopulateCombo(FormatCombo);
        _history.Load();
        _tray.Attach(this);
        _startMinimizedToTray = AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs());
        if (App.Settings.MinimizeToTray || App.Settings.StartWithWindows || _startMinimizedToTray)
            _tray.ShowTrayIcon();

        Loaded += MainWindow_OnLoaded;
        Closed += MainWindow_OnClosed;
        _previewTimer.Tick += PreviewTimer_OnTick;
        _statusTickTimer.Tick += StatusTickTimer_OnTick;
        _coverUrlTimer.Tick += CoverUrlTimer_OnTick;
        _queue.Changed += (_, _) => Dispatcher.Invoke(RefreshQueueUi);
        _extensionHost.DownloadRequested += ExtensionHost_OnDownloadRequested;
        _extensionHost.CheckUrl = CheckUrlForExtension;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFolderDisplay();
        LogPathHint.Text = $"Logs folder: {AppPaths.LogsDirectory}";
        ApplySettingsToMainUi();
        SelectFormat(App.Settings.PreferredFormat);
        RefreshCoverArtStatus();
        RefreshToolStatus();
        UpdateContentHint();
        UpdatePauseQueueButton();
        SchedulePreviewRefresh();
        BrowserExtensionInstaller.EnsureInstalled();
        _extensionHost.ApplySettings(App.Settings);
        SetupActivateSignalWatcher();

        if (_startMinimizedToTray || (App.Settings.StartWithWindows && AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs())))
        {
            _tray.ShowTrayIcon();
            Hide();
        }
    }

    private void SetupActivateSignalWatcher()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            _activateSignalWatcher = new FileSystemWatcher(AppPaths.DataDirectory)
            {
                Filter = "activate.signal",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _activateSignalWatcher.Created += (_, _) => Dispatcher.Invoke(ActivateFromSecondInstance);
            _activateSignalWatcher.Changed += (_, _) => Dispatcher.Invoke(ActivateFromSecondInstance);
        }
        catch
        {
            /* ignore */
        }
    }

    private void ActivateFromSecondInstance()
    {
        try
        {
            if (File.Exists(SingleInstanceService.ActivateSignalPath))
                File.Delete(SingleInstanceService.ActivateSignalPath);
        }
        catch
        {
            /* ignore */
        }

        _tray.ShowMainWindow();
    }

    public void OnAutoStartChanged()
    {
        if (App.Settings.StartWithWindows || App.Settings.MinimizeToTray)
            _tray.ShowTrayIcon();
        else if (!IsProcessing)
            _tray.HideTrayIcon();
    }

    private void ExtensionHost_OnDownloadRequested(object? sender, ExtensionDownloadRequest e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                _tray.ShowMainWindow();
                await EnqueueFromExtensionAsync(e).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Extension download failed.";
                AppendLog($"Extension download error: {ex.Message}");
            }
        });
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _activateSignalWatcher?.Dispose();
        _activateSignalWatcher = null;
        _logService.Dispose();
        _extensionHost.Dispose();
        _tray.Dispose();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || !(App.Settings.MinimizeToTray || App.Settings.StartWithWindows))
            return;

        e.Cancel = true;
        _tray.MinimizeToTray();
    }

    public void ForceExit() => _forceClose = true;

    public void ShowHistory()
    {
        var dlg = new HistoryWindow(_history) { Owner = this };
        dlg.RedownloadRequested += (_, entry) =>
        {
            UrlBox.Text = entry.Url;
            SelectFormat(entry.Format);
            _ = EnqueueOrStartAsync(ParseScope(entry.Scope), forceRedownload: true);
        };
        dlg.ShowDialog();
    }

    public void ShowQueue()
    {
        if (_queueWindow is { IsVisible: true })
        {
            _queueWindow.Activate();
            _queueWindow.Refresh();
            return;
        }

        _queueWindow = new QueueWindow(
            _queue,
            _queueRuntime,
            BuildQueueSnapshot,
            index =>
            {
                _queue.RemoveAt(index);
                StatusText.Text = _queue.Count > 0 ? $"{_queue.Count} item(s) in queue." : "Queue cleared.";
            },
            (from, to) => _queue.Move(from, to),
            () =>
            {
                _queue.Clear();
                StatusText.Text = "Queue cleared.";
            },
            () => Cancel_OnClick(this, new RoutedEventArgs()),
            ToggleQueuePaused)
        {
            Owner = this,
        };
        _queueWindow.Closed += (_, _) => _queueWindow = null;
        _queueWindow.Show();
    }

    private QueueWindowSnapshot BuildQueueSnapshot()
    {
        var activeJobs = _activeDownloads.Select(c => c.Job).ToList();
        var rawEta = QueueDisplayHelper.EstimateQueueRemaining(
            _queueRuntime,
            PrimaryJob,
            _queue.Snapshot(),
            _completedJobDurations,
            _completedItemDurations);
        _queueEtaCountdown.Sync(rawEta);
        return new QueueWindowSnapshot
        {
            IsProcessing = IsProcessing,
            ActiveJob = PrimaryJob,
            ActiveJobs = activeJobs,
            ActiveCount = activeJobs.Count,
            QueuePaused = _queuePaused,
            Runtime = _queueRuntime,
            Waiting = _queue.Snapshot(),
            CompletedThisSession = _sessionCompletedCount,
            QueueEta = _queueEtaCountdown.Remaining ?? rawEta,
            CoverArtPath = _displayCoverArtPath,
        };
    }

    private DuplicateCheckResult CheckUrlForExtension(string url)
    {
        var format = DownloadFormats.FromTag(App.Settings.PreferredFormat);
        var quality = DownloadFormats.IsVideo(format) ? App.Settings.VideoQuality : App.Settings.AudioQuality;
        var chosen = ContentKind.Auto;
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        var job = new DownloadJob
        {
            Url = url,
            Scope = DownloadScope.SingleVideo,
            Format = format,
            Quality = quality,
            EmbedThumbnail = App.Settings.EmbedThumbnail,
            ContentKind = namingKind,
            OutputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format),
        };

        var warning = DuplicateDetectionService.Check(
            job,
            _queue,
            _history,
            predictedPath: null,
            activeUrls: _activeDownloads.Select(c => c.Job.Url));
        return DuplicateDetectionService.ToCheckResult(warning);
    }

    private static DownloadScope ParseScope(string scope) =>
        string.Equals(scope, nameof(DownloadScope.Playlist), StringComparison.OrdinalIgnoreCase)
            ? DownloadScope.Playlist
            : DownloadScope.SingleVideo;

    private void History_OnClick(object sender, RoutedEventArgs e) => ShowHistory();

    private void Queue_OnClick(object sender, RoutedEventArgs e) => ShowQueue();

    private void FormatCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _applyingSmartFormat)
            return;

        _formatManuallyChanged = true;
        var format = GetSelectedFormat();
        var quality = DownloadFormats.IsVideo(format) ? App.Settings.VideoQuality : App.Settings.AudioQuality;
        QualityPresets.ApplyToCombo(QualityCombo, format, quality);
        PersistCurrentChoices();
        RefreshFolderDisplay();
        UpdateContentHint();
        SchedulePreviewRefresh();
    }

    private void QualityCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _applyingSmartFormat)
            return;
        PersistCurrentChoices();
    }

    private void ChooseCoverArt_OnClick(object sender, RoutedEventArgs e) =>
        OpenCoverArtPicker(forcePrompt: true);

    private async Task<bool> EnsureCoverArtChoiceAsync()
    {
        if (EmbedThumbnailBox.IsChecked != true)
            return true;

        if (!App.Settings.AlwaysAskCoverArt && !string.IsNullOrWhiteSpace(_customCoverArtPath))
            return true;

        if (!App.Settings.AlwaysAskCoverArt)
            return true;

        return OpenCoverArtPicker(forcePrompt: true);
    }

    private bool OpenCoverArtPicker(bool forcePrompt)
    {
        var dlg = new CoverArtPickerWindow(_customCoverArtPath, UrlBox.Text.Trim())
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return !forcePrompt || !App.Settings.AlwaysAskCoverArt;

        if (dlg.UseVideoDefault || string.IsNullOrWhiteSpace(dlg.SelectedCoverArtPath))
        {
            ClearCustomCoverArtSelection();
        }
        else
        {
            _customCoverArtPath = dlg.SelectedCoverArtPath;
            _suppressCoverUrlChange = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(dlg.CoverSourceUrl))
                    CoverUrlBox.Text = dlg.CoverSourceUrl;
            }
            finally
            {
                _suppressCoverUrlChange = false;
            }
        }

        RefreshCoverArtStatus();
        _ = RefreshUrlCoverPreviewAsync();
        return true;
    }

    private string? ResolveCoverArtPathForJob()
    {
        if (EmbedThumbnailBox.IsChecked != true)
            return null;
        if (string.IsNullOrWhiteSpace(_customCoverArtPath) || !File.Exists(_customCoverArtPath))
            return null;
        return _customCoverArtPath;
    }

    private void RefreshCoverArtStatus()
    {
        if (CoverArtStatusText is null)
            return;

        if (!string.IsNullOrWhiteSpace(_customCoverArtPath) && File.Exists(_customCoverArtPath))
            CoverArtStatusText.Text = "Cover: custom (all tracks)";
        else
            CoverArtStatusText.Text = "Cover: video default";

        if (!IsProcessing)
            _ = RefreshUrlCoverPreviewAsync();
    }

    private async Task RefreshUrlCoverPreviewAsync()
    {
        _coverPreviewCts?.Cancel();
        _coverPreviewCts = new CancellationTokenSource();
        var token = _coverPreviewCts.Token;

        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count != 1)
        {
            SetCoverImage(CoverPreviewImage, CoverPreviewPlaceholder, null);
            return;
        }

        var url = YouTubeUrlHelper.ExpandToWatchUrl(urls[0]);
        var playlistUrl = YouTubeUrlHelper.TryGetPlaylistUrl(urls[0]);
        var namingKind = ContentKindDetector.Resolve(GetSelectedContentKind(), urls[0]);
        var primaryUrl = namingKind == ContentKind.Music && playlistUrl is not null ? playlistUrl : url;
        var fallbackUrl = namingKind == ContentKind.Music && playlistUrl is not null ? url : playlistUrl ?? url;
        try
        {
            var path = await CoverArtPreviewService.ResolveDisplayPathAsync(
                _customCoverArtPath,
                primaryUrl,
                fallbackUrl,
                token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
                return;

            if (!IsProcessing)
            {
                SetCoverImage(CoverPreviewImage, CoverPreviewPlaceholder, path);
                _displayCoverArtPath = path;
            }
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
    }

    private void ScheduleDownloadCoverRefresh(DownloadJob job, string? videoUrl)
    {
        var custom = job.CustomCoverArtPath ?? ResolveCoverArtPathForJob();
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            if (!string.Equals(_displayCoverArtPath, custom, StringComparison.OrdinalIgnoreCase))
            {
                _displayCoverArtPath = custom;
                _lastCoverVideoUrl = videoUrl;
                ApplyActiveCoverImages(custom);
            }

            return;
        }

        var lookupUrl = !string.IsNullOrWhiteSpace(videoUrl) ? videoUrl : job.Url;
        if (string.Equals(_lastCoverVideoUrl, lookupUrl, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_displayCoverArtPath) &&
            File.Exists(_displayCoverArtPath))
            return;

        _lastCoverVideoUrl = lookupUrl;
        _ = LoadDownloadCoverAsync(job, lookupUrl);
    }

    private async Task LoadDownloadCoverAsync(DownloadJob job, string? videoUrl)
    {
        _coverPreviewCts?.Cancel();
        _coverPreviewCts = new CancellationTokenSource();
        var token = _coverPreviewCts.Token;

        try
        {
            var path = await CoverArtPreviewService.ResolveDisplayPathAsync(
                job.CustomCoverArtPath ?? ResolveCoverArtPathForJob(),
                videoUrl,
                job.Url,
                token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
                return;

            _displayCoverArtPath = path;
            ApplyActiveCoverImages(path);
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
    }

    private void ApplyActiveCoverImages(string? path)
    {
        SetCoverImage(CoverPreviewImage, CoverPreviewPlaceholder, path);
        SetCoverImage(DownloadCoverImage, DownloadCoverPlaceholder, path);
        _queueWindow?.Refresh();
    }

    private static void SetCoverImage(System.Windows.Controls.Image image, TextBlock placeholder, string? path)
    {
        var bmp = CoverArtPreviewService.LoadBitmap(path, 192);
        if (bmp is null)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            placeholder.Visibility = Visibility.Visible;
            return;
        }

        image.Source = bmp;
        image.Visibility = Visibility.Visible;
        placeholder.Visibility = Visibility.Collapsed;
    }

    private void StatusTickTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsProcessing)
        {
            _statusTickTimer.Stop();
            return;
        }

        UpdateDownloadMetricsUi();
        _queueWindow?.Refresh();
    }

    private void UpdateDownloadMetricsUi()
    {
        if (!IsProcessing)
        {
            DownloadMetricsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DownloadMetricsPanel.Visibility = Visibility.Visible;
        var label = PrimaryJob is { } job
            ? QueueDisplayHelper.TitleFor(job, _queueRuntime)
            : "Downloading…";
        var trackHint = PrimaryJob is { } activeJob &&
                        (activeJob.Scope == DownloadScope.Playlist || activeJob.PlaylistTrackIndex is not null)
            ? QueueDisplayHelper.PlaylistTrackHint(_queueRuntime, activeJob)
            : null;
        MetricTrackText.Text = !string.IsNullOrWhiteSpace(trackHint)
            ? $"{label}\n{trackHint}"
            : label ?? "—";

        var fileEta = _queueRuntime.LiveFileEta;
        MetricFileEtaText.Text = !string.IsNullOrWhiteSpace(fileEta) ? fileEta! : "—";

        var rawEta = QueueDisplayHelper.EstimateQueueRemaining(
            _queueRuntime,
            PrimaryJob,
            _queue.Snapshot(),
            _completedJobDurations,
            _completedItemDurations);
        _queueEtaCountdown.Sync(rawEta);
        MetricQueueEtaText.Text = _queueEtaCountdown.Remaining is { } q && q > TimeSpan.Zero
            ? $"~{QueueDisplayHelper.FormatDuration(q)}"
            : "—";
    }

    private void EmbedThumbnailBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _applyingEmbedThumbnailCheckbox)
            return;

        App.Settings.EmbedThumbnail = EmbedThumbnailBox.IsChecked == true;
        App.SaveSettings();
    }

    private void SkipAlreadyDownloadedBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _applyingSkipCheckbox)
            return;

        App.Settings.SkipAlreadyDownloaded = SkipAlreadyDownloadedBox.IsChecked == true;
        App.SaveSettings();
    }

    private void SetEmbedThumbnailCheckbox(bool value)
    {
        _applyingEmbedThumbnailCheckbox = true;
        EmbedThumbnailBox.IsChecked = value;
        _applyingEmbedThumbnailCheckbox = false;
    }

    private void ApplySettingsToMainUi()
    {
        SetEmbedThumbnailCheckbox(App.Settings.EmbedThumbnail);
        SetSkipAlreadyDownloadedCheckbox(App.Settings.SkipAlreadyDownloaded);
        _logForceHidden = false;
        if (!App.Settings.ShowLogPanel)
            _logPeekVisible = false;
        RefreshLogPanelVisibility();
    }

    private bool IsLogPanelVisible()
    {
        if (_logForceHidden)
            return false;

        return App.Settings.ShowLogPanel || _logPeekVisible;
    }

    private void RefreshLogPanelVisibility()
    {
        var visible = IsLogPanelVisible();
        LogCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ToggleLogButton.Content = visible ? "Hide log" : "Show log";
    }

    private void ToggleLog_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsLogPanelVisible())
        {
            if (App.Settings.ShowLogPanel)
                _logForceHidden = true;
            else
                _logPeekVisible = false;
        }
        else
        {
            _logForceHidden = false;
            _logPeekVisible = true;
        }

        RefreshLogPanelVisibility();
    }

    private void SetSkipAlreadyDownloadedCheckbox(bool value)
    {
        _applyingSkipCheckbox = true;
        SkipAlreadyDownloadedBox.IsChecked = value;
        _applyingSkipCheckbox = false;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;

        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox { IsReadOnly: false } focused &&
            !ReferenceEquals(focused, UrlBox) &&
            focused.IsKeyboardFocusWithin)
            return;

        if (ReferenceEquals(Keyboard.FocusedElement, UrlBox))
            return;

        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;

            var text = System.Windows.Clipboard.GetText().Trim();
            if (!IsLikelyYouTubeUrl(text) && !UrlBatchParser.Parse(text).Any(IsLikelyYouTubeUrl))
                return;

            UrlBox.Text = text;
            UrlBox.Focus();
            e.Handled = true;
        }
        catch
        {
            /* ignore clipboard errors */
        }
    }

    private void ContentKindCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var kind = GetSelectedContentKind();
        if (kind == ContentKind.Auto)
        {
            // Allow URL-based smart format again.
            _formatManuallyChanged = false;
            ApplySmartFormatDefaults();
        }
        else
        {
            // Explicit Music/Video should switch format + quality options immediately
            // (even with an empty URL box).
            var suggested = ContentKindDetector.SuggestedFormat(kind);
            _applyingSmartFormat = true;
            SelectFormat(DownloadFormats.ToTag(suggested));
            _applyingSmartFormat = false;
            _formatManuallyChanged = true;
            PersistCurrentChoices();
        }

        RefreshFolderDisplay();
        UpdateContentHint();
        UpdateDownloadActionsUi();
        SchedulePreviewRefresh();
    }

    private void UrlBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ResetCoverArtIfUrlChanged();
        UpdateContentHint();
        UpdateDownloadActionsUi();
        ApplySmartFormatDefaults();
        SchedulePreviewRefresh();
    }

    private void ResetCoverArtIfUrlChanged()
    {
        var identity = GetPrimaryUrlIdentity(UrlBox.Text);
        if (string.Equals(identity, _coverBoundUrlIdentity, StringComparison.OrdinalIgnoreCase))
            return;

        _coverBoundUrlIdentity = identity;
        ClearCustomCoverArtSelection();
    }

    private static string? GetPrimaryUrlIdentity(string text)
    {
        var urls = UrlBatchParser.Parse(text);
        if (urls.Count == 0)
            return null;
        if (urls.Count > 1)
            return "multi:" + string.Join("|", urls.Select(static u =>
                YouTubeUrlHelper.TryGetListId(u) ?? YouTubeUrlHelper.TryGetVideoId(u) ?? u.Trim()));

        var url = urls[0];
        return YouTubeUrlHelper.TryGetListId(url)
               ?? YouTubeUrlHelper.TryGetVideoId(url)
               ?? url.Trim();
    }

    private void ClearCustomCoverArtSelection()
    {
        _customCoverArtPath = null;
        _displayCoverArtPath = null;
        _suppressCoverUrlChange = true;
        try
        {
            if (CoverUrlBox is not null)
                CoverUrlBox.Text = "";
        }
        finally
        {
            _suppressCoverUrlChange = false;
        }

        if (CoverArtStatusText is not null)
            CoverArtStatusText.Text = "Cover: video default";

        SetCoverImage(CoverPreviewImage, CoverPreviewPlaceholder, null);
        if (!IsProcessing)
            SetCoverImage(DownloadCoverImage, DownloadCoverPlaceholder, null);
    }

    private void FolderBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateContentHint();
        SchedulePreviewRefresh();
    }

    private void CoverUrlBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCoverUrlChange)
            return;

        _coverUrlTimer.Stop();
        _coverUrlTimer.Start();
    }

    private void CoverUrlTimer_OnTick(object? sender, EventArgs e)
    {
        _coverUrlTimer.Stop();
        _ = FetchCoverFromUrlBoxAsync();
    }

    private async Task FetchCoverFromUrlBoxAsync()
    {
        var url = CoverUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            if (string.IsNullOrWhiteSpace(_customCoverArtPath))
            {
                RefreshCoverArtStatus();
                if (!IsProcessing)
                    await RefreshUrlCoverPreviewAsync().ConfigureAwait(true);
            }

            return;
        }

        if (!url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return;

        _coverPreviewCts?.Cancel();
        _coverPreviewCts = new CancellationTokenSource();
        var token = _coverPreviewCts.Token;

        try
        {
            CoverArtStatusText.Text = "Fetching cover…";
            var (path, error) = await CoverArtFetchService.FetchThumbnailFromUrlAsync(url, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            if (path is null)
            {
                CoverArtStatusText.Text = error ?? "Could not fetch cover.";
                return;
            }

            _customCoverArtPath = path;
            _displayCoverArtPath = path;
            RefreshCoverArtStatus();
            ApplyActiveCoverImages(path);
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
    }

    private void FilenamePreviewBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _updatingPreviewBox)
            return;
    }

    private void SchedulePreviewRefresh()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void PreviewTimer_OnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        StartPreviewTask();
    }

    private void StartPreviewTask()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _previewTask = RefreshFilenamePreviewAsync();
        _ = RefreshUrlCoverPreviewAsync();
        _ = RefreshPlaylistKindHintAsync();
    }

    private async Task RefreshPlaylistKindHintAsync()
    {
        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count != 1)
            return;

        var url = urls[0];
        if (YouTubeUrlHelper.TryGetListId(url) is null)
            return;

        if (YouTubeUrlHelper.TryGetVideoId(url) is not null)
            return;

        var token = _previewCts?.Token ?? CancellationToken.None;
        try
        {
            var playlistUrl = YouTubeUrlHelper.TryGetPlaylistUrl(url);
            if (playlistUrl is null)
                return;

            var title = await YtDlpMetadataService.GetPlaylistTitleAsync(playlistUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            var kind = MusicPlaylistHeuristics.LooksLikeMusic(title) ? ContentKind.Music : ContentKind.Video;
            ContentKindDetector.CachePlaylistKind(url, kind);
            UpdateContentHint();
            ApplySmartFormatDefaults();
            UpdateDownloadActionsUi();
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
        catch
        {
            /* ignore playlist title probe failures */
        }
    }

    private async Task EnsureFilenamePreviewForUrlAsync(string url, DownloadScope scope)
    {
        if (scope != DownloadScope.SingleVideo)
            return;

        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count == 1 && string.Equals(urls[0], url, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureFilenamePreviewReadyAsync().ConfigureAwait(true);
            return;
        }

        var format = GetSelectedFormat();
        var chosen = GetSelectedContentKind();
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        var outputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);

        _previewIsLoading = true;
        try
        {
            StatusText.Text = "Fetching filename…";
            var preview = await YtDlpPreviewService.GetPreviewAsync(
                url, scope, format, namingKind, outputFolder).ConfigureAwait(true);

            _predictedFileName = preview?.FileName;
            _predictedFullPath = preview?.FullPath;
        }
        finally
        {
            _previewIsLoading = false;
        }
    }

    private async Task EnsureFilenamePreviewReadyAsync()
    {
        _previewTimer.Stop();

        if (_previewTask is { IsCompleted: false })
        {
            StatusText.Text = "Fetching filename…";
            try
            {
                await _previewTask.ConfigureAwait(true);
            }
            catch
            {
                /* preview failed — proceed with yt-dlp default naming */
            }

            return;
        }

        if (_predictedFileName is not null)
            return;

        var text = FilenamePreviewBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(text) && !PreviewPlaceholders.Contains(text))
            return;

        StartPreviewTask();
        StatusText.Text = "Fetching filename…";
        try
        {
            await _previewTask.ConfigureAwait(true);
        }
        catch
        {
            /* preview failed — proceed with yt-dlp default naming */
        }
    }

    private async Task RefreshFilenamePreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count != 1)
        {
            SetPreviewText(urls.Count > 1 ? "(Multiple URLs — filenames vary)" : "");
            _predictedFileName = null;
            _predictedFullPath = null;
            SetCoverImage(CoverPreviewImage, CoverPreviewPlaceholder, null);
            return;
        }

        var url = urls[0];
        var scope = DownloadScope.SingleVideo;
        var format = GetSelectedFormat();
        var chosen = GetSelectedContentKind();
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        var outputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);

        _previewIsLoading = true;
        SetPreviewText("Loading preview…");
        try
        {
            var preview = await YtDlpPreviewService.GetPreviewAsync(
                url, scope, format, namingKind, outputFolder, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
                return;

            _previewIsLoading = false;

            if (preview is null)
            {
                SetPreviewText("(Preview unavailable)");
                _predictedFileName = null;
                _predictedFullPath = null;
                return;
            }

            _predictedFileName = preview.FileName;
            _predictedFullPath = preview.FullPath;
            SetPreviewText(preview.FileName);
            await RefreshUrlCoverPreviewAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
        finally
        {
            _previewIsLoading = false;
        }
    }

    private void SetPreviewText(string text)
    {
        _updatingPreviewBox = true;
        FilenamePreviewBox.Text = text;
        _updatingPreviewBox = false;
    }

    private void ApplySmartFormatDefaults()
    {
        if (_formatManuallyChanged || GetSelectedContentKind() != ContentKind.Auto)
            return;

        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count != 1)
            return;

        var resolved = ContentKindDetector.Resolve(ContentKind.Auto, urls[0]);
        var suggested = ContentKindDetector.SuggestedFormat(resolved);
        _applyingSmartFormat = true;
        SelectFormat(DownloadFormats.ToTag(suggested));
        _applyingSmartFormat = false;
    }

    private void UpdateContentHint()
    {
        var urls = UrlBatchParser.Parse(UrlBox.Text);
        var url = urls.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            ContentHintText.Text = "Paste a URL to see where files will be saved.";
            return;
        }

        var chosen = GetSelectedContentKind();
        var format = GetSelectedFormat();
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        var storageKind = ContentPathResolver.ResolveStorageKind(chosen, format);
        var targetFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);

        var label = ContentKindDetector.DisplayLabel(namingKind);
        var storageLabel = ContentKindDetector.DisplayLabel(storageKind);
        var emoji = ContentKindDetector.Emoji(namingKind);
        var batch = urls.Count > 1 ? $" · {urls.Count} URLs" : "";

        ContentHintText.Text = chosen == ContentKind.Auto
            ? $"{emoji} Detected: {label}{batch} — saves to {targetFolder} ({format.ToString().ToUpperInvariant()} → {storageLabel} folder)"
            : $"{emoji} {label}{batch} — saves to {targetFolder}";

        UpdateDownloadActionsUi();
    }

    private void UpdateDownloadActionsUi()
    {
        var urls = UrlBatchParser.Parse(UrlBox.Text);
        var chosen = GetSelectedContentKind();
        var firstUrl = urls.FirstOrDefault() ?? "";

        if (urls.Count != 1 || string.IsNullOrWhiteSpace(firstUrl))
        {
            DownloadVideoButton.Content = "This video only";
            DownloadPlaylistButton.Content = "Whole playlist";
            DownloadVideoButton.Visibility = Visibility.Visible;
            DownloadPlaylistButton.Visibility = urls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ReviewAlbumButton.Visibility = Visibility.Collapsed;
            ApplyDownloadButtonTooltips(isMusic: false);
            return;
        }

        var ctx = PlaylistKindResolver.GetDownloadContext(firstUrl, chosen);
        var resolved = ContentKindDetector.Resolve(chosen, firstUrl);
        var isMusic = resolved == ContentKind.Music;

        DownloadVideoButton.Content = isMusic ? "This song only" : "This video only";
        DownloadPlaylistButton.Content = isMusic ? "Whole album" : "Whole playlist";
        ApplyDownloadButtonTooltips(isMusic);

        switch (ctx)
        {
            case UrlDownloadContext.SingleTrack:
                DownloadVideoButton.Visibility = Visibility.Visible;
                DownloadPlaylistButton.Visibility = Visibility.Collapsed;
                ReviewAlbumButton.Visibility = Visibility.Collapsed;
                break;
            case UrlDownloadContext.MusicAlbum:
                DownloadVideoButton.Visibility = Visibility.Visible;
                DownloadPlaylistButton.Visibility = Visibility.Visible;
                ReviewAlbumButton.Visibility = Visibility.Visible;
                break;
            case UrlDownloadContext.VideoPlaylist:
            case UrlDownloadContext.AmbiguousPlaylist:
                DownloadVideoButton.Visibility = Visibility.Visible;
                DownloadPlaylistButton.Visibility = Visibility.Visible;
                ReviewAlbumButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void ApplyDownloadButtonTooltips(bool isMusic)
    {
        DownloadVideoButton.ToolTip = isMusic
            ? "Downloads only the song in the URL (--no-playlist)"
            : "Downloads only the video in the URL (--no-playlist)";
        DownloadPlaylistButton.ToolTip = isMusic
            ? "Downloads every song in the album / playlist"
            : "Downloads every item in the playlist";
    }

    private async Task<ContentKind?> EnsurePlaylistKindResolvedAsync(string url, ContentKind chosen)
    {
        if (chosen != ContentKind.Auto)
            return chosen;

        if (!PlaylistKindResolver.IsAmbiguousPlaylist(url))
            return ContentKindDetector.Resolve(chosen, url);

        await ProbePlaylistKindFromTitleAsync(url).ConfigureAwait(true);
        if (!PlaylistKindResolver.IsAmbiguousPlaylist(url))
            return ContentKindDetector.Resolve(chosen, url);

        string? title = null;
        try
        {
            var playlistUrl = YouTubeUrlHelper.TryGetPlaylistUrl(url) ?? url;
            title = await YtDlpMetadataService.GetPlaylistTitleAsync(playlistUrl).ConfigureAwait(true);
        }
        catch
        {
            /* prompt with generic title */
        }

        var result = PlaylistKindPromptWindow.Show(this, title);
        if (result is null)
            return null;

        ContentKindDetector.CachePlaylistKind(url, result.Value);
        UpdateDownloadActionsUi();
        UpdateContentHint();
        ApplySmartFormatDefaults();
        return result;
    }

    private async Task ProbePlaylistKindFromTitleAsync(string url)
    {
        if (!PlaylistKindResolver.IsAmbiguousPlaylist(url))
            return;

        try
        {
            var playlistUrl = YouTubeUrlHelper.TryGetPlaylistUrl(url);
            if (playlistUrl is null)
                return;

            var title = await YtDlpMetadataService.GetPlaylistTitleAsync(playlistUrl).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(title))
                return;

            var kind = MusicPlaylistHeuristics.LooksLikeMusic(title) ? ContentKind.Music : ContentKind.Video;
            ContentKindDetector.CachePlaylistKind(url, kind);
            UpdateContentHint();
            UpdateDownloadActionsUi();
        }
        catch
        {
            /* ignore probe failures — prompt later if needed */
        }
    }

    private void RefreshFolderDisplay()
    {
        FolderBox.Text = ContentPathResolver.ResolveOutputFolder(
            App.Settings,
            GetSelectedContentKind(),
            GetSelectedFormat());
    }

    public void ShowUpdateAvailable(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        UpdateTitle.Text = $"Version {result.LatestVersion} is available";
        UpdateBody.Text = $"You are on {App.VersionDisplay}. Run the installer to update — your settings in {AppPaths.DataDirectory} are kept.";
        UpdateCard.Visibility = Visibility.Visible;
    }

    private void UpdateLater_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.LatestVersion is { } v)
            App.Settings.DismissedUpdateVersion = v;
        App.SaveSettings();
        UpdateCard.Visibility = Visibility.Collapsed;
    }

    private void UpdateDownload_OnClick(object sender, RoutedEventArgs e) =>
        UpdateCheckService.OpenUpdateDownload(_pendingUpdate?.InstallerDownloadUrl);

    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            ApplySettingsToMainUi();
            DashboardUi.ApplyTheme(this, App.Settings.UseDarkTheme);
            SelectFormat(App.Settings.PreferredFormat);
            RefreshCoverArtStatus();
            if (App.Settings.MinimizeToTray || App.Settings.StartWithWindows)
                _tray.ShowTrayIcon();
            else
                _tray.HideTrayIcon();
            RefreshFolderDisplay();
            UpdateContentHint();
            OnAutoStartChanged();
            SchedulePreviewRefresh();
            _extensionHost.ApplySettings(App.Settings);
            _ = ProcessNextJobAsync();
        }
    }

    private void Paste_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                UrlBox.Text = System.Windows.Clipboard.GetText().Trim();
        }
        catch (Exception ex)
        {
            AppendLog("Paste failed: " + ex.Message);
        }
    }

    private void BrowseFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var chosen = GetSelectedContentKind();
        var format = GetSelectedFormat();
        var storageKind = ContentPathResolver.ResolveStorageKind(chosen, format);
        var current = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);

        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"Choose folder for {format.ToString().ToUpperInvariant()} downloads",
            SelectedPath = Directory.Exists(current) ? current : AppPaths.DefaultMusicFolder,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        if (storageKind == ContentKind.Music)
            App.Settings.MusicOutputFolder = dlg.SelectedPath;
        else
            App.Settings.VideosOutputFolder = dlg.SelectedPath;

        App.SaveSettings();
        RefreshFolderDisplay();
        UpdateContentHint();
        SchedulePreviewRefresh();
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e) =>
        OpenPath(FolderBox.Text, "Folder does not exist yet.");

    private void OpenLogsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        OpenPath(AppPaths.LogsDirectory, "Could not open logs folder.");
    }

    private void ImportToMusicHub_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastSavedPath))
            return;

        var result = _lastImportIsAlbum
            ? LocalMusicHubIntegration.RequestImportAlbum(_lastSavedPath, _lastSavedContentKind)
            : LocalMusicHubIntegration.RequestImport(_lastSavedPath, _lastSavedContentKind);
        MessageBox.Show(this, result.Message, "Local Music Hub",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OpenLastSaved_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastSavedPath))
            return;

        if (File.Exists(_lastSavedPath))
            OpenPath(_lastSavedPath, "Saved file not found.");
        else if (Directory.Exists(_lastSavedPath))
            OpenPath(_lastSavedPath, "Saved folder not found.");
        else
            OpenLastFolder_OnClick(sender, e);
    }

    private void OpenLastFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var folder = ResolveContainingFolder(_lastSavedPath);
        if (folder is null)
        {
            MessageBox.Show(this, "No saved location yet.", "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPath(folder, "Folder does not exist yet.");
    }

    private static string? ResolveContainingFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (Directory.Exists(path))
            return path;
        if (File.Exists(path))
            return Path.GetDirectoryName(path);
        var parent = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) ? parent : null;
    }

    private void OpenPath(string path, string missingMessage)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(this, missingMessage, "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DownloadVideo_OnClick(object sender, RoutedEventArgs e) =>
        await EnqueueOrStartAsync(DownloadScope.SingleVideo);

    private async void DownloadPlaylist_OnClick(object sender, RoutedEventArgs e) =>
        await EnqueueOrStartAsync(DownloadScope.Playlist);

    private async void ReviewAlbum_OnClick(object sender, RoutedEventArgs e) =>
        await ReviewAlbumAndEnqueueAsync();

    private async Task ReviewAlbumAndEnqueueAsync(bool forceRedownload = false)
    {
        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Paste a YouTube playlist or album URL first.", "Review album", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (urls.Count > 1)
        {
            MessageBox.Show(this, "Review album works with one playlist URL at a time.", "Review album", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound)
        {
            MessageBox.Show(this, ToolDependencyService.InstallHint, "Missing tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PersistCurrentChoices();
        if (!await EnsureCoverArtChoiceAsync().ConfigureAwait(true))
            return;

        var url = urls[0];
        var format = GetSelectedFormat();
        var chosen = GetSelectedContentKind();
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        if (namingKind != ContentKind.Music)
        {
            MessageBox.Show(this, "Album review is for music playlists. Set content type to Music or Auto with a music album URL.", "Review album", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var reviewJobs = await TryBuildJobsFromAlbumReviewAsync(url, format, namingKind, forceRedownload).ConfigureAwait(true);
        if (reviewJobs is null)
            return;

        HideDoneState();
        await AcceptJobsAsync(reviewJobs).ConfigureAwait(true);
    }

    private async Task EnqueueOrStartAsync(DownloadScope scope, bool forceRedownload = false)
    {
        var urls = UrlBatchParser.Parse(UrlBox.Text);
        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Paste a YouTube URL first.", "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound)
        {
            MessageBox.Show(this, ToolDependencyService.InstallHint, "Missing tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PersistCurrentChoices();

        if (!await EnsureCoverArtChoiceAsync().ConfigureAwait(true))
            return;

        var chosenContent = GetSelectedContentKind();
        if (scope == DownloadScope.Playlist)
        {
            foreach (var url in urls.Where(PlaylistKindResolver.HasPlaylist).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var resolved = await EnsurePlaylistKindResolvedAsync(url, chosenContent).ConfigureAwait(true);
                if (resolved is null)
                    return;
            }
        }

        var jobs = new List<DownloadJob>();
        var skipped = 0;
        foreach (var url in urls)
        {
            if (!IsLikelyYouTubeUrl(url))
            {
                var confirm = MessageBox.Show(this,
                    $"This does not look like a YouTube URL:\n{url}\n\nSkip it?",
                    "YouTube Downloader",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                    continue;
            }

            await EnsureFilenamePreviewForUrlAsync(url, scope);

            var format = GetSelectedFormat();
            var chosen = GetSelectedContentKind();
            var namingKind = ContentKindDetector.Resolve(chosen, url);

            if (scope == DownloadScope.Playlist &&
                namingKind == ContentKind.Music &&
                App.Settings.ReviewAlbumTracksBeforeDownload)
            {
                var reviewJobs = await TryBuildJobsFromAlbumReviewAsync(url, format, namingKind, forceRedownload)
                    .ConfigureAwait(true);
                if (reviewJobs is null)
                    continue;

                jobs.AddRange(reviewJobs);
                continue;
            }

            string? musicBaseName = null;
            if (namingKind == ContentKind.Music && scope == DownloadScope.SingleVideo && _predictedFileName is null)
            {
                musicBaseName = await YtDlpMetadataService.ResolveMusicBaseNameAsync(
                    url, scope, format, namingKind).ConfigureAwait(true);
            }

            string? collectionTitle = null;
            MusicPlaylistFolderInfo? musicPlaylistInfo = null;
            if (scope == DownloadScope.Playlist)
            {
                if (namingKind == ContentKind.Music)
                {
                    musicPlaylistInfo = await YtDlpMetadataService.GetMusicPlaylistFolderInfoAsync(
                        url, format, namingKind).ConfigureAwait(true);
                }

                collectionTitle = await YtDlpMetadataService.ResolveCollectionTitleAsync(
                    url, scope, format, namingKind).ConfigureAwait(true);
            }

            var job = BuildJob(scope, url, musicBaseName, forceRedownload, collectionTitle, musicPlaylistInfo);
            var decision = await ConfirmIfDuplicateAsync(job, forceRedownload);
            if (decision == DuplicateDecision.Skip)
            {
                skipped++;
                continue;
            }

            if (decision == DuplicateDecision.Cancel)
                continue;

            jobs.Add(job);
        }

        if (jobs.Count == 0)
        {
            if (skipped > 0)
                StatusText.Text = skipped == 1
                    ? "Skipped — already downloaded."
                    : $"Skipped {skipped} already-downloaded URL(s).";
            return;
        }

        HideDoneState();
        await AcceptJobsAsync(jobs, skipped).ConfigureAwait(true);
    }

    private async Task<IReadOnlyList<DownloadJob>?> TryBuildJobsFromAlbumReviewAsync(
        string url,
        DownloadFormat format,
        ContentKind namingKind,
        bool forceRedownload)
    {
        var previousStatus = StatusText.Text;
        StatusText.Text = "Detecting album tracks…";
        try
        {
            var review = await YtDlpMetadataService.GetAlbumReviewAsync(
                url,
                format,
                namingKind,
                new Progress<(int Done, int Total, string? Message)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = p.Total > 0
                            ? $"Detecting track {p.Done} of {p.Total}…"
                            : p.Message ?? "Detecting album tracks…";
                    });
                })).ConfigureAwait(true);

            if (review is null)
            {
                MessageBox.Show(this,
                    "Could not read this playlist. Try again or download without review.",
                    "Review album",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            var dialog = new PlaylistReviewWindow(review, format, namingKind) { Owner = this };
            if (dialog.ShowDialog() != true)
                return null;

            var quality = GetSelectedQuality();
            var outputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, GetSelectedContentKind(), format);
            var embedThumbnail = EmbedThumbnailBox.IsChecked == true;
            var coverPath = ResolveCoverArtPathForJob();
            var built = AlbumReviewService.BuildJobs(
                dialog.Result,
                format,
                quality,
                namingKind,
                outputFolder,
                embedThumbnail,
                coverPath,
                forceRedownload).ToList();

            return await FilterAlbumBatchDuplicatesAsync(built, forceRedownload).ConfigureAwait(true);
        }
        finally
        {
            if (StatusText.Text.StartsWith("Detecting", StringComparison.Ordinal))
                StatusText.Text = previousStatus;
        }
    }

    private async Task EnqueueFromExtensionAsync(ExtensionDownloadRequest request)
    {
        if (!IsLikelyYouTubeUrl(request.Url))
            return;

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound)
        {
            StatusText.Text = "Extension download skipped — install yt-dlp and ffmpeg first.";
            return;
        }

        var format = request.Format ?? DownloadFormats.FromTag(App.Settings.PreferredFormat);
        var quality = request.Quality ?? (DownloadFormats.IsVideo(format)
            ? App.Settings.VideoQuality
            : App.Settings.AudioQuality);

        UrlBox.Text = request.Url;
        SelectFormat(DownloadFormats.ToTag(format));
        ApplySmartFormatDefaults();
        UpdateDownloadActionsUi();

        var contentKind = request.ContentKind;
        if (request.Scope == DownloadScope.Playlist && (contentKind ?? ContentKind.Auto) == ContentKind.Auto)
        {
            _tray.ShowMainWindow();
            var resolved = await EnsurePlaylistKindResolvedAsync(request.Url, ContentKind.Auto).ConfigureAwait(true);
            if (resolved is null)
            {
                StatusText.Text = "Extension download cancelled — playlist type not chosen.";
                return;
            }

            contentKind = resolved;
        }

        var effectiveRequest = new ExtensionDownloadRequest
        {
            Url = request.Url,
            Scope = request.Scope,
            Format = request.Format,
            Quality = request.Quality,
            ContentKind = contentKind,
            ForceRedownload = request.ForceRedownload,
        };

        var job = await BuildJobFromExtensionAsync(effectiveRequest, format, quality, request.ForceRedownload)
            .ConfigureAwait(true);
        HideDoneState();
        _tray.ShowMainWindow();

        var decision = await ConfirmIfDuplicateAsync(job, request.ForceRedownload, silentSkip: true);
        if (decision != DuplicateDecision.Proceed)
        {
            StatusText.Text = decision == DuplicateDecision.Skip
                ? "Extension download skipped — already downloaded."
                : "Extension download skipped — already in queue.";
            return;
        }

        await AcceptJobsAsync([job]).ConfigureAwait(true);
    }

    private async Task AcceptJobsAsync(IReadOnlyList<DownloadJob> jobs, int skipped = 0)
    {
        var maxParallel = Math.Clamp(App.Settings.MaxParallelDownloads, 1, 5);
        var freeSlots = Math.Max(0, maxParallel - _activeDownloads.Count);
        var shouldQueue = AddToQueueBox.IsChecked == true || freeSlots <= 0 || _queuePaused;

        if (shouldQueue)
        {
            _queue.EnqueueMany(jobs);
            var skipNote = skipped > 0 ? $" ({skipped} skipped)" : "";
            StatusText.Text = jobs.Count == 1
                ? $"Added to queue ({_queue.Count} waiting).{skipNote}"
                : $"Added {jobs.Count} URLs to queue ({_queue.Count} waiting).{skipNote}";
            await ProcessNextJobAsync().ConfigureAwait(true);
            return;
        }

        if (jobs.Count == 1)
        {
            await RunJobAsync(jobs[0]).ConfigureAwait(true);
            return;
        }

        var startNow = Math.Min(freeSlots, jobs.Count);
        for (var i = 0; i < startNow; i++)
            await RunJobAsync(jobs[i]).ConfigureAwait(true);

        if (startNow < jobs.Count)
        {
            _queue.EnqueueMany(jobs.Skip(startNow));
            StatusText.Text = $"Started {startNow}; {_queue.Count} waiting.";
        }
    }

    private async Task<DownloadJob> BuildJobFromExtensionAsync(
        ExtensionDownloadRequest request,
        DownloadFormat format,
        int quality,
        bool forceRedownload = false)
    {
        var chosen = request.ContentKind ?? ContentKind.Auto;
        var namingKind = chosen == ContentKind.Auto
            ? ContentKindDetector.Resolve(chosen, request.Url)
            : chosen;
        var outputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);
        var customFileName = namingKind == ContentKind.Music && request.Scope == DownloadScope.SingleVideo
            ? await YtDlpMetadataService.ResolveMusicBaseNameAsync(
                request.Url, request.Scope, format, namingKind).ConfigureAwait(true)
            : null;

        return new DownloadJob
        {
            Url = request.Url,
            Scope = request.Scope,
            Format = format,
            Quality = quality,
            EmbedThumbnail = App.Settings.EmbedThumbnail,
            ContentKind = namingKind,
            OutputFolder = outputFolder,
            CustomFileName = customFileName,
            PredictedTitle = customFileName ?? request.Url,
            ForceRedownload = forceRedownload,
            CustomCoverArtPath = null,
        };
    }

    private async Task<DownloadJob> BuildJobFromSettingsAsync(DownloadScope scope, string url)
    {
        var format = DownloadFormats.FromTag(App.Settings.PreferredFormat);
        var quality = DownloadFormats.IsVideo(format) ? App.Settings.VideoQuality : App.Settings.AudioQuality;
        return await BuildJobFromExtensionAsync(
            new ExtensionDownloadRequest { Url = url, Scope = scope },
            format,
            quality).ConfigureAwait(true);
    }

    private static bool IsLikelyYouTubeUrl(string url) =>
        url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    private enum DuplicateDecision
    {
        Proceed,
        Skip,
        Cancel,
    }

    private Task<DuplicateDecision> ConfirmIfDuplicateAsync(
        DownloadJob job,
        bool forceRedownload = false,
        bool silentSkip = false)
    {
        var warning = GetDuplicateWarning(job);
        if (warning is null)
            return Task.FromResult(DuplicateDecision.Proceed);

        if (forceRedownload && warning.Kind != DuplicateKind.InQueue)
            return Task.FromResult(DuplicateDecision.Proceed);

        if (warning.Kind == DuplicateKind.InQueue)
        {
            if (forceRedownload)
            {
                var forceQueue = MessageBox.Show(this,
                    warning.Message + "\n\nDownload anyway?",
                    "Possible duplicate",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return Task.FromResult(forceQueue == MessageBoxResult.Yes
                    ? DuplicateDecision.Proceed
                    : DuplicateDecision.Cancel);
            }

            if (silentSkip)
                return Task.FromResult(DuplicateDecision.Cancel);

            var queueResult = MessageBox.Show(this,
                warning.Message + "\n\nDownload anyway?",
                "Possible duplicate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return Task.FromResult(queueResult == MessageBoxResult.Yes
                ? DuplicateDecision.Proceed
                : DuplicateDecision.Cancel);
        }

        if (App.Settings.SkipAlreadyDownloaded &&
            warning.Kind is DuplicateKind.InHistory or DuplicateKind.FileExists)
        {
            if (!silentSkip)
                StatusText.Text = "Skipped — already downloaded.";
            AppendLog($"Skipped duplicate: {warning.Message.Replace('\n', ' ')}");
            return Task.FromResult(DuplicateDecision.Skip);
        }

        var result = MessageBox.Show(this,
            warning.Message + "\n\nDownload anyway?",
            "Possible duplicate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes
            ? DuplicateDecision.Proceed
            : DuplicateDecision.Cancel);
    }

    private DuplicateWarning? GetDuplicateWarning(DownloadJob job)
    {
        var predicted = DownloadJobPathHelper.ResolveExpectedOutputPath(job)
            ?? (job.Scope == DownloadScope.SingleVideo ? _predictedFullPath : null);
        return DuplicateDetectionService.Check(
            job,
            _queue,
            _history,
            predicted,
            _activeDownloads.Select(c => c.Job.Url));
    }

    private Task<IReadOnlyList<DownloadJob>?> FilterAlbumBatchDuplicatesAsync(
        IReadOnlyList<DownloadJob> jobs,
        bool forceRedownload)
    {
        if (jobs.Count == 0)
            return Task.FromResult<IReadOnlyList<DownloadJob>?>(jobs);

        if (forceRedownload)
            return Task.FromResult<IReadOnlyList<DownloadJob>?>(jobs);

        var duplicateJobs = new List<DownloadJob>();
        var freshJobs = new List<DownloadJob>();
        var queuedJobs = new List<DownloadJob>();

        foreach (var job in jobs)
        {
            var warning = GetDuplicateWarning(job);
            if (warning is null)
            {
                freshJobs.Add(job);
                continue;
            }

            if (warning.Kind == DuplicateKind.InQueue)
            {
                queuedJobs.Add(job);
                continue;
            }

            duplicateJobs.Add(job);
        }

        if (duplicateJobs.Count == 0 && queuedJobs.Count == 0)
            return Task.FromResult<IReadOnlyList<DownloadJob>?>(jobs);

        if (duplicateJobs.Count == 0)
        {
            AppendLog($"Skipped {queuedJobs.Count} track(s) already in the queue.");
            return Task.FromResult<IReadOnlyList<DownloadJob>?>(freshJobs);
        }

        var collectionName = jobs[0].CollectionTitle ?? "this album";
        var duplicateCount = duplicateJobs.Count;
        var total = jobs.Count;
        var newCount = freshJobs.Count;

        var message = duplicateCount == total
            ? $"All {total} tracks in {collectionName} appear to be already downloaded."
            : $"{duplicateCount} of {total} tracks in {collectionName} appear to be already downloaded.";

        if (newCount > 0)
            message += $"\n\n{newCount} track(s) are new and will download either way.";

        if (queuedJobs.Count > 0)
            message += $"\n\n{queuedJobs.Count} track(s) are already waiting in the queue and will be skipped.";

        message += "\n\nYes = Re-download the entire album\nNo = Skip already-downloaded tracks\nCancel = Don't download";

        var result = MessageBox.Show(
            this,
            message,
            "Already downloaded",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                AppendLog($"Re-downloading all {total} track(s) in {collectionName}.");
                return Task.FromResult<IReadOnlyList<DownloadJob>?>(
                    jobs.Select(static j => j.WithForceRedownload(true)).ToList());

            case MessageBoxResult.No:
                if (freshJobs.Count == 0)
                {
                    StatusText.Text = "Skipped — already downloaded.";
                    AppendLog($"Skipped {duplicateCount} already-downloaded track(s) in {collectionName}.");
                    return Task.FromResult<IReadOnlyList<DownloadJob>?>([]);
                }

                AppendLog($"Downloading {freshJobs.Count} new track(s); skipped {duplicateCount} already-downloaded track(s).");
                return Task.FromResult<IReadOnlyList<DownloadJob>?>(freshJobs);

            default:
                return Task.FromResult<IReadOnlyList<DownloadJob>?>(null);
        }
    }

    private DownloadJob BuildJob(
        DownloadScope scope,
        string url,
        string? musicBaseNameOverride = null,
        bool forceRedownload = false,
        string? collectionTitle = null,
        MusicPlaylistFolderInfo? musicPlaylistInfo = null)
    {
        var format = GetSelectedFormat();
        var quality = GetSelectedQuality();
        var chosen = GetSelectedContentKind();
        var namingKind = ContentKindDetector.Resolve(chosen, url);
        var outputFolder = ContentPathResolver.ResolveOutputFolder(App.Settings, chosen, format);
        var customName = GetCustomFileName(scope, url) ?? musicBaseNameOverride;
        var predicted = !string.IsNullOrWhiteSpace(collectionTitle)
            ? collectionTitle
            : customName ?? _predictedFileName ?? url;

        TrackMetadataOverride? metadataOverride = null;
        if (musicPlaylistInfo is not null)
        {
            metadataOverride = TrackMetadataOverride.ForMusicAlbum(
                musicPlaylistInfo.ArtistFolder,
                musicPlaylistInfo.AlbumFolder);
            if (!metadataOverride.HasAny)
                metadataOverride = null;
        }

        return new DownloadJob
        {
            Url = url,
            Scope = scope,
            Format = format,
            Quality = quality,
            EmbedThumbnail = EmbedThumbnailBox.IsChecked == true,
            ContentKind = namingKind,
            OutputFolder = outputFolder,
            CustomFileName = customName,
            PredictedTitle = predicted,
            CollectionTitle = collectionTitle,
            ForceRedownload = forceRedownload,
            CustomCoverArtPath = ResolveCoverArtPathForJob(),
            MetadataOverride = metadataOverride,
            MusicArtistFolder = musicPlaylistInfo?.ArtistFolder,
            MusicAlbumFolder = musicPlaylistInfo?.AlbumFolder,
        };
    }

    private string? GetCustomFileName(DownloadScope scope, string url)
    {
        if (_previewIsLoading)
            return null;

        if (scope != DownloadScope.SingleVideo || UrlBatchParser.Parse(UrlBox.Text).Count != 1)
            return null;

        if (!string.Equals(UrlBatchParser.Parse(UrlBox.Text)[0], url, StringComparison.OrdinalIgnoreCase))
            return null;

        var edited = FilenamePreviewBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(edited) && !PreviewPlaceholders.Contains(edited))
            return OutputTemplateBuilder.SanitizeFileName(Path.GetFileNameWithoutExtension(edited));

        if (!string.IsNullOrWhiteSpace(_predictedFileName))
            return OutputTemplateBuilder.SanitizeFileName(Path.GetFileNameWithoutExtension(_predictedFileName));

        return null;
    }

    private void PersistCurrentChoices()
    {
        var format = GetSelectedFormat();
        var quality = GetSelectedQuality();
        App.Settings.PreferredFormat = DownloadFormats.ToTag(format);
        if (DownloadFormats.IsVideo(format))
            App.Settings.VideoQuality = quality;
        else
            App.Settings.AudioQuality = quality;
        App.SaveSettings();
    }

    private async Task ProcessNextJobAsync()
    {
        if (_queuePaused)
            return;

        var maxParallel = Math.Clamp(App.Settings.MaxParallelDownloads, 1, 5);
        while (_activeDownloads.Count < maxParallel && _queue.Count > 0)
        {
            if (!_queue.TryDequeue(out var job) || job is null)
                break;

            await RunJobAsync(job).ConfigureAwait(true);
        }
    }

    private async Task RunJobAsync(DownloadJob job)
    {
        var downloader = new YtDlpDownloadService();
        var cts = new CancellationTokenSource();
        var runtime = new QueueRuntimeState();
        var context = new ActiveDownloadContext
        {
            Job = job,
            Downloader = downloader,
            Cts = cts,
            Runtime = runtime,
            JobStartedUtc = DateTime.UtcNow,
            ItemStartedUtc = DateTime.UtcNow,
            IsPrimary = _activeDownloads.Count == 0,
        };

        downloader.Progress += (_, e) => Downloader_OnProgress(context, e);
        downloader.Completed += (_, e) => Downloader_OnCompleted(context, e);

        _activeDownloads.Add(context);
        _queueSessionCount++;
        if (context.IsPrimary)
        {
            _queueRuntime.Reset();
            _queueRuntime.SeedFromJob(job);
            ProgressBar.Value = 0;
            ProgressBar.Visibility = Visibility.Visible;
        }

        SetDownloadingUi(true);
        RefreshQueueUi();
        if (context.IsPrimary)
            ScheduleDownloadCoverRefresh(job, _queueRuntime.CurrentVideoUrl);

        var scopeLabel = job.Scope == DownloadScope.Playlist ? "playlist" : "single video";
        var formatLabel = DownloadFormats.ToDisplayLabel(job.Format);
        var kindLabel = ContentKindDetector.DisplayLabel(job.ContentKind);

        if (context.IsPrimary)
        {
            var title = !string.IsNullOrWhiteSpace(job.CollectionTitle)
                ? job.CollectionTitle
                : $"{kindLabel} ({formatLabel})";
            StatusText.Text = $"Downloading {title}…";
        }
        else
            StatusText.Text = $"{_activeDownloads.Count} downloads running…";

        if (!_sessionLogStarted)
        {
            LogBox.Clear();
            _logService.BeginSession($"{formatLabel} / {kindLabel} / {scopeLabel} — {job.Url}");
            AppendLog($"Saving logs to: {_logService.CurrentLogPath}");
            AppendLog($"Output folder: {job.OutputFolder}");
            if (YouTubeUrlHelper.IsMusicAlbumOrPlaylist(job.Url) && !ToolDependencyService.Check().DenoFound)
                AppendLog("WARNING: YouTube Music album — install Deno (winget install DenoLand.Deno -e) and restart the app.");
            _sessionLogStarted = true;
        }
        else
        {
            AppendLog($"--- Next in queue: {formatLabel} / {kindLabel} / {scopeLabel} ---");
            AppendLog($"Output folder: {job.OutputFolder}");
        }

        try
        {
            await downloader.StartAsync(
                job.Url,
                job.OutputFolder,
                job.Format,
                job.Scope,
                job.ContentKind,
                job.Quality,
                job.EmbedThumbnail,
                job.CustomFileName,
                job.MusicArtistFolder,
                job.MusicAlbumFolder,
                job.MetadataOverride,
                App.Settings.ConcurrentFragments,
                useDownloadArchive: App.Settings.SkipAlreadyDownloaded &&
                                    !job.ForceRedownload &&
                                    job.Scope == DownloadScope.Playlist,
                job.CustomCoverArtPath,
                cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            StatusText.Text = "Download failed.";
            RemoveActiveContext(context);
            if (_activeDownloads.Count == 0 && _queue.Count == 0)
                FinishQueueSession();
            else
                await ProcessNextJobAsync().ConfigureAwait(true);
        }
    }

    private void RemoveActiveContext(ActiveDownloadContext context)
    {
        _activeDownloads.Remove(context);
        try
        {
            context.Cts.Dispose();
            context.Downloader.Dispose();
        }
        catch
        {
            /* ignore */
        }

        if (_activeDownloads.Count > 0 && !_activeDownloads.Any(c => c.IsPrimary))
            _activeDownloads[0].IsPrimary = true;

        RefreshQueueUi();
    }

    private void FinishQueueSession()
    {
        var count = _queueSessionCount;
        foreach (var ctx in _activeDownloads.ToList())
            RemoveActiveContext(ctx);

        SetDownloadingUi(false);
        ProgressBar.Visibility = Visibility.Collapsed;
        _sessionLogStarted = false;
        _queueSessionCount = 0;
        _sessionCompletedCount = 0;
        _queueRuntime.Reset();

        if (count > 0 && App.Settings.NotifyOnQueueComplete)
            _tray.NotifyQueueComplete(count);
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        var primary = PrimaryContext;
        if (primary is null)
            return;

        primary.Cts.Cancel();
        primary.Downloader.Cancel();
        StatusText.Text = "Cancelling…";
    }

    private void PauseQueue_OnClick(object sender, RoutedEventArgs e) => ToggleQueuePaused();

    private void ToggleQueuePaused()
    {
        _queuePaused = !_queuePaused;
        UpdatePauseQueueButton();
        StatusText.Text = _queuePaused
            ? "Queue paused — active downloads continue."
            : "Queue resumed.";
        _queueWindow?.Refresh();
        if (!_queuePaused)
            _ = ProcessNextJobAsync();
    }

    private void UpdatePauseQueueButton()
    {
        PauseQueueButton.Content = _queuePaused ? "Resume queue" : "Pause queue";
    }

    private static void AppendToDownloadArchive(string url)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var videoId = YouTubeUrlHelper.TryGetVideoId(url);
            var line = !string.IsNullOrWhiteSpace(videoId)
                ? $"youtube {videoId}"
                : url.Trim();
            File.AppendAllText(AppPaths.DownloadArchivePath, line + Environment.NewLine);
        }
        catch
        {
            /* ignore archive write failures */
        }
    }

    private void RemoveQueueItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedIndex < 0)
            return;
        _queue.RemoveAt(QueueList.SelectedIndex);
        StatusText.Text = _queue.Count > 0 ? $"{_queue.Count} item(s) in queue." : "Queue cleared.";
    }

    private void ClearQueue_OnClick(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        StatusText.Text = "Queue cleared.";
    }

    private void MoveQueueUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index > 0)
            _queue.Move(index, index - 1);
    }

    private void MoveQueueDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index >= 0 && index < _queue.Count - 1)
            _queue.Move(index, index + 1);
    }

    private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragSourceIndex = QueueList.SelectedIndex;
        _dragStartPoint = e.GetPosition(null);
    }

    private void QueueList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (QueueList.SelectedItem is DownloadJob job)
            DragDrop.DoDragDrop(QueueList, job, System.Windows.DragDropEffects.Move);
    }

    private void QueueList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DownloadJob)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void QueueList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DownloadJob)) is not DownloadJob)
            return;

        var targetIndex = GetQueueDropIndex(e);
        if (_dragSourceIndex >= 0 && targetIndex >= 0)
            _queue.Move(_dragSourceIndex, targetIndex);

        _dragSourceIndex = -1;
    }

    private int GetQueueDropIndex(System.Windows.DragEventArgs e)
    {
        var point = e.GetPosition(QueueList);
        for (var i = 0; i < QueueList.Items.Count; i++)
        {
            var item = (ListBoxItem)QueueList.ItemContainerGenerator.ContainerFromIndex(i);
            if (item is null)
                continue;
            if (point.Y < item.TranslatePoint(new System.Windows.Point(0, item.ActualHeight / 2), QueueList).Y)
                return i;
        }

        return QueueList.Items.Count - 1;
    }

    private void RefreshQueueUi()
    {
        QueueList.ItemsSource = null;
        QueueList.ItemsSource = _queue.Snapshot();

        var hasItems = _queue.Count > 0;
        QueueCard.Visibility = hasItems || IsProcessing || _queuePaused ? Visibility.Visible : Visibility.Collapsed;
        if (hasItems)
            QueueCountText.Text = _queuePaused
                ? $"{_queue.Count} waiting (paused)"
                : $"{_queue.Count} waiting";
        else if (IsProcessing)
            QueueCountText.Text = _activeDownloads.Count > 1
                ? $"{_activeDownloads.Count} downloading…"
                : "Downloading…";
        else
            QueueCountText.Text = _queuePaused ? "Paused" : "";
    }

    private ContentKind GetSelectedContentKind()
    {
        if (ContentKindCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (string.Equals(tag, "music", StringComparison.OrdinalIgnoreCase))
                return ContentKind.Music;
            if (string.Equals(tag, "video", StringComparison.OrdinalIgnoreCase))
                return ContentKind.Video;
        }

        return ContentKind.Auto;
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

    private void Downloader_OnProgress(ActiveDownloadContext context, DownloadProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AppendLog(e.Line);

            if (e.PlaylistIndex is { } idx &&
                context.LastPlaylistIndex is { } prev &&
                idx != prev)
            {
                var itemDuration = DateTime.UtcNow - context.ItemStartedUtc;
                if (itemDuration > TimeSpan.FromSeconds(2))
                    RememberDuration(_completedItemDurations, itemDuration);
                context.ItemStartedUtc = DateTime.UtcNow;
            }

            if (e.PlaylistIndex is not null)
                context.LastPlaylistIndex = e.PlaylistIndex;

            context.Runtime.ApplyProgress(e);

            if (!context.IsPrimary && PrimaryContext is not null)
                return;

            _queueRuntime.ApplyProgress(e);

            if (!string.IsNullOrWhiteSpace(e.CurrentVideoUrl) ||
                !string.IsNullOrWhiteSpace(e.CurrentTrackTitle) ||
                e.PlaylistIndex is not null)
            {
                ScheduleDownloadCoverRefresh(context.Job, _queueRuntime.CurrentVideoUrl ?? e.CurrentVideoUrl);
            }

            var label = QueueDisplayHelper.TitleFor(context.Job, _queueRuntime);
            var rawEta = QueueDisplayHelper.EstimateQueueRemaining(
                _queueRuntime,
                PrimaryJob,
                _queue.Snapshot(),
                _completedJobDurations,
                _completedItemDurations);
            _queueEtaCountdown.Sync(rawEta);
            var queueEta = _queueEtaCountdown.Remaining ?? rawEta;

            if (e.Percent is { } p)
            {
                ProgressBar.Value = p;
                ProgressBar.Visibility = Visibility.Visible;
                if (_activeDownloads.Count > 1)
                {
                    StatusText.Text = $"{_activeDownloads.Count} downloads running · {p:0.#}%";
                }
                else
                {
                    StatusText.Text = $"{label} · {p:0.#}%";
                }
            }
            else if (!string.IsNullOrWhiteSpace(e.Status))
            {
                StatusText.Text = context.Job.Scope == DownloadScope.Playlist
                    ? label
                    : $"{label} · {e.Status}";
            }

            UpdateDownloadMetricsUi();
            _ = queueEta; // refreshed via countdown in UpdateDownloadMetricsUi
        });
    }

    private void Downloader_OnCompleted(ActiveDownloadContext context, DownloadCompletedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            var itemDuration = DateTime.UtcNow - context.ItemStartedUtc;
            if (itemDuration > TimeSpan.FromSeconds(2))
                RememberDuration(_completedItemDurations, itemDuration);

            var jobDuration = DateTime.UtcNow - context.JobStartedUtc;
            if (jobDuration > TimeSpan.FromSeconds(2))
                RememberDuration(_completedJobDurations, jobDuration);

            _sessionCompletedCount++;
            var wasPrimary = context.IsPrimary;
            RemoveActiveContext(context);

            var formatLabel = DownloadFormats.ToDisplayLabel(e.Format);
            var kindLabel = ContentKindDetector.DisplayLabel(e.ContentKind);
            var scopeLabel = e.Scope == DownloadScope.Playlist ? "Playlist" : "Video";

            if (e.Success)
            {
                _lastSavedPath = e.OutputPath;
                _lastSavedContentKind = e.ContentKind;
                _lastImportIsAlbum = false;

                var albumCompleting = e.ContentKind == ContentKind.Music
                                      && _activeDownloads.Count == 0
                                      && _queue.Count == 0
                                      && IsAlbumSessionComplete(context.Job);

                if (wasPrimary || _activeDownloads.Count == 0)
                {
                    if (albumCompleting)
                    {
                        var albumFolder = DownloadJobPathHelper.ResolveAlbumFolder(context.Job)
                                          ?? Path.GetDirectoryName(e.OutputPath);
                        if (!string.IsNullOrWhiteSpace(albumFolder) && Directory.Exists(albumFolder))
                            ShowAlbumDoneState(albumFolder, _sessionCompletedCount, e.ContentKind);
                        else
                            ShowDoneState(e, kindLabel, formatLabel, scopeLabel);
                    }
                    else
                    {
                        ShowDoneState(e, kindLabel, formatLabel, scopeLabel, context.Job);
                    }
                }
                AppendLog("Done!");
                RecordHistory(e, context.Job);
                if (App.Settings.SkipAlreadyDownloaded && e.Scope == DownloadScope.SingleVideo)
                    AppendToDownloadArchive(context.Job.Url);
            }
            else
            {
                if (wasPrimary || _activeDownloads.Count == 0)
                    HideDoneState();
                StatusText.Text = e.ErrorMessage ?? "Download failed.";
                if (!string.IsNullOrWhiteSpace(e.ErrorMessage))
                    AppendLog(e.ErrorMessage);
            }

            if (_activeDownloads.Count == 0)
                ProgressBar.Visibility = Visibility.Collapsed;
            else if (PrimaryContext is { } nextPrimary)
            {
                ProgressBar.Value = nextPrimary.Runtime.Percent ?? 0;
                ProgressBar.Visibility = Visibility.Visible;
            }

            await ProcessNextJobAsync().ConfigureAwait(true);

            if (_activeDownloads.Count == 0 && _queue.Count == 0)
                FinishQueueSession();
        });
    }

    private static void RememberDuration(List<TimeSpan> list, TimeSpan duration)
    {
        list.Add(duration);
        while (list.Count > 12)
            list.RemoveAt(0);
    }

    private void RecordHistory(DownloadCompletedEventArgs e, DownloadJob job)
    {
        _history.Add(new DownloadHistoryEntry
        {
            Url = job.Url,
            Title = job.PredictedTitle ?? Path.GetFileNameWithoutExtension(e.OutputPath),
            OutputPath = e.OutputPath,
            OutputFolder = e.OutputFolder,
            Format = DownloadFormats.ToTag(e.Format),
            ContentKind = e.ContentKind.ToString(),
            Scope = e.Scope.ToString(),
            CompletedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Success = true,
        });
    }

    private void ShowAlbumDoneState(string albumFolder, int trackCount, ContentKind contentKind)
    {
        _lastSavedPath = albumFolder;
        _lastSavedContentKind = contentKind;
        _lastImportIsAlbum = true;

        StatusText.Text = "Done!";
        DonePathText.Text = trackCount > 1
            ? $"Saved music album ({trackCount} tracks) to:\n{albumFolder}"
            : $"Saved music album to:\n{albumFolder}";
        DonePathText.Visibility = Visibility.Visible;
        DoneActions.Visibility = Visibility.Visible;

        if (ImportToMusicHubButton is not null)
        {
            ImportToMusicHubButton.Content = "Import album to Music Hub";
            ImportToMusicHubButton.Visibility = LocalMusicHubIntegration.CanImportAlbumFolder(albumFolder, contentKind)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (App.Settings.PlaySoundOnComplete && _queue.Count == 0 && _activeDownloads.Count == 0)
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static bool IsAlbumSessionComplete(DownloadJob job)
    {
        if (job.ContentKind != ContentKind.Music)
            return false;

        if (job.Scope == DownloadScope.Playlist)
            return true;

        if (string.IsNullOrWhiteSpace(job.MusicAlbumFolder))
            return false;

        if (job.PlaylistTrackTotal is int total && job.PlaylistTrackIndex is int index)
            return index >= total;

        return false;
    }

    private void ShowDoneState(
        DownloadCompletedEventArgs e,
        string kindLabel,
        string formatLabel,
        string scopeLabel,
        DownloadJob? job = null)
    {
        _lastImportIsAlbum = false;
        StatusText.Text = "Done!";
        var scopeText = job?.MusicAlbumFolder is not null && !string.IsNullOrWhiteSpace(job.MusicAlbumFolder)
            ? "album track"
            : scopeLabel.ToLower(CultureInfo.InvariantCulture);
        DonePathText.Text = $"Saved {kindLabel} {formatLabel} ({scopeText}) to:\n{e.OutputPath}";
        DonePathText.Visibility = Visibility.Visible;
        DoneActions.Visibility = Visibility.Visible;
        if (ImportToMusicHubButton is not null)
        {
            ImportToMusicHubButton.Content = "Import to Music Hub";
            ImportToMusicHubButton.Visibility = LocalMusicHubIntegration.CanImport(e.OutputPath, e.ContentKind)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (App.Settings.PlaySoundOnComplete && _queue.Count == 0 && _activeDownloads.Count == 0)
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private void HideDoneState()
    {
        DonePathText.Visibility = Visibility.Collapsed;
        DoneActions.Visibility = Visibility.Collapsed;
    }

    private void SetDownloadingUi(bool downloading)
    {
        // Keep controls usable so users can queue more while downloads run.
        CancelButton.IsEnabled = downloading;
        UrlBox.IsReadOnly = false;
        FormatCombo.IsEnabled = true;
        QualityCombo.IsEnabled = true;
        EmbedThumbnailBox.IsEnabled = true;
        ContentKindCombo.IsEnabled = true;
        FilenamePreviewBox.IsReadOnly = false;
        ChooseCoverArtButton.IsEnabled = true;

        if (downloading)
        {
            DownloadMetricsPanel.Visibility = Visibility.Visible;
            if (!_statusTickTimer.IsEnabled)
                _statusTickTimer.Start();
            UpdateDownloadMetricsUi();
        }
        else if (!IsProcessing)
        {
            _statusTickTimer.Stop();
            _queueEtaCountdown.Reset();
            DownloadMetricsPanel.Visibility = Visibility.Collapsed;
            MetricTrackText.Text = "—";
            MetricFileEtaText.Text = "—";
            MetricQueueEtaText.Text = "—";
            _displayCoverArtPath = null;
            _lastCoverVideoUrl = null;
            SetCoverImage(DownloadCoverImage, DownloadCoverPlaceholder, null);
            _ = RefreshUrlCoverPreviewAsync();
        }
    }

    private void AppendLog(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
        _logService.WriteLine(line);
    }

    private void RefreshToolStatus()
    {
        var tools = ToolDependencyService.Check();
        if (tools.AllFound)
        {
            AppendLog($"yt-dlp: {tools.YtDlpPath}");
            AppendLog($"ffmpeg: {tools.FfmpegPath}");
            if (tools.DenoFound)
                AppendLog($"deno: {tools.DenoPath} (YouTube JS — recommended)");
            else
                AppendLog("WARNING: Deno not on PATH — install with: winget install DenoLand.Deno -e");
            AppendLog($"Logs folder: {AppPaths.LogsDirectory}");
            AppendLog($"History: {AppPaths.HistoryPath}");
            StatusText.Text = "Ready.";
            return;
        }

        StatusText.Text = $"Missing: {tools.MissingSummary}. Install tools to download.";
        AppendLog($"WARNING: Missing {tools.MissingSummary}.");
        AppendLog(ToolDependencyService.InstallHint);
    }
}
