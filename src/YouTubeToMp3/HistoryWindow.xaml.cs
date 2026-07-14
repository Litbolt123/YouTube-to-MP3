using System.Diagnostics;
using System.Globalization;
using System.Windows;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeToMp3;

public partial class HistoryWindow
{
    private readonly DownloadHistoryService _history;

    public event EventHandler<DownloadHistoryEntry>? RedownloadRequested;

    public HistoryWindow(DownloadHistoryService history)
    {
        _history = history;
        DashboardUi.EnsureTheme(this);
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
        HistoryList.SelectionChanged += (_, _) => UpdateImportButton();
    }

    private void RefreshList()
    {
        HistoryList.ItemsSource = _history.Entries
            .Select(e => new HistoryRow(e))
            .ToList();
        UpdateImportButton();
    }

    private void UpdateImportButton()
    {
        if (ImportToMusicHubButton is null)
            return;

        var entry = GetSelectedEntry();
        ImportToMusicHubButton.Visibility = entry is not null && LocalMusicHubIntegration.CanImportHistoryEntry(entry)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ImportToMusicHub_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        var result = LocalMusicHubIntegration.RequestImport(entry.OutputPath);
        MessageBox.Show(this, result.Message, "Local Music Hub",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private DownloadHistoryEntry? GetSelectedEntry()
    {
        if (HistoryList.SelectedItem is not HistoryRow row)
            return null;
        return _history.Entries.FirstOrDefault(e => e.Id == row.Id);
    }

    private void OpenFile_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        if (File.Exists(entry.OutputPath))
            OpenPath(entry.OutputPath);
        else
            MessageBox.Show(this, "File no longer exists.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        var folder = File.Exists(entry.OutputPath)
            ? Path.GetDirectoryName(entry.OutputPath)
            : entry.OutputFolder;

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            OpenPath(folder);
        else
            MessageBox.Show(this, "Folder no longer exists.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Redownload_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        RedownloadRequested?.Invoke(this, entry);
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class HistoryRow
    {
        public HistoryRow(DownloadHistoryEntry entry)
        {
            Id = entry.Id;
            Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title;
            OutputPath = entry.OutputPath;
            TypeDisplay = $"{entry.ContentKind} · {entry.Format.ToUpperInvariant()}";
            DateDisplay = DateTime.TryParse(entry.CompletedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("g")
                : entry.CompletedUtc;
        }

        public string Id { get; }
        public string Title { get; }
        public string OutputPath { get; }
        public string TypeDisplay { get; }
        public string DateDisplay { get; }
    }
}
