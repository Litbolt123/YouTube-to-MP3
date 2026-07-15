using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
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
        var rows = DownloadHistoryViewBuilder.Build(_history.Entries).ToList();

        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DownloadHistoryViewBuilder.HistoryDisplayItem.GroupLabel)));
        view.SortDescriptions.Add(new SortDescription(nameof(DownloadHistoryViewBuilder.HistoryDisplayItem.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(DownloadHistoryViewBuilder.HistoryDisplayItem.CompletedAt), ListSortDirection.Descending));

        HistoryList.ItemsSource = view;
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

        if (entry is not null && entry.IsCollection)
            ImportToMusicHubButton.Content = "Import album to Music Hub";
        else
            ImportToMusicHubButton.Content = "Import to Music Hub";
    }

    private void ImportToMusicHub_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        var path = entry.OutputPath;
        var result = entry.IsCollection && Directory.Exists(path)
            ? LocalMusicHubIntegration.RequestImportAlbum(path)
            : LocalMusicHubIntegration.RequestImport(path);

        MessageBox.Show(this, result.Message, "Local Music Hub",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private DownloadHistoryEntry? GetSelectedEntry()
    {
        if (HistoryList.SelectedItem is not DownloadHistoryViewBuilder.HistoryDisplayItem row)
            return null;
        return row.Entry;
    }

    private void OpenFile_OnClick(object sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        if (entry.IsCollection)
        {
            OpenFolderPath(entry);
            return;
        }

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

        OpenFolderPath(entry);
    }

    private void OpenFolderPath(DownloadHistoryEntry entry)
    {
        var folder = entry.IsCollection && Directory.Exists(entry.OutputPath)
            ? entry.OutputPath
            : File.Exists(entry.OutputPath)
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
}
