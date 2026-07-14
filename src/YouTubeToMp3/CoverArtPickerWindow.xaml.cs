using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace YouTubeToMp3;

public partial class CoverArtPickerWindow
{
    private readonly DispatcherTimer _urlTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private CancellationTokenSource? _fetchCts;

    public string? SelectedCoverArtPath { get; private set; }
    public bool UseVideoDefault { get; private set; } = true;
    public string CoverSourceUrl => UrlBox.Text.Trim();

    public CoverArtPickerWindow(string? currentCoverPath = null, string? suggestedVideoUrl = null)
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();
        _urlTimer.Tick += UrlTimer_OnTick;
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(suggestedVideoUrl))
            {
                UrlBox.Text = suggestedVideoUrl;
                ScheduleUrlFetch();
            }
        };
        Closed += (_, _) =>
        {
            _urlTimer.Stop();
            _fetchCts?.Cancel();
        };

        if (!string.IsNullOrWhiteSpace(currentCoverPath) && File.Exists(currentCoverPath))
        {
            SelectedCoverArtPath = currentCoverPath;
            UseVideoDefault = false;
            ShowPreview(currentCoverPath);
            StatusText.Text = Path.GetFileName(currentCoverPath);
        }
        else
        {
            StatusText.Text = "Using each video’s YouTube thumbnail (default).";
            ClearPreview();
        }
    }

    private void UrlBox_OnTextChanged(object sender, TextChangedEventArgs e) => ScheduleUrlFetch();

    private void ScheduleUrlFetch()
    {
        _urlTimer.Stop();
        _urlTimer.Start();
    }

    private void UrlTimer_OnTick(object? sender, EventArgs e)
    {
        _urlTimer.Stop();
        _ = FetchFromUrlAsync(showMissingUrlMessage: false);
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*",
            Title = "Choose cover art image",
        };
        if (dlg.ShowDialog() != true)
            return;

        SelectedCoverArtPath = dlg.FileName;
        UseVideoDefault = false;
        ShowPreview(dlg.FileName);
        StatusText.Text = Path.GetFileName(dlg.FileName);
    }

    private async void FetchFromUrl_OnClick(object sender, RoutedEventArgs e) =>
        await FetchFromUrlAsync(showMissingUrlMessage: true).ConfigureAwait(true);

    private async Task FetchFromUrlAsync(bool showMissingUrlMessage)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            if (showMissingUrlMessage)
            {
                MessageBox.Show(this, "Paste a YouTube video URL whose thumbnail you want to use.",
                    "Cover art", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        if (!url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return;

        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var token = _fetchCts.Token;

        FetchFromUrlButton.IsEnabled = false;
        StatusText.Text = "Fetching thumbnail…";
        try
        {
            var (path, error) = await CoverArtFetchService.FetchThumbnailFromUrlAsync(url, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            if (path is null)
            {
                StatusText.Text = error ?? "Could not fetch thumbnail.";
                if (showMissingUrlMessage)
                {
                    MessageBox.Show(this, error ?? "Could not fetch thumbnail.", "Cover art",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            SelectedCoverArtPath = path;
            UseVideoDefault = false;
            ShowPreview(path);
            StatusText.Text = "Cover ready — applies to every track in this download.";
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }
        finally
        {
            FetchFromUrlButton.IsEnabled = true;
        }
    }

    private void UseDefault_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedCoverArtPath = null;
        UseVideoDefault = true;
        ClearPreview();
        StatusText.Text = "Using each video’s YouTube thumbnail (default).";
    }

    private void UseForDownload_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowPreview(string path)
    {
        try
        {
            var bmp = CoverArtPreviewService.LoadBitmap(path, 384);
            if (bmp is null)
                throw new InvalidOperationException("Could not load image.");

            PreviewImage.Source = bmp;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ClearPreview();
            StatusText.Text = "Could not preview image, but the path will still be used.";
        }
    }

    private void ClearPreview()
    {
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }
}
