using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeToMp3;

public partial class QueueWindow
{
    private readonly Func<QueueWindowSnapshot> _getSnapshot;
    private readonly Action<int> _removeWaitingAt;
    private readonly Action<int, int> _moveWaiting;
    private readonly Action _clearWaiting;
    private readonly Action _cancelActive;
    private readonly Action _togglePause;
    private readonly DispatcherTimer _refreshTimer;

    public QueueWindow(
        DownloadQueue queue,
        QueueRuntimeState runtime,
        Func<QueueWindowSnapshot> getSnapshot,
        Action<int> removeWaitingAt,
        Action<int, int> moveWaiting,
        Action clearWaiting,
        Action cancelActive,
        Action togglePause)
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();

        _getSnapshot = getSnapshot;
        _removeWaitingAt = removeWaitingAt;
        _moveWaiting = moveWaiting;
        _clearWaiting = clearWaiting;
        _cancelActive = cancelActive;
        _togglePause = togglePause;

        queue.Changed += (_, _) => Refresh();
        runtime.Changed += (_, _) => Refresh();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public void Refresh()
    {
        if (!IsLoaded)
            return;

        var snapshot = _getSnapshot();
        MetricStatusText.Text = QueueDisplayHelper.BuildSummary(
            snapshot.IsProcessing,
            snapshot.Waiting.Count,
            snapshot.CompletedThisSession,
            snapshot.Runtime,
            queueEta: null,
            snapshot.ActiveCount,
            snapshot.QueuePaused);

        if (snapshot.QueueEta is { } q && q > TimeSpan.Zero)
            MetricQueueEtaText.Text = $"~{QueueDisplayHelper.FormatDuration(q)}";
        else
            MetricQueueEtaText.Text = "—";

        var fileEta = snapshot.Runtime.LiveFileEta;
        MetricFileEtaText.Text = !string.IsNullOrWhiteSpace(fileEta) ? fileEta : "—";
        MetricSpeedText.Text = !string.IsNullOrWhiteSpace(snapshot.Runtime.Speed)
            ? snapshot.Runtime.Speed!
            : "—";

        PauseResumeButton.Content = snapshot.QueuePaused ? "Resume queue" : "Pause queue";

        if (snapshot.IsProcessing && snapshot.ActiveJob is { } active)
        {
            ActiveCard.Visibility = Visibility.Visible;
            CancelActiveButton.Visibility = Visibility.Visible;
            ActiveTitleText.Text = QueueDisplayHelper.TitleFor(active, snapshot.Runtime);
            ActiveKindText.Text = ContentKindDetector.DisplayLabel(active.ContentKind);
            ActiveFormatText.Text = DownloadFormats.ToDisplayLabel(active.Format);
            ActiveQualityText.Text = QualityPresets.GetLabel(active.Format, active.Quality);
            ActiveScopeText.Text = QueueDisplayHelper.ScopeFor(active);
            ActiveFolderText.Text = active.OutputFolder;
            ActiveUrlText.Text = active.Url;
            ActiveProgressText.Text = QueueDisplayHelper.FormatActiveProgress(snapshot.Runtime);
            ActiveProgressBar.Value = snapshot.Runtime.Percent ?? 0;
            ActiveProgressBar.Visibility = snapshot.Runtime.Percent is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
            SetCoverImage(ActiveCoverImage, ActiveCoverPlaceholder, snapshot.CoverArtPath);
        }
        else
        {
            ActiveCard.Visibility = Visibility.Collapsed;
            CancelActiveButton.Visibility = Visibility.Collapsed;
            SetCoverImage(ActiveCoverImage, ActiveCoverPlaceholder, null);
        }

        var rows = new List<QueueRowViewModel>();
        var position = 1;
        foreach (var job in snapshot.ActiveJobs)
        {
            var isPrimary = snapshot.ActiveJob is not null &&
                            ReferenceEquals(job, snapshot.ActiveJob);
            rows.Add(QueueDisplayHelper.ToRow(
                job,
                position++,
                isPrimary ? "Active" : "Running",
                isPrimary
                    ? QueueDisplayHelper.FormatActiveProgress(snapshot.Runtime)
                    : "Downloading…",
                isPrimary ? snapshot.Runtime : null));
        }

        for (var i = 0; i < snapshot.Waiting.Count; i++)
        {
            rows.Add(QueueDisplayHelper.ToRow(
                snapshot.Waiting[i],
                position++,
                "Waiting"));
        }

        QueueItemsList.ItemsSource = rows;
    }

    private int GetSelectedWaitingIndex()
    {
        if (QueueItemsList.SelectedItem is not QueueRowViewModel row || row.Status != "Waiting")
            return -1;

        var snapshot = _getSnapshot();
        return row.Position - snapshot.ActiveCount - 1;
    }

    private void MoveUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = GetSelectedWaitingIndex();
        if (index > 0)
            _moveWaiting(index, index - 1);
    }

    private void MoveDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = GetSelectedWaitingIndex();
        var waiting = _getSnapshot().Waiting.Count;
        if (index >= 0 && index < waiting - 1)
            _moveWaiting(index, index + 1);
    }

    private void Remove_OnClick(object sender, RoutedEventArgs e)
    {
        var index = GetSelectedWaitingIndex();
        if (index >= 0)
            _removeWaitingAt(index);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => _clearWaiting();

    private void CancelActive_OnClick(object sender, RoutedEventArgs e) => _cancelActive();

    private void PauseResume_OnClick(object sender, RoutedEventArgs e) => _togglePause();

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueItemsList.SelectedItem is not QueueRowViewModel row)
        {
            MessageBox.Show(this, "Select a queue row first.", "Download Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var folder = row.Job.OutputFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Folder does not exist yet.", "Download Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Download Queue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void QueueItemsList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Selection drives move/remove/open folder; no extra UI needed.
    }

    private static void SetCoverImage(System.Windows.Controls.Image image, System.Windows.Controls.TextBlock placeholder, string? path)
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
}

public sealed class QueueWindowSnapshot
{
    public required bool IsProcessing { get; init; }
    public required DownloadJob? ActiveJob { get; init; }
    public IReadOnlyList<DownloadJob> ActiveJobs { get; init; } = [];
    public int ActiveCount { get; init; }
    public bool QueuePaused { get; init; }
    public required QueueRuntimeState Runtime { get; init; }
    public required IReadOnlyList<DownloadJob> Waiting { get; init; }
    public required int CompletedThisSession { get; init; }
    public TimeSpan? QueueEta { get; init; }
    public string? CoverArtPath { get; init; }
}
